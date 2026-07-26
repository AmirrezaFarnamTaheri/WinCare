<#
.SYNOPSIS
Reads local Group Policy .pol files through the source-built native policy parser.
#>
function Get-WinCarePolicyFileEntries {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$PolFilePath)
    try{$path=(Resolve-Path -LiteralPath $PolFilePath -ErrorAction Stop).Path}catch{return New-WinCareResult -Success $false -Status Failed -Code 'PolicyFileNotFound' -Message $_.Exception.Message -ExitCode 2}
    if(-not ('WinCare.Native.PolicyEngine' -as [type])){return New-WinCareResult -Success $false -Status Blocked -Code 'NativePolicyEngineUnavailable' -Message 'The source-built WinCare.Native.PolicyEngine assembly is not loaded.' -ExitCode 78 -Data @{Path=$path;Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}}
    try{
        $hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant();$entries=@([WinCare.Native.PolicyEngine]::LoadPolFile($path))
        New-WinCareResult -Success $true -Code 'PolicyFileParsed' -Message 'The policy file was parsed by the native policy engine.' -Data @{Path=$path;Sha256=$hash;Entries=$entries;Count=$entries.Count;EvidenceType='FileSha256AndNativeParserOutput'}
    }catch{New-WinCareResult -Success $false -Status Failed -Code 'PolicyFileParseFailed' -Message $_.Exception.Message -ExitCode 1}
}
if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Get-WinCarePolicyFileEntries }
