$script:WinCareGuiState = @{}
$script:WinCareGuiTheme = 'Normal'

function Get-WinCareGuiRepositoryRoot {
    [CmdletBinding()]
    param()
    Split-Path (Split-Path $script:WinCareModuleRoot -Parent) -Parent
}

function Get-WinCareGuiBrush {
    param([Parameter(Mandatory)][string]$Color)
    [Windows.Media.BrushConverter]::new().ConvertFromString($Color)
}

function Get-WinCareGuiRiskColor {
    param([Parameter(Mandatory)][string]$Risk)
    if($script:WinCareGuiTheme -eq 'Monochrome'){return $(if($Risk -eq 'Critical'){'#262626'}elseif($Risk -in @('High','Moderate')){'#1E1E1E'}else{'#171717'})}
    if($script:WinCareGuiTheme -eq 'HighContrast'){return '#000000'}
    switch($Risk){
        'ReadOnly'{'#173B46'}
        'Low'{'#173D35'}
        'Moderate'{'#3D3520'}
        'High'{'#4A2F24'}
        'Critical'{'#512128'}
        default{'#263550'}
    }
}

function Get-WinCareGuiRiskForeground {
    param([Parameter(Mandatory)][string]$Risk)
    if($script:WinCareGuiTheme -eq 'Monochrome'){return '#FFFFFF'}
    if($script:WinCareGuiTheme -eq 'HighContrast'){return $(switch($Risk){'ReadOnly'{'#00FFFF'}'Low'{'#00FF00'}'Moderate'{'#FFFF00'}'High'{'#FFB000'}'Critical'{'#FF6B6B'}default{'#FFFFFF'}})}
    switch($Risk){
        'ReadOnly'{'#36C5F0'}
        'Low'{'#2DD4A8'}
        'Moderate'{'#F6C85F'}
        'High'{'#F2A65A'}
        'Critical'{'#F97066'}
        default{'#F4F7FB'}
    }
}

function ConvertTo-WinCareGuiActionView {
    param([Parameter(Mandatory)][object]$Action)
    [pscustomobject]@{
        Id=$Action.Id;Title=$Action.Title;Description=$Action.Description;Category=$Action.Category;Icon=$Action.Icon
        Command=$Action.Command;Risk=$Action.Risk;IsReadOnly=$Action.IsReadOnly;CanApply=$Action.CanApply;Critical=$Action.Critical
        Featured=$Action.Featured;DefaultArgumentsJson=$Action.DefaultArgumentsJson;ArgumentHelp=$Action.ArgumentHelp
        Keywords=@($Action.Keywords);SourceRecords=@($Action.SourceRecords);CatalogKind=$Action.CatalogKind
        RiskBackground=Get-WinCareGuiBrush (Get-WinCareGuiRiskColor $Action.Risk)
        RiskForeground=Get-WinCareGuiBrush (Get-WinCareGuiRiskForeground $Action.Risk)
        SearchText=((@($Action.Title,$Action.Description,$Action.Category,$Action.Command)+@($Action.Keywords)+@($Action.SourceRecords))-join ' ').ToLowerInvariant()
    }
}

function Import-WinCareGuiWindow {
    [CmdletBinding()]
    param()
    $path=Join-Path (Get-WinCareGuiDataRoot) 'WinCare.MainWindow.xaml'
    $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$settings.XmlResolver=$null
    $reader=[Xml.XmlReader]::Create($path,$settings)
    try{
        $window=[Windows.Markup.XamlReader]::Load($reader)
        $iconPath=Join-Path (Get-WinCareGuiDataRoot) 'WinCare.ico'
        if(Test-Path -LiteralPath $iconPath -PathType Leaf){
            $iconUri=[Uri]::new($iconPath,[UriKind]::Absolute)
            $window.Icon=[Windows.Media.Imaging.BitmapFrame]::Create($iconUri)
        }
        $window
    }finally{$reader.Dispose()}
}


function Set-WinCareGuiVersionIdentity {
    param([Parameter(Mandatory)][object]$Window,[Parameter(Mandatory)][Collections.IDictionary]$Controls)
    $display="WinCare $script:WinCareVersion"
    $Window.Title=$display
    $Controls.ProductVersionText.Text=$display
    $Controls.FooterVersionText.Text="v$script:WinCareVersion"
}

function Set-WinCareGuiTheme {
    param([Parameter(Mandatory)][object]$Window,[ValidateSet('Normal','HighContrast','Monochrome')][string]$Theme)
    $script:WinCareGuiTheme=$Theme
    $palette=switch($Theme){
        'HighContrast'{@{BackgroundBrush='#000000';SidebarBrush='#000000';SurfaceBrush='#000000';SurfaceRaisedBrush='#111111';SurfaceHoverBrush='#222222';BorderBrush='#FFFFFF';AccentBrush='#FFFF00';AccentSoftBrush='#333300';CyanBrush='#00FFFF';SuccessBrush='#00FF00';WarningBrush='#FFFF00';CriticalBrush='#FF6B6B';TextBrush='#FFFFFF';TextMutedBrush='#FFFFFF';TextDimBrush='#D0D0D0';HeroBrush='#000000'}}
        'Monochrome'{@{BackgroundBrush='#0B0B0B';SidebarBrush='#101010';SurfaceBrush='#171717';SurfaceRaisedBrush='#232323';SurfaceHoverBrush='#303030';BorderBrush='#5A5A5A';AccentBrush='#E6E6E6';AccentSoftBrush='#343434';CyanBrush='#D8D8D8';SuccessBrush='#F2F2F2';WarningBrush='#CCCCCC';CriticalBrush='#FFFFFF';TextBrush='#FFFFFF';TextMutedBrush='#C8C8C8';TextDimBrush='#9A9A9A';HeroBrush='#1A1A1A'}}
        default{@{}}
    }
    foreach($name in $palette.Keys){$Window.Resources[$name]=Get-WinCareGuiBrush $palette[$name]}
}

