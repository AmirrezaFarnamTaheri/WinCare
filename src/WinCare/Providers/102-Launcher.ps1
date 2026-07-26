function Get-WinCareLaunchIndexRoots {
    $roots=[Collections.Generic.List[string]]::new()
    foreach($path in @(
        (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'),
        (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs')
    )){if($path){$roots.Add((Resolve-WinCareCanonicalPath -LiteralPath $path -AllowMissing))}}
    @($roots|Sort-Object -Unique)
}

function Get-WinCareLaunchTargetHash {
    param([Parameter(Mandatory)][string]$Kind,[Parameter(Mandatory)][string]$Target)
    if($Kind -in @('Shortcut','Executable','ControlPanel')){if(-not(Test-Path -LiteralPath $Target -PathType Leaf)){return 'absent'};return Get-WinCareSha256 -LiteralPath $Target}
    Get-WinCareSha256Text -Text "$Kind`n$Target"
}

function New-WinCareLaunchTargetRecord {
    param([string]$Kind,[string]$Name,[string]$Target,[string]$Category,[string[]]$Keywords=@(),[string]$Source='')
    $hash=Get-WinCareLaunchTargetHash -Kind $Kind -Target $Target
    $id=Get-WinCareSha256Text -Text "$Kind`n$Target"
    [pscustomobject]@{Id=$id;Kind=$Kind;Name=$Name;Target=$Target;Category=$Category;Keywords=@($Keywords);Source=$Source;TargetHash=$hash}
}

function Get-WinCareBoundedLaunchShortcutFile {
    param([ValidateRange(1,20000)][int]$MaximumFiles=10000,[ValidateRange(1,32)][int]$MaximumDepth=16)
    $results=[Collections.Generic.List[string]]::new();$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($root in Get-WinCareLaunchIndexRoots){
        if(-not(Test-Path -LiteralPath $root -PathType Container)){continue}
        $queue=[Collections.Generic.Queue[object]]::new();$queue.Enqueue([pscustomobject]@{Path=$root;Depth=0})
        while($queue.Count -and $results.Count -lt $MaximumFiles){
            $current=$queue.Dequeue();if([int]$current.Depth -gt $MaximumDepth){continue}
            try{$items=@(Get-ChildItem -LiteralPath $current.Path -Force -ErrorAction Stop)}catch{Write-WinCareLog -Level Warning -Message 'Launcher index skipped an unreadable directory.' -Data @{path=$current.Path;error=$_.Exception.Message};continue}
            foreach($item in $items){
                if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){continue}
                $path=Resolve-WinCareCanonicalPath -LiteralPath $item.FullName -AllowMissing
                if(-not(Test-WinCarePathWithin -Child $path -Parent $root -AllowEqual)){continue}
                if($item.PSIsContainer){if($seen.Add($path)){$queue.Enqueue([pscustomobject]@{Path=$path;Depth=([int]$current.Depth+1)})}}
                elseif($item.Extension -ieq '.lnk'){$results.Add($path);if($results.Count -ge $MaximumFiles){break}}
            }
        }
    }
    @($results)
}

function Get-WinCareLaunchTarget {
    [CmdletBinding()]param([string]$Id,[string]$Query='',[ValidateRange(1,5000)][int]$Limit=500)
    if(-not $IsWindows){return @()}
    $records=[Collections.Generic.List[object]]::new()
    foreach($shortcut in Get-WinCareBoundedLaunchShortcutFile -MaximumFiles ([math]::Min(20000,[int](Get-WinCareConfig 'LauncherIndexLimit')))){
        $name=[IO.Path]::GetFileNameWithoutExtension($shortcut);$records.Add((New-WinCareLaunchTargetRecord -Kind Shortcut -Name $name -Target $shortcut -Category 'Start menu' -Keywords @('app','start','shortcut') -Source 'StartMenu'))
    }
    foreach($root in @('HKCU:\Software\Microsoft\Windows\CurrentVersion\App Paths\*','HKLM:\Software\Microsoft\Windows\CurrentVersion\App Paths\*')){
        foreach($entry in @(Get-ItemProperty $root -ErrorAction SilentlyContinue)){
            $path=[string](Get-WinCarePropertyValue $entry '(default)' '')
            if(-not $path){$path=[string](Get-WinCarePropertyValue $entry '' '')}
            $path=$path.Trim('"')
            if($path -and (Test-Path -LiteralPath $path -PathType Leaf)){$records.Add((New-WinCareLaunchTargetRecord -Kind Executable -Name ([IO.Path]::GetFileNameWithoutExtension($entry.PSChildName)) -Target (Resolve-Path -LiteralPath $path).Path -Category 'Application path' -Keywords @('app','executable') -Source $root))}
        }
    }
    $system32=Join-Path $env:SystemRoot 'System32'
    if(Test-Path -LiteralPath $system32){foreach($cpl in @(Get-ChildItem -LiteralPath $system32 -Filter '*.cpl' -File -ErrorAction SilentlyContinue|Select-Object -First 256)){$records.Add((New-WinCareLaunchTargetRecord -Kind ControlPanel -Name ([IO.Path]::GetFileNameWithoutExtension($cpl.Name)) -Target $cpl.FullName -Category 'Control Panel' -Keywords @('control','settings','panel') -Source 'System32'))}}
    $settings=[ordered]@{
        'Windows Settings'='ms-settings:';'Installed apps'='ms-settings:appsfeatures';'Storage'='ms-settings:storagesense';'Windows Update'='ms-settings:windowsupdate';'Windows Security'='windowsdefender:';'Network settings'='ms-settings:network-status';'Bluetooth'='ms-settings:bluetooth';'Display settings'='ms-settings:display';'Power settings'='ms-settings:powersleep';'Default apps'='ms-settings:defaultapps'
    }
    foreach($name in $settings.Keys){$records.Add((New-WinCareLaunchTargetRecord -Kind SettingsUri -Name $name -Target $settings[$name] -Category 'Windows settings' -Keywords @('settings','windows') -Source 'BuiltIn'))}
    $unique=@($records|Group-Object Id|ForEach-Object{$_.Group|Select-Object -First 1})
    if($Id){$unique=@($unique|Where-Object Id -eq $Id)}
    if($Query){
        $needle=$Query.Trim().ToLowerInvariant();$unique=@($unique|ForEach-Object{$hay=("$($_.Name) $($_.Category) $($_.Keywords -join ' ')").ToLowerInvariant();$score=0;if($hay.StartsWith($needle)){$score+=100};if($hay -like "*$needle*"){$score+=50};$cursor=0;foreach($ch in $needle.ToCharArray()){$pos=$hay.IndexOf($ch,$cursor);if($pos -lt 0){$score=0;break};$score+=2;$cursor=$pos+1};if($score -gt 0){$_|Add-Member -NotePropertyName Score -NotePropertyValue $score -Force;$_}}|Sort-Object @{Expression='Score';Descending=$true},Name)
    }
    @($unique|Select-Object -First $Limit)
}

function Resolve-WinCareLaunchTarget {
    param([Parameter(Mandatory)][string]$Id)
    $target=Get-WinCareLaunchTarget -Id $Id -Limit 1|Select-Object -First 1
    if(-not $target){throw 'Launch target is no longer present in the current index.'}
    $target
}

function New-WinCareOpenLaunchTargetPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$Id)
    $target=Resolve-WinCareLaunchTarget $Id
    $action=New-WinCareAction -Type OpenLaunchTarget -Label "Open: $($target.Name)" -Risk Low -Parameters @{Id=$target.Id;Kind=$target.Kind;Target=$target.Target;ExpectedHash=$target.TargetHash} -Tags @('Launcher','Explicit') -SourceRecords @('SRC:4a9fce4f41') -TimeoutSeconds 60
    New-WinCarePlan -Title "Open $($target.Name)" -Description 'Open an exact target from the current local launcher index. Arbitrary command strings are not accepted.' -Actions @($action) -SourceRecords @('SRC:4a9fce4f41')
}

