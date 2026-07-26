function Get-WinCareBrowserDefinition {
    @(
        [pscustomobject]@{Id='edge';Name='Microsoft Edge';ProcessNames=@('msedge');Kind='Chromium';Roots=@("${env}:LOCALAPPDATA\Microsoft\Edge\User Data")},
        [pscustomobject]@{Id='chrome';Name='Google Chrome';ProcessNames=@('chrome');Kind='Chromium';Roots=@("${env}:LOCALAPPDATA\Google\Chrome\User Data")},
        [pscustomobject]@{Id='brave';Name='Brave';ProcessNames=@('brave');Kind='Chromium';Roots=@("${env}:LOCALAPPDATA\BraveSoftware\Brave-Browser\User Data")},
        [pscustomobject]@{Id='vivaldi';Name='Vivaldi';ProcessNames=@('vivaldi');Kind='Chromium';Roots=@("${env}:LOCALAPPDATA\Vivaldi\User Data")},
        [pscustomobject]@{Id='firefox';Name='Mozilla Firefox';ProcessNames=@('firefox');Kind='Firefox';Roots=@("${env}:APPDATA\Mozilla\Firefox\Profiles")}
    )
}

function Get-WinCareBrowserPermissionRisk {
    param([string[]]$Permissions)
    $values=@($Permissions|ForEach-Object{[string]$_}|Where-Object{$_}|Sort-Object -Unique)
    $critical=@('debugger','nativeMessaging','management','proxy')
    $high=@('<all_urls>','webRequest','webRequestBlocking','cookies','history','downloads','clipboardRead','privacy')
    $moderate=@('tabs','activeTab','clipboardWrite','bookmarks','notifications')
    if(@($values|Where-Object{$_ -in $critical}).Count){return 'Critical'}
    if(@($values|Where-Object{$_ -in $high -or $_ -match '^(\*|https?|file|ftp)://\*/\*' -or $_ -match '^\*://'}).Count){return 'High'}
    if(@($values|Where-Object{$_ -in $moderate}).Count){return 'Moderate'}
    'Low'
}

function Get-WinCareBrowserConfinedDirectory {
    param([Parameter(Mandatory)][string]$LiteralPath,[Parameter(Mandatory)][string]$Root)
    if(-not(Test-Path -LiteralPath $LiteralPath -PathType Container)){return $null}
    try{
        $safe=Assert-WinCareSafePath -LiteralPath $LiteralPath -AllowedRoots @($Root)
        $item=Get-Item -LiteralPath $safe -Force -ErrorAction Stop
        if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0){return $null}
        $safe
    }catch{
        Write-WinCareLog -Level Warning -Message 'Skipping an unsafe browser workspace directory.' -Data @{path=$LiteralPath;error=$_.Exception.Message}
        $null
    }
}

