#requires -Version 7.2

# Canonical operator-surface menu composition.

function Get-WinCareMainMenuItems {
    [CmdletBinding()]
    param()
    $definitions = @(
        @('dashboard', 'Dashboard', 'Dashboard'), @('quick', 'Quick actions', 'Quick'),
        @('applications', 'Applications', 'Applications'), @('cleanup', 'Cleanup', 'Cleanup'),
        @('storage', 'Storage', 'Storage'), @('profiles', 'Profiles', 'Profiles'),
        @('desktop', 'Desktop controls', 'Desktop'), @('startup', 'Startup', 'Startup'),
        @('health', 'Health', 'Health'), @('security', 'Security', 'Security'),
        @('windows-update', 'Windows Update', 'Wua'), @('updates', 'Updates', 'Updates'),
        @('network', 'Network', 'Network'), @('internals', 'Internals', 'Internals'),
        @('expert-lab', 'Expert laboratory', 'ExpertLab'), @('wdac', 'WDAC', 'WDAC'),
        @('security-baseline', 'Security baseline', 'Baseline'),
        @('provisioning', 'Provisioning', 'Provisioning'), @('offline', 'Offline image', 'Offline'),
        @('boot', 'Boot', 'Boot'), @('shell', 'Shell', 'Shell'),
        @('customization', 'Customization', 'Customization'), @('widgets', 'Widgets', 'Widgets'),
        @('bluetooth', 'Bluetooth', 'Bluetooth'), @('maintenance', 'Maintenance', 'Maintenance'),
        @('playbooks', 'Playbooks', 'Playbooks'), @('power', 'Power sessions', 'Power'),
        @('windows', 'Window workspace', 'Windows'), @('colour', 'Colour studio', 'Color'),
        @('notes', 'Local notes', 'Notes'), @('browser', 'Browser workspace', 'Browser'),
        @('remote-support', 'Remote support', 'RemoteSupport'),
        @('downloads', 'Downloads', 'Downloads'), @('telemetry', 'Telemetry', 'Telemetry'),
        @('launcher', 'Launcher', 'Launcher'), @('layouts', 'Workspace layouts', 'Layouts'),
        @('game-state', 'Game state', 'GameState'),
        @('workspace-studio', 'Workspace and device studio', 'WorkspaceStudio'),
        @('cleaner-preview', 'Cleaner preview studio', 'CleanerPreview'),
        @('peer-utilities', 'Advanced utilities', 'PeerUtilities'),
        @('automation', 'Automation', 'Automation'), @('recovery', 'Recovery', 'Recovery'),
        @('reports', 'Reports', 'Reports'), @('knowledge', 'Knowledge', 'Knowledge'),
        @('settings', 'Settings', 'Settings'), @('help', 'Help', 'Help'),
        @('palette', 'Command palette', 'Palette'), @('exit', 'Exit', 'Exit')
    )
    foreach ($definition in $definitions) {
        [pscustomobject]@{
            Id = $definition[0]
            Title = $definition[1]
            Action = $definition[2]
        }
    }
}
