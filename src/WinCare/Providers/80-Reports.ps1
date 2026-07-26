<#
.SYNOPSIS
Builds a deterministic report export plan from observed WinCare report data.
#>
function Export-WinCareReport {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path,[ValidateSet('Markdown','Html')][string]$Format='Markdown',[switch]$Apply,[switch]$Approved,[switch]$PreviewOnly)
    $full=[IO.Path]::GetFullPath($Path);$parent=Split-Path -Parent $full
    if([string]::IsNullOrWhiteSpace($parent)){return New-WinCareResult -Success $false -Status Blocked -Code 'ReportParentRequired' -Message 'The report path must include a parent directory.' -ExitCode 22}
    $data=if(Get-Command New-WinCareSystemReportData -ErrorAction SilentlyContinue){New-WinCareSystemReportData}else{[ordered]@{GeneratedAt=[datetime]::UtcNow.ToString('o');ComputerName=[Environment]::MachineName;OperatingSystem=[Runtime.InteropServices.RuntimeInformation]::OSDescription;EvidenceType='RuntimeObservation'}}
    if($Format -eq 'Markdown'){
        $json=$data|ConvertTo-Json -Depth 30
        $content="# WinCare Diagnostic Report`n`n- Generated (UTC): $([datetime]::UtcNow.ToString('o'))`n- Computer: $([Environment]::MachineName)`n- Evidence: observed WinCare provider output`n`n## Structured evidence`n`n``````json`n$json`n``````n"
    }else{
        $json=[Net.WebUtility]::HtmlEncode(($data|ConvertTo-Json -Depth 30))
        $h1='<!doctype html><html><head><meta charset="utf-8"><title>WinCare Diagnostic Report</title></head><body><h1>WinCare Diagnostic Report</h1><p>Generated UTC: '
        $h2='</p><p>Computer: '
        $h3='</p><h2>Structured evidence</h2><pre>'
        $h4='</pre></body></html>'
        $content=$h1 + [datetime]::UtcNow.ToString('o') + $h2 + [Net.WebUtility]::HtmlEncode([Environment]::MachineName) + $h3 + $json + $h4
    }
    $bytes=[Text.Encoding]::UTF8.GetBytes($content);$encoded=[Convert]::ToBase64String($bytes);$expected=if(Test-Path -LiteralPath $full){(Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()}else{''}
    $action=New-WinCareAction -Type 'WriteManagedFile' -Label "Export WinCare $Format report" -Risk Low -Parameters @{Path=$full;ContentBase64=$encoded;ExpectedBeforeHash=$expected;AllowedRoots=@($parent)} -Reversible $true -Verification 'The report file must be byte-identical to the planned content.'
    $plan=New-WinCarePlan -Title "Export WinCare $Format report" -Actions @($action) -Metadata @{Path=$full;Format=$Format;ContentSha256=([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))).ToLowerInvariant();EvidenceType='ObservedReportDataAndContentHash'}
    if(-not $Apply -and -not $PreviewOnly){return $plan};Invoke-WinCarePlan -Plan $plan -Approved:$Approved -PreviewOnly:$PreviewOnly
}
if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Export-WinCareReport }