function Get-WinCareBrowserExtensionInventory {
    [CmdletBinding()]param([string]$BrowserId,[int]$Limit=5000)
    if($Limit -lt 1 -or $Limit -gt 50000){throw 'Browser extension inventory limit is outside 1..50000.'}
    $results=[Collections.Generic.List[object]]::new();$visitedCandidates=0;$scanLimit=[math]::Min(50000,[math]::Max($Limit,$Limit*4))
    foreach($browser in Get-WinCareBrowserDefinition|Where-Object{-not $BrowserId -or $_.Id -eq $BrowserId}){
        foreach($rawRoot in @($browser.Roots|Where-Object{$_ -and (Test-Path -LiteralPath $_ -PathType Container)})){
            $root=Get-WinCareBrowserConfinedDirectory -LiteralPath $rawRoot -Root $rawRoot
            if(-not $root){continue}
            try{$profileCandidates=@(Get-ChildItem -LiteralPath $root -Directory -Force -ErrorAction Stop|Where-Object{($_.Attributes -band [IO.FileAttributes]::ReparsePoint)-eq 0}|Select-Object -First 1024)}
            catch{Write-WinCareLog -Level Warning -Message 'Browser profile inventory was unavailable.' -Data @{path=$root;error=$_.Exception.Message};continue}
            if($browser.Kind -eq 'Chromium'){
                foreach($profileItem in $profileCandidates){
                    $profile=Get-WinCareBrowserConfinedDirectory -LiteralPath $profileItem.FullName -Root $root;if(-not $profile){continue}
                    $extensionRoot=Get-WinCareBrowserConfinedDirectory -LiteralPath (Join-Path $profile 'Extensions') -Root $profile;if(-not $extensionRoot){continue}
                    try{$idCandidates=@(Get-ChildItem -LiteralPath $extensionRoot -Directory -Force -ErrorAction Stop|Where-Object{($_.Attributes -band [IO.FileAttributes]::ReparsePoint)-eq 0}|Select-Object -First 10000)}
                    catch{Write-WinCareLog -Level Warning -Message 'Chromium extension directory inventory was unavailable.' -Data @{path=$extensionRoot;error=$_.Exception.Message};continue}
                    foreach($idItem in $idCandidates){
                        if($visitedCandidates -ge $scanLimit -or $results.Count -ge $Limit){return @($results)}
                        $idPath=Get-WinCareBrowserConfinedDirectory -LiteralPath $idItem.FullName -Root $extensionRoot;if(-not $idPath){continue}
                        try{$versions=@(Get-ChildItem -LiteralPath $idPath -Directory -Force -ErrorAction Stop|Where-Object{($_.Attributes -band [IO.FileAttributes]::ReparsePoint)-eq 0}|Sort-Object Name -Descending|Select-Object -First 64)}
                        catch{Write-WinCareLog -Level Warning -Message 'Chromium extension version inventory was unavailable.' -Data @{path=$idPath;error=$_.Exception.Message};continue}
                        $versionItem=$versions|Select-Object -First 1;if(-not $versionItem){continue}
                        $versionPath=Get-WinCareBrowserConfinedDirectory -LiteralPath $versionItem.FullName -Root $idPath;if(-not $versionPath){continue}
                        $manifestPath=Join-Path $versionPath 'manifest.json';$visitedCandidates++
                        try{
                            $manifestPath=Assert-WinCareSafePath -LiteralPath $manifestPath -AllowedRoots @($profile)
                            $manifestItem=Get-Item -LiteralPath $manifestPath -Force -ErrorAction Stop
                            if(($manifestItem.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0 -or $manifestItem.Length -gt 2MB){continue}
                            $manifest=Read-WinCareBoundedJson -LiteralPath $manifestPath -MaximumBytes 2097152 -AsHashtable -Depth 40
                            $permissions=@(@($manifest.permissions)+@($manifest.host_permissions)+@($manifest.optional_permissions)+@($manifest.optional_host_permissions)|Where-Object{$_ -is [string]}|Sort-Object -Unique)
                            $results.Add([pscustomobject]@{BrowserId=$browser.Id;Browser=$browser.Name;Profile=$profileItem.Name;ExtensionId=$idItem.Name;Name=[string]$manifest.name;Version=[string]$manifest.version;ManifestVersion=[int]$manifest.manifest_version;Permissions=$permissions;PermissionRisk=Get-WinCareBrowserPermissionRisk $permissions;Path=$versionPath;PathConfined=$true;ManifestSha256=Get-WinCareSha256 $manifestPath;Unpacked=$false;ValuesCollected=$false})
                        }catch{Write-WinCareLog -Level Warning -Message 'Ignoring an invalid or unsafe browser extension manifest.' -Data @{path=$manifestPath;error=$_.Exception.Message}}
                    }
                }
            }else{
                foreach($profileItem in $profileCandidates){
                    if($visitedCandidates -ge $scanLimit -or $results.Count -ge $Limit){return @($results)}
                    $profile=Get-WinCareBrowserConfinedDirectory -LiteralPath $profileItem.FullName -Root $root;if(-not $profile){continue}
                    $extensionsPath=Join-Path $profile 'extensions.json'
                    try{
                        $extensionsPath=Assert-WinCareSafePath -LiteralPath $extensionsPath -AllowedRoots @($profile)
                        $extensionsItem=Get-Item -LiteralPath $extensionsPath -Force -ErrorAction Stop
                        if(($extensionsItem.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0 -or $extensionsItem.Length -gt 10MB){continue}
                        $doc=Read-WinCareBoundedJson -LiteralPath $extensionsPath -MaximumBytes 10485760 -AsHashtable -Depth 60
                        foreach($addon in @($doc.addons)){
                            if(-not $addon){continue};$visitedCandidates++
                            if($visitedCandidates -gt $scanLimit -or $results.Count -ge $Limit){return @($results)}
                            $permissions=@(@($addon.userPermissions.permissions)+@($addon.userPermissions.origins)|Where-Object{$_ -is [string]}|Sort-Object -Unique)
                            $confinedPath='';$confinedHash='';$pathConfined=$false
                            if([string]$addon.path -and (Test-Path -LiteralPath ([string]$addon.path) -PathType Leaf)){
                                try{
                                    $candidate=Assert-WinCareSafePath -LiteralPath ([string]$addon.path) -AllowedRoots @($profile)
                                    $candidateItem=Get-Item -LiteralPath $candidate -Force -ErrorAction Stop
                                    if(($candidateItem.Attributes -band [IO.FileAttributes]::ReparsePoint)-eq 0){$confinedPath=$candidate;$confinedHash=Get-WinCareSha256 $candidate;$pathConfined=$true}
                                }catch{Write-WinCareLog -Level Warning -Message 'Firefox extension package is outside the confined profile and was not hashed.' -Data @{profile=$profile;error=$_.Exception.Message}}
                            }
                            $results.Add([pscustomobject]@{BrowserId=$browser.Id;Browser=$browser.Name;Profile=$profileItem.Name;ExtensionId=[string]$addon.id;Name=[string]$addon.defaultLocale.name;Version=[string]$addon.version;ManifestVersion=2;Permissions=$permissions;PermissionRisk=Get-WinCareBrowserPermissionRisk $permissions;Path=$confinedPath;PathConfined=$pathConfined;ManifestSha256=$confinedHash;Unpacked=[bool]($addon.location -eq 'app-profile');ValuesCollected=$false})
                        }
                    }catch{Write-WinCareLog -Level Warning -Message 'Ignoring an invalid or unsafe Firefox extension database.' -Data @{path=$extensionsPath;error=$_.Exception.Message}}
                }
            }
        }
    }
    @($results)
}

function Get-WinCareBrowserWorkspaceSummary {
    [CmdletBinding()]param()
    $windows=if(Get-Command Get-WinCareWindowInventory -ErrorAction SilentlyContinue){@(Get-WinCareWindowInventory)}else{@()};$extensions=@(Get-WinCareBrowserExtensionInventory -Limit ([int](Get-WinCareConfig 'BrowserExtensionInventoryLimit')));$results=[Collections.Generic.List[object]]::new()
    foreach($browser in Get-WinCareBrowserDefinition){
        $processes=@(Get-Process -ErrorAction SilentlyContinue|Where-Object ProcessName -in @($browser.ProcessNames));$browserWindows=@($windows|Where-Object ProcessName -in @($browser.ProcessNames));$browserExtensions=@($extensions|Where-Object BrowserId -eq $browser.Id);$profileRoots=@($browser.Roots|Where-Object{$_ -and (Test-Path -LiteralPath $_ -PathType Container)})
        $profileCount=0;foreach($root in $profileRoots){if($browser.Kind -eq 'Chromium'){$profileCount+=@(Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue|Where-Object{$_.Name -eq 'Default' -or $_.Name -like 'Profile *'}).Count}else{$profileCount+=@(Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue).Count}}
        if($processes.Count -or $browserExtensions.Count -or $profileCount){$results.Add([pscustomobject]@{Id=$browser.Id;Name=$browser.Name;Installed=$profileRoots.Count -gt 0;RunningProcesses=$processes.Count;VisibleWindows=$browserWindows.Count;Profiles=$profileCount;Extensions=$browserExtensions.Count;HighRiskExtensions=@($browserExtensions|Where-Object PermissionRisk -in @('High','Critical')).Count;WindowTitles=@($browserWindows.Title|Where-Object{$_}|Select-Object -First 50);TabCountAvailable=$false;TabCountRationale='WinCare does not enable browser debugging or install an extension merely to count tabs.'})}
    }
    @($results)
}


