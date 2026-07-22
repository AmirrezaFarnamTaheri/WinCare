#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RequestPath,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$RequestSha256,
    [Parameter(Mandatory)][string]$SecretBase64
)

Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

function Get-LocalSha256([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Get-LocalTextSha256([string]$Text){[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Text))).ToLowerInvariant()}
function Get-LocalHmac([byte[]]$Key,[string]$Text){$h=[Security.Cryptography.HMACSHA256]::new($Key);try{[Convert]::ToHexString($h.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text))).ToLowerInvariant()}finally{$h.Dispose()}}
function Test-LocalFixedHex([string]$Expected,[string]$Actual){if($Expected -notmatch '^[a-fA-F0-9]{64}$' -or $Actual -notmatch '^[a-fA-F0-9]{64}$'){return $false};[Security.Cryptography.CryptographicOperations]::FixedTimeEquals([Convert]::FromHexString($Expected),[Convert]::FromHexString($Actual))}
function Resolve-LocalPath([string]$Path,[switch]$AllowMissing){$full=[IO.Path]::GetFullPath($Path);if(-not $AllowMissing -and -not(Test-Path -LiteralPath $full)){throw "Path does not exist: $full"};$full.TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)}
function Test-LocalWithin([string]$Child,[string]$Parent,[switch]$AllowEqual){$c=Resolve-LocalPath $Child -AllowMissing;$p=Resolve-LocalPath $Parent -AllowMissing;$comparison=[StringComparison]::OrdinalIgnoreCase;if($AllowEqual -and [string]::Equals($c,$p,$comparison)){return $true};$c.StartsWith($p+[IO.Path]::DirectorySeparatorChar,$comparison)}
function Assert-LocalNoReparse([string]$Path){$item=Get-Item -LiteralPath $Path -Force;while($item){if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0){throw "Reparse point is not allowed: $($item.FullName)"};$parent=Split-Path -Parent $item.FullName;if(-not $parent -or $parent -eq $item.FullName){break};$item=Get-Item -LiteralPath $parent -Force -ErrorAction SilentlyContinue}}
function Get-LocalModuleTreeHash([string]$Root){$files=Get-ChildItem -LiteralPath $Root -File -Recurse|Sort-Object FullName;$builder=[Text.StringBuilder]::new();foreach($file in $files){$relative=[IO.Path]::GetRelativePath($Root,$file.FullName).Replace('\','/');$null=$builder.Append($relative).Append(':').Append((Get-LocalSha256 $file.FullName)).Append("`n")};Get-LocalTextSha256 $builder.ToString()}
function Write-LocalAtomicJson([string]$Path,[object]$Object){$parent=Split-Path -Parent $Path;$temp=Join-Path $parent ('.result.'+[guid]::NewGuid().ToString('N')+'.tmp');try{$Object|ConvertTo-Json -Depth 50|Set-Content -LiteralPath $temp -Encoding utf8NoBOM;Move-Item -LiteralPath $temp -Destination $Path -Force}finally{Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue}}

$secret=$null
$payload=$null
$resultPath=$null
try {
    if(-not $IsWindows){throw 'Elevated action host is Windows-only.'}
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent();$principal=[Security.Principal.WindowsPrincipal]::new($identity)
    if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'The elevated action host did not receive administrator privileges.'}
    $requestPathFull=Resolve-LocalPath $RequestPath
    Assert-LocalNoReparse $requestPathFull
    if(-not(Test-LocalFixedHex (Get-LocalSha256 $requestPathFull) $RequestSha256)){throw 'Elevation request file hash mismatch.'}
    $secret=[Convert]::FromBase64String($SecretBase64);if($secret.Length -ne 32){throw 'Elevation secret length is invalid.'}
    $wrapper=Get-Content -LiteralPath $requestPathFull -Raw|ConvertFrom-Json -AsHashtable -Depth 50
    $unknown=@($wrapper.Keys|Where-Object{$_ -notin @('SchemaVersion','PayloadBase64','Signature')});if($unknown.Count){throw "Unknown request envelope field(s): $($unknown -join ', ')"}
    if([int]$wrapper.SchemaVersion -ne 1){throw 'Unsupported request envelope schema.'}
    $expected=Get-LocalHmac $secret ([string]$wrapper.PayloadBase64)
    if(-not(Test-LocalFixedHex $expected ([string]$wrapper.Signature))){throw 'Elevation request authentication failed.'}
    $payloadJson=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([string]$wrapper.PayloadBase64))
    $payload=$payloadJson|ConvertFrom-Json -AsHashtable -Depth 50
    $allowed=@('SchemaVersion','Nonce','CreatedAt','ExpiresAt','OperationId','ActionId','ActionType','ActionHash','ActionBase64','ModuleManifest','ModuleTreeHash','HelperHash','StateRoot','PolicyHash','ResultPath')
    $unknown=@($payload.Keys|Where-Object{$_ -notin $allowed});if($unknown.Count){throw "Unknown elevation payload field(s): $($unknown -join ', ')"}
    if([int]$payload.SchemaVersion -ne 3){throw 'Unsupported elevation payload schema.'}
    if([string]$payload.Nonce -notmatch '^[a-f0-9]{32}$'){throw 'Invalid elevation nonce.'}
    $created=[datetime]::Parse([string]$payload.CreatedAt).ToUniversalTime();$expires=[datetime]::Parse([string]$payload.ExpiresAt).ToUniversalTime();$now=[datetime]::UtcNow
    if($created -gt $now.AddMinutes(1) -or $expires -le $now -or $expires -gt $created.AddMinutes(10)){throw 'Elevation request is expired or has an invalid lifetime.'}
    $requestDir=Split-Path -Parent $requestPathFull
    $resultPath=Resolve-LocalPath ([string]$payload.ResultPath) -AllowMissing
    if(-not(Test-LocalWithin $resultPath $requestDir)){throw 'Result path is outside the authenticated handoff directory.'}
    $manifest=Resolve-LocalPath ([string]$payload.ModuleManifest);Assert-LocalNoReparse $manifest
    $moduleRoot=Split-Path -Parent $manifest
    if(-not(Test-LocalFixedHex (Get-LocalSha256 $PSCommandPath) ([string]$payload.HelperHash))){throw 'Elevated helper integrity validation failed.'}
    $treeHash=Get-LocalModuleTreeHash $moduleRoot
    if(-not(Test-LocalFixedHex $treeHash ([string]$payload.ModuleTreeHash))){throw 'WinCare module tree changed after approval.'}
    $actionJson=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([string]$payload.ActionBase64))
    if(-not(Test-LocalFixedHex (Get-LocalTextSha256 $actionJson) ([string]$payload.ActionHash))){throw 'Elevated action payload hash mismatch.'}
    $action=$actionJson|ConvertFrom-Json -AsHashtable -Depth 50
    if([string]$action.Id -ne [string]$payload.ActionId -or [string]$action.Type -ne [string]$payload.ActionType -or -not[bool]$action.RequiresAdmin){throw 'Elevated action identity or privilege declaration is invalid.'}
    $stateRoot=Resolve-LocalPath ([string]$payload.StateRoot) -AllowMissing
    $replayRoot=Join-Path $stateRoot 'Broker\Elevation\Used';$null=New-Item -ItemType Directory -Path $replayRoot -Force
    $replayPath=Join-Path $replayRoot ([string]$payload.Nonce+'.used')
    try{$stream=[IO.File]::Open($replayPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None);$stream.Dispose()}catch{throw 'Elevation nonce was already used or could not be reserved.'}

    $module=Import-Module $manifest -Force -PassThru -ErrorAction Stop
    $result=& $module {
        param($ElevatedAction,$ElevatedOperationId,$ExpectedStateRoot,$ExpectedPolicyHash,$ExpectedTreeHash)
        Initialize-WinCareState -SkipConfigSave
        if(-not $script:WinCareState.IsAdmin){throw 'Administrative state was not established.'}
        if(-not(Test-WinCarePathWithin -Child $script:WinCareState.Root -Parent $ExpectedStateRoot -AllowEqual)){throw 'Elevated state root differs from the approved state root.'}
        $policyJson=$script:WinCareState.Policy|ConvertTo-Json -Compress -Depth 30
        if((Get-WinCareSha256Text -Text $policyJson) -ne $ExpectedPolicyHash){throw 'Effective policy changed after approval.'}
        if((Get-WinCareModuleTreeHash) -ne $ExpectedTreeHash){throw 'Module tree changed after import.'}
        if([string]$ElevatedAction.Type -notin (Get-WinCareElevatedActionAllowlist)){throw "Action type is not permitted in the elevated host: $($ElevatedAction.Type)"}
        $contract=Test-WinCareActionContract -Action $ElevatedAction -ForElevation;if(-not $contract.Success){throw $contract.Message}
        Invoke-WinCareAction -Action $ElevatedAction -OperationId $ElevatedOperationId
    } $action ([string]$payload.OperationId) $stateRoot ([string]$payload.PolicyHash) $treeHash
    $resultPayload=[ordered]@{SchemaVersion=3;Nonce=$payload.Nonce;OperationId=$payload.OperationId;ActionId=$payload.ActionId;ActionHash=$payload.ActionHash;ModuleTreeHash=$treeHash;FinishedAt=[datetime]::UtcNow.ToString('o');Result=$result}
    $resultJson=$resultPayload|ConvertTo-Json -Compress -Depth 50;$resultBase64=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($resultJson))
    Write-LocalAtomicJson $resultPath ([ordered]@{SchemaVersion=1;PayloadBase64=$resultBase64;Signature=(Get-LocalHmac $secret $resultBase64)})
    exit $(if($result.Success){0}else{1})
} catch {
    if($secret -and $resultPath){
        try{
            $failure=[ordered]@{PSTypeName='WinCare.Result';SchemaVersion=3;Success=$false;Status='Failed';Code='ElevatedHostFailure';Message=$_.Exception.Message;Data=$null;ExitCode=1;Warnings=@();OperationId=if($payload){$payload.OperationId}else{''};ActionId=if($payload){$payload.ActionId}else{''};Timestamp=[datetime]::UtcNow}
            $resultPayload=[ordered]@{SchemaVersion=3;Nonce=if($payload){$payload.Nonce}else{''};OperationId=if($payload){$payload.OperationId}else{''};ActionId=if($payload){$payload.ActionId}else{''};ActionHash=if($payload){$payload.ActionHash}else{''};ModuleTreeHash=if($payload){$payload.ModuleTreeHash}else{''};FinishedAt=[datetime]::UtcNow.ToString('o');Result=$failure}
            $resultJson=$resultPayload|ConvertTo-Json -Compress -Depth 50;$resultBase64=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($resultJson))
            Write-LocalAtomicJson $resultPath ([ordered]@{SchemaVersion=1;PayloadBase64=$resultBase64;Signature=(Get-LocalHmac $secret $resultBase64)})
        }catch { Write-Verbose 'A best-effort operation was unavailable.' }
    }
    exit 1
} finally {if($secret){[Array]::Clear($secret,0,$secret.Length)}}