function Get-WinCareGuiControlTable {
    param([Parameter(Mandatory)][object]$Window)
    $names=@(
        'NavigationList','PageTitleText','PageSubtitleText','GlobalSearchTextBox','RefreshButton','PolicyStatusText',
        'DashboardPanel','ActionsPanel','AssurancePanel','SettingsPanel','OpenActionsButton','OpenAssuranceButton',
        'HeroMachineText','HeroBuildText','HeroAdminText','SecurityValueText','SecurityDetailText','StorageValueText','StorageProgress','StorageDetailText','MemoryValueText','MemoryProgress','MemoryDetailText','RestartValueText','RestartDetailText',
        'QuickActionList','ActivityList','CriticalPolicyText','ReadOnlyPolicyText','PreviewPolicyText',
        'ActionSearchTextBox','CategoryFilterComboBox','RiskFilterComboBox','ActionCountText','ActionList','CatalogCoverageText',
        'ActionTitleText','ActionCommandText','ActionRiskBadge','ActionRiskText','ActionDescriptionText','ActionSourceText','ArgumentsTextBox','ArgumentHelpText','PreviewActionButton','ApplyActionButton','CopyOutputButton','ActionOutputTextBox',
        'AssuranceSourceText','AssuranceExportText','AssuranceTestText','AssuranceNativeText','AssuranceGrid','OpenAssuranceFolderButton',
        'WorkspaceModeComboBox','ReducedMotionCheckBox','ScreenReaderCheckBox','SaveGuiSettingsButton','PolicyNameText','PolicySummaryText',
        'ProductVersionText','FooterVersionText','StatusText','BusyOverlay','BusyTitleText','BusyDetailText','ConfirmOverlay','ConfirmTitleText','ConfirmRiskText','ConfirmDescriptionText','ConfirmPhrasePanel','ConfirmPhraseTextBox','ConfirmCancelButton','ConfirmExecuteButton','ToastBorder','ToastText'
    )
    $table=@{}
    foreach($name in $names){$control=$Window.FindName($name);if($null -eq $control){throw "Required GUI control was not found: $name"};$table[$name]=$control}
    $table
}

function Set-WinCareGuiPanelVisibility {
    param([Parameter(Mandatory)][string]$Panel)
    $controls=$script:WinCareGuiState.Controls
    foreach($name in @('DashboardPanel','ActionsPanel','AssurancePanel','SettingsPanel')){$controls[$name].Visibility=[Windows.Visibility]::Collapsed}
    $controls[$Panel].Visibility=[Windows.Visibility]::Visible
}

function Set-WinCareGuiBusy {
    param([bool]$Busy,[string]$Title='Working',[string]$Detail='WinCare is collecting evidence and evaluating policy.')
    $controls=$script:WinCareGuiState.Controls
    $controls.BusyTitleText.Text=$Title;$controls.BusyDetailText.Text=$Detail
    $controls.BusyOverlay.Visibility=if($Busy){[Windows.Visibility]::Visible}else{[Windows.Visibility]::Collapsed}
    $controls.StatusText.Text=if($Busy){$Detail}else{'Ready'}
}

function Show-WinCareGuiToast {
    param([Parameter(Mandatory)][string]$Message,[ValidateSet('Info','Success','Warning','Critical')][string]$Kind='Info')
    $controls=$script:WinCareGuiState.Controls
    $controls.ToastText.Text=$Message
    $controls.ToastBorder.BorderBrush=Get-WinCareGuiBrush $(switch($Kind){'Success'{Get-WinCareGuiRiskForeground 'Low'}'Warning'{Get-WinCareGuiRiskForeground 'Moderate'}'Critical'{Get-WinCareGuiRiskForeground 'Critical'}default{Get-WinCareGuiRiskForeground 'ReadOnly'}})
    $controls.ToastBorder.Visibility=[Windows.Visibility]::Visible
    $script:WinCareGuiState.ToastHideAt=[datetime]::UtcNow.AddSeconds(4)
}

function Format-WinCareGuiBytes {
    param([long]$Bytes)
    if($Bytes -ge 1TB){'{0:N1} TB' -f ($Bytes/1TB)}elseif($Bytes -ge 1GB){'{0:N1} GB' -f ($Bytes/1GB)}elseif($Bytes -ge 1MB){'{0:N1} MB' -f ($Bytes/1MB)}elseif($Bytes -ge 1KB){'{0:N1} KB' -f ($Bytes/1KB)}else{"$Bytes B"}
}

function Add-WinCareGuiActivity {
    param([string]$Title,[string]$Status,[string]$Kind='Info')
    $brush=Get-WinCareGuiBrush $(switch($Kind){'Success'{Get-WinCareGuiRiskForeground 'Low'}'Warning'{Get-WinCareGuiRiskForeground 'Moderate'}'Critical'{Get-WinCareGuiRiskForeground 'Critical'}default{Get-WinCareGuiRiskForeground 'ReadOnly'}})
    $script:WinCareGuiState.Activity.Insert(0,[pscustomobject]@{Time=(Get-Date).ToString('HH:mm:ss');Title=$Title;Status=$Status;StatusBrush=$brush})
    while($script:WinCareGuiState.Activity.Count -gt 20){$script:WinCareGuiState.Activity.RemoveAt($script:WinCareGuiState.Activity.Count-1)}
    $script:WinCareGuiState.Controls.ActivityList.ItemsSource=$null;$script:WinCareGuiState.Controls.ActivityList.ItemsSource=@($script:WinCareGuiState.Activity)
}

