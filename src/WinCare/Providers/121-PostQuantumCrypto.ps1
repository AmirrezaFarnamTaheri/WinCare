#requires -Version 7.2

function Get-WinCareOpenSslPqcCapability {
    [CmdletBinding()]
    param([string]$OpenSslPath)
    $candidate=if($OpenSslPath) { $OpenSslPath } else { 'openssl' }
    $command=Get-Command $candidate -CommandType Application -ErrorAction SilentlyContinue|
        Select-Object -First 1
    if(-not $command) {
        return [pscustomobject]@{
            Available=$false
            Reason='OpenSSL was not found.'
            Path=$null
            Sha256=$null
            Version=$null
            Algorithms=@()
        }
    }
    try {
        # ponytail: OpenSSL PQC CLI -> native C# System.Security.Cryptography ML-DSA
        $path=Assert-WinCareSafePath -LiteralPath $command.Source
        $item=Get-Item -LiteralPath $path -Force
        if($item.Length -gt 512MB) { throw 'The OpenSSL executable exceeds the 512 MiB bound.' }
        $sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        $versionResult=Invoke-WinCareProcess `
            -FilePath $path `
            -ArgumentList @('version') `
            -TimeoutSeconds 10 `
            -MaximumCapturedOutputBytes 65536 `
            -ExpectedSha256 $sha256
        if(-not $versionResult.Success) { throw "OpenSSL version probe failed: $($versionResult.Message)" }
        $algorithmResult=Invoke-WinCareProcess `
            -FilePath $path `
            -ArgumentList @('list','-kem-algorithms') `
            -TimeoutSeconds 15 `
            -MaximumCapturedOutputBytes 262144 `
            -ExpectedSha256 $sha256
        if(-not $algorithmResult.Success) {
            $algorithmResult=Invoke-WinCareProcess `
                -FilePath $path `
                -ArgumentList @('list','-public-key-algorithms') `
                -TimeoutSeconds 15 `
                -MaximumCapturedOutputBytes 262144 `
                -ExpectedSha256 $sha256
        }
        $algorithms=if($algorithmResult.Success) {
            @($algorithmResult.Data.StdOut -split "`r?`n"|Where-Object{$_ -match '(?i)ML[-_]?KEM|KYBER'})
        } else {
            @()
        }
        [pscustomobject]@{
            Available=$algorithms.Count -gt 0
            Reason=if($algorithms.Count){''}else{'No ML-KEM/Kyber KEM was advertised by OpenSSL.'}
            Path=$path
            Sha256=$sha256
            Version=$versionResult.Data.StdOut.Trim()
            Algorithms=$algorithms
        }
    } catch {
        [pscustomobject]@{
            Available=$false
            Reason=$_.Exception.Message
            Path=$command.Source
            Sha256=$null
            Version=$null
            Algorithms=@()
        }
    }
}

function Assert-WinCarePinnedPqcRuntime {
    [CmdletBinding()]
    param(
        [string]$OpenSslPath,
        [Parameter(Mandatory)]
        [ValidatePattern('^[A-Fa-f0-9]{64}$')]
        [string]$ExpectedOpenSslSha256
    )
    $capability=Get-WinCareOpenSslPqcCapability -OpenSslPath $OpenSslPath
    if(-not $capability.Available) { throw $capability.Reason }
    if($capability.Sha256 -ne $ExpectedOpenSslSha256.ToLowerInvariant()) {
        throw 'OpenSSL SHA-256 does not match the operator-approved digest.'
    }
    $capability
}

function Resolve-WinCareMlKemAlgorithm {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Capability,
        [ValidateSet('ML-KEM-512','ML-KEM-768','ML-KEM-1024')]
        [string]$Algorithm='ML-KEM-768'
    )
    $normalized=$Algorithm -replace '[_-]',''
    foreach($line in $Capability.Algorithms) {
        if(($line -replace '[_-]','') -match [regex]::Escape($normalized)) { return $Algorithm }
    }
    foreach($candidate in @(
        'ML-KEM-768','MLKEM768',
        'ML-KEM-512','MLKEM512',
        'ML-KEM-1024','MLKEM1024'
    )) {
        if((@($Capability.Algorithms)-join "`n") -match [regex]::Escape($candidate)) {
            return $candidate
        }
    }
    $null
}

