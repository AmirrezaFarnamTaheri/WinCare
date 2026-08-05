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

function Start-WinCareIpcServer {
    [CmdletBinding()]
    param(
        [string]$PipeName = 'WinCareIpcStream',
        [ValidateRange(100,300000)][int]$TimeoutMs = 5000,
        [scriptblock]$Handler = $null
    )

    if (-not $IsWindows) { throw 'IPC named-pipe server requires Windows.' }
    if ($PipeName -notmatch '^[A-Za-z0-9._-]{8,160}$') { throw 'Invalid IPC pipe name.' }

    $pipeSecurity = [System.IO.Pipes.PipeSecurity]::new()
    $userSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
    $systemSid = [System.Security.Principal.SecurityIdentifier]::new([System.Security.Principal.WellKnownSidType]::LocalSystemSid, $null)
    $fullControl = [System.IO.Pipes.PipeAccessRights]::FullControl
    $allow = [System.Security.AccessControl.AccessControlType]::Allow
    $pipeSecurity.AddAccessRule([System.IO.Pipes.PipeAccessRule]::new($userSid, $fullControl, $allow))
    $pipeSecurity.AddAccessRule([System.IO.Pipes.PipeAccessRule]::new($systemSid, $fullControl, $allow))

    $server = [System.IO.Pipes.NamedPipeServerStreamAcl]::Create(
        $PipeName, [System.IO.Pipes.PipeDirection]::InOut, 1,
        [System.IO.Pipes.PipeTransmissionMode]::Byte,
        [System.IO.Pipes.PipeOptions]::Asynchronous,
        65536, 65536, $pipeSecurity
    )
    $timeoutSeconds = [Math]::Max(1, [int][Math]::Ceiling($TimeoutMs / 1000.0))
    try {
        $wait = $server.WaitForConnectionAsync()
        if (-not $wait.Wait($TimeoutMs)) {
            return [pscustomobject]@{Status='Timeout';PipeName=$PipeName;EvidenceType='WinCareIpcServerStatus'}
        }
        $line = Read-WinCareBrokerFrame -Stream $server -MaximumBytes 65536 -TimeoutSeconds $timeoutSeconds
        $response = if ($Handler -and -not [string]::IsNullOrWhiteSpace($line)) {
            [string](& $Handler $line)
        } elseif (-not [string]::IsNullOrWhiteSpace($line)) {
            $msg = $line | ConvertFrom-Json -AsHashtable -Depth 24
            [ordered]@{Status='Received';Command=$msg.Command;Payload=$msg.Payload;Timestamp=[datetime]::UtcNow.ToString('o')} | ConvertTo-Json -Compress -Depth 24
        } else { '{"Status":"EmptyInput"}' }
        Write-WinCareBrokerFrame -Stream $server -Text $response -MaximumBytes 65536 -TimeoutSeconds $timeoutSeconds
        $server.WaitForPipeDrain()
        [pscustomobject]@{Status='Success';PipeName=$PipeName;InputMessage=$line;Response=$response;EvidenceType='WinCareIpcServerStatus'}
    } finally { $server.Dispose() }
}

function Send-WinCareIpcMessage {
    [CmdletBinding()]
    param(
        [string]$PipeName = 'WinCareIpcStream',
        [Parameter(Mandatory)][string]$Command,
        [hashtable]$Payload = @{},
        [ValidateRange(100,300000)][int]$TimeoutMs = 5000
    )
    if ($PipeName -notmatch '^[A-Za-z0-9._-]{8,160}$') { throw 'Invalid IPC pipe name.' }
    $client = [System.IO.Pipes.NamedPipeClientStream]::new('.', $PipeName, [System.IO.Pipes.PipeDirection]::InOut, [System.IO.Pipes.PipeOptions]::Asynchronous)
    $timeoutSeconds = [Math]::Max(1, [int][Math]::Ceiling($TimeoutMs / 1000.0))
    try {
        $client.Connect($TimeoutMs)
        $message = [ordered]@{Command=$Command;Payload=$Payload;Timestamp=[datetime]::UtcNow.ToString('o')} | ConvertTo-Json -Compress -Depth 24
        Write-WinCareBrokerFrame -Stream $client -Text $message -MaximumBytes 65536 -TimeoutSeconds $timeoutSeconds
        Read-WinCareBrokerFrame -Stream $client -MaximumBytes 65536 -TimeoutSeconds $timeoutSeconds
    } finally { $client.Dispose() }
}

