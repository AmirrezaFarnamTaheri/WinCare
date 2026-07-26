#requires -Version 7.2

function Start-WinCareWpfDashboardWindow {
    [CmdletBinding()]
    param(
        [string]$DashboardHtmlPath,
        [switch]$AsJob
    )

    if (-not $IsWindows) {
        return [pscustomobject]@{
            Supported    = $false
            Error        = 'Windows OS is required for the native WPF desktop shell.'
            EvidenceType = 'PlatformCapabilityRequirement'
        }
    }

    $moduleRoot = [System.IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
    $repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $moduleRoot '..\..'))
    $xamlPath = [System.IO.Path]::GetFullPath((Join-Path $moduleRoot 'UI\MainWindow.xaml'))

    if (-not (Test-Path -LiteralPath $xamlPath -PathType Leaf)) {
        throw "The WinCare WPF shell XAML asset is missing: $xamlPath"
    }

    if ($DashboardHtmlPath) {
        $targetHtml = [System.IO.Path]::GetFullPath($DashboardHtmlPath)
    } else {
        $packagedDashboard = [System.IO.Path]::GetFullPath((Join-Path $moduleRoot 'Assets\dashboard\index.html'))
        $repositoryDashboard = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'docs\dashboard\index.html'))
        $targetHtml = if (Test-Path -LiteralPath $packagedDashboard -PathType Leaf) {
            $packagedDashboard
        } else {
            $repositoryDashboard
        }
    }

    if (-not (Test-Path -LiteralPath $targetHtml -PathType Leaf)) {
        throw "The WinCare dashboard HTML asset is missing: $targetHtml"
    }

    $scriptBlock = {
        param($xamlFile, $htmlFile)

        $ErrorActionPreference = 'Stop'
        Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
        $xml = Get-Content -LiteralPath $xamlFile -Raw -ErrorAction Stop
        $stringReader = [System.IO.StringReader]::new($xml)
        $xmlReader = $null
        try {
            $settings = [System.Xml.XmlReaderSettings]::new()
            $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
            $settings.XmlResolver = $null
            $xmlReader = [System.Xml.XmlReader]::Create($stringReader, $settings)
            $window = [System.Windows.Markup.XamlReader]::Load($xmlReader)
        } finally {
            if ($xmlReader) { $xmlReader.Dispose() }
            $stringReader.Dispose()
        }

        $browser = $window.FindName('DashboardBrowser')
        if (-not $browser) {
            throw 'The WPF shell does not contain the required DashboardBrowser control.'
        }
        $browser.Navigate([System.Uri]::new($htmlFile).AbsoluteUri)
        $null = $window.ShowDialog()
    }

    if ($AsJob) {
        $job = Start-Job -ScriptBlock $scriptBlock -ArgumentList $xamlPath, $targetHtml
        return [pscustomobject]@{
            WindowMode   = 'AsynchronousJob'
            JobId        = $job.Id
            XamlPath     = $xamlPath
            DashboardUri = $targetHtml
            Status       = 'Launched'
            AuditTime    = [datetime]::UtcNow.ToString('o')
            EvidenceType = 'NativeWpfShellLaunchResult'
        }
    }

    [pscustomobject]@{
        WindowMode   = 'ValidatedAssets'
        XamlPath     = $xamlPath
        DashboardUri = $targetHtml
        Status       = 'Ready'
        AuditTime    = [datetime]::UtcNow.ToString('o')
        EvidenceType = 'NativeWpfShellLaunchResult'
    }
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) {
    Export-ModuleMember -Function Start-WinCareWpfDashboardWindow
}