function Invoke-WinCareOpenLaunchTargetAction {
    param([Parameter(Mandatory)][object]$Action)
    try{$target=Resolve-WinCareLaunchTarget ([string]$Action.Parameters.Id)}catch{return New-WinCareResult -Success $false -Status Blocked -Code 'LaunchTargetMissing' -Message $_.Exception.Message -ExitCode 2}
    if([string]$target.Kind -ne [string]$Action.Parameters.Kind -or [string]$target.Target -ne [string]$Action.Parameters.Target -or [string]$target.TargetHash -ne [string]$Action.Parameters.ExpectedHash){return New-WinCareResult -Success $false -Status Blocked -Code 'LaunchTargetChanged' -Message 'Launch target changed after preview.' -ExitCode 74}
    try{
        switch([string]$target.Kind){
            'Shortcut'{$process=Start-WinCareDetachedProcess -FilePath 'explorer.exe' -ArgumentList @($target.Target);if(-not $process.Success){throw $process.Message};$result=$process.Data}
            'Executable'{$process=Start-WinCareDetachedProcess -FilePath $target.Target;if(-not $process.Success){throw $process.Message};$result=$process.Data}
            'ControlPanel'{$process=Start-WinCareDetachedProcess -FilePath 'control.exe' -ArgumentList @($target.Target);if(-not $process.Success){throw $process.Message};$result=$process.Data}
            'SettingsUri'{$process=Start-WinCareDetachedProcess -FilePath 'explorer.exe' -ArgumentList @($target.Target);if(-not $process.Success){throw $process.Message};$result=$process.Data}
            default{throw 'Unsupported launch target kind.'}
        }
        New-WinCareResult -Success $true -Code 'LaunchTargetOpened' -Message "Opened $($target.Name)." -Data @{Target=$target;Process=$result}
    }catch{New-WinCareResult -Success $false -Code 'LaunchTargetFailed' -Message $_.Exception.Message -ExitCode 1}
}

