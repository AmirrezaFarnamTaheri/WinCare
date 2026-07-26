function Initialize-WinCareCommandLineInterop {
    [CmdletBinding()]
    param()
    if (-not $IsWindows) { return $false }
    return Initialize-WinCareNativeRuntime -RequiredTypes @('WinCare.Native.CommandLine')
}

function Split-WinCareCommandLine {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$CommandLine)
    if ([string]::IsNullOrWhiteSpace($CommandLine)) { return @() }
    if ($IsWindows) {
        if (-not (Initialize-WinCareCommandLineInterop)) {
            throw 'The source-built WinCare command-line parser is unavailable.'
        }
        return [WinCare.Native.CommandLine]::Split($CommandLine)
    }
    if ($CommandLine -match '^\s*"([^"]+)"\s*(.*)$') {
        return @($Matches[1]) + @($Matches[2] -split '\s+' | Where-Object { $_ })
    }
    return @($CommandLine -split '\s+' | Where-Object { $_ })
}

function Get-WinCareMsiProductCode {
    [CmdletBinding()]
    param([AllowNull()][string]$Text)
    if ($Text -and $Text -match '(?i)\{[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\}') {
        return $Matches[0].ToUpperInvariant()
    }
    return $null
}

function Test-WinCareUnsafeCommandHost {
    param([string]$FilePath)
    if ([string]::IsNullOrWhiteSpace($FilePath)) { return $true }
    $name = [IO.Path]::GetFileName($FilePath).ToLowerInvariant()
    return $name -in @('cmd.exe','powershell.exe','pwsh.exe','wscript.exe','cscript.exe','mshta.exe')
}

function ConvertTo-WinCareUninstallInvocation {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Application)

    $productCode = Get-WinCareMsiProductCode -Text ([string]$Application.ProductCode)
    if (-not $productCode) { $productCode = Get-WinCareMsiProductCode -Text ([string]$Application.UninstallString) }
    if ($Application.InstallerType -eq 'MSI' -or $productCode) {
        if (-not $productCode) { return $null }
        return [pscustomobject]@{
            FilePath = 'msiexec.exe'
            Arguments = @('/x', $productCode, '/passive', '/norestart')
            RequiresAdmin = $Application.Scope -eq 'Machine'
            SuccessExitCodes = @(0,1641,3010)
        }
    }

    $raw = if ($Application.QuietUninstallString) { $Application.QuietUninstallString } else { $Application.UninstallString }
    if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
    $raw=[Environment]::ExpandEnvironmentVariables([string]$raw)
    $parts = @(Split-WinCareCommandLine -CommandLine $raw)
    if ($parts.Count -lt 1 -or (Test-WinCareUnsafeCommandHost $parts[0])) { return $null }
    [pscustomobject]@{
        FilePath = $parts[0]
        Arguments = @($parts | Select-Object -Skip 1)
        RequiresAdmin = $Application.Scope -eq 'Machine'
        SuccessExitCodes = @(0)
    }
}

