# tools/Invoke-BoundedFileRead.ps1
# T6.1: Canonical Read-BoundedUtf8Text implementation, extracted from 5 verbatim copies
# across release.yml (3×), recover-release.yml (2×), and windows-release-validation.yml (1×).
#
# Usage: . (Join-Path $PSScriptRoot '../tools/Invoke-BoundedFileRead.ps1')
#        $text = Read-BoundedUtf8Text -LiteralPath $path -MaximumBytes 16777216
#
# Contract:
#   - Rejects directories, reparse points, and files exceeding MaximumBytes.
#   - Reads the full file into a byte array; verifies length is unchanged after read.
#   - Decodes as strict UTF-8 (no BOM emitted; throws on invalid UTF-8).
#   - Clears the byte buffer before returning to limit secret exposure window.
#   - Returns the decoded string on success; throws on any violation.

function Read-BoundedUtf8Text {
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [ValidateRange(1,16777216)][int]$MaximumBytes
    )
    $item = Get-Item -LiteralPath $LiteralPath -Force -ErrorAction Stop
    if ($item.PSIsContainer -or
        ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
        [long]$item.Length -gt $MaximumBytes) {
        throw "Unsafe or oversized text file: $LiteralPath"
    }
    $bytes = [IO.File]::ReadAllBytes($item.FullName)
    try {
        if ($bytes.Length -ne [long]$item.Length) {
            throw "Text file changed while reading: $LiteralPath"
        }
        [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
    } finally {
        if ($bytes.Length) { [Array]::Clear($bytes, 0, $bytes.Length) }
    }
}