function Update-WinCareGuiPolicyState {
    $controls=$script:WinCareGuiState.Controls;$policy=Get-WinCarePolicy;$config=Get-WinCareConfig
    $critical=[bool]$policy.AllowCriticalActions
    $controls.PolicyStatusText.Text=if($critical){'Critical actions permitted by policy'}else{'Safe defaults active'}
    $controls.CriticalPolicyText.Text=if($critical){'Permitted'}else{'Blocked'}
    $controls.CriticalPolicyText.Foreground=Get-WinCareGuiBrush $(if($critical){Get-WinCareGuiRiskForeground 'Critical'}else{Get-WinCareGuiRiskForeground 'Low'})
    $controls.ReadOnlyPolicyText.Text=if([bool]$config.ReadOnly){'On'}else{'Off'}
    $controls.PreviewPolicyText.Text=if([bool]$policy.RequirePreview -or [bool]$config.RequirePreviewForChanges){'Yes'}else{'No'}
    $controls.PolicyNameText.Text=[string]$policy.PolicyName
    $controls.PolicySummaryText.Text="Critical=$critical • LegacyUnsafe=$([bool]$policy.AllowLegacyUnsafeProfiles) • PermanentSecurityReduction=$([bool]$policy.AllowPermanentSecurityReduction) • VerifiedPeerTools=$([bool]$policy.AllowVerifiedPeerTools)"
    $mode=[string]$config.WorkspaceMode
    foreach($item in @($controls.WorkspaceModeComboBox.Items)){if([string]$item.Content -eq $mode){$controls.WorkspaceModeComboBox.SelectedItem=$item;break}}
    $controls.ReducedMotionCheckBox.IsChecked=[bool]$config.ReducedMotion
    $controls.ScreenReaderCheckBox.IsChecked=[bool]$config.ScreenReaderMode
}

function Update-WinCareGuiDashboard {
    $controls=$script:WinCareGuiState.Controls
    Set-WinCareGuiBusy $true 'Refreshing overview' 'Collecting bounded local system evidence.'
    try{
        $summary=Get-WinCareSystemSummary
        $controls.HeroMachineText.Text=[string]$summary.ComputerName
        $controls.HeroBuildText.Text="$($summary.Windows) • build $($summary.Build) • uptime $([math]::Floor($summary.Uptime.TotalHours))h"
        $controls.HeroAdminText.Text=if($summary.IsAdmin){'Administrator context'}else{'Standard-user context; elevation is operation-scoped'}
        $controls.SecurityValueText.Text=[string]$summary.Defender
        $controls.SecurityValueText.Foreground=Get-WinCareGuiBrush $(switch([string]$summary.Defender){'Protected'{Get-WinCareGuiRiskForeground 'Low'}'Limited'{Get-WinCareGuiRiskForeground 'Moderate'}'Disabled'{Get-WinCareGuiRiskForeground 'Critical'}default{Get-WinCareGuiRiskForeground 'ReadOnly'}})
        $controls.SecurityDetailText.Text='Microsoft Defender realtime posture'
        $systemDrive=@($summary.Drives|Where-Object{$_.DeviceId -eq $env:SystemDrive}|Select-Object -First 1)
        if($systemDrive.Count){$drive=$systemDrive[0];$controls.StorageValueText.Text="$([math]::Round([double]$drive.UsedPercent))% used";$controls.StorageProgress.Value=[double]$drive.UsedPercent;$controls.StorageDetailText.Text="$(Format-WinCareGuiBytes ([long]$drive.Free)) free on $($drive.DeviceId)"}else{$controls.StorageValueText.Text='Unavailable';$controls.StorageProgress.Value=0}
        $memoryPercent=if([long]$summary.MemoryTotal -gt 0){[math]::Round(([double]$summary.MemoryUsed/[double]$summary.MemoryTotal)*100)}else{0}
        $controls.MemoryValueText.Text="$memoryPercent% used";$controls.MemoryProgress.Value=$memoryPercent;$controls.MemoryDetailText.Text="$(Format-WinCareGuiBytes ([long]$summary.MemoryUsed)) of $(Format-WinCareGuiBytes ([long]$summary.MemoryTotal))"
        $controls.RestartValueText.Text=if($summary.PendingRestart){'Pending'}else{'Clear'}
        $controls.RestartValueText.Foreground=Get-WinCareGuiBrush $(if($summary.PendingRestart){Get-WinCareGuiRiskForeground 'Moderate'}else{Get-WinCareGuiRiskForeground 'Low'})
        $controls.RestartDetailText.Text=if($summary.PendingRestart){'Restart before deep servicing'}else{'No restart marker detected'}
        Update-WinCareGuiPolicyState
        Add-WinCareGuiActivity -Title 'Dashboard refreshed' -Status 'Verified' -Kind Success
    }catch{
        Add-WinCareGuiActivity -Title 'Dashboard refresh' -Status 'Failed' -Kind Critical
        Show-WinCareGuiToast -Message $_.Exception.Message -Kind Critical
    }finally{Set-WinCareGuiBusy $false}
}