function Get-WinCareRegistryApplication {
    [CmdletBinding()]
    param()
    if (-not $IsWindows) { return @() }

    $locations = @(
        @{ Path='HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*'; Scope='Machine'; Architecture='64-bit' },
        @{ Path='HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'; Scope='Machine'; Architecture='32-bit' },
        @{ Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*'; Scope='User'; Architecture='Current' }
    )
    $items=[System.Collections.Generic.List[object]]::new()
    foreach ($location in $locations) {
        foreach ($record in Get-ItemProperty -Path $location.Path -ErrorAction SilentlyContinue) {
            $displayName=[string](Get-WinCarePropertyValue $record 'DisplayName' '')
            $systemComponent=[int](Get-WinCarePropertyValue $record 'SystemComponent' 0)
            if ([string]::IsNullOrWhiteSpace($displayName) -or $systemComponent -eq 1) { continue }
            $childName=[string](Get-WinCarePropertyValue $record 'PSChildName' '')
            $uninstallString=[string](Get-WinCarePropertyValue $record 'UninstallString' '')
            $productCode=Get-WinCareMsiProductCode -Text $childName
            if (-not $productCode) { $productCode=Get-WinCareMsiProductCode -Text $uninstallString }
            $windowsInstaller=[int](Get-WinCarePropertyValue $record 'WindowsInstaller' 0)
            $installerType=if ($windowsInstaller -eq 1 -or $productCode) {'MSI'} else {'Registry'}
            $estimatedSize=[long](Get-WinCarePropertyValue $record 'EstimatedSize' 0)
            $quiet=[string](Get-WinCarePropertyValue $record 'QuietUninstallString' '')
            $modify=[string](Get-WinCarePropertyValue $record 'ModifyPath' '')
            $items.Add([pscustomobject]@{
                PSTypeName           = 'WinCare.Application'
                Id                   = "registry:$($location.Scope):$childName"
                Name                 = $displayName
                Version              = [string](Get-WinCarePropertyValue $record 'DisplayVersion' '')
                Publisher            = [string](Get-WinCarePropertyValue $record 'Publisher' '')
                EstimatedBytes       = $estimatedSize * 1KB
                InstallDate          = [string](Get-WinCarePropertyValue $record 'InstallDate' '')
                InstallLocation      = [string](Get-WinCarePropertyValue $record 'InstallLocation' '')
                Scope                = $location.Scope
                Architecture         = $location.Architecture
                InstallerType        = $installerType
                ProductCode          = $productCode
                UninstallString      = $uninstallString
                QuietUninstallString = $quiet
                ModifyPath           = $modify
                RepairSupported      = [bool]($modify -or $productCode)
                ResetSupported       = $false
                UninstallSupported   = [bool]($uninstallString -or $quiet -or $productCode)
                IsSystem             = $false
                Source               = 'Registry'
                PackageFullName      = $null
                PackageFamilyName    = $null
                NonRemovable         = $false
            })
        }
    }
    return @($items)
}

function Get-WinCareAppxApplication {
    [CmdletBinding()]
    param([switch]$IncludeSystem)
    if (-not (Test-WinCareCapability 'Get-AppxPackage')) { return @() }
    try {
        Get-AppxPackage -ErrorAction Stop |
            Where-Object {
                $isFramework=[bool](Get-WinCarePropertyValue $_ 'IsFramework' $false)
                $nonRemovable=[bool](Get-WinCarePropertyValue $_ 'NonRemovable' $false)
                $IncludeSystem -or (-not $isFramework -and -not $nonRemovable)
            } |
            ForEach-Object {
                $name=[string](Get-WinCarePropertyValue $_ 'Name' '')
                $family=[string](Get-WinCarePropertyValue $_ 'PackageFamilyName' '')
                $fullName=[string](Get-WinCarePropertyValue $_ 'PackageFullName' '')
                $nonRemovable=[bool](Get-WinCarePropertyValue $_ 'NonRemovable' $false)
                $isFramework=[bool](Get-WinCarePropertyValue $_ 'IsFramework' $false)
                [pscustomobject]@{
                    PSTypeName           = 'WinCare.Application'
                    Id                   = "appx:$fullName"
                    Name                 = if ($name) {$name} else {$family}
                    Version              = [string](Get-WinCarePropertyValue $_ 'Version' '')
                    Publisher            = [string](Get-WinCarePropertyValue $_ 'PublisherDisplayName' (Get-WinCarePropertyValue $_ 'Publisher' ''))
                    EstimatedBytes       = 0L
                    InstallDate          = ''
                    InstallLocation      = [string](Get-WinCarePropertyValue $_ 'InstallLocation' '')
                    Scope                = 'User'
                    Architecture         = [string](Get-WinCarePropertyValue $_ 'Architecture' '')
                    InstallerType        = 'AppX/MSIX'
                    ProductCode          = $null
                    UninstallString      = $null
                    QuietUninstallString = $null
                    ModifyPath           = $null
                    RepairSupported      = $false
                    ResetSupported       = [bool](Test-WinCareCapability 'Reset-AppxPackage') -and -not $nonRemovable
                    UninstallSupported   = -not $nonRemovable
                    IsSystem             = $isFramework
                    Source               = 'AppX'
                    PackageFullName      = $fullName
                    PackageFamilyName    = $family
                    NonRemovable         = $nonRemovable
                }
            }
    } catch {
        Write-WinCareLog -Level Warning -Message 'AppX inventory failed.' -Data @{ error = $_.Exception.Message }
        @()
    }
}
function Get-WinCareInstalledApplication {
    [CmdletBinding()]
    param(
        [string]$Search,
        [switch]$IncludeSystem,
        [switch]$IncludeStoreApps,
        [switch]$Refresh
    )
    Ensure-WinCareState
    $includeStore = if ($PSBoundParameters.ContainsKey('IncludeStoreApps')) { [bool]$IncludeStoreApps } else { [bool](Get-WinCareConfig 'IncludeStoreApps') }
    $cacheKey = "apps:$([bool]$IncludeSystem):$includeStore"
    if (-not $Refresh -and $script:WinCareState.ContainsKey($cacheKey)) {
        $apps = $script:WinCareState[$cacheKey]
    } else {
        $all = @()
        $all += @(Get-WinCareRegistryApplication)
        if ($includeStore) { $all += @(Get-WinCareAppxApplication -IncludeSystem:$IncludeSystem) }
        $dedup = @{}
        foreach ($app in $all) {
            $key = "{0}|{1}|{2}|{3}" -f $app.Name.ToLowerInvariant(),$app.Version,$app.Scope,$app.InstallerType
            if (-not $dedup.ContainsKey($key)) { $dedup[$key] = $app }
        }
        $apps = @($dedup.Values | Sort-Object Name,Version)
        $script:WinCareState[$cacheKey] = $apps
    }
    if ($Search) { return @($apps | Where-Object { $_.Name -like "*$Search*" -or $_.Publisher -like "*$Search*" }) }
    return @($apps)
}

function New-WinCareUninstallPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object[]]$Applications)
    $plan = New-WinCarePlan -Title "Uninstall $($Applications.Count) application(s)" -Description 'Uses each application native uninstall mechanism and verifies removal.'
    foreach ($app in $Applications) {
        if (-not $app.UninstallSupported) { continue }
        if ($app.InstallerType -eq 'AppX/MSIX') {
            $action = New-WinCareAction -Type 'RemoveAppxPackage' -Label "Uninstall $($app.Name)" -Risk Moderate -Parameters @{
                PackageFullName = $app.PackageFullName; Name = $app.Name
            } -Reversible $false -Verification 'Package is no longer returned by Get-AppxPackage.'
        } else {
            $invocation = ConvertTo-WinCareUninstallInvocation -Application $app
            if (-not $invocation) { continue }
            $action = New-WinCareAction -Type 'UninstallRegistryApp' -Label "Uninstall $($app.Name)" -Risk Moderate -Parameters @{
                ApplicationId = $app.Id; Name = $app.Name; FilePath = $invocation.FilePath; Arguments = @($invocation.Arguments); SuccessExitCodes = @($invocation.SuccessExitCodes)
            } -RequiresAdmin $invocation.RequiresAdmin -EstimatedBytes $app.EstimatedBytes -Verification 'Application identity is absent from refreshed inventory.'
        }
        $null = Add-WinCarePlanAction -Plan $plan -Action $action
    }
    return $plan
}

