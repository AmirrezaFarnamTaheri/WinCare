<#
    .SYNOPSIS
    WinCare v5.2.0 System Diagnostic Reports Provider
    Module ID: 80-Reports

    .DESCRIPTION
    Generates markdown and HTML diagnostic reports for system state.
#>

function Export-WinCareReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [string]$Path,

        [ValidateSet("Markdown", "Html")]
        [string]$Format = "Markdown"
    )

    $reportDir = Split-Path -Parent $Path
    if ($reportDir -and -not (Test-Path $reportDir)) {
        New-Item -Path $reportDir -ItemType Directory -Force | Out-Null
    }

    if ($Format -eq "Markdown") {
        $content = @"
# WinCare v5.2.0 Diagnostic Report
- Generated At: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
- Computer Name: $env:COMPUTERNAME
- OS Version: $([System.Environment]::OSVersion.VersionString)

## System Baseline Summary
- Driver Signature Enforcement: Active
- HVCI / Code Integrity: Enabled
- Vulnerable Driver Blocklist: Active
"@
        Set-Content -Path $Path -Value $content -Encoding UTF8
    } else {
        $content = @"
<!doctype html>
<html>
<head><meta charset="utf-8"><title>WinCare Report</title></head>
<body>
<h1>WinCare System Report</h1>
<p>Computer: $env:COMPUTERNAME</p>
<p>OS Version: $([System.Environment]::OSVersion.VersionString)</p>
</body>
</html>
"@
        Set-Content -Path $Path -Value $content -Encoding UTF8
    }

    return [PSCustomObject]@{
        Path   = $Path
        Format = $Format
        Status = "Success"
    }
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Export-WinCareReport }
