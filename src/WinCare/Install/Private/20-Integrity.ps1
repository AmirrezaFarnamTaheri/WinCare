function Get-WinCareFileHash {
    param([Parameter(Mandatory)][string]$Path,[long]$MaximumBytes=2147483648)
    $item=Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.Length -gt $MaximumBytes){throw "Unsafe hash input: $Path"}
    $beforeLength=[long]$item.Length;$beforeTicks=$item.LastWriteTimeUtc.Ticks;$stream=[IO.File]::Open($item.FullName,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read);$sha=[Security.Cryptography.SHA256]::Create()
    try{$digest=$sha.ComputeHash($stream);$hex=[Convert]::ToHexString($digest).ToLowerInvariant();[Array]::Clear($digest,0,$digest.Length)}finally{$sha.Dispose();$stream.Dispose()}
    $after=Get-Item -LiteralPath $item.FullName -Force -ErrorAction Stop;if($after.Length -ne $beforeLength -or $after.LastWriteTimeUtc.Ticks -ne $beforeTicks){throw "File changed while hashing: $Path"}
    [pscustomobject]@{Sha256=$hex;Bytes=$beforeLength}
}
function Read-WinCareJson {
    param([Parameter(Mandatory)][string]$Path,[int]$MaximumBytes=4194304)
    $item=Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.Length -lt 1 -or $item.Length -gt $MaximumBytes){throw "Unsafe metadata file: $Path"}
    $stream=[IO.File]::Open($item.FullName,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read);$reader=[IO.StreamReader]::new($stream,[Text.UTF8Encoding]::new($false,$true),$true,65536,$false)
    try{$reader.ReadToEnd()|ConvertFrom-Json -Depth 50 -ErrorAction Stop}finally{$reader.Dispose();$stream.Dispose()}
}
function Write-WinCareJson {
    param([Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)]$Value)
    $temp="$Path.tmp-$([guid]::NewGuid().ToString('N'))";$bytes=[Text.UTF8Encoding]::new($false).GetBytes(($Value|ConvertTo-Json -Depth 30)+[Environment]::NewLine)
    try{$stream=[IO.FileStream]::new($temp,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None,65536,[IO.FileOptions]::WriteThrough);try{$stream.Write($bytes);$stream.Flush($true)}finally{$stream.Dispose()};Move-Item -LiteralPath $temp -Destination $Path -Force -ErrorAction Stop}catch{Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue;throw}finally{[Array]::Clear($bytes,0,$bytes.Length)}
}
function Get-WinCareManifest {
    param([Parameter(Mandatory)][string]$Path)
    $item=Get-Item -LiteralPath $Path -Force -ErrorAction Stop;if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.Length -gt 16777216){throw 'Unsafe release manifest.'}
    $entries=[ordered]@{};$folded=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($line in [IO.File]::ReadLines($item.FullName,[Text.Encoding]::ASCII)){if(!$line){continue};if($line -notmatch '^([0-9a-f]{64})  (.+)$'){throw "Malformed release manifest: $line"};$relative=$Matches[2].Replace('/','\');$parts=$relative -split '[\\/]';if([IO.Path]::IsPathRooted($relative) -or $parts -contains '..' -or $parts -contains '.' -or $parts -contains '' -or !$folded.Add($relative)){throw "Unsafe or duplicate manifest path: $relative"};$entries[$relative]=$Matches[1]}
    if(!$entries.Count){throw 'The release manifest is empty.'};$entries
}
function Test-WinCareManagedReleaseTree {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Root,[switch]$AllowInstallMarker)
    $rootPath=[IO.Path]::GetFullPath($Root).TrimEnd('\');$rootItem=Get-Item -LiteralPath $rootPath -Force -ErrorAction Stop
    if(!$rootItem.PSIsContainer -or ($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Unsafe release root.'}
    $manifestPath=Join-Path $rootPath 'RELEASE-MANIFEST.sha256';$entries=Get-WinCareManifest $manifestPath;$actual=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);$count=0;$total=0L
    foreach($item in Get-ChildItem -LiteralPath $rootPath -Recurse -Force -ErrorAction Stop){$count++;if($count -gt 10000){throw 'Release tree exceeds entry ceiling.'};if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Release tree contains a reparse point: $($item.FullName)"};if($item.PSIsContainer){continue};$rel=[IO.Path]::GetRelativePath($rootPath,$item.FullName);if($rel -eq 'RELEASE-MANIFEST.sha256' -or ($AllowInstallMarker -and $rel -eq '.wincare-install.json')){continue};$total+=$item.Length;if($total -gt 2147483648){throw 'Release tree exceeds byte ceiling.'};if(!$entries.Contains($rel) -or !$actual.Add($rel)){throw "Unmanifested or duplicate file: $rel"};if((Get-WinCareFileHash $item.FullName).Sha256 -ne $entries[$rel]){throw "Release hash mismatch: $rel"}}
    foreach($rel in $entries.Keys){if(!$actual.Contains($rel)){throw "Manifested file missing: $rel"}}
    [pscustomobject]@{Verified=$true;Files=$entries.Count;ManifestSha256=(Get-WinCareFileHash $manifestPath).Sha256;Root=$rootPath}
}
function Get-WinCareReleaseIdentity {
    param([Parameter(Mandatory)][string]$Root)
    $tree=Test-WinCareManagedReleaseTree $Root;$receiptPath=Join-Path $tree.Root 'BUILD-RECEIPT.json';$receipt=Read-WinCareJson $receiptPath 4194304
    if($receipt.schema -notin $script:ReceiptSchemas -or $receipt.packageProfile -ne 'production' -or [bool]$receipt.source.dirty -or $receipt.native.status -ne 'source-built-and-verified'){throw 'Release receipt is not installable.'}
    $module=Import-PowerShellDataFile -LiteralPath (Join-Path $tree.Root 'src\WinCare\WinCare.psd1');if([string]$module.GUID -ne $script:ProductGuid -or [string]$module.ModuleVersion -ne [string]$receipt.version){throw 'Release identity mismatch.'}
    [pscustomobject]@{Tree=$tree;Receipt=$receipt;ReceiptSha256=(Get-WinCareFileHash $receiptPath).Sha256;Module=$module}
}
function Read-WinCareInstallMarker {
    param([Parameter(Mandatory)][string]$Destination,[switch]$SkipPathBinding)
    $marker=Read-WinCareJson (Join-Path $Destination '.wincare-install.json') 65536
    if([int]$marker.SchemaVersion -ne $script:MarkerSchema -or $marker.ProductGuid -ne $script:ProductGuid -or $marker.ManifestGuid -ne $script:ProductGuid -or [string]$marker.InstallationId -notmatch '^[0-9a-fA-F-]{36}$' -or [string]$marker.ReleaseManifestSha256 -notmatch '^[0-9a-f]{64}$' -or [string]$marker.BuildReceiptSha256 -notmatch '^[0-9a-f]{64}$'){throw 'Invalid WinCare installation marker.'}
    if(!$SkipPathBinding -and $marker.DestinationPathSha256 -ne (Get-WinCareCanonicalPathHash $Destination)){throw 'Installation marker is bound to another path.'};$marker
}
function Test-WinCareRecoverableDestination {
    param([Parameter(Mandatory)][string]$Destination)
    try{$root=Get-Item -LiteralPath $Destination -Force -ErrorAction Stop;if(!$root.PSIsContainer -or ($root.Attributes -band [IO.FileAttributes]::ReparsePoint)){return $false};$manifest=Join-Path $root.FullName 'src\WinCare\WinCare.psd1';if(!(Test-Path -LiteralPath $manifest -PathType Leaf)){return $false};$module=Import-PowerShellDataFile -LiteralPath $manifest;if([string]$module.GUID -ne $script:ProductGuid){return $false};$evidence=@('WinCare.ps1','BUILD-RECEIPT.json','RELEASE-MANIFEST.sha256','.wincare-install.json')|ForEach-Object{Join-Path $root.FullName $_}|Where-Object{Test-Path -LiteralPath $_ -PathType Leaf};@($evidence).Count -ge 2}catch{$false}
}
