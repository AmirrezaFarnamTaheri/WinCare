#requires -Version 7.2

function Get-WinCareWasiRuntime {
    [CmdletBinding()]
    param([string]$RuntimePath)
    foreach($candidate in @($RuntimePath,'wasmtime','wasmer')|Where-Object{$_}) {
        $command=Get-Command $candidate -CommandType Application -ErrorAction SilentlyContinue|Select-Object -First 1
        if($command) { return $command }
    }
    $null
}

function Invoke-WinCareWasiPlugin {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PluginPath,
        [string[]]$Argument=@(),
        [string[]]$PreopenDirectory=@(),
        [ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedSha256,
        [string]$RuntimePath,
        [ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedRuntimeSha256,
        [ValidateRange(1,1800)][int]$TimeoutSeconds=60,
        [ValidateRange(1024,4194304)][int]$MaximumCapturedOutputBytes=1048576
    )
    if(-not (Test-Path -LiteralPath $PluginPath -PathType Leaf)) {
        return New-WinCareResult `
            -Success $false -Status Blocked -Code 'WasiPluginNotFound' `
            -Message 'The WASI plugin file does not exist.' -ExitCode 2
    }
    $pluginFull=Assert-WinCareSafePath -LiteralPath $PluginPath
    $pluginItem=Get-Item -LiteralPath $pluginFull -Force
    if([IO.Path]::GetExtension($pluginFull).ToLowerInvariant() -notin @('.wasm','.wat')) {
        return New-WinCareResult `
            -Success $false -Status Blocked -Code 'InvalidWasiPluginType' `
            -Message 'Only .wasm and .wat plugins are accepted.' -ExitCode 65
    }
    if($pluginItem.Length -gt 64MB) {
        return New-WinCareResult `
            -Success $false -Status Blocked -Code 'WasiPluginTooLarge' `
            -Message 'WASI plugins are limited to 64 MiB.' -ExitCode 77
    }
    $hash=(Get-FileHash -LiteralPath $pluginFull -Algorithm SHA256).Hash.ToLowerInvariant()
    if(-not $ExpectedSha256) {
        return New-WinCareResult `
            -Success $false -Status Blocked -Code 'WasiPluginDigestRequired' `
            -Message 'ExpectedSha256 is required before plugin execution.' -ExitCode 78 `
            -Data @{ObservedSha256=$hash;EvidenceType='PluginHash'}
    }
    if($hash -ne $ExpectedSha256.ToLowerInvariant()) {
        return New-WinCareResult `
            -Success $false -Status Blocked -Code 'WasiPluginHashMismatch' `
            -Message 'The plugin hash does not match the expected digest.' -ExitCode 74 `
            -Data @{ObservedSha256=$hash;ExpectedSha256=$ExpectedSha256;EvidenceType='PluginHash'}
    }
    if($Argument.Count -gt 128 -or @($Argument|Where-Object{([string]$_).Length -gt 4096}).Count) {
        return New-WinCareResult `
            -Success $false -Status Blocked -Code 'WasiArgumentLimitExceeded' `
            -Message 'WASI execution accepts at most 128 arguments of at most 4096 characters each.' `
            -ExitCode 77
    }
    if($PreopenDirectory.Count -gt 32) {
        return New-WinCareResult `
            -Success $false -Status Blocked -Code 'WasiPreopenLimitExceeded' `
            -Message 'WASI execution accepts at most 32 preopened directories.' -ExitCode 77
    }
    $runtime=Get-WinCareWasiRuntime -RuntimePath $RuntimePath
    if(-not $runtime) {
        return New-WinCareResult `
            -Success $false -Status Blocked -Code 'WasiRuntimeMissing' `
            -Message 'Neither Wasmtime nor Wasmer was found.' -ExitCode 78 `
            -Data @{PluginSha256=$hash;EvidenceType='DependencyProbe'}
    }
    try {
        $runtimeFull=Assert-WinCareSafePath -LiteralPath $runtime.Source
        $runtimeItem=Get-Item -LiteralPath $runtimeFull -Force
        if($runtimeItem.Length -gt 512MB) { throw 'The WASI runtime exceeds the 512 MiB execution bound.' }
        $runtimeHash=(Get-FileHash -LiteralPath $runtimeFull -Algorithm SHA256).Hash.ToLowerInvariant()
        if(-not $ExpectedRuntimeSha256) { throw 'ExpectedRuntimeSha256 is required before WASI runtime execution.' }
        if($runtimeHash -ne $ExpectedRuntimeSha256.ToLowerInvariant()) { throw 'WASI runtime SHA-256 mismatch.' }
        $preopens=[Collections.Generic.List[string]]::new()
        foreach($directory in $PreopenDirectory) {
            if(-not (Test-Path -LiteralPath $directory -PathType Container)) { throw "Preopen directory not found: $directory" }
            $full=Assert-WinCareSafePath -LiteralPath $directory
            $item=Get-Item -LiteralPath $full -Force
            if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Preopen directory cannot be a reparse point: $directory"
            }
            $preopens.Add($full)
        }
        $runtimeName=[IO.Path]::GetFileNameWithoutExtension($runtimeFull).ToLowerInvariant()
        $runtimeArguments=[Collections.Generic.List[string]]::new()
        $runtimeArguments.Add('run')
        foreach($directory in $preopens) {
            $runtimeArguments.Add('--dir')
            $runtimeArguments.Add($directory)
        }
        $runtimeArguments.Add($pluginFull)
        if($Argument.Count) {
            $runtimeArguments.Add('--')
            foreach($item in $Argument) { $runtimeArguments.Add([string]$item) }
        }
        if((Get-WinCareSha256 -LiteralPath $pluginFull) -ne $ExpectedSha256.ToLowerInvariant()){throw 'WASI plugin changed after admission.'}
        $execution=Invoke-WinCareProcess `
            -FilePath $runtimeFull `
            -ArgumentList @($runtimeArguments) `
            -TimeoutSeconds $TimeoutSeconds `
            -SuccessExitCodes @(0) `
            -MaximumCapturedOutputBytes $MaximumCapturedOutputBytes `
            -ExpectedSha256 $ExpectedRuntimeSha256
        $version=Invoke-WinCareProcess `
            -FilePath $runtimeFull `
            -ArgumentList @('--version') `
            -TimeoutSeconds 10 `
            -SuccessExitCodes @(0) `
            -MaximumCapturedOutputBytes 65536 `
            -ExpectedSha256 $ExpectedRuntimeSha256
        $data=@{
            Plugin=$pluginFull
            PluginSha256=$hash
            Runtime=$runtimeFull
            RuntimeSha256=$runtimeHash
            RuntimeFamily=$runtimeName
            RuntimeVersion=if($version.Success){$version.Data.StdOut.Trim()}else{$null}
            PreopenDirectories=@($preopens)
            Stdout=$execution.Data.StdOut
            Stderr=$execution.Data.StdErr
            StdoutTruncated=[bool]$execution.Data.StdoutTruncated
            StderrTruncated=[bool]$execution.Data.StderrTruncated
            EvidenceType='DigestPinnedBoundedWasiRuntimeExecution'
        }
        $code=if($execution.Success) {
            'WasiPluginCompleted'
        } elseif($execution.ExitCode -eq 124) {
            'WasiPluginTimedOut'
        } else {
            'WasiPluginFailed'
        }
        $message=if($execution.Success) {
            'The digest-pinned WASI runtime executed the digest-pinned plugin within configured resource bounds.'
        } else {
            $execution.Message
        }
        New-WinCareResult `
            -Success $execution.Success -Status $execution.Status -Code $code `
            -Message $message -ExitCode $execution.ExitCode -Data $data
    } catch {
        New-WinCareResult `
            -Success $false -Status Blocked -Code 'WasiPluginFailed' `
            -Message $_.Exception.Message -ExitCode 1 `
            -Data @{Plugin=$pluginFull;PluginSha256=$hash;EvidenceType='PluginAdmission'}
    }
}

function Get-WinCareWasiMarketplaceCatalog {
    [CmdletBinding()]
    param([string]$CatalogPath)
    if(-not $CatalogPath) {
        $CatalogPath=Join-Path $PSScriptRoot '..\..\..\config\wasi-marketplace.json'
    }
    $full=[IO.Path]::GetFullPath($CatalogPath)
    if(-not (Test-Path -LiteralPath $full -PathType Leaf)) {
        return New-WinCareResult `
            -Success $true -Code 'WasiCatalogEmpty' `
            -Message 'No local WASI marketplace catalog is configured.' `
            -Data @{CatalogPath=$full;Entries=@();EvidenceType='LocalCatalogProbe'}
    }
    try {
        $catalog=Read-WinCareBoundedJson -LiteralPath $full -MaximumBytes 1048576 -Depth 16
        if([int]$catalog.SchemaVersion -ne 1) { throw 'Unsupported WASI catalog schema.' }
        $entries=foreach($entry in @($catalog.Entries)) {
            $available=Test-Path -LiteralPath $entry.Path -PathType Leaf
            $observed=if($available) {
                (Get-FileHash -LiteralPath $entry.Path -Algorithm SHA256).Hash.ToLowerInvariant()
            } else {
                $null
            }
            [pscustomobject]@{
                Id=$entry.Id
                Version=$entry.Version
                Category=$entry.Category
                Path=$entry.Path
                ExpectedSha256=$entry.Sha256
                ObservedSha256=$observed
                Available=$available
                IntegrityValid=($available -and $observed -eq ([string]$entry.Sha256).ToLowerInvariant())
            }
        }
        $status=if(@($entries|Where-Object{-not $_.IntegrityValid}).Count) {
            'SucceededWithWarnings'
        } else {
            'Succeeded'
        }
        New-WinCareResult `
            -Success $true -Status $status -Code 'WasiCatalogLoaded' `
            -Message 'The local WASI catalog was loaded and each available plugin digest was checked.' `
            -Data @{
                CatalogPath=$full
                CatalogSha256=(Get-FileHash $full -Algorithm SHA256).Hash.ToLowerInvariant()
                Entries=@($entries)
                EvidenceType='VerifiedLocalPluginCatalog'
            }
    } catch {
        New-WinCareResult `
            -Success $false -Code 'WasiCatalogInvalid' `
            -Message $_.Exception.Message -ExitCode 65
    }
}