function Get-WinCareGuiAssurancePath {
    [CmdletBinding()]
    param()
    $root=Get-WinCareGuiRepositoryRoot
    $artifactPath=Join-Path $root 'artifacts'
    if(Test-Path -LiteralPath $artifactPath -PathType Container){return $artifactPath}
    return $root
}

function Update-WinCareGuiActionList {
    param([string]$CategoryOverride='')
    $controls=$script:WinCareGuiState.Controls
    $query=[string]$controls.ActionSearchTextBox.Text;$category=if($CategoryOverride){$CategoryOverride}elseif($controls.CategoryFilterComboBox.SelectedItem){[string]$controls.CategoryFilterComboBox.SelectedItem}else{'All'}
    $risk=if($controls.RiskFilterComboBox.SelectedItem){[string]$controls.RiskFilterComboBox.SelectedItem}else{'All'}
    $items=@($script:WinCareGuiState.Actions)
    if($category -and $category -ne 'All'){$items=@($items|Where-Object Category -eq $category)}
    if($risk -and $risk -ne 'All'){$items=@($items|Where-Object Risk -eq $risk)}
    if(-not [string]::IsNullOrWhiteSpace($query)){$needle=$query.ToLowerInvariant();$items=@($items|Where-Object{$_.SearchText.Contains($needle)})}
    $controls.ActionList.ItemsSource=$null;$controls.ActionList.ItemsSource=@($items|Sort-Object @{Expression='CatalogKind';Descending=$true},@{Expression='Featured';Descending=$true},Title)
    $controls.ActionCountText.Text="$($items.Count) shown"
    $controls.CatalogCoverageText.Text="$($script:WinCareGuiState.Actions.Count) GUI entries cover $(@(Get-WinCareHeadlessCommandName).Count) headless commands."
    if($items.Count -and $null -eq $controls.ActionList.SelectedItem){$controls.ActionList.SelectedIndex=0}
}

function Set-WinCareGuiSelectedAction {
    param([object]$Action)
    $controls=$script:WinCareGuiState.Controls
    if($null -eq $Action){
        $script:WinCareGuiState.SelectedAction=$null
        $controls.ActionTitleText.Text='Select an action';$controls.ActionCommandText.Text='';$controls.ActionRiskText.Text='—'
        $controls.ActionRiskBadge.Background=Get-WinCareGuiBrush (Get-WinCareGuiRiskColor 'ReadOnly');$controls.ActionRiskText.Foreground=Get-WinCareGuiBrush (Get-WinCareGuiRiskForeground 'ReadOnly')
        $controls.ActionDescriptionText.Text='Choose a command to inspect its contract, implementation references, arguments, and execution path.'
        $controls.ActionSourceText.Text='';$controls.ArgumentsTextBox.Text='{}';$controls.ArgumentHelpText.Text=''
        $controls.PreviewActionButton.IsEnabled=$false;$controls.ApplyActionButton.IsEnabled=$false;$controls.ActionOutputTextBox.Text=''
        return
    }
    $script:WinCareGuiState.SelectedAction=$Action
    $controls.ActionTitleText.Text=[string]$Action.Title;$controls.ActionCommandText.Text=[string]$Action.Command;$controls.ActionRiskText.Text=[string]$Action.Risk
    $controls.ActionRiskBadge.Background=$Action.RiskBackground;$controls.ActionRiskText.Foreground=$Action.RiskForeground
    $controls.ActionDescriptionText.Text=[string]$Action.Description
    $controls.ActionSourceText.Text=if(@($Action.SourceRecords).Count){'Implementation references: '+(@($Action.SourceRecords)-join ', ')}else{'Implementation: generated command coverage; see the command contract and current validation report.'}
    $controls.ArgumentsTextBox.Text=[string]$Action.DefaultArgumentsJson;$controls.ArgumentHelpText.Text=[string]$Action.ArgumentHelp
    $controls.PreviewActionButton.IsEnabled=$true;$controls.PreviewActionButton.Content=if($Action.IsReadOnly){'Run'}else{'Run preview'}
    $controls.ApplyActionButton.IsEnabled=[bool]$Action.CanApply
    $controls.ApplyActionButton.Content=if($Action.Critical){'Review critical apply'}else{'Apply exact plan'}
    $controls.ApplyActionButton.Style=$controls.ApplyActionButton.FindResource($(if($Action.Critical){'DangerButton'}else{'PrimaryButton'}))
    $controls.ActionOutputTextBox.Text=''
}

function Set-WinCareGuiSection {
    param([Parameter(Mandatory)][string]$Id)
    $controls=$script:WinCareGuiState.Controls;$nav=@(Get-WinCareGuiNavigation|Where-Object id -eq $Id|Select-Object -First 1)
    if($nav.Count){$controls.PageTitleText.Text=[string]$nav[0].label;$controls.PageSubtitleText.Text=[string]$nav[0].description}
    switch($Id){
        'dashboard'{Set-WinCareGuiPanelVisibility 'DashboardPanel'}
        'assurance'{Set-WinCareGuiPanelVisibility 'AssurancePanel';Update-WinCareGuiAssurance}
        'settings'{Set-WinCareGuiPanelVisibility 'SettingsPanel';Update-WinCareGuiPolicyState}
        default{
            Set-WinCareGuiPanelVisibility 'ActionsPanel'
            $map=@{profiles='Profiles';health='Health';security='Security';maintenance='Maintenance';network='Network';workspace='Workspace';actions='All'}
            $category=if($map.ContainsKey($Id)){$map[$Id]}else{'All'}
            $controls.CategoryFilterComboBox.SelectedItem=$category
            Update-WinCareGuiActionList -CategoryOverride $category
        }
    }
    $script:WinCareGuiState.ActiveSection=$Id
}

