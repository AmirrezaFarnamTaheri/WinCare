#requires -Version 7.2

function Resolve-WinCareNativeAssemblyPath {
    <#
        Resolve the trusted location of the source-built native assembly.

        Both the developer build (Native/Build-WinCareNativePolyglot.ps1 defaults
        -OutputDirectory to '<repo>/bin') and the shipped package
        (tools/build_release.py writes every native artifact under 'bin/') place
        WinCare.Native.dll one level above the module directory, not inside it.
        $script:WinCareModuleRoot is '<root>/src/WinCare', so the package-relative
        location is '<root>/bin'. A module-local '<root>/src/WinCare/bin' is still
        accepted as a fallback so a side-by-side layout keeps working.

        Returns the first candidate that exists; when none exists it returns the
        package-relative path so callers report the canonical expected location.
        The single resolved path is used both to load the assembly and to verify
        every resolved type's Assembly.Location, so the trust check stays exact.
    #>
    [CmdletBinding()]
    param()

    $relative = 'bin\WinCare.Native.dll'
    $moduleRoot = $script:WinCareModuleRoot
    $candidates = [Collections.Generic.List[string]]::new()
    $packageRoot = Split-Path (Split-Path $moduleRoot -Parent) -Parent
    if ($packageRoot) {
        $candidates.Add([IO.Path]::GetFullPath((Join-Path $packageRoot $relative)))
    }
    $candidates.Add([IO.Path]::GetFullPath((Join-Path $moduleRoot $relative)))

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    return $candidates[0]
}

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

    $assemblyPath = Resolve-WinCareNativeAssemblyPath
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
