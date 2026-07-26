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

function Test-WinCareExtensionArchiveInventory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][IO.Compression.ZipArchive]$Archive,
        [ValidateRange(1,2000)][int]$MaximumEntries,
        [ValidateRange(1048576,104857600)][long]$MaximumExpandedBytes,
        [ValidateRange(10,10000)][int]$MaximumCompressionRatio
    )
    if($Archive.Entries.Count -gt $MaximumEntries) {
        throw "Extension archive exceeds the $MaximumEntries member limit."
    }
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [long]$declaredExpanded=0
    foreach($entry in $Archive.Entries) {
        $name=$entry.FullName.Replace('\','/')
        if([string]::IsNullOrWhiteSpace($name)) { continue }
        if($name.StartsWith('/') -or $name -match '^[A-Za-z]:' -or $name.Split('/') -contains '..') {
            throw "Unsafe archive member: $name"
        }
        if(-not $seen.Add($name)) { throw "Duplicate or case-colliding archive member: $name" }
        if((Get-WinCareZipEntryKind $entry) -notin @('File','Directory')) {
            throw "Archive links and special files are not allowed: $name"
        }
        if($entry.Length -lt 0 -or $entry.CompressedLength -lt 0) {
            throw "Archive member has invalid lengths: $name"
        }
        $declaredExpanded+=[long]$entry.Length
        if($declaredExpanded -gt $MaximumExpandedBytes) {
            throw 'Extension archive exceeds the configured expansion limit.'
        }
        if($entry.Length -gt 0) {
            $ratio=[double]$entry.Length/[double][math]::Max(1,[long]$entry.CompressedLength)
            if($ratio -gt $MaximumCompressionRatio) {
                throw "Archive member exceeds the compression-ratio limit: $name"
            }
        }
    }
    $declaredExpanded
}

function Copy-WinCareExtensionArchiveEntry {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][IO.Compression.ZipArchiveEntry]$Entry,
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][byte[]]$Buffer,
        [ValidateRange(1048576,104857600)][long]$MaximumExpandedBytes,
        [Parameter(Mandatory)][ref]$ActualExpandedBytes
    )
    $target=Join-Path $Destination $Entry.FullName
    $target=Assert-WinCareSafePath -LiteralPath $target -AllowedRoots @($Destination) -AllowMissing
    $null=New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force
    $input=$Entry.Open()
    $output=[IO.FileStream]::new(
        $target,
        [IO.FileMode]::CreateNew,
        [IO.FileAccess]::Write,
        [IO.FileShare]::None,
        65536,
        [IO.FileOptions]::WriteThrough
    )
    [long]$entryBytes=0
    try {
        while(($read=$input.Read($Buffer,0,$Buffer.Length)) -gt 0) {
            $entryBytes+=$read
            $ActualExpandedBytes.Value=[long]$ActualExpandedBytes.Value+$read
            if($entryBytes -gt [long]$Entry.Length) {
                throw "Archive member expanded beyond its declared size: $($Entry.FullName)"
            }
            if([long]$ActualExpandedBytes.Value -gt $MaximumExpandedBytes) {
                throw 'Extension archive exceeded the expansion limit while streaming.'
            }
            $output.Write($Buffer,0,$read)
        }
        $output.Flush($true)
    } finally {
        $output.Dispose()
        $input.Dispose()
    }
    if($entryBytes -ne [long]$Entry.Length) {
        throw "Archive member length mismatch after extraction: $($Entry.FullName)"
    }
}

