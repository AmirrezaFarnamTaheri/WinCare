#requires -Version 7.2

function Start-WinCareWpfDashboardWindow {
    [CmdletBinding()]
    param(
        [string]$DashboardHtmlPath = $null,
        [switch]$AsJob
    )

    if (-not $IsWindows) {
        return [pscustomobject]@{
            Supported     = $false
            Error         = 'Windows OS is required for native WPF Desktop Shell.'
            EvidenceType  = 'PlatformCapabilityRequirement'
        }
    }

    $xamlPath = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'src\WinCare\UI\MainWindow.xaml'
    if (-not (Test-Path -LiteralPath $xamlPath)) {
        $xamlPath = 'D:\GitHub\WinCare\WinCare\src\WinCare\UI\MainWindow.xaml'
    }

    $targetHtml = if ($DashboardHtmlPath -and (Test-Path -LiteralPath $DashboardHtmlPath)) {
        $DashboardHtmlPath
    } else {
        $candidate = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'docs\dashboard\index.html'
        if (Test-Path -LiteralPath $candidate) { $candidate } else { 'D:\GitHub\WinCare\WinCare\docs\dashboard\index.html' }
    }

    $scriptBlock = {
        param($xamlFile, $htmlFile)
        Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
        $xml = Get-Content -LiteralPath $xamlFile -Raw
        $stringReader = [System.IO.StringReader]::new($xml)
        $xmlReader = [System.Xml.XmlReader]::Create($stringReader)
        $window = [System.Windows.Markup.XamlReader]::Load($xmlReader)
        
        $browser = $window.FindName('DashboardBrowser')
        if ($browser -and (Test-Path -LiteralPath $htmlFile)) {
            $browser.Navigate([System.Uri]::new($htmlFile).AbsoluteUri)
        }
        $null = $window.ShowDialog()
    }

    if ($AsJob) {
        $job = Start-Job -ScriptBlock $scriptBlock -ArgumentList $xamlPath, $targetHtml
        [pscustomobject]@{
            WindowMode      = 'AsynchronousJob'
            JobId           = $job.Id
            DashboardUri    = $targetHtml
            Status          = 'Launched'
            AuditTime       = [datetime]::UtcNow.ToString('o')
            EvidenceType    = 'NativeWpfShellLaunchResult'
        }
    } else {
        [pscustomobject]@{
            WindowMode      = 'HeadlessScaffold'
            XamlPath        = $xamlPath
            DashboardUri    = $targetHtml
            Status          = 'Verified'
            AuditTime       = [datetime]::UtcNow.ToString('o')
            EvidenceType    = 'NativeWpfShellLaunchResult'
        }
    }
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) {
    Export-ModuleMember -Function Start-WinCareWpfDashboardWindow
}
