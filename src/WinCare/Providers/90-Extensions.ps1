try { Add-Type -AssemblyName System.Security.Cryptography.Pkcs -ErrorAction Stop } catch { Write-Verbose 'A best-effort operation was unavailable.' }

function Get-WinCareExtensionRoot { Join-Path $script:WinCareState.Root 'Extensions' }

function Test-WinCareExtensionManifest {
    param([Parameter(Mandatory)][Collections.IDictionary]$Manifest)
    $allowed=@('schemaVersion','id','name','version','publisher','description','minimumWinCareVersion','files','catalogFiles','knowledgeFiles','commandFiles','sourceRecords')
    $null=Test-WinCareStrictObjectKeys -InputObject $Manifest -AllowedKeys $allowed -Context 'extension manifest'
    if([int]$Manifest.schemaVersion -ne 1){throw 'Unsupported extension manifest schema.'}
    if([string]$Manifest.id -notmatch '^[a-z0-9][a-z0-9.-]{2,80}$'){throw 'Invalid extension ID.'}
    if([string]::IsNullOrWhiteSpace([string]$Manifest.name) -or ([string]$Manifest.name).Length -gt 120){throw 'Invalid extension name.'}
    if([string]::IsNullOrWhiteSpace([string]$Manifest.publisher) -or ([string]$Manifest.publisher).Length -gt 160){throw 'Invalid extension publisher.'}
    if(([string]$Manifest.description).Length -gt 2000){throw 'Extension description is too long.'}
    $parsedVersion=$null;if(-not [version]::TryParse([string]$Manifest.version,[ref]$parsedVersion)){throw 'Invalid extension version.'}
    $minimum=$null;if(-not [version]::TryParse([string]$Manifest.minimumWinCareVersion,[ref]$minimum)){throw 'Invalid minimum WinCare version.'}
    if($minimum -gt [version]$script:WinCareVersion){throw "Extension requires WinCare $minimum or later."}
    if(@($Manifest.files).Count -lt 1 -or @($Manifest.files).Count -gt 1000){throw 'Extension file count must be 1..1000.'}
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($file in @($Manifest.files)){
        $null=Test-WinCareStrictObjectKeys -InputObject $file -AllowedKeys @('path','sha256','bytes') -Context 'extension file'
        $path=([string]$file.path).Replace('\','/')
        if($path -match '(^/|^[A-Za-z]:|(^|/)\.\.(/|$))' -or $path -notmatch '^[A-Za-z0-9._/-]+$' -or -not $seen.Add($path)){throw "Unsafe, duplicate, or case-colliding extension path: $path"}
        if([string]$file.sha256 -notmatch '^[a-f0-9]{64}$'){throw "Invalid extension hash: $path"}
        if([long]$file.bytes -lt 0 -or [long]$file.bytes -gt 50MB){throw "Invalid extension file size: $path"}
    }
    $declared=@($Manifest.files|ForEach-Object{([string]$_.path).Replace('\','/')})
    foreach($property in @('catalogFiles','knowledgeFiles','commandFiles')){
        $values=@($Manifest[$property]);if($values.Count -gt 128){throw "Extension $property exceeds 128 files."}
        $categorySeen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach($file in $values){
            if($file -isnot [string]){throw "Extension $property entries must be strings."}
            $normalized=([string]$file).Replace('\','/')
            if(-not $categorySeen.Add($normalized)){throw "Extension $property contains a duplicate or case-colliding path: $file"}
            if($normalized -notin $declared){throw "Extension data file is not declared in files: $file"}
            if([IO.Path]::GetExtension($normalized).ToLowerInvariant() -ne '.json'){throw "Extension $property files must be JSON: $file"}
        }
    }
    foreach($record in @($Manifest.sourceRecords)){if([string]$record -notmatch '^[A-Za-z0-9._:-]{2,160}$'){throw "Invalid extension source record: $record"}}
    return $true
}

function Get-WinCareZipEntryKind {
    param([IO.Compression.ZipArchiveEntry]$Entry)
    if([string]::IsNullOrEmpty($Entry.Name)){return 'Directory'}
    $unixMode=($Entry.ExternalAttributes -shr 16) -band 0xF000
    if($unixMode -eq 0xA000){return 'Symlink'}
    if($unixMode -notin @(0,0x8000)){return 'Special'}
    return 'File'
}

function Expand-WinCareExtensionArchive {
    param([Parameter(Mandatory)][string]$ArchivePath,[Parameter(Mandatory)][string]$Destination)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive=[IO.Compression.ZipFile]::OpenRead($ArchivePath);try{
        if($archive.Entries.Count -gt 2000){throw 'Extension archive has too many members.'};$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);$expanded=0L
        foreach($entry in $archive.Entries){$name=$entry.FullName.Replace('\','/');if([string]::IsNullOrWhiteSpace($name)){continue};if($name.StartsWith('/') -or $name -match '^[A-Za-z]:' -or $name.Split('/') -contains '..'){throw "Unsafe archive member: $name"};if(-not $seen.Add($name)){throw "Duplicate or case-colliding archive member: $name"};$kind=Get-WinCareZipEntryKind $entry;if($kind -notin @('File','Directory')){throw "Archive links and special files are not allowed: $name"};$expanded+=[long]$entry.Length;if($expanded -gt 100MB){throw 'Extension archive exceeds the 100 MB expansion limit.'}}
        foreach($entry in $archive.Entries){if([string]::IsNullOrEmpty($entry.Name)){continue};$target=Join-Path $Destination $entry.FullName;$null=Assert-WinCareSafePath -LiteralPath $target -AllowedRoots @($Destination) -AllowMissing;$parent=Split-Path -Parent $target;$null=New-Item -ItemType Directory -Path $parent -Force;$input=$entry.Open();$output=[IO.File]::Create($target);try{$input.CopyTo($output)}finally{$output.Dispose();$input.Dispose()}}
    }finally{$archive.Dispose()}
}

function Test-WinCareDetachedCmsSignature {
    param([Parameter(Mandatory)][byte[]]$Content,[Parameter(Mandatory)][byte[]]$Signature)
    $cms=[Security.Cryptography.Pkcs.SignedCms]::new([Security.Cryptography.Pkcs.ContentInfo]::new($Content),$true);$cms.Decode($Signature);$cms.CheckSignature($true)
    $signer=$cms.SignerInfos|Select-Object -First 1;if(-not $signer -or -not $signer.Certificate){throw 'Extension signature has no signer certificate.'}
    $chain=[Security.Cryptography.X509Certificates.X509Chain]::new();$chain.ChainPolicy.RevocationMode=[Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck;$trusted=$chain.Build($signer.Certificate)
    [pscustomobject]@{Thumbprint=$signer.Certificate.Thumbprint.ToLowerInvariant();Subject=$signer.Certificate.Subject;NotBefore=$signer.Certificate.NotBefore;NotAfter=$signer.Certificate.NotAfter;ChainTrusted=$trusted;ChainStatus=@($chain.ChainStatus.StatusInformation)}
}

function Test-WinCareExtensionPackage {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$ArchivePath)
    $archive=Assert-WinCareSafePath -LiteralPath $ArchivePath;$staging=Join-Path (Join-Path $script:WinCareState.Root 'Cache') ('extension-'+[guid]::NewGuid().ToString('N'))
    $null=New-Item -ItemType Directory -Path $staging
    try{
        Expand-WinCareExtensionArchive -ArchivePath $archive -Destination $staging
        $manifestPath=Join-Path $staging 'extension.json';if(-not(Test-Path -LiteralPath $manifestPath)){throw 'Extension archive is missing extension.json.'}
        $manifest=Read-WinCareJsonHashtable -LiteralPath $manifestPath;$null=Test-WinCareExtensionManifest -Manifest $manifest
        $actualFiles=@(Get-ChildItem -LiteralPath $staging -File -Recurse|ForEach-Object{[IO.Path]::GetRelativePath($staging,$_.FullName).Replace('\','/')}|Where-Object{$_ -notin @('extension.json','extension.p7s')})
        $declared=@($manifest.files.path);$extra=@($actualFiles|Where-Object{$_ -notin $declared});$missing=@($declared|Where-Object{$_ -notin $actualFiles});if($extra.Count -or $missing.Count){throw "Extension file membership mismatch. Extra: $($extra -join ', '); missing: $($missing -join ', ')"}
        foreach($file in @($manifest.files)){if([IO.Path]::GetExtension([string]$file.path).ToLowerInvariant() -in @('.ps1','.psm1','.psd1','.exe','.dll','.com','.bat','.cmd','.js','.jse','.vbs','.vbe','.wsf','.wsh','.msi','.msp','.scr','.cpl','.hta','.lnk','.url')){throw "Executable extension content is forbidden: $($file.path)"};$path=Join-Path $staging $file.path;if((Get-WinCareSha256 $path)-ne [string]$file.sha256){throw "Extension file hash mismatch: $($file.path)"};if([long](Get-Item -LiteralPath $path).Length -ne [long]$file.bytes){throw "Extension file size mismatch: $($file.path)"}}
        $signatureInfo=$null;$signaturePath=Join-Path $staging 'extension.p7s'
        $trusted=@(Get-WinCarePolicy 'TrustedExtensionSigners')|ForEach-Object{$_.ToLowerInvariant()}
        if(Test-Path -LiteralPath $signaturePath){$signatureInfo=Test-WinCareDetachedCmsSignature -Content ([IO.File]::ReadAllBytes($manifestPath)) -Signature ([IO.File]::ReadAllBytes($signaturePath));if($trusted.Count -and $signatureInfo.Thumbprint -notin $trusted){throw 'Extension signer is not on the policy allowlist.'};if(-not $signatureInfo.ChainTrusted -and $trusted.Count -eq 0){throw 'Extension certificate chain is not trusted.'}}
        elseif(-not [bool](Get-WinCarePolicy 'AllowUnsignedExtensions')){throw 'Unsigned extensions are disabled by policy.'}
        if(@($manifest.catalogFiles).Count -gt 0 -and ($null -eq $signatureInfo -or $signatureInfo.Thumbprint -notin $trusted)){throw 'Catalog-changing extensions require a detached CMS signature from an explicitly trusted signer.'}
        return [pscustomobject]@{Valid=$true;ArchivePath=$archive;ArchiveSha256=Get-WinCareSha256 $archive;Manifest=$manifest;StagingPath=$staging;Signature=$signatureInfo}
    }catch{Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue;throw}
}

function New-WinCareInstallExtensionPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$ArchivePath)
    $analysis=Test-WinCareExtensionPackage -ArchivePath $ArchivePath;Remove-Item -LiteralPath $analysis.StagingPath -Recurse -Force -ErrorAction SilentlyContinue
    $existing=Join-Path (Get-WinCareExtensionRoot) ([string]$analysis.Manifest.id);if(Test-Path -LiteralPath $existing){throw 'An extension with this ID is already installed. Remove it through the recoverable removal workflow before installing another version.'}
    $action=New-WinCareAction -Type InstallDeclarativeExtension -Label "Install extension $($analysis.Manifest.name) $($analysis.Manifest.version)" -Risk High -Parameters @{ArchivePath=$analysis.ArchivePath;ArchiveSha256=$analysis.ArchiveSha256;ExtensionId=$analysis.Manifest.id} -RequiresAdmin $false -Reversible $true -Compensator @{Type='RemoveDeclarativeExtension';Parameters=@{ExtensionId=$analysis.Manifest.id}} -Tags @('Extension','SupplyChain') -SourceRecords @('D13-PLUGIN-PROTOCOL','D18-PLUGIN-VALIDATION') -RecoveryDescription 'Remove the installed extension directory and reload the catalog.'
    New-WinCarePlan -Title 'Install declarative WinCare extension' -Actions @($action) -SourceRecords @('D13-PLUGIN-PROTOCOL','D18-PLUGIN-VALIDATION')
}

function Invoke-WinCareInstallExtensionAction {
    param([object]$Action)
    $archive=Assert-WinCareSafePath -LiteralPath ([string]$Action.Parameters.ArchivePath);if((Get-WinCareSha256 $archive)-ne [string]$Action.Parameters.ArchiveSha256){return New-WinCareResult -Success $false -Message 'Extension package changed after preview.' -ExitCode 74}
    $analysis=Test-WinCareExtensionPackage -ArchivePath $archive;$extensionRoot=Get-WinCareExtensionRoot;$target=Assert-WinCareSafePath -LiteralPath (Join-Path $extensionRoot ([string]$analysis.Manifest.id)) -AllowedRoots @($extensionRoot) -AllowMissing
    if(Test-Path -LiteralPath $target){Remove-Item -LiteralPath $analysis.StagingPath -Recurse -Force -ErrorAction SilentlyContinue;return New-WinCareResult -Success $false -Status Blocked -Code 'ExtensionAlreadyInstalled' -Message 'An extension with this ID is already installed.' -ExitCode 17}
    try{
        Move-Item -LiteralPath $analysis.StagingPath -Destination $target
        $null=$script:WinCareState.Remove('catalog');$null=$script:WinCareState.Remove('knowledge')
        $loaded=Get-WinCareInstalledExtension -Id ([string]$analysis.Manifest.id)|Select-Object -First 1;if(-not $loaded){throw 'Installed extension did not reload successfully.'}
        New-WinCareResult -Success $true -Message 'Declarative extension installed and verified.' -Data @{Id=$loaded.id;Version=$loaded.version;Path=$target;TreeHash=Get-WinCarePathContentHash $target;Signer=$analysis.Signature}
    }catch{if(Test-Path -LiteralPath $target){Remove-Item -LiteralPath $target -Recurse -Force};throw}
}

function New-WinCareRemoveExtensionPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$ExtensionId)
    $extension=Get-WinCareInstalledExtension -Id $ExtensionId|Select-Object -First 1
    if(-not $extension){throw "Extension is not installed: $ExtensionId"}
    $source=Assert-WinCareSafePath -LiteralPath ([string]$extension.path) -AllowedRoots @((Get-WinCareExtensionRoot))
    $destination=Join-Path (Join-Path $script:WinCareState.Root 'Quarantine') ("extension-{0}-{1}" -f $ExtensionId,[guid]::NewGuid().ToString('N'))
    $hash=Get-WinCarePathContentHash $source
    $action=New-WinCareAction -Type MoveFile -Label "Quarantine extension $($extension.name)" -Risk Moderate -Parameters @{Source=$source;Destination=$destination;ExpectedSourceHash=$hash;AllowedRoots=@((Get-WinCareExtensionRoot),(Join-Path $script:WinCareState.Root 'Quarantine'))} -Reversible $true -Compensator @{Type='MoveFile';Parameters=@{Source=$destination;Destination=$source;ExpectedSourceHash=$hash;AllowedRoots=@((Get-WinCareExtensionRoot),(Join-Path $script:WinCareState.Root 'Quarantine'))}} -Tags @('Extension','Quarantine') -SourceRecords @('D13-PLUGIN-PROTOCOL','D18-PLUGIN-VALIDATION') -RecoveryDescription 'Move the exact integrity-verified extension directory back from quarantine.'
    New-WinCarePlan -Title "Remove extension $($extension.name)" -Description 'Moves the extension into WinCare quarantine so it can be restored through Undo.' -Actions @($action) -SourceRecords @('D13-PLUGIN-PROTOCOL','D18-PLUGIN-VALIDATION')
}

function Invoke-WinCareRemoveExtensionAction {
    param([object]$Action)
    $id=[string]$Action.Parameters.ExtensionId;if($id -notmatch '^[a-z0-9][a-z0-9.-]{2,80}$'){return New-WinCareResult -Success $false -Message 'Invalid extension ID.' -ExitCode 22}
    $root=Get-WinCareExtensionRoot;$target=Assert-WinCareSafePath -LiteralPath (Join-Path $root $id) -AllowedRoots @($root) -AllowMissing;if(Test-Path -LiteralPath $target){Remove-Item -LiteralPath $target -Recurse -Force}
    $null=$script:WinCareState.Remove('catalog');$null=$script:WinCareState.Remove('knowledge')
    New-WinCareResult -Success (-not(Test-Path -LiteralPath $target)) -Message 'Declarative extension removed.' -Data @{Id=$id}
}

function Get-WinCareInstalledExtension {
    [CmdletBinding()]param([string]$Id)
    if($Id -and $Id -notmatch '^[a-z0-9][a-z0-9.-]{2,80}$'){throw 'Invalid extension ID.'}
    $root=Get-WinCareExtensionRoot;$directories=if($Id){@(Join-Path $root $Id)}else{@(Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue|Select-Object -ExpandProperty FullName)}
    $results=[Collections.Generic.List[object]]::new()
    foreach($directory in $directories){
        $manifestPath=Join-Path $directory 'extension.json';if(-not(Test-Path -LiteralPath $manifestPath -PathType Leaf)){continue}
        try{
            $safe=Assert-WinCareSafePath -LiteralPath $directory -AllowedRoots @($root)
            $manifest=Read-WinCareJsonHashtable -LiteralPath $manifestPath;$null=Test-WinCareExtensionManifest -Manifest $manifest
            if([string]$manifest.id -ne (Split-Path -Leaf $safe)){throw 'Installed extension directory does not match its manifest ID.'}
            foreach($file in @($manifest.files)){$path=Assert-WinCareSafePath -LiteralPath (Join-Path $safe ([string]$file.path)) -AllowedRoots @($safe);if((Get-WinCareSha256 $path)-ne [string]$file.sha256 -or [long](Get-Item -LiteralPath $path).Length-ne[long]$file.bytes){throw "Installed extension file verification failed: $($file.path)"}}
            $signatureInfo=$null;$signaturePath=Join-Path $safe 'extension.p7s';$trusted=@(Get-WinCarePolicy 'TrustedExtensionSigners')|ForEach-Object{$_.ToLowerInvariant()}
            if(Test-Path -LiteralPath $signaturePath -PathType Leaf){$signatureInfo=Test-WinCareDetachedCmsSignature -Content ([IO.File]::ReadAllBytes($manifestPath)) -Signature ([IO.File]::ReadAllBytes($signaturePath));if($trusted.Count -and $signatureInfo.Thumbprint -notin $trusted){throw 'Installed extension signer is no longer trusted.'};if(-not $signatureInfo.ChainTrusted -and $trusted.Count -eq 0){throw 'Installed extension certificate chain is not trusted.'}}
            elseif(-not[bool](Get-WinCarePolicy 'AllowUnsignedExtensions')){throw 'Installed unsigned extension is disabled by current policy.'}
            if(@($manifest.catalogFiles).Count -gt 0 -and ($null -eq $signatureInfo -or $signatureInfo.Thumbprint -notin $trusted)){throw 'Installed catalog extension lacks an explicitly trusted signer.'}
            $results.Add([pscustomobject]@{id=$manifest.id;name=$manifest.name;version=$manifest.version;publisher=$manifest.publisher;description=$manifest.description;path=$safe;treeHash=Get-WinCarePathContentHash $safe;catalogFiles=@($manifest.catalogFiles);knowledgeFiles=@($manifest.knowledgeFiles);commandFiles=@($manifest.commandFiles);sourceRecords=@($manifest.sourceRecords);signer=$signatureInfo})
        }catch{Write-WinCareLog -Level Warning -Message 'Ignoring an invalid installed extension.' -Data @{path=$directory;error=$_.Exception.Message}}
    }
    @($results)
}

function Get-WinCareExtensionCatalogRule {
    [CmdletBinding()]param()
    $rules=[Collections.Generic.List[object]]::new();foreach($extension in Get-WinCareInstalledExtension){foreach($relative in @($extension.catalogFiles)){$path=Join-Path $extension.path $relative;$doc=Read-WinCareJsonHashtable -LiteralPath $path;$null=Test-WinCareStrictObjectKeys -InputObject $doc -AllowedKeys @('schemaVersion','rules') -Context "extension catalog $($extension.id)";foreach($rule in @($doc.rules)){$null=Test-WinCareCatalogRule -Rule $rule;$rules.Add($rule)}}};@($rules)
}

function Get-WinCareExtensionKnowledgeTopic {
    [CmdletBinding()]param()
    $topics=[Collections.Generic.List[object]]::new();foreach($extension in Get-WinCareInstalledExtension){foreach($relative in @($extension.knowledgeFiles)){$path=Join-Path $extension.path $relative;$doc=Read-WinCareJsonHashtable -LiteralPath $path;$null=Test-WinCareStrictObjectKeys -InputObject $doc -AllowedKeys @('schemaVersion','topics') -Context "extension knowledge $($extension.id)";foreach($topic in @($doc.topics)){$topics.Add($topic)}}};@($topics)
}


function Get-WinCareExtensionCommand {
    [CmdletBinding()]param()
    $allowedActions=@('Dashboard','Quick','Applications','Cleanup','Storage','Profiles','Desktop','Startup','Health','Security','Wua','Updates','Network','Internals','ExpertLab','WDAC','Baseline','Provisioning','Offline','Boot','Shell','Customization','Widgets','Bluetooth','Maintenance','Playbooks','Automation','Recovery','Reports','Knowledge','Settings','Help','Power','Windows','Color','Notes','Browser','RemoteSupport')
    $commands=[Collections.Generic.List[object]]::new();$ids=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($extension in Get-WinCareInstalledExtension){
        foreach($relative in @($extension.commandFiles)){
            $path=Assert-WinCareSafePath -LiteralPath (Join-Path $extension.path $relative) -AllowedRoots @($extension.path)
            $doc=Read-WinCareJsonHashtable -LiteralPath $path;$null=Test-WinCareStrictObjectKeys -InputObject $doc -AllowedKeys @('schemaVersion','commands') -Context "extension commands $($extension.id)"
            if([int]$doc.schemaVersion -ne 1 -or @($doc.commands).Count -gt 64){throw "Extension command document is invalid: $relative"}
            foreach($command in @($doc.commands)){
                $null=Test-WinCareStrictObjectKeys -InputObject $command -AllowedKeys @('id','label','description','keywords','action') -Context 'extension command'
                if([string]$command.id -notmatch '^[a-z0-9][a-z0-9.-]{2,80}$' -or -not $ids.Add("$($extension.id):$($command.id)")){throw 'Extension command identity is invalid or duplicated.'}
                if([string]::IsNullOrWhiteSpace([string]$command.label) -or ([string]$command.label).Length -gt 120 -or ([string]$command.description).Length -gt 500 -or ([string]$command.keywords).Length -gt 500){throw 'Extension command text is invalid.'}
                if([string]$command.action -notin $allowedActions){throw "Extension command action is not an approved workspace route: $($command.action)"}
                $commands.Add([pscustomobject]@{Id="$($extension.id):$($command.id)";Label=[string]$command.label;Description=[string]$command.description;Keywords=[string]$command.keywords;Action=[string]$command.action;ExtensionId=[string]$extension.id})
            }
        }
    }
    @($commands)
}