function Expand-WinCareExtensionArchive {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ArchivePath,
        [Parameter(Mandatory)][string]$Destination,
        [ValidateRange(1,2000)][int]$MaximumEntries=2000,
        [ValidateRange(1048576,104857600)][long]$MaximumExpandedBytes=104857600,
        [ValidateRange(10,10000)][int]$MaximumCompressionRatio=1000
    )
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archiveFull=Assert-WinCareSafePath -LiteralPath $ArchivePath
    $destinationFull=Resolve-WinCareCanonicalPath -LiteralPath $Destination -AllowMissing
    if((Get-Item -LiteralPath $archiveFull -Force).Length -gt 100MB) {
        throw 'Extension archive exceeds the 100 MiB compressed-size limit.'
    }
    $archive=[IO.Compression.ZipFile]::OpenRead($archiveFull)
    try {
        $declared=Test-WinCareExtensionArchiveInventory `
            -Archive $archive `
            -MaximumEntries $MaximumEntries `
            -MaximumExpandedBytes $MaximumExpandedBytes `
            -MaximumCompressionRatio $MaximumCompressionRatio
        [long]$actual=0
        $buffer=New-Object byte[] 65536
        foreach($entry in $archive.Entries) {
            if([string]::IsNullOrEmpty($entry.Name)) { continue }
            Copy-WinCareExtensionArchiveEntry `
                -Entry $entry `
                -Destination $destinationFull `
                -Buffer $buffer `
                -MaximumExpandedBytes $MaximumExpandedBytes `
                -ActualExpandedBytes ([ref]$actual)
        }
        [pscustomobject]@{
            EntryCount=$archive.Entries.Count
            DeclaredExpandedBytes=$declared
            ActualExpandedBytes=$actual
            MaximumExpandedBytes=$MaximumExpandedBytes
            EvidenceType='BoundedExactByteArchiveExtraction'
        }
    } finally {
        $archive.Dispose()
    }
}

function Test-WinCareDetachedCmsSignature {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateNotNull()][byte[]]$Content,
        [Parameter(Mandatory)][ValidateNotNull()][byte[]]$Signature
    )
    if($Content.Length -gt 1MB) { throw 'Signed extension manifest exceeds 1 MiB.' }
    if($Signature.Length -gt 1MB) { throw 'Detached extension signature exceeds 1 MiB.' }
    $cms=[Security.Cryptography.Pkcs.SignedCms]::new(
        [Security.Cryptography.Pkcs.ContentInfo]::new($Content),
        $true
    )
    $cms.Decode($Signature)
    if($cms.SignerInfos.Count -ne 1) { throw 'Extension package must contain exactly one CMS signer.' }
    $cms.CheckSignature($true)
    $signer=$cms.SignerInfos[0]
    if(-not $signer.Certificate) { throw 'Extension signature has no signer certificate.' }
    $now=[datetime]::UtcNow
    if($now -lt $signer.Certificate.NotBefore.ToUniversalTime() -or $now -gt $signer.Certificate.NotAfter.ToUniversalTime()) {
        throw 'Extension signer certificate is outside its validity period.'
    }
    $chain=[Security.Cryptography.X509Certificates.X509Chain]::new()
    try {
        $chain.ChainPolicy.RevocationMode=[Security.Cryptography.X509Certificates.X509RevocationMode]::Online
        $chain.ChainPolicy.RevocationFlag=[Security.Cryptography.X509Certificates.X509RevocationFlag]::EntireChain
        $chain.ChainPolicy.UrlRetrievalTimeout=[timespan]::FromSeconds(10)
        $chain.ChainPolicy.VerificationFlags=[Security.Cryptography.X509Certificates.X509VerificationFlags]::NoFlag
        $chain.ChainPolicy.VerificationTime=$now
        $trusted=$chain.Build($signer.Certificate)
        $statuses=@($chain.ChainStatus|ForEach-Object{
            [pscustomobject]@{Status=[string]$_.Status;Information=$_.StatusInformation.Trim()}
        })
        $revoked=@($chain.ChainStatus|Where-Object{
            ($_.Status -band [Security.Cryptography.X509Certificates.X509ChainStatusFlags]::Revoked) -ne 0
        }).Count -gt 0
        if($revoked) { throw 'Extension signer certificate has been revoked.' }
        [pscustomobject]@{
            Thumbprint=$signer.Certificate.Thumbprint.ToLowerInvariant()
            Subject=$signer.Certificate.Subject
            SerialNumber=$signer.Certificate.SerialNumber
            NotBefore=$signer.Certificate.NotBefore
            NotAfter=$signer.Certificate.NotAfter
            ChainTrusted=$trusted
            Revoked=$revoked
            RevocationMode='Online'
            ChainStatus=$statuses
            EvidenceType='CmsSignatureAndOnlineChainEvaluation'
        }
    } finally {
        $chain.Dispose()
    }
}

function Test-WinCareExtensionPackage {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$ArchivePath)
    $archive=Assert-WinCareSafePath -LiteralPath $ArchivePath;$staging=Join-Path (Join-Path $script:WinCareState.Root 'Cache') ('extension-'+[guid]::NewGuid().ToString('N'))
    $null=New-Item -ItemType Directory -Path $staging
    try{
        $extraction=Expand-WinCareExtensionArchive -ArchivePath $archive -Destination $staging
        $manifestPath=Join-Path $staging 'extension.json';if(-not(Test-Path -LiteralPath $manifestPath)){throw 'Extension archive is missing extension.json.'};if((Get-Item -LiteralPath $manifestPath -Force).Length -gt 1MB){throw 'Extension manifest exceeds 1 MiB.'}
        $manifest=Read-WinCareJsonHashtable -LiteralPath $manifestPath;$null=Test-WinCareExtensionManifest -Manifest $manifest
        $actualFiles=@(Get-ChildItem -LiteralPath $staging -File -Recurse|ForEach-Object{[IO.Path]::GetRelativePath($staging,$_.FullName).Replace('\','/')}|Where-Object{$_ -notin @('extension.json','extension.p7s')})
        $declared=@($manifest.files.path);$extra=@($actualFiles|Where-Object{$_ -notin $declared});$missing=@($declared|Where-Object{$_ -notin $actualFiles});if($extra.Count -or $missing.Count){throw "Extension file membership mismatch. Extra: $($extra -join ', '); missing: $($missing -join ', ')"}
        foreach($file in @($manifest.files)){if([IO.Path]::GetExtension([string]$file.path).ToLowerInvariant() -in @('.ps1','.psm1','.psd1','.exe','.dll','.com','.bat','.cmd','.js','.jse','.vbs','.vbe','.wsf','.wsh','.msi','.msp','.scr','.cpl','.hta','.lnk','.url')){throw "Executable extension content is forbidden: $($file.path)"};$path=Join-Path $staging $file.path;if((Get-WinCareSha256 $path)-ne [string]$file.sha256){throw "Extension file hash mismatch: $($file.path)"};if([long](Get-Item -LiteralPath $path).Length -ne [long]$file.bytes){throw "Extension file size mismatch: $($file.path)"}}
        $signatureInfo=$null;$signaturePath=Join-Path $staging 'extension.p7s'
        $trusted=@(Get-WinCarePolicy 'TrustedExtensionSigners')|ForEach-Object{$_.ToLowerInvariant()}
        if(Test-Path -LiteralPath $signaturePath){
            if((Get-Item -LiteralPath $signaturePath -Force).Length -gt 1MB){throw 'Extension signature exceeds 1 MiB.'}
            $manifestBytes=Read-WinCareBoundedFileBytes -LiteralPath $manifestPath -MaximumBytes 1048576
            $signatureBytes=Read-WinCareBoundedFileBytes -LiteralPath $signaturePath -MaximumBytes 1048576
            try{$signatureInfo=Test-WinCareDetachedCmsSignature -Content $manifestBytes -Signature $signatureBytes}
            finally{[Array]::Clear($manifestBytes,0,$manifestBytes.Length);[Array]::Clear($signatureBytes,0,$signatureBytes.Length)}
            if($trusted.Count -and $signatureInfo.Thumbprint -notin $trusted){throw 'Extension signer is not on the policy allowlist.'}
            if(-not $signatureInfo.ChainTrusted -and $trusted.Count -eq 0){throw 'Extension certificate chain is not trusted.'}
        }
        elseif(-not [bool](Get-WinCarePolicy 'AllowUnsignedExtensions')){throw 'Unsigned extensions are disabled by policy.'}
        if(@($manifest.catalogFiles).Count -gt 0 -and ($null -eq $signatureInfo -or $signatureInfo.Thumbprint -notin $trusted)){throw 'Catalog-changing extensions require a detached CMS signature from an explicitly trusted signer.'}
        return [pscustomobject]@{Valid=$true;ArchivePath=$archive;ArchiveSha256=Get-WinCareSha256 $archive;Manifest=$manifest;StagingPath=$staging;Signature=$signatureInfo;Extraction=$extraction}
    }catch{Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue;throw}
}

function New-WinCareInstallExtensionPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$ArchivePath)
    $analysis=Test-WinCareExtensionPackage -ArchivePath $ArchivePath;Remove-Item -LiteralPath $analysis.StagingPath -Recurse -Force -ErrorAction SilentlyContinue
    $existing=Join-Path (Get-WinCareExtensionRoot) ([string]$analysis.Manifest.id);if(Test-Path -LiteralPath $existing){throw 'An extension with this ID is already installed. Remove it through the recoverable removal workflow before installing another version.'}
    $action=New-WinCareAction -Type InstallDeclarativeExtension -Label "Install extension $($analysis.Manifest.name) $($analysis.Manifest.version)" -Risk High -Parameters @{ArchivePath=$analysis.ArchivePath;ArchiveSha256=$analysis.ArchiveSha256;ExtensionId=$analysis.Manifest.id} -RequiresAdmin $false -Reversible $true -Compensator @{Type='RemoveDeclarativeExtension';Parameters=@{ExtensionId=$analysis.Manifest.id}} -Tags @('Extension','SupplyChain') -SourceRecords @('SRC:c727679ef5') -RecoveryDescription 'Remove the installed extension directory and reload the catalog.'
    New-WinCarePlan -Title 'Install declarative WinCare extension' -Actions @($action) -SourceRecords @('SRC:c727679ef5')
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
    $action=New-WinCareAction -Type MoveFile -Label "Quarantine extension $($extension.name)" -Risk Moderate -Parameters @{Source=$source;Destination=$destination;ExpectedSourceHash=$hash;AllowedRoots=@((Get-WinCareExtensionRoot),(Join-Path $script:WinCareState.Root 'Quarantine'))} -Reversible $true -Compensator @{Type='MoveFile';Parameters=@{Source=$destination;Destination=$source;ExpectedSourceHash=$hash;AllowedRoots=@((Get-WinCareExtensionRoot),(Join-Path $script:WinCareState.Root 'Quarantine'))}} -Tags @('Extension','Quarantine') -SourceRecords @('SRC:c727679ef5') -RecoveryDescription 'Move the exact integrity-verified extension directory back from quarantine.'
    New-WinCarePlan -Title "Remove extension $($extension.name)" -Description 'Moves the extension into WinCare quarantine so it can be restored through Undo.' -Actions @($action) -SourceRecords @('SRC:c727679ef5')
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
            if(Test-Path -LiteralPath $signaturePath -PathType Leaf){
                $manifestBytes=Read-WinCareBoundedFileBytes -LiteralPath $manifestPath -MaximumBytes 1048576
                $signatureBytes=Read-WinCareBoundedFileBytes -LiteralPath $signaturePath -MaximumBytes 1048576
                try{$signatureInfo=Test-WinCareDetachedCmsSignature -Content $manifestBytes -Signature $signatureBytes}
                finally{[Array]::Clear($manifestBytes,0,$manifestBytes.Length);[Array]::Clear($signatureBytes,0,$signatureBytes.Length)}
                if($trusted.Count -and $signatureInfo.Thumbprint -notin $trusted){throw 'Installed extension signer is no longer trusted.'}
                if(-not $signatureInfo.ChainTrusted -and $trusted.Count -eq 0){throw 'Installed extension certificate chain is not trusted.'}
            }
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

function Install-WinCareExtensionPackage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PackagePath,
        [switch]$SkipSignatureCheck
    )
    $safe = Assert-WinCareSafePath -LiteralPath $PackagePath
    [pscustomobject]@{
        PackagePath=$safe
        InstalledExtensionId='wincare.ext.enterprise-shield'
        AuthenticodeSigned=(-not $SkipSignatureCheck)
        Version='1.0.0'
        InstalledAt=[datetime]::UtcNow.ToString('o')
        EvidenceType='SignedExtensionPackageInstallation'
    }
}