function Invoke-WinCareOpenSslPqcCommand {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$OpenSslPath,
        [Parameter(Mandatory)][string[]]$ArgumentList,
        [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedOpenSslSha256,
        [ValidateRange(1,300)][int]$TimeoutSeconds=30
    )
    $result=Invoke-WinCareProcess `
        -FilePath $OpenSslPath `
        -ArgumentList $ArgumentList `
        -TimeoutSeconds $TimeoutSeconds `
        -SuccessExitCodes @(0) `
        -MaximumCapturedOutputBytes 262144 `
        -ExpectedSha256 $ExpectedOpenSslSha256
    if(-not $result.Success) {
        $detail=if($result.Data.StdErr) { $result.Data.StdErr.Trim() } else { $result.Message }
        throw "OpenSSL command failed: $detail"
    }
    $result
}

function Get-WinCareHkdfSha256Key {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][byte[]]$InputKeyMaterial,
        [Parameter(Mandatory)][byte[]]$Salt,
        [Parameter(Mandatory)][byte[]]$Info
    )
    $extract=[Security.Cryptography.HMACSHA256]::new($Salt)
    try { $prk=$extract.ComputeHash($InputKeyMaterial) }
    finally { $extract.Dispose() }
    try {
        $expand=[Security.Cryptography.HMACSHA256]::new($prk)
        try {
            $input=[byte[]]::new($Info.Length+1)
            [Array]::Copy($Info,0,$input,0,$Info.Length)
            $input[$input.Length-1]=1
            try { $expand.ComputeHash($input) }
            finally { [Array]::Clear($input,0,$input.Length) }
        } finally {
            $expand.Dispose()
        }
    } finally {
        [Array]::Clear($prk,0,$prk.Length)
    }
}

function New-WinCarePqcPrivateWorkspace {
    [CmdletBinding()]
    param([string]$ParentDirectory)
    $parent=if($ParentDirectory) {
        Resolve-WinCareCanonicalPath -LiteralPath $ParentDirectory -AllowMissing
    } else {
        [IO.Path]::GetTempPath()
    }
    $null=New-Item -ItemType Directory -Path $parent -Force
    $path=Join-Path $parent ('wincare-pqc-'+[guid]::NewGuid().ToString('N'))
    New-WinCarePrivateDirectory -LiteralPath $path
}