function Write-WinCareSharedMemoryBuffer {
    [CmdletBinding()]
    param(
        [string]$BufferName = 'WinCareSharedStateBuffer',
        [Parameter(Mandatory)][hashtable]$State,
        [int]$SequenceId = 1
    )

    $mutexName = "Local\${BufferName}Mutex"
    $mutex = [System.Threading.Mutex]::new($false, $mutexName)
    $acquired = $false

    try {
        $acquired = $mutex.WaitOne(3000)
        if (-not $acquired) {
            throw "Timed out acquiring shared memory buffer write lock."
        }

        $mmf = [System.IO.MemoryMappedFiles.MemoryMappedFile]::CreateOrOpen($BufferName, 4096)
        try {
            $accessor = $mmf.CreateViewAccessor(0, 4096)
            try {
                $json = $State | ConvertTo-Json -Compress -Depth 24
                $payloadBytes = [System.Text.Encoding]::UTF8.GetBytes($json)
                if ($payloadBytes.Length -gt 4080) {
                    throw "Shared memory payload exceeds 4080 bytes bound."
                }

                $accessor.Write(0, [uint32]0x57494E43)
                $accessor.Write(4, [int32]$SequenceId)
                $accessor.Write(8, [int32]$payloadBytes.Length)
                $accessor.WriteArray(12, $payloadBytes, 0, $payloadBytes.Length)

                return [pscustomobject]@{
                    BufferName   = $BufferName
                    SequenceId   = $SequenceId
                    BytesWritten = $payloadBytes.Length
                    Status       = 'Updated'
                    EvidenceType = 'WinCareSharedMemoryBufferStatus'
                }
            } finally {
                $accessor.Dispose()
            }
        } finally {
            $mmf.Dispose()
        }
    } finally {
        if ($acquired) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
}

function Read-WinCareSharedMemoryBuffer {
    [CmdletBinding()]
    param(
        [string]$BufferName = 'WinCareSharedStateBuffer'
    )

    $mutexName = "Local\${BufferName}Mutex"
    $mutex = [System.Threading.Mutex]::new($false, $mutexName)
    $acquired = $false

    try {
        $acquired = $mutex.WaitOne(3000)
        if (-not $acquired) {
            throw "Timed out acquiring shared memory buffer read lock."
        }

        try {
            $mmf = [System.IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting($BufferName)
        } catch {
            return $null
        }

        try {
            $accessor = $mmf.CreateViewAccessor(0, 4096)
            try {
                $magic = $accessor.ReadUInt32(0)
                if ($magic -ne 0x57494E43) {
                    throw "Invalid shared memory magic header: 0x$($magic.ToString('X8'))"
                }

                $sequenceId = $accessor.ReadInt32(4)
                $payloadLength = $accessor.ReadInt32(8)

                if ($payloadLength -le 0 -or $payloadLength -gt 4080) {
                    throw "Invalid payload length in shared memory: $payloadLength"
                }

                $payloadBytes = [byte[]]::new($payloadLength)
                $null = $accessor.ReadArray(12, $payloadBytes, 0, $payloadLength)

                $json = [System.Text.Encoding]::UTF8.GetString($payloadBytes)
                $state = $json | ConvertFrom-Json -AsHashtable -Depth 24

                return [pscustomobject]@{
                    BufferName    = $BufferName
                    Magic         = 'WINC'
                    SequenceId    = $sequenceId
                    PayloadLength = $payloadLength
                    State         = $state
                    EvidenceType  = 'WinCareSharedMemoryBufferRead'
                }
            } finally {
                $accessor.Dispose()
            }
        } finally {
            $mmf.Dispose()
        }
    } finally {
        if ($acquired) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) {
    Export-ModuleMember -Function Start-WinCareWpfDashboardWindow, Start-WinCareIpcServer, Send-WinCareIpcMessage, Write-WinCareSharedMemoryBuffer, Read-WinCareSharedMemoryBuffer
}
