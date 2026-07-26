function ConvertTo-WinCareCanonicalValueInternal {
    [CmdletBinding()]
    param(
        [AllowNull()][object]$Value,
        [Parameter(Mandatory)][hashtable]$State,
        [ValidateRange(0,80)][int]$Depth=0
    )
    if($null -eq $Value) { return $null }
    if($Depth -gt $State.MaximumDepth) {
        throw "Canonical serialization exceeded the maximum object depth of $($State.MaximumDepth)."
    }
    $State.Items++
    if($State.Items -gt $State.MaximumItems) {
        throw "Canonical serialization exceeded the $($State.MaximumItems)-item ceiling."
    }
    if($Value -is [string]) {
        if($Value.Length -gt $State.MaximumStringCharacters) {
            throw "Canonical serialization encountered a string longer than $($State.MaximumStringCharacters) characters."
        }
        return $Value
    }
    if($Value -is [datetime]) { return $Value.ToUniversalTime().ToString('o') }
    if($Value -is [datetimeoffset]) { return $Value.ToUniversalTime().ToString('o') }
    if($Value -is [guid]) { return $Value.ToString('D').ToLowerInvariant() }
    if($Value -is [timespan]) { return $Value.ToString('c') }
    if($Value -is [enum]) { return $Value.ToString() }
    if(
        $Value -is [char] -or
        $Value -is [bool] -or
        $Value.GetType().IsPrimitive -or
        $Value -is [decimal]
    ) { return $Value }

    $trackReference=-not $Value.GetType().IsValueType
    if($trackReference -and -not $State.Seen.Add($Value)) {
        throw 'Canonical serialization encountered a reference cycle.'
    }
    try {
        if($Value -is [Collections.IDictionary]) {
            $pairs=[Collections.Generic.List[object]]::new()
            $canonicalKeys=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            $count=0
            foreach($originalKey in $Value.Keys) {
                $count++
                if($count -gt $State.MaximumProperties) {
                    throw "Canonical serialization exceeded the $($State.MaximumProperties)-property ceiling."
                }
                $key=[string]$originalKey
                if($key.Length -gt $State.MaximumKeyCharacters) {
                    throw "Canonical serialization encountered a key longer than $($State.MaximumKeyCharacters) characters."
                }
                if(-not $canonicalKeys.Add($key)) {
                    throw "Canonical serialization encountered duplicate key text: $key"
                }
                $pairs.Add([pscustomobject]@{Key=$key;OriginalKey=$originalKey})
            }
            $ordered=[ordered]@{}
            foreach($pair in @($pairs | Sort-Object Key -CaseSensitive)) {
                $ordered[$pair.Key]=ConvertTo-WinCareCanonicalValueInternal `
                    -Value $Value[$pair.OriginalKey] `
                    -State $State `
                    -Depth ($Depth+1)
            }
            return $ordered
        }
        if($Value -is [Collections.IEnumerable]) {
            $items=[Collections.Generic.List[object]]::new()
            $count=0
            foreach($item in $Value) {
                $count++
                if($count -gt $State.MaximumCollectionItems) {
                    throw "Canonical serialization exceeded the $($State.MaximumCollectionItems)-element collection ceiling."
                }
                $items.Add((ConvertTo-WinCareCanonicalValueInternal -Value $item -State $State -Depth ($Depth+1)))
            }
            return @($items)
        }
        $properties=@(
            $Value.PSObject.Properties |
                Where-Object { $_.MemberType -in @('NoteProperty','Property','AliasProperty') } |
                Sort-Object Name -CaseSensitive
        )
        if($properties.Count -eq 0) {
            $scalar=[string]$Value
            if($scalar.Length -gt $State.MaximumStringCharacters) {
                throw "Canonical serialization encountered scalar text longer than $($State.MaximumStringCharacters) characters."
            }
            return $scalar
        }
        if($properties.Count -gt $State.MaximumProperties) {
            throw "Canonical serialization exceeded the $($State.MaximumProperties)-property ceiling."
        }
        $object=[ordered]@{}
        foreach($property in $properties) {
            if($property.Name.Length -gt $State.MaximumKeyCharacters) {
                throw "Canonical serialization encountered a property name longer than $($State.MaximumKeyCharacters) characters."
            }
            $object[$property.Name]=ConvertTo-WinCareCanonicalValueInternal `
                -Value $property.Value `
                -State $State `
                -Depth ($Depth+1)
        }
        return $object
    } finally {
        if($trackReference) { $null=$State.Seen.Remove($Value) }
    }
}

function ConvertTo-WinCareCanonicalValue {
    [CmdletBinding()]
    param(
        [AllowNull()][object]$Value,
        [ValidateRange(0,80)][int]$Depth=0,
        [ValidateRange(1,1000000)][int]$MaximumItems=100000,
        [ValidateRange(1,100000)][int]$MaximumProperties=10000,
        [ValidateRange(1,1000000)][int]$MaximumCollectionItems=100000,
        [ValidateRange(1,1048576)][int]$MaximumStringCharacters=262144,
        [ValidateRange(1,32768)][int]$MaximumKeyCharacters=1024
    )
    $state=@{
        Items=0
        MaximumDepth=80
        MaximumItems=$MaximumItems
        MaximumProperties=$MaximumProperties
        MaximumCollectionItems=$MaximumCollectionItems
        MaximumStringCharacters=$MaximumStringCharacters
        MaximumKeyCharacters=$MaximumKeyCharacters
        Seen=[Collections.Generic.HashSet[object]]::new(
            [Collections.Generic.ReferenceEqualityComparer]::Instance
        )
    }
    ConvertTo-WinCareCanonicalValueInternal -Value $Value -State $state -Depth $Depth
}

function ConvertTo-WinCareCanonicalJson {
    [CmdletBinding()]
    param(
        [AllowNull()][object]$InputObject,
        [ValidateRange(2,80)][int]$Depth=80,
        [ValidateRange(1024,268435456)][long]$MaximumOutputBytes=16777216,
        [ValidateRange(1,1000000)][int]$MaximumItems=100000,
        [ValidateRange(1,100000)][int]$MaximumProperties=10000,
        [ValidateRange(1,1000000)][int]$MaximumCollectionItems=100000,
        [ValidateRange(1,1048576)][int]$MaximumStringCharacters=262144
    )
    $canonical=ConvertTo-WinCareCanonicalValue `
        -Value $InputObject `
        -MaximumItems $MaximumItems `
        -MaximumProperties $MaximumProperties `
        -MaximumCollectionItems $MaximumCollectionItems `
        -MaximumStringCharacters $MaximumStringCharacters
    $json=$canonical | ConvertTo-Json -Compress -Depth $Depth
    $length=[Text.Encoding]::UTF8.GetByteCount($json)
    if($length -gt $MaximumOutputBytes) {
        throw "Canonical JSON exceeded the $MaximumOutputBytes-byte output ceiling."
    }
    return $json
}

function Get-WinCareCanonicalObjectHash {
    [CmdletBinding()]
    param(
        [AllowNull()][object]$InputObject,
        [ValidateRange(1024,268435456)][long]$MaximumOutputBytes=16777216
    )
    Get-WinCareSha256Text -Text (
        ConvertTo-WinCareCanonicalJson -InputObject $InputObject -Depth 80 -MaximumOutputBytes $MaximumOutputBytes
    )
}