function ConvertFrom-WinCareGuiArguments {
    $text=[string]$script:WinCareGuiState.Controls.ArgumentsTextBox.Text
    if([string]::IsNullOrWhiteSpace($text)){return @{}}
    $parsed=$text|ConvertFrom-Json -AsHashtable -Depth 50
    if($parsed -isnot [Collections.IDictionary]){throw 'Arguments JSON must be an object.'}
    $parsed
}

function Update-WinCareGuiProcessCapture {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$State)

    foreach ($name in @('Stdout', 'Stderr')) {
        $closedKey = $name + 'Closed'
        $taskKey = $name + 'ReadTask'
        $streamKey = $name + 'Stream'
        $bufferKey = $name + 'Buffer'
        $memoryKey = $name + 'Memory'
        $truncatedKey = $name + 'Truncated'
        while (-not [bool]$State.$closedKey -and $State.$taskKey.IsCompleted) {
            try {
                $count = $State.$taskKey.GetAwaiter().GetResult()
            } catch {
                $State.$closedKey = $true
                throw "GUI $name capture failed: $($_.Exception.Message)"
            }
            if ($count -eq 0) {
                $State.$closedKey = $true
                break
            }
            if (Add-WinCareCapturedProcessBytes -Destination $State.$memoryKey `
                -Buffer $State.$bufferKey -Count $count `
                -MaximumBytes $State.MaximumBytes) {
                $State.$truncatedKey = $true
            }
            $State.$taskKey = $State.$streamKey.ReadAsync(
                $State.$bufferKey,
                0,
                $State.$bufferKey.Length,
                $State.Cancellation.Token
            )
        }
    }
}

function Start-WinCareGuiCommandProcess {
    param([Parameter(Mandatory)][object]$Action,[switch]$Apply,[hashtable]$Arguments)
    if($script:WinCareGuiState.CommandProcess){Show-WinCareGuiToast -Message 'Another command is already running.' -Kind Warning;return}
    $process=$null
    $state=$null
    try{
        if($null -eq $Arguments){$Arguments=ConvertFrom-WinCareGuiArguments}
        $json=$Arguments|ConvertTo-Json -Compress -Depth 50
        if([Text.Encoding]::UTF8.GetByteCount($json) -gt 24576){throw 'Arguments JSON exceeds the GUI process limit of 24576 bytes.'}
        $root=Get-WinCareGuiRepositoryRoot;$launcher=Join-Path $root 'WinCare.ps1';$pwsh=Get-WinCareCurrentPowerShellExecutable
        $psi=[Diagnostics.ProcessStartInfo]::new();$psi.FileName=$pwsh;$psi.WorkingDirectory=$root;$psi.UseShellExecute=$false;$psi.CreateNoWindow=$true;$psi.RedirectStandardOutput=$true;$psi.RedirectStandardError=$true
        foreach($arg in @('-NoLogo','-NoProfile','-NonInteractive','-File',$launcher,'-Command',[string]$Action.Command,'-ArgumentsJson',$json)){[void]$psi.ArgumentList.Add($arg)}
        if($Apply){[void]$psi.ArgumentList.Add('-Apply')};[void]$psi.ArgumentList.Add('-Json')
        $psi.Environment['POWERSHELL_TELEMETRY_OPTOUT']='1'
        $process=[Diagnostics.Process]::new();$process.StartInfo=$psi
        if(-not $process.Start()){throw 'The command process did not start.'}
        $maximumBytes=4194304
        $cancellation=[Threading.CancellationTokenSource]::new()
        $stdoutBuffer=[byte[]]::new(65536);$stderrBuffer=[byte[]]::new(65536)
        $stdoutStream=$process.StandardOutput.BaseStream;$stderrStream=$process.StandardError.BaseStream
        $state=[pscustomobject]@{
            Process=$process;Action=$Action;Apply=[bool]$Apply;StartedAt=[datetime]::UtcNow
            MaximumBytes=$maximumBytes;Cancellation=$cancellation
            StdoutStream=$stdoutStream;StderrStream=$stderrStream
            StdoutBuffer=$stdoutBuffer;StderrBuffer=$stderrBuffer
            StdoutMemory=[IO.MemoryStream]::new($maximumBytes);StderrMemory=[IO.MemoryStream]::new($maximumBytes)
            StdoutReadTask=$stdoutStream.ReadAsync($stdoutBuffer,0,$stdoutBuffer.Length,$cancellation.Token)
            StderrReadTask=$stderrStream.ReadAsync($stderrBuffer,0,$stderrBuffer.Length,$cancellation.Token)
            StdoutClosed=$false;StderrClosed=$false;StdoutTruncated=$false;StderrTruncated=$false
        }
        $script:WinCareGuiState.CommandProcess=$state
        Set-WinCareGuiBusy $true $(if($Apply){'Applying exact plan'}elseif($Action.IsReadOnly){'Collecting evidence'}else{'Generating preview'}) "$($Action.Title) • $($Action.Command)"
        Add-WinCareGuiActivity -Title $Action.Title -Status $(if($Apply){'Applying'}else{'Running'}) -Kind Info
    }catch{
        if($state){$state.Cancellation.Cancel();$state.Cancellation.Dispose();$state.StdoutMemory.Dispose();$state.StderrMemory.Dispose()}
        if($process){$process.Dispose()}
        Show-WinCareGuiToast -Message $_.Exception.Message -Kind Critical;Set-WinCareGuiBusy $false
    }
}

