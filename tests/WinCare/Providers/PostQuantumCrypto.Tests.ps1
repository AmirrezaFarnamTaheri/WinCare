BeforeAll { Import-Module (Join-Path $PSScriptRoot '..\..\..\src\WinCare\WinCare.psd1') -Force }
Describe 'WinCare post-quantum cryptography adapter' {
    It 'exports key exchange and file envelope commands' {
        Get-Command Invoke-WinCarePqcKeyExchange -Module WinCare | Should -Not -BeNullOrEmpty
        Get-Command Protect-WinCarePqcFile -Module WinCare | Should -Not -BeNullOrEmpty
        Get-Command Unprotect-WinCarePqcFile -Module WinCare | Should -Not -BeNullOrEmpty
    }
    It 'blocks a key exchange when the configured OpenSSL runtime does not exist' {
        $result = Invoke-WinCarePqcKeyExchange -OpenSslPath (Join-Path $TestDrive 'missing-openssl.exe') -ExpectedOpenSslSha256 ('0' * 64)
        $result.Success | Should -BeFalse
        $result.Status | Should -Be 'Blocked'
        $result.Code | Should -Be 'PqcRuntimeUnavailable'
    }
    It 'requires a real input file and recipient key before protection' {
        $result = Protect-WinCarePqcFile -Path (Join-Path $TestDrive 'missing.dat') -RecipientPublicKeyPath (Join-Path $TestDrive 'missing.pem') -Confirm:$false
        $result.Success | Should -BeFalse
        $result.Status | Should -Be 'Blocked'
    }
}
