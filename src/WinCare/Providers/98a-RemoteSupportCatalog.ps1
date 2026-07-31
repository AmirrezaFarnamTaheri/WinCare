#requires -Version 7.2

# Canonical remote-support product and process catalog.

function Get-WinCareRemoteSupportCatalog {
    @(
        [pscustomobject]@{Id='rustdesk';
            Name='RustDesk';
            ProcessNames=@('rustdesk');
            ServicePatterns=@('RustDesk*');
            AppPattern='(?i)rustdesk';
            ConfigRoots=@("$env:APPDATA\RustDesk","$env:ProgramData\RustDesk")},
        [pscustomobject]@{Id='mousekeyproxy';
            Name='MouseKeyProxy';
            ProcessNames=@('MouseKeyProxy.Agent','MouseKeyProxy.Service','mkp');
            ServicePatterns=@('MouseKeyProxy*');
            AppPattern='(?i)mousekeyproxy';
            ConfigRoots=@("$env:LOCALAPPDATA\MouseKeyProxy","$env:ProgramData\MouseKeyProxy")},
        [pscustomobject]@{Id='anydesk';
            Name='AnyDesk';
            ProcessNames=@('AnyDesk','AnyDeskMSI');
            ServicePatterns=@('AnyDesk*');
            AppPattern='(?i)anydesk';
            ConfigRoots=@("$env:APPDATA\AnyDesk","$env:ProgramData\AnyDesk")},
        [pscustomobject]@{Id='teamviewer';
            Name='TeamViewer';
            ProcessNames=@('TeamViewer','TeamViewer_Service','TeamViewer_Desktop');
            ServicePatterns=@('TeamViewer*');
            AppPattern='(?i)teamviewer';
            ConfigRoots=@("$env:APPDATA\TeamViewer","$env:ProgramData\TeamViewer")},
        [pscustomobject]@{Id='quickassist';Name='Quick Assist';ProcessNames=@('QuickAssist');ServicePatterns=@();AppPattern='(?i)quick assist';ConfigRoots=@()},
        [pscustomobject]@{Id='remoteassistance';Name='Windows Remote Assistance';ProcessNames=@('msra');ServicePatterns=@();AppPattern='(?i)remote assistance';ConfigRoots=@()},
        [pscustomobject]@{Id='remotedesktop';Name='Remote Desktop';ProcessNames=@('mstsc','msrdc','RdClient.Windows');ServicePatterns=@('TermService');AppPattern='(?i)remote desktop';ConfigRoots=@()},
        [pscustomobject]@{Id='chromeremotedesktop';
            Name='Chrome Remote Desktop';
            ProcessNames=@('remoting_host','remote_assistance_host');
            ServicePatterns=@('chromoting*');
            AppPattern='(?i)chrome remote desktop';
            ConfigRoots=@("$env:ProgramData\Google\Chrome Remote Desktop")},
        [pscustomobject]@{Id='parsec';
            Name='Parsec';
            ProcessNames=@('parsecd','pservice');
            ServicePatterns=@('Parsec*');
            AppPattern='(?i)parsec';
            ConfigRoots=@("$env:APPDATA\Parsec","$env:ProgramData\Parsec")},
        [pscustomobject]@{Id='splashtop';
            Name='Splashtop';
            ProcessNames=@('SRServer','SRManager','SplashtopRemoteService');
            ServicePatterns=@('Splashtop*');
            AppPattern='(?i)splashtop';
            ConfigRoots=@("$env:ProgramData\Splashtop")}
    )
}