function Complete-WinCareGuiCommandProcess {
    $state=$script:WinCareGuiState.CommandProcess;if($null -eq $state){return}
    try{Update-WinCareGuiProcessCapture -State $state}catch{
        try{$state.Cancellation.Cancel();if(-not $state.Process.HasExited){$state.Process.Kill($true)}}catch{}
        Show-WinCareGuiToast -Message $_.Exception.Message -Kind Critical
    }
    if(-not $state.Process.HasExited -or -not $state.StdoutClosed -or -not $state.StderrClosed){return}
    $controls=$script:WinCareGuiState.Controls
    try{
        $state.Process.WaitForExit()
        $stdout=ConvertFrom-WinCareCapturedProcessBytes -Stream $state.StdoutMemory
        $stderr=ConvertFrom-WinCareCapturedProcessBytes -Stream $state.StderrMemory
        $exitCode=$state.Process.ExitCode
        if($state.StdoutTruncated){$stdout += "`n[WinCare GUI truncated stdout at $($state.MaximumBytes) bytes.]"}
        if($state.StderrTruncated){$stderr += "`n[WinCare GUI truncated stderr at $($state.MaximumBytes) bytes.]"}
        $parsed=$null
        if(-not [string]::IsNullOrWhiteSpace($stdout)){try{$parsed=$stdout|ConvertFrom-Json -Depth 60}catch{$parsed=$null}}
        $display=if($parsed){$parsed|ConvertTo-Json -Depth 60}else{($stdout+$(if($stderr){"`nSTDERR:`n$stderr"}else{''})).Trim()}
        $controls.ActionOutputTextBox.Text=$display
        $hasSuccess=$parsed -and $parsed.PSObject.Properties['Success']
        $success=if($hasSuccess){[bool]$parsed.Success}else{$exitCode -eq 0}
        $status=if($parsed -and $parsed.PSObject.Properties['Status']){[string]$parsed.Status}elseif($success){'Succeeded'}else{'Failed'}
        Add-WinCareGuiActivity -Title $state.Action.Title -Status $status -Kind $(if($success){'Success'}else{'Critical'})
        Show-WinCareGuiToast -Message "$($state.Action.Title): $status" -Kind $(if($success){'Success'}else{'Critical'})
        if($success -and $state.Apply){Update-WinCareGuiDashboard}
    }catch{Show-WinCareGuiToast -Message "Command completion failed: $($_.Exception.Message)" -Kind Critical}
    finally{
        $state.Cancellation.Cancel();$state.Cancellation.Dispose()
        [Array]::Clear($state.StdoutBuffer,0,$state.StdoutBuffer.Length);[Array]::Clear($state.StderrBuffer,0,$state.StderrBuffer.Length)
        $state.StdoutMemory.Dispose();$state.StderrMemory.Dispose();$state.Process.Dispose()
        $script:WinCareGuiState.CommandProcess=$null;Set-WinCareGuiBusy $false
    }
}

function Show-WinCareGuiConfirmation {
    param([Parameter(Mandatory)][object]$Action)
    $controls=$script:WinCareGuiState.Controls;$script:WinCareGuiState.PendingApplyAction=$Action
    $controls.ConfirmTitleText.Text=$Action.Title;$controls.ConfirmRiskText.Text=$Action.Risk
    $controls.ConfirmDescriptionText.Text="$($Action.Description)`n`nCommand: $($Action.Command). Review arguments and policy before continuing."
    $controls.ConfirmPhraseTextBox.Text=''
    $controls.ConfirmPhrasePanel.Visibility=if($Action.Critical){[Windows.Visibility]::Visible}else{[Windows.Visibility]::Collapsed}
    $controls.ConfirmExecuteButton.IsEnabled=(-not $Action.Critical)
    $controls.ConfirmOverlay.Visibility=[Windows.Visibility]::Visible
    if($Action.Critical){$controls.ConfirmPhraseTextBox.Focus()}
}

function Invoke-WinCareGuiConfirmedApply {
    $action=$script:WinCareGuiState.PendingApplyAction;if($null -eq $action){return}
    try{
        $arguments=ConvertFrom-WinCareGuiArguments
        if($action.Critical){
            $phrase=[string]$script:WinCareGuiState.Controls.ConfirmPhraseTextBox.Text
            if($phrase -cne 'I ACCEPT LEGACY UNSAFE MUTATIONS'){throw 'The exact Critical acknowledgement was not entered.'}
            $arguments['Acknowledgement']=$phrase
        }
        $script:WinCareGuiState.Controls.ConfirmOverlay.Visibility=[Windows.Visibility]::Collapsed
        $script:WinCareGuiState.PendingApplyAction=$null
        Start-WinCareGuiCommandProcess -Action $action -Apply -Arguments $arguments
    }catch{Show-WinCareGuiToast -Message $_.Exception.Message -Kind Critical}
}

function Save-WinCareGuiSettings {
    $controls=$script:WinCareGuiState.Controls
    try{
        $mode=if($controls.WorkspaceModeComboBox.SelectedItem){[string]$controls.WorkspaceModeComboBox.SelectedItem.Content}else{'All'}
        $null=Set-WinCareConfig -Name WorkspaceMode -Value $mode
        $null=Set-WinCareConfig -Name ReducedMotion -Value ([bool]$controls.ReducedMotionCheckBox.IsChecked)
        $null=Set-WinCareConfig -Name ScreenReaderMode -Value ([bool]$controls.ScreenReaderCheckBox.IsChecked)
        Show-WinCareGuiToast -Message 'Interface settings saved.' -Kind Success
    }catch{Show-WinCareGuiToast -Message $_.Exception.Message -Kind Critical}
}

