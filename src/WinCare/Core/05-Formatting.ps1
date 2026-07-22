function Format-WinCareBytes {
    [CmdletBinding()]
    param([long]$Bytes)
    if ($Bytes -ge 1TB) { return '{0:N2} TB' -f ($Bytes / 1TB) }
    if ($Bytes -ge 1GB) { return '{0:N2} GB' -f ($Bytes / 1GB) }
    if ($Bytes -ge 1MB) { return '{0:N1} MB' -f ($Bytes / 1MB) }
    if ($Bytes -ge 1KB) { return '{0:N0} KB' -f ($Bytes / 1KB) }
    return "$Bytes B"
}

function Limit-WinCareText {
    param([AllowNull()][string]$Text,[int]$Width)
    if ($Width -le 0) { return '' }
    if ([string]::IsNullOrEmpty($Text)) { return '' }
    if ($Text.Length -le $Width) { return $Text.PadRight($Width) }
    if ($Width -le 1) { return $Text.Substring(0,$Width) }
    return $Text.Substring(0,$Width - 1) + '…'
}

function ConvertTo-WinCareSafeFileName {
    param([Parameter(Mandatory)][string]$Name)
    $invalid = [IO.Path]::GetInvalidFileNameChars()
    $chars = $Name.ToCharArray() | ForEach-Object { if ($invalid -contains $_) {'_'} else {$_} }
    -join $chars
}

function Get-WinCarePropertyValue {
    [CmdletBinding()]
    param(
        [AllowNull()][object]$InputObject,
        [Parameter(Mandatory)][string]$Name,
        [object]$Default=$null
    )
    if ($null -eq $InputObject) { return $Default }
    $property=$InputObject.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return $Default }
    return $property.Value
}