function New-WinCareRepairPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Application)
    if (-not $Application.RepairSupported) { return $null }
    $productCode = Get-WinCareMsiProductCode -Text $Application.ProductCode
    if ($productCode) {
        $file = 'msiexec.exe'; $args = @('/fa',$productCode,'/passive','/norestart')
    } elseif ($Application.ModifyPath) {
        $parts = @(Split-WinCareCommandLine ([Environment]::ExpandEnvironmentVariables([string]$Application.ModifyPath)))
        if ($parts.Count -lt 1 -or (Test-WinCareUnsafeCommandHost $parts[0])) { return $null }
        $file = $parts[0]; $args = @($parts | Select-Object -Skip 1)
    } else { return $null }
    $successExitCodes = if ($productCode) { @(0,1641,3010) } else { @(0) }
    $action = New-WinCareAction -Type 'RepairApplication' -Label "Repair $($Application.Name)" -Risk Moderate -Parameters @{
        Name=$Application.Name; FilePath=$file; Arguments=$args; SuccessExitCodes=$successExitCodes
    } -RequiresAdmin ($Application.Scope -eq 'Machine') -Verification 'Repair process exits successfully.'
    New-WinCarePlan -Title "Repair $($Application.Name)" -Actions @($action)
}

function New-WinCareResetAppxPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Application)
    if (-not $Application.ResetSupported -or -not $Application.PackageFullName) { return $null }
    $action = New-WinCareAction -Type 'ResetAppxPackage' -Label "Reset $($Application.Name) application data" -Risk High -Parameters @{
        PackageFullName=$Application.PackageFullName; Name=$Application.Name
    } -Reversible $false -Verification 'Reset-AppxPackage completes and the package remains registered.'
    New-WinCarePlan -Title "Reset $($Application.Name)" -Description 'Deletes application-local data and restores the package to its initial state. Sign-in and settings may be lost.' -Actions @($action)
}

