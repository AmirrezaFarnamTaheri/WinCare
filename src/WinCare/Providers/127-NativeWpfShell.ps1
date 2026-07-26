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

    foreach ($asset in @(
        @{ Path = $xamlPath; MaximumBytes = 1048576; Name = 'WPF XAML' },
        @{ Path = $targetHtml; MaximumBytes = 4194304; Name = 'dashboard HTML' }
    )) {
        if (-not (Test-Path -LiteralPath $asset.Path -PathType Leaf)) {
            throw "The WinCare $($asset.Name) asset is missing: $($asset.Path)"
        }
        $item = Get-Item -LiteralPath $asset.Path -Force -ErrorAction Stop
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "The WinCare $($asset.Name) asset must not be a reparse point: $($asset.Path)"
        }
        if ([long]$item.Length -gt [long]$asset.MaximumBytes) {
            throw "The WinCare $($asset.Name) asset exceeds its $($asset.MaximumBytes)-byte ceiling: $($asset.Path)"
        }
    }

    $scriptBlock = {
        param($xamlFile, $htmlFile)

        $ErrorActionPreference = 'Stop'
        Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase

        $xamlItem = Get-Item -LiteralPath $xamlFile -Force -ErrorAction Stop
        if ($xamlItem.PSIsContainer -or
            ($xamlItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or
            [long]$xamlItem.Length -gt 1048576L) {
            throw 'The WPF XAML asset failed bounded regular-file validation.'
        }

        $stream = [System.IO.FileStream]::new(
            $xamlFile,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::Read,
            65536,
            [System.IO.FileOptions]::SequentialScan
        )
        $reader = [System.IO.StreamReader]::new(
            $stream,
            [System.Text.UTF8Encoding]::new($false, $true),
            $true,
            65536,
            $false
        )
        $builder = [System.Text.StringBuilder]::new([int][Math]::Min([long]$xamlItem.Length, 1048576L))
        $buffer = [char[]]::new(32768)
        try {
            while (($count = $reader.ReadBlock($buffer, 0, $buffer.Length)) -gt 0) {
                if ($builder.Length + $count -gt 1048576) {
                    throw 'The WPF XAML asset exceeded its character ceiling while reading.'
                }
                $null = $builder.Append($buffer, 0, $count)
            }
            $xml = $builder.ToString()
        } finally {
            [System.Array]::Clear($buffer, 0, $buffer.Length)
            $reader.Dispose()
            $stream.Dispose()
        }

        $stringReader = [System.IO.StringReader]::new($xml)
        $xmlReader = $null
        try {
            $settings = [System.Xml.XmlReaderSettings]::new()
            $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
            $settings.XmlResolver = $null
            $settings.MaxCharactersInDocument = 1048576
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
