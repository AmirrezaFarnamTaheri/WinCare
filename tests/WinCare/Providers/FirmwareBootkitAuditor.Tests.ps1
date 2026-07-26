BeforeAll { Import-Module (Join-Path $PSScriptRoot '..\..\..\src\WinCare\WinCare.psd1') -Force }
Describe 'WinCare firmware evidence provider' {
    It 'exports Secure Boot and TPM evidence commands' {
        Get-Command Audit-WinCareUefiDbxSignatures -Module WinCare | Should -Not -BeNullOrEmpty
        Get-Command Get-WinCareTpmPcrReport -Module WinCare | Should -Not -BeNullOrEmpty
    }
    It 'returns explicit support and evidence fields rather than a fixed secure verdict' {
        $result = Audit-WinCareUefiDbxSignatures
        $result.PSObject.Properties.Name | Should -Contain 'Success'
        $result.PSObject.Properties.Name | Should -Contain 'Code'
        $result.Code | Should -Not -Be 'Secure'
    }
    It 'does not claim TPM boot integrity without observed evidence' {
        $result = Get-WinCareTpmPcrReport
        $result.PSObject.Properties.Name | Should -Contain 'Success'
        $result.Code | Should -Not -Be 'Verified'
    }
}
