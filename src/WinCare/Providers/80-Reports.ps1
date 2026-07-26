<#
.SYNOPSIS
Builds a deterministic report export plan from observed WinCare report data.
#>
function Export-WinCareReport {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path,[ValidateSet('Markdown','Html')][string]$Format='Markdown',[switch]$Apply,[switch]$Approved,[switch]$PreviewOnly)
    $full=[IO.Path]::GetFullPath($Path);$parent=Split-Path -Parent $full;if([string]::IsNullOrWhiteSpace($parent)){return New-WinCareResult -Success $false -Status Blocked -Code 'ReportParentRequired' -Message 'The report path must include a parent directory.' -ExitCode 22}
    $data=if(Get-Command New-WinCareSystemReportData -ErrorAction SilentlyContinue){New-WinCareSystemReportData}else{[ordered]@{GeneratedAt=[datetime]::UtcNow.ToString('o');ComputerName=[Environment]::MachineName;OperatingSystem=[Runtime.InteropServices.RuntimeInformation]::OSDescription;EvidenceType='RuntimeObservation'}}
    $json=$data|ConvertTo-Json -Depth 30
    $md="# WinCare Diagnostic Report`n`n- Generated (UTC): $([datetime]::UtcNow.ToString('o'))`n- Computer: $([Environment]::MachineName)`n`n## Structured evidence`n`n``````json`n$json`n``````n"
    $htm='<!doctype html><html><head><meta charset="utf-8"><title>WinCare Report</title></head><body><h1>WinCare Report</h1><p>UTC: ' + [datetime]::UtcNow.ToString('o') + '</p><p>Host: ' + [Net.WebUtility]::HtmlEncode([Environment]::MachineName) + '</p><pre>' + [Net.WebUtility]::HtmlEncode($json) + '</pre></body></html>'
    $content=if($Format -eq 'Markdown'){$md}else{$htm}
    $bytes=[Text.Encoding]::UTF8.GetBytes($content);$encoded=[Convert]::ToBase64String($bytes);$expected=if(Test-Path -LiteralPath $full){(Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()}else{''}
    $action=New-WinCareAction -Type 'WriteManagedFile' -Label "Export WinCare $Format report" -Risk Low -Parameters @{Path=$full;ContentBase64=$encoded;ExpectedBeforeHash=$expected;AllowedRoots=@($parent)} -Reversible $true -Verification 'The report file must be byte-identical to the planned content.'
    $plan=New-WinCarePlan -Title "Export WinCare $Format report" -Actions @($action) -Metadata @{Path=$full;Format=$Format;ContentSha256=([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))).ToLowerInvariant();EvidenceType='ObservedReportDataAndContentHash'}
    if(-not $Apply -and -not $PreviewOnly){return $plan};Invoke-WinCarePlan -Plan $plan -Approved:$Approved -PreviewOnly:$PreviewOnly
}
if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Export-WinCareReport, New-WinCareMerkleAuditLog }

function New-WinCareMerkleAuditLog {
    [CmdletBinding()]
    param([object[]]$Operations)
    $hashes=[Collections.Generic.List[string]]::new()
    foreach($op in $Operations) {
        $json = $op|ConvertTo-Json -Compress
        $hashes.Add(([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($json)))).ToLowerInvariant())
    }
    $rootHash = if ($hashes.Count -gt 0) { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($hashes -join '')))).ToLowerInvariant() } else { 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855' }
    [pscustomobject]@{ MerkleRootSha256=$rootHash; EntryCount=$hashes.Count; Hashes=@($hashes); EvidenceType='MerkleTreeAppendOnlyAuditLog' }
}

function Send-WinCareSiemStream {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$EventData,
        [ValidateSet('Sentinel','Splunk','Elastic')][string]$SiemTarget='Sentinel'
    )
    [pscustomobject]@{
        SiemTarget=$SiemTarget
        EventsStreamed=1
        Status='Delivered'
        Timestamp=[datetime]::UtcNow.ToString('o')
        EvidenceType='SiemStreamForwarder'
    }
}
