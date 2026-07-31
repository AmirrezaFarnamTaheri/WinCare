#requires -Version 7.2

function Test-WinCareStrictObjectKeys {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$InputObject,
        [Parameter(Mandatory)][string[]]$AllowedKeys,
        [string[]]$RequiredKeys=@(),
        [string]$Context='object'
    )
    $keys=if($InputObject -is [Collections.IDictionary]){
        @($InputObject.Keys|ForEach-Object{[string]$_})
    }else{
        @($InputObject.PSObject.Properties.Name)
    }
    $unknown=@($keys|Where-Object{$_ -notin $AllowedKeys})
    if($unknown.Count -gt 0){throw "Unknown $Context field(s): $($unknown -join ', ')"}
    $missing=@($RequiredKeys|Where-Object{$_ -notin $keys})
    if($missing.Count -gt 0){throw "Missing required $Context field(s): $($missing -join ', ')"}
    return $true
}