function Invoke-WinCareRegistryUninstallAction {
    param([object]$Action)
    $result = Invoke-WinCareProcess -FilePath $Action.Parameters.FilePath -ArgumentList @($Action.Parameters.Arguments) -RequireAdmin:$Action.RequiresAdmin -TimeoutSeconds 7200 -SuccessExitCodes @($Action.Parameters.SuccessExitCodes)
    if (-not $result.Success) { return $result }
    $remaining=$null
    for ($attempt=0; $attempt -lt 20; $attempt++) {
        # ponytail: O(1) package removal profile hashtable lookup -> in-memory dictionary cache
        $remaining = Get-WinCareInstalledApplication -Refresh | Where-Object Id -eq $Action.Parameters.ApplicationId
        if (-not $remaining) { break }
        Start-Sleep -Milliseconds 500
    }
    if ($remaining) { return New-WinCareResult -Success $false -Message 'The uninstaller exited, but the application is still registered after verification.' -ExitCode 1 -Data $result.Data }
    New-WinCareResult -Success $true -Message "$($Action.Parameters.Name) was uninstalled." -Data $result.Data
}

function Invoke-WinCareAppxRemoveAction {
    param([object]$Action)
    try {
        Remove-AppxPackage -Package $Action.Parameters.PackageFullName -ErrorAction Stop
        $remaining = Get-AppxPackage -PackageTypeFilter Main,Bundle -ErrorAction SilentlyContinue |
            Where-Object PackageFullName -eq $Action.Parameters.PackageFullName
        New-WinCareResult -Success (-not $remaining) -Message $(if ($remaining) {'Package still appears installed.'} else {'App package removed.'})
    } catch { New-WinCareResult -Success $false -Message $_.Exception.Message -ExitCode 1 }
}

function Invoke-WinCareRepairAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $result = Invoke-WinCareProcess -FilePath $Action.Parameters.FilePath -ArgumentList @($Action.Parameters.Arguments) -RequireAdmin:$Action.RequiresAdmin -TimeoutSeconds 7200 -SuccessExitCodes @($Action.Parameters.SuccessExitCodes)
    if (-not $result.Success) { return $result }
    New-WinCareResult -Success $true -Status $result.Status -Code 'ApplicationRepairProcessCompleted' -Message "The repair process for $($Action.Parameters.Name) completed with an accepted exit code." -ExitCode $result.ExitCode -RestartRequired $result.RestartRequired -Warnings @($result.Warnings) -Data @{
        ApplicationName = [string]$Action.Parameters.Name
        FilePath = [string]$Action.Parameters.FilePath
        Process = $result.Data
        EvidenceType = 'RepairProcessExitAndInvocation'
    }
}


function Invoke-WinCareAppxResetAction {
    param([object]$Action)
    if (-not (Get-Command Reset-AppxPackage -ErrorAction SilentlyContinue)) {
        return New-WinCareResult -Success $false -Message 'Reset-AppxPackage is unavailable on this Windows version.' -ExitCode 127
    }
    try {
        $package = Get-AppxPackage -ErrorAction Stop | Where-Object PackageFullName -eq $Action.Parameters.PackageFullName | Select-Object -First 1
        if (-not $package) { return New-WinCareResult -Success $false -Message 'The application package is no longer installed.' -ExitCode 2 }
        $package | Reset-AppxPackage -ErrorAction Stop
        $remaining = Get-AppxPackage -ErrorAction SilentlyContinue | Where-Object PackageFullName -eq $Action.Parameters.PackageFullName
        New-WinCareResult -Success ([bool]$remaining) -Message $(if ($remaining) {'Application data was reset.'} else {'Reset completed, but package verification failed.'})
    } catch { New-WinCareResult -Success $false -Message $_.Exception.Message -ExitCode 1 }
}


