#requires -Version 7.2

function Initialize-WinCareNativeRuntime {
    [CmdletBinding()]
    param(
        [string[]]$RequiredTypes = @(),
        [switch]$ThrowOnMissing
    )

    if (-not $IsWindows) {
        if ($ThrowOnMissing) { throw 'The WinCare native runtime is available only on Windows.' }
        return $false
    }

    $assemblyPath = [IO.Path]::GetFullPath((Join-Path $script:WinCareModuleRoot 'bin\WinCare.Native.dll'))
    $loadedTypes = @($RequiredTypes | Where-Object { $_ -as [type] })
    if ($loadedTypes.Count -eq $RequiredTypes.Count -and $RequiredTypes.Count -gt 0) {
        foreach ($typeName in $RequiredTypes) {
            $type = $typeName -as [type]
            $location = [IO.Path]::GetFullPath($type.Assembly.Location)
            if (-not $location.Equals($assemblyPath, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Native type $typeName was loaded from an untrusted assembly location: $location"
            }
        }
        return $true
    }

    if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
        if ($ThrowOnMissing) { throw "The source-built native assembly is missing: $assemblyPath" }
        return $false
    }

    $item = Get-Item -LiteralPath $assemblyPath -Force -ErrorAction Stop
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "The native assembly is not a regular non-reparse file: $assemblyPath"
    }
    if ([long]$item.Length -lt 1024 -or [long]$item.Length -gt 67108864) {
        throw "The native assembly size is outside the supported range: $assemblyPath"
    }

    $identity = [Reflection.AssemblyName]::GetAssemblyName($assemblyPath)
    if ($identity.Name -ne 'WinCare.Native') {
        throw "Unexpected native assembly identity '$($identity.Name)': $assemblyPath"
    }

    Add-Type -Path $assemblyPath -ErrorAction Stop
    foreach ($typeName in $RequiredTypes) {
        $type = $typeName -as [type]
        if (-not $type) {
            throw "The native assembly does not expose required type $typeName."
        }
        $location = [IO.Path]::GetFullPath($type.Assembly.Location)
        if (-not $location.Equals($assemblyPath, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Native type $typeName resolved from an unexpected assembly: $location"
        }
    }

    return $true
}