function Register-WinCareGuiEvents {
    $controls=$script:WinCareGuiState.Controls;$window=$script:WinCareGuiState.Window
    $controls.NavigationList.Add_SelectionChanged({$item=$script:WinCareGuiState.Controls.NavigationList.SelectedItem;if($item){Set-WinCareGuiSection -Id ([string]$item.id)}})
    $controls.OpenActionsButton.Add_Click({Set-WinCareGuiSection -Id 'actions';$script:WinCareGuiState.Controls.NavigationList.SelectedIndex=1})
    $controls.OpenAssuranceButton.Add_Click({Set-WinCareGuiSection -Id 'assurance';$script:WinCareGuiState.Controls.NavigationList.SelectedIndex=8})
    $controls.RefreshButton.Add_Click({switch($script:WinCareGuiState.ActiveSection){'dashboard'{Update-WinCareGuiDashboard}'assurance'{Update-WinCareGuiAssurance}default{Update-WinCareGuiActionList}}})
    $controls.GlobalSearchTextBox.Add_TextChanged({$text=[string]$script:WinCareGuiState.Controls.GlobalSearchTextBox.Text;$script:WinCareGuiState.Controls.ActionSearchTextBox.Text=$text;if(-not [string]::IsNullOrWhiteSpace($text)){Set-WinCareGuiSection -Id 'actions';$script:WinCareGuiState.Controls.NavigationList.SelectedIndex=1}})
    $controls.ActionSearchTextBox.Add_TextChanged({Update-WinCareGuiActionList})
    $controls.CategoryFilterComboBox.Add_SelectionChanged({Update-WinCareGuiActionList})
    $controls.RiskFilterComboBox.Add_SelectionChanged({Update-WinCareGuiActionList})
    $controls.ActionList.Add_SelectionChanged({Set-WinCareGuiSelectedAction -Action $script:WinCareGuiState.Controls.ActionList.SelectedItem})
    $controls.QuickActionList.Add_MouseDoubleClick({$action=$script:WinCareGuiState.Controls.QuickActionList.SelectedItem;if($action){Set-WinCareGuiSection -Id 'actions';$script:WinCareGuiState.Controls.NavigationList.SelectedIndex=1;$script:WinCareGuiState.Controls.ActionSearchTextBox.Text=[string]$action.Title}})
    $controls.PreviewActionButton.Add_Click({$action=$script:WinCareGuiState.SelectedAction;if($action){Start-WinCareGuiCommandProcess -Action $action}})
    $controls.ApplyActionButton.Add_Click({$action=$script:WinCareGuiState.SelectedAction;if($action){Show-WinCareGuiConfirmation -Action $action}})
    $controls.CopyOutputButton.Add_Click({if($script:WinCareGuiState.Controls.ActionOutputTextBox.Text){[Windows.Clipboard]::SetText([string]$script:WinCareGuiState.Controls.ActionOutputTextBox.Text);Show-WinCareGuiToast -Message 'Result copied to clipboard.' -Kind Success}})
    $controls.ConfirmCancelButton.Add_Click({$script:WinCareGuiState.Controls.ConfirmOverlay.Visibility=[Windows.Visibility]::Collapsed;$script:WinCareGuiState.PendingApplyAction=$null})
    $controls.ConfirmPhraseTextBox.Add_TextChanged({$script:WinCareGuiState.Controls.ConfirmExecuteButton.IsEnabled=([string]$script:WinCareGuiState.Controls.ConfirmPhraseTextBox.Text -ceq 'I ACCEPT LEGACY UNSAFE MUTATIONS')})
    $controls.ConfirmExecuteButton.Add_Click({Invoke-WinCareGuiConfirmedApply})
    $controls.OpenAssuranceFolderButton.Add_Click({try{$null=Start-WinCareDetachedProcess -FilePath 'explorer.exe' -ArgumentList @((Get-WinCareGuiAssurancePath))}catch{Show-WinCareGuiToast -Message $_.Exception.Message -Kind Warning}})
    $controls.SaveGuiSettingsButton.Add_Click({Save-WinCareGuiSettings})
    $window.Add_KeyDown({param($sender,$event)
        if(($event.KeyboardDevice.Modifiers -band [Windows.Input.ModifierKeys]::Control) -and $event.Key -eq [Windows.Input.Key]::K){$script:WinCareGuiState.Controls.GlobalSearchTextBox.Focus();$script:WinCareGuiState.Controls.GlobalSearchTextBox.SelectAll();$event.Handled=$true}
        elseif(($event.KeyboardDevice.Modifiers -band [Windows.Input.ModifierKeys]::Control) -and $event.Key -eq [Windows.Input.Key]::R){$script:WinCareGuiState.Controls.RefreshButton.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent));$event.Handled=$true}
        elseif($event.Key -eq [Windows.Input.Key]::Escape){if($script:WinCareGuiState.Controls.ConfirmOverlay.Visibility -eq [Windows.Visibility]::Visible){$script:WinCareGuiState.Controls.ConfirmOverlay.Visibility=[Windows.Visibility]::Collapsed;$script:WinCareGuiState.PendingApplyAction=$null}else{$script:WinCareGuiState.Controls.GlobalSearchTextBox.Text='';$script:WinCareGuiState.Controls.ActionSearchTextBox.Text=''};$event.Handled=$true}
    })
    $window.Add_Closing({param($sender,$event)
        if($script:WinCareGuiState.CommandProcess -and -not $script:WinCareGuiState.CommandProcess.Process.HasExited){
            $answer=[Windows.MessageBox]::Show('A WinCare command is still running. Request cancellation by closing the GUI? The operation journal remains authoritative.','WinCare command running',[Windows.MessageBoxButton]::YesNo,[Windows.MessageBoxImage]::Warning)
            if($answer -ne [Windows.MessageBoxResult]::Yes){$event.Cancel=$true}
        }
    })
}