function Get-WinCareArithmeticToken {
    param([Parameter(Mandatory)][string]$Expression)
    if($Expression.Length -gt 512){throw 'Calculator expression is too long.'}
    $matches=[regex]::Matches($Expression,'\G\s*(?:(?<number>(?:\d+(?:\.\d*)?|\.\d+))|(?<operator>[+\-*/%()]))')
    $tokens=[Collections.Generic.List[object]]::new();$position=0
    foreach($match in $matches){if($match.Index -ne $position){throw "Unsupported calculator token near position $position."};$position=$match.Index+$match.Length;if($match.Groups['number'].Success){$tokens.Add([pscustomobject]@{Kind='Number';Value=[double]::Parse($match.Groups['number'].Value,[Globalization.CultureInfo]::InvariantCulture)})}else{$tokens.Add([pscustomobject]@{Kind='Operator';Value=$match.Groups['operator'].Value})}}
    if($position -ne $Expression.Length){throw "Unsupported calculator token near position $position."};if($tokens.Count -gt 256){throw 'Calculator token limit exceeded.'};@($tokens)
}

function Invoke-WinCareCalculator {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$Expression)
    $tokens=Get-WinCareArithmeticToken $Expression;$values=[Collections.Generic.Stack[double]]::new();$ops=[Collections.Generic.Stack[string]]::new()
    function Get-Precedence([string]$op){if($op -in @('*','/','%')){2}elseif($op -in @('+','-')){1}else{0}}
    function Apply-Operator([Collections.Generic.Stack[double]]$v,[Collections.Generic.Stack[string]]$o){if($v.Count -lt 2 -or $o.Count -lt 1){throw 'Calculator expression is incomplete.'};$right=$v.Pop();$left=$v.Pop();$op=$o.Pop();switch($op){'+'{$v.Push($left+$right)};'-'{$v.Push($left-$right)};'*'{$v.Push($left*$right)};'/'{if($right -eq 0){throw 'Division by zero.'};$v.Push($left/$right)};'%'{if($right -eq 0){throw 'Modulo by zero.'};$v.Push($left%$right)};default{throw 'Unknown calculator operator.'}}}
    $expectValue=$true
    foreach($token in $tokens){
        if($token.Kind -eq 'Number'){if(-not $expectValue){throw 'Missing operator between values.'};$values.Push([double]$token.Value);$expectValue=$false;continue}
        $op=[string]$token.Value
        if($op -eq '('){if(-not $expectValue){throw 'Missing operator before parenthesis.'};$ops.Push($op);continue}
        if($op -eq ')'){if($expectValue){throw 'Closing parenthesis is misplaced.'};while($ops.Count -and $ops.Peek() -ne '('){Apply-Operator $values $ops};if(-not $ops.Count){throw 'Unbalanced parenthesis.'};$null=$ops.Pop();$expectValue=$false;continue}
        if($expectValue){if($op -eq '-'){$values.Push(0.0)}else{throw 'Operator is misplaced.'}}
        while($ops.Count -and $ops.Peek() -ne '(' -and (Get-Precedence $ops.Peek()) -ge (Get-Precedence $op)){Apply-Operator $values $ops}
        $ops.Push($op);$expectValue=$true
    }
    if($expectValue){throw 'Calculator expression is incomplete.'};while($ops.Count){if($ops.Peek() -eq '('){throw 'Unbalanced parenthesis.'};Apply-Operator $values $ops};if($values.Count -ne 1){throw 'Calculator expression could not be reduced.'}
    [pscustomobject]@{Expression=$Expression;Value=$values.Pop()}
}