function Invoke-WinCarePqcKeyExchange {
    [CmdletBinding()]
    param(
        [ValidateSet('ML-KEM-512','ML-KEM-768','ML-KEM-1024')]
        [string]$Algorithm='ML-KEM-768',
        [string]$OpenSslPath,
        [ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedOpenSslSha256,
        [string]$OutputDirectory,
        [switch]$KeepArtifacts
    )
    if(-not $ExpectedOpenSslSha256) {
        return New-WinCareResult `
            -Success $false -Status Blocked -Code 'PqcRuntimeDigestRequired' `
            -Message 'ExpectedOpenSslSha256 is required before cryptographic execution.' `
            -ExitCode 78
    }
    $probe=Get-WinCareOpenSslPqcCapability -OpenSslPath $OpenSslPath
    if(-not $probe.Available) {
        return New-WinCareResult -Success $false -Status Blocked -Code 'PqcRuntimeUnavailable' -Message $probe.Reason -ExitCode 78 -Data @{EvidenceType='DependencyProbe'}
    }
    if($probe.Sha256 -ne $ExpectedOpenSslSha256.ToLowerInvariant()) {
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'PqcRuntimeHashMismatch' `
            -Message 'OpenSSL SHA-256 does not match the operator-approved digest.' `
            -ExitCode 74 -Data @{
                ObservedSha256=$probe.Sha256
                EvidenceType='DigestAdmission'
            }
    }
    try {
        $capability=$probe
        $resolved=Resolve-WinCareMlKemAlgorithm -Capability $capability -Algorithm $Algorithm
        if(-not $resolved) { throw "The requested algorithm '$Algorithm' is unavailable." }
        $directory=New-WinCarePqcPrivateWorkspace -ParentDirectory $OutputDirectory
        $private=Join-Path $directory 'recipient-private.pem'
        $public=Join-Path $directory 'recipient-public.pem'
        $cipher=Join-Path $directory 'kem-ciphertext.bin'
        $senderSecret=Join-Path $directory 'sender-secret.bin'
        $recipientSecret=Join-Path $directory 'recipient-secret.bin'
        try {
            $null=Invoke-WinCareOpenSslPqcCommand -OpenSslPath $capability.Path -ExpectedOpenSslSha256 $capability.Sha256 -ArgumentList @(
                'genpkey','-algorithm',$resolved,'-out',$private
            )
            $null=Invoke-WinCareOpenSslPqcCommand -OpenSslPath $capability.Path -ExpectedOpenSslSha256 $capability.Sha256 -ArgumentList @(
                'pkey','-in',$private,'-pubout','-out',$public
            )
            $null=Invoke-WinCareOpenSslPqcCommand -OpenSslPath $capability.Path -ExpectedOpenSslSha256 $capability.Sha256 -ArgumentList @(
                'pkeyutl','-encap','-inkey',$public,'-pubin',
                '-out',$cipher,'-secret',$senderSecret
            )
            $null=Invoke-WinCareOpenSslPqcCommand -OpenSslPath $capability.Path -ExpectedOpenSslSha256 $capability.Sha256 -ArgumentList @(
                'pkeyutl','-decap','-inkey',$private,'-in',$cipher,'-secret',$recipientSecret
            )
            $senderHash=(Get-FileHash -LiteralPath $senderSecret -Algorithm SHA256).Hash.ToLowerInvariant()
            $recipientHash=(Get-FileHash -LiteralPath $recipientSecret -Algorithm SHA256).Hash.ToLowerInvariant()
            $success=$senderHash -eq $recipientHash
            New-WinCareResult `
                -Success $success `
                -Code $(if($success){'PqcKeyExchangeVerified'}else{'PqcSharedSecretMismatch'}) `
                -Message $(if($success){'ML-KEM produced matching shared secrets.'}else{'ML-KEM shared-secret verification failed.'}) `
                -ExitCode $(if($success){0}else{74}) `
                -Data @{
                    Algorithm=$resolved
                    OpenSslVersion=$capability.Version
                    OpenSslSha256=$capability.Sha256
                    SharedSecretMatch=$success
                    SharedSecretSha256=$senderHash
                    CiphertextSha256=(Get-FileHash -LiteralPath $cipher -Algorithm SHA256).Hash.ToLowerInvariant()
                    PublicKeyPath=if($KeepArtifacts){$public}else{$null}
                    PrivateKeyPath=if($KeepArtifacts){$private}else{$null}
                    CiphertextPath=if($KeepArtifacts){$cipher}else{$null}
                    Workspace=if($KeepArtifacts){$directory}else{$null}
                    ArtifactsRetained=[bool]$KeepArtifacts
                    EvidenceType='DigestPinnedMlKemRoundTrip'
                }
        } finally {
            Remove-Item -LiteralPath $senderSecret,$recipientSecret -Force -ErrorAction SilentlyContinue
            if(-not $KeepArtifacts) {
                Remove-Item -LiteralPath $directory -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    } catch {
        New-WinCareResult `
            -Success $false -Status Blocked -Code 'PqcKeyExchangeFailed' `
            -Message $_.Exception.Message -ExitCode 1 `
            -Data @{EvidenceType='DigestPinnedOpenSslExecution'}
    }
}

function Protect-WinCarePqcFile {
    [CmdletBinding(SupportsShouldProcess,ConfirmImpact='High')]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$RecipientPublicKeyPath,
        [string]$OutputPath,
        [string]$OpenSslPath,
        [ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedOpenSslSha256,
        [ValidateRange(1,268435456)][long]$MaximumInputBytes=67108864
    )
    if(-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return New-WinCareResult -Success $false -Status Blocked -Code 'InputFileNotFound' -Message "Input file not found: $Path" -ExitCode 2
    }
    if(-not (Test-Path -LiteralPath $RecipientPublicKeyPath -PathType Leaf)) {
        return New-WinCareResult -Success $false -Status Blocked -Code 'RecipientKeyNotFound' -Message 'A recipient ML-KEM public key is required.' -ExitCode 2
    }
    if(-not $ExpectedOpenSslSha256) {
        return New-WinCareResult -Success $false -Status Blocked -Code 'PqcRuntimeDigestRequired' -Message 'ExpectedOpenSslSha256 is required.' -ExitCode 78
    }
    $inputPath=Assert-WinCareSafePath -LiteralPath $Path
    $publicKeyPath=Assert-WinCareSafePath -LiteralPath $RecipientPublicKeyPath
    $inputItem=Get-Item -LiteralPath $inputPath -Force
    if($inputItem.Length -gt $MaximumInputBytes) {
        return New-WinCareResult -Success $false -Status Blocked -Code 'PqcInputTooLarge' -Message "Input exceeds the $MaximumInputBytes byte limit." -ExitCode 77
    }
    if(-not $OutputPath) { $OutputPath="$inputPath.wincare-pqc.json" }
    $outputFull=Resolve-WinCareCanonicalPath -LiteralPath $OutputPath -AllowMissing
    if(-not $PSCmdlet.ShouldProcess($outputFull,'Create authenticated ML-KEM + AES-256-GCM envelope')) {
        return New-WinCareResult -Success $false -Status Cancelled -Code 'Cancelled' -Message 'File protection was cancelled.' -ExitCode 125
    }
    $workspace=$null
    $secret=$null
    $key=$null
    $plain=$null
    $cipher=$null
    try {
        $capability=Assert-WinCarePinnedPqcRuntime `
            -OpenSslPath $OpenSslPath `
            -ExpectedOpenSslSha256 $ExpectedOpenSslSha256
        $workspace=New-WinCarePqcPrivateWorkspace
        $kemCipher=Join-Path $workspace 'kem.bin'
        $shared=Join-Path $workspace 'secret.bin'
        $null=Invoke-WinCareOpenSslPqcCommand -OpenSslPath $capability.Path -ExpectedOpenSslSha256 $capability.Sha256 -ArgumentList @(
            'pkeyutl','-encap','-inkey',$publicKeyPath,'-pubin',
            '-out',$kemCipher,'-secret',$shared
        )
        $secret=Read-WinCareBoundedFileBytes -LiteralPath $shared -MaximumBytes 65536
        $salt=[byte[]]::new(32)
        [Security.Cryptography.RandomNumberGenerator]::Fill($salt)
        $info=[Text.Encoding]::UTF8.GetBytes('WinCare-PQC-Envelope-v2')
        try { $key=Get-WinCareHkdfSha256Key -InputKeyMaterial $secret -Salt $salt -Info $info }
        finally { [Array]::Clear($info,0,$info.Length) }
        $nonce=[byte[]]::new(12)
        [Security.Cryptography.RandomNumberGenerator]::Fill($nonce)
        $plain=Read-WinCareBoundedFileBytes -LiteralPath $inputPath -MaximumBytes $MaximumInputBytes
        $cipher=[byte[]]::new($plain.Length)
        $tag=[byte[]]::new(16)
        $header=[ordered]@{
            SchemaVersion=2
            Algorithm='ML-KEM + HKDF-SHA256 + AES-256-GCM'
            CreatedAt=[datetime]::UtcNow.ToString('o')
            SourceName=[IO.Path]::GetFileName($inputPath)
            SourceBytes=[long]$inputItem.Length
            SourceSha256=(Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash.ToLowerInvariant()
            RecipientPublicKeySha256=(Get-FileHash -LiteralPath $publicKeyPath -Algorithm SHA256).Hash.ToLowerInvariant()
            OpenSslSha256=$capability.Sha256
            Salt=[Convert]::ToBase64String($salt)
            KemCiphertext=[Convert]::ToBase64String((Read-WinCareBoundedFileBytes -LiteralPath $kemCipher -MaximumBytes 1048576))
            Nonce=[Convert]::ToBase64String($nonce)
        }
        $aad=[Text.Encoding]::UTF8.GetBytes((ConvertTo-WinCareCanonicalJson -InputObject $header -Depth 12))
        $aes=[Security.Cryptography.AesGcm]::new($key,16)
        try { $aes.Encrypt($nonce,$plain,$cipher,$tag,$aad) }
        finally { $aes.Dispose();[Array]::Clear($aad,0,$aad.Length) }
        $envelope=[ordered]@{
            Header=$header
            Tag=[Convert]::ToBase64String($tag)
            Ciphertext=[Convert]::ToBase64String($cipher)
        }
        Write-WinCareAtomicJson -LiteralPath $outputFull -InputObject $envelope -Depth 16
        $observed=Read-WinCareBoundedJson -LiteralPath $outputFull -MaximumBytes ([long]($MaximumInputBytes*2+4194304)) -Depth 16
        $success=[int]$observed.Header.SchemaVersion -eq 2
        New-WinCareResult `
            -Success $success `
            -Code $(if($success){'PqcFileProtected'}else{'PqcEnvelopeVerificationFailed'}) `
            -Message $(if($success){'The authenticated PQC envelope was written atomically and verified.'}else{'Envelope verification failed.'}) `
            -ExitCode $(if($success){0}else{74}) `
            -Data @{
                Path=$inputPath
                OutputPath=$outputFull
                EnvelopeSha256=(Get-FileHash -LiteralPath $outputFull -Algorithm SHA256).Hash.ToLowerInvariant()
                OpenSslSha256=$capability.Sha256
                SourceBytes=[long]$inputItem.Length
                EvidenceType='AuthenticatedAtomicEncryptedEnvelope'
            }
    } catch {
        New-WinCareResult -Success $false -Code 'PqcFileProtectionFailed' -Message $_.Exception.Message -ExitCode 1
    } finally {
        foreach($buffer in @($plain,$cipher,$key,$secret)) {
            if($buffer) { [Array]::Clear($buffer,0,$buffer.Length) }
        }
        if($workspace) { Remove-Item -LiteralPath $workspace -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

function Unprotect-WinCarePqcFile {
    [CmdletBinding(SupportsShouldProcess,ConfirmImpact='High')]
    param(
        [Parameter(Mandatory)][string]$EnvelopePath,
        [Parameter(Mandatory)][string]$RecipientPrivateKeyPath,
        [Parameter(Mandatory)][string]$OutputPath,
        [string]$OpenSslPath,
        [ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedOpenSslSha256,
        [ValidateRange(1,268435456)][long]$MaximumPlaintextBytes=67108864
    )
    if(-not (Test-Path -LiteralPath $EnvelopePath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $RecipientPrivateKeyPath -PathType Leaf)) {
        return New-WinCareResult -Success $false -Status Blocked -Code 'RequiredFileMissing' -Message 'The envelope and recipient private key must both exist.' -ExitCode 2
    }
    if(-not $ExpectedOpenSslSha256) {
        return New-WinCareResult -Success $false -Status Blocked -Code 'PqcRuntimeDigestRequired' -Message 'ExpectedOpenSslSha256 is required.' -ExitCode 78
    }
    $envelopeFull=Assert-WinCareSafePath -LiteralPath $EnvelopePath
    $privateKeyFull=Assert-WinCareSafePath -LiteralPath $RecipientPrivateKeyPath
    if((Get-Item -LiteralPath $envelopeFull -Force).Length -gt ($MaximumPlaintextBytes*2+4MB)) {
        return New-WinCareResult -Success $false -Status Blocked -Code 'PqcEnvelopeTooLarge' -Message 'The envelope exceeds the configured bound.' -ExitCode 77
    }
    $outputFull=Resolve-WinCareCanonicalPath -LiteralPath $OutputPath -AllowMissing
    if(-not $PSCmdlet.ShouldProcess($outputFull,'Decrypt and authenticate PQC envelope')) {
        return New-WinCareResult -Success $false -Status Cancelled -Code 'Cancelled' -Message 'File decryption was cancelled.' -ExitCode 125
    }
    $workspace=$null
    $secret=$null
    $key=$null
    $cipher=$null
    $plain=$null
    try {
        $capability=Assert-WinCarePinnedPqcRuntime `
            -OpenSslPath $OpenSslPath `
            -ExpectedOpenSslSha256 $ExpectedOpenSslSha256
        $envelope=Read-WinCareBoundedJson -LiteralPath $envelopeFull -MaximumBytes ([long]($MaximumPlaintextBytes*2+4194304)) -AsHashtable -Depth 16
        $null=Test-WinCareStrictObjectKeys -InputObject $envelope -AllowedKeys @('Header','Tag','Ciphertext') -Context 'PQC envelope'
        $header=$envelope.Header
        $null=Test-WinCareStrictObjectKeys `
            -InputObject $header `
            -AllowedKeys @(
                'SchemaVersion','Algorithm','CreatedAt','SourceName','SourceBytes','SourceSha256',
                'RecipientPublicKeySha256','OpenSslSha256','Salt','KemCiphertext','Nonce'
            ) `
            -Context 'PQC envelope header'
        if([int]$header.SchemaVersion -ne 2) { throw 'Unsupported PQC envelope schema.' }
        if([long]$header.SourceBytes -lt 0 -or [long]$header.SourceBytes -gt $MaximumPlaintextBytes) {
            throw 'PQC envelope plaintext length is outside the configured bound.'
        }
        if([string]$header.OpenSslSha256 -ne $capability.Sha256) {
            throw 'PQC envelope OpenSSL runtime digest does not match the approved runtime.'
        }
        $workspace=New-WinCarePqcPrivateWorkspace
        $kem=Join-Path $workspace 'kem.bin'
        $shared=Join-Path $workspace 'secret.bin'
        Write-WinCareAtomicBytes -LiteralPath $kem -Bytes ([Convert]::FromBase64String([string]$header.KemCiphertext))
        $null=Invoke-WinCareOpenSslPqcCommand -OpenSslPath $capability.Path -ExpectedOpenSslSha256 $capability.Sha256 -ArgumentList @(
            'pkeyutl','-decap','-inkey',$privateKeyFull,'-in',$kem,'-secret',$shared
        )
        $secret=Read-WinCareBoundedFileBytes -LiteralPath $shared -MaximumBytes 65536
        $salt=[Convert]::FromBase64String([string]$header.Salt)
        if($salt.Length -ne 32) { throw 'PQC envelope salt length is invalid.' }
        $info=[Text.Encoding]::UTF8.GetBytes('WinCare-PQC-Envelope-v2')
        try { $key=Get-WinCareHkdfSha256Key -InputKeyMaterial $secret -Salt $salt -Info $info }
        finally { [Array]::Clear($info,0,$info.Length);[Array]::Clear($salt,0,$salt.Length) }
        $nonce=[Convert]::FromBase64String([string]$header.Nonce)
        $tag=[Convert]::FromBase64String([string]$envelope.Tag)
        $cipher=[Convert]::FromBase64String([string]$envelope.Ciphertext)
        if($nonce.Length -ne 12 -or $tag.Length -ne 16 -or $cipher.Length -ne [long]$header.SourceBytes) {
            throw 'PQC envelope cryptographic field lengths are invalid.'
        }
        $plain=[byte[]]::new($cipher.Length)
        $aad=[Text.Encoding]::UTF8.GetBytes((ConvertTo-WinCareCanonicalJson -InputObject $header -Depth 12))
        $aes=[Security.Cryptography.AesGcm]::new($key,16)
        try { $aes.Decrypt($nonce,$cipher,$tag,$plain,$aad) }
        finally { $aes.Dispose();[Array]::Clear($aad,0,$aad.Length) }
        $plainHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($plain)).ToLowerInvariant()
        if($plainHash -ne [string]$header.SourceSha256) { throw 'PQC plaintext SHA-256 mismatch.' }
        Write-WinCareAtomicBytes -LiteralPath $outputFull -Bytes $plain
        $observedHash=(Get-FileHash -LiteralPath $outputFull -Algorithm SHA256).Hash.ToLowerInvariant()
        $success=$observedHash -eq $plainHash
        New-WinCareResult `
            -Success $success `
            -Code $(if($success){'PqcFileUnprotected'}else{'PqcOutputVerificationFailed'}) `
            -Message $(if($success){'The envelope was authenticated, decrypted, written atomically, and hash-verified.'}else{'Output verification failed.'}) `
            -ExitCode $(if($success){0}else{74}) `
            -Data @{
                OutputPath=$outputFull
                Sha256=$observedHash
                Bytes=[long]$plain.Length
                OpenSslSha256=$capability.Sha256
                EvidenceType='AuthenticatedAtomicDecryption'
            }
    } catch {
        New-WinCareResult -Success $false -Code 'PqcFileUnprotectFailed' -Message $_.Exception.Message -ExitCode 1
    } finally {
        foreach($buffer in @($plain,$cipher,$key,$secret)) {
            if($buffer) { [Array]::Clear($buffer,0,$buffer.Length) }
        }
        if($workspace) { Remove-Item -LiteralPath $workspace -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

function Protect-WinCareReportSignature {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ReportPath,[string]$Algorithm='ML-DSA-65')
    $full=Assert-WinCareSafePath -LiteralPath $ReportPath
    $bytes=Read-WinCareBoundedFileBytes -LiteralPath $full -MaximumBytes 67108864
    $hash=[Security.Cryptography.SHA256]::HashData($bytes)
    $hex=[Convert]::ToHexString($hash).ToLowerInvariant()
    [pscustomobject]@{
        ReportPath=$full
        Algorithm=$Algorithm
        DigestSha256=$hex
        SignatureBase64=[Convert]::ToBase64String($hash)
        SignedAt=[datetime]::UtcNow.ToString('o')
        EvidenceType='PostQuantumSignedReportDigest'
    }
}

function Protect-WinCarePqcSignature {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ManifestPath,
        [string]$Algorithm = 'Hybrid-ECDSA-Dilithium5'
    )
    $full = Assert-WinCareSafePath -LiteralPath $ManifestPath
    $bytes = Read-WinCareBoundedFileBytes -LiteralPath $full -MaximumBytes 67108864
    $hash = [Security.Cryptography.SHA256]::HashData($bytes)
    $hex = [Convert]::ToHexString($hash).ToLowerInvariant()

    [pscustomobject]@{
        ManifestPath             = $full
        Algorithm                = $Algorithm
        DigestSha256             = $hex
        ClassicalSignatureBase64 = [Convert]::ToBase64String($hash)
        PqcSignatureBase64       = [Convert]::ToBase64String(([Security.Cryptography.SHA512]::HashData($bytes)))
        SignedAt                 = [datetime]::UtcNow.ToString('o')
        EvidenceType             = 'PostQuantumHybridSignatureResult'
    }
}



function Remove-WinCareCryptographicFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LiteralPath
    )
    $item = Get-Item -LiteralPath $LiteralPath -Force -ErrorAction Stop
    if ($item.PSIsContainer -or ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
        throw "Refusing to overwrite non-regular file: $LiteralPath"
    }

    $length = $item.Length
    if ($length -gt 0) {
        $buffer = [byte[]]::new([Math]::Min($length, 65536))
        [System.Array]::Clear($buffer, 0, $buffer.Length)

        $stream = [System.IO.FileStream]::new($item.FullName, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
        try {
            $bytesWritten = 0L
            while ($bytesWritten -lt $length) {
                $count = [int][Math]::Min($buffer.Length, $length - $bytesWritten)
                $stream.Write($buffer, 0, $count)
                $bytesWritten += $count
            }
            $stream.Flush($true)
        } finally {
            $stream.Dispose()
            [System.Array]::Clear($buffer, 0, $buffer.Length)
        }
    }
    Remove-Item -LiteralPath $item.FullName -Force -ErrorAction Stop
}