function Start-WinCareGui {
    [CmdletBinding()]
    param([switch]$ReadOnly,[ValidateSet('Normal','HighContrast','Monochrome')][string]$Theme='Normal')
    if(-not $IsWindows){throw 'The WinCare graphical interface requires Windows 10 or Windows 11.'}
    if([Threading.Thread]::CurrentThread.GetApartmentState() -ne [Threading.ApartmentState]::STA){throw 'The WinCare graphical interface requires an STA PowerShell process. Use WinCare-GUI.ps1 or run-wincare-gui.cmd.'}
    Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
    $created = $false
    $hasMutex = $false
    try {
        $mutex = [Threading.Mutex]::new($true, 'Local\WinCare.Gui.Session', [ref]$created)
        $hasMutex = $true
        if (-not $created) {
            $acquired = $false
            try {
                $acquired = $mutex.WaitOne(100, $false)
            } catch [Threading.AbandonedMutexException] {
                $acquired = $true
            }
            if (-not $acquired) {
                $mutex.Dispose()
                $hasMutex = $false
                # Allow user to open new session cleanly if prior crashed
                Write-Warning 'Another WinCare graphical session flag was detected; continuing with session initialization.'
            }
        }
    } catch {
        $hasMutex = $false
    }
    try{
        Initialize-WinCareState -ReadOnly:$ReadOnly -Theme $Theme
        $catalogTest=Test-WinCareGuiCatalog;if(-not $catalogTest.Success){throw "GUI catalog validation failed: $($catalogTest.Errors -join '; ')"}
        $window=Import-WinCareGuiWindow
        $effectiveTheme=if($Theme -eq 'Normal' -and [Windows.SystemParameters]::HighContrast){'HighContrast'}else{$Theme}
        Set-WinCareGuiTheme -Window $window -Theme $effectiveTheme
        $controls=Get-WinCareGuiControlTable -Window $window;Set-WinCareGuiVersionIdentity -Window $window -Controls $controls
        $actions=@(Get-WinCareGuiActionCatalog|ForEach-Object{ConvertTo-WinCareGuiActionView $_})
        $script:WinCareGuiState=@{Window=$window;Controls=$controls;Actions=$actions;Activity=[Collections.Generic.List[object]]::new();SelectedAction=$null;PendingApplyAction=$null;CommandProcess=$null;ActiveSection='dashboard';ToastHideAt=[datetime]::MinValue;Theme=$effectiveTheme}
        $controls.NavigationList.ItemsSource=@(Get-WinCareGuiNavigation)
        $controls.CategoryFilterComboBox.ItemsSource=@('All','Profiles','Health','Security','Maintenance','Network','Workspace','Assurance','Advanced');$controls.CategoryFilterComboBox.SelectedIndex=0
        $controls.RiskFilterComboBox.ItemsSource=@('All','ReadOnly','Low','Moderate','High','Critical');$controls.RiskFilterComboBox.SelectedIndex=0
        $featured=@($actions|Where-Object Featured|Select-Object -First 6);$controls.QuickActionList.ItemsSource=$featured
        Register-WinCareGuiEvents
        $timer=[Windows.Threading.DispatcherTimer]::new();$timer.Interval=[timespan]::FromMilliseconds(250);$timer.Add_Tick({Complete-WinCareGuiCommandProcess;if($script:WinCareGuiState.ToastHideAt -ne [datetime]::MinValue -and [datetime]::UtcNow -ge $script:WinCareGuiState.ToastHideAt){$script:WinCareGuiState.Controls.ToastBorder.Visibility=[Windows.Visibility]::Collapsed;$script:WinCareGuiState.ToastHideAt=[datetime]::MinValue}});$script:WinCareGuiState.Timer=$timer;$timer.Start()
        $controls.NavigationList.SelectedIndex=0;Update-WinCareGuiActionList;Update-WinCareGuiAssurance;Update-WinCareGuiPolicyState
        $window.Add_ContentRendered({Update-WinCareGuiDashboard})
        Write-WinCareLog -Level Info -Message 'WinCare GUI session started.' -Data @{version=$script:WinCareVersion;actions=$actions.Count;headlessCommands=@(Get-WinCareHeadlessCommandName).Count}
        [void]$window.ShowDialog()
    }finally{
        if($script:WinCareGuiState.ContainsKey('Timer') -and $null -ne $script:WinCareGuiState.Timer){$script:WinCareGuiState.Timer.Stop()}
        if($script:WinCareGuiState.ContainsKey('CommandProcess') -and $script:WinCareGuiState.CommandProcess -and -not $script:WinCareGuiState.CommandProcess.Process.HasExited){try{$script:WinCareGuiState.CommandProcess.Process.Kill($true)}catch{}}
        try{Write-WinCareLog -Level Info -Message 'WinCare GUI session ended.' -Data @{}}catch{}
        try{if($hasMutex -and $null -ne $mutex){$mutex.ReleaseMutex();$mutex.Dispose()}}catch{};$script:WinCareGuiState=@{}
    }
}
