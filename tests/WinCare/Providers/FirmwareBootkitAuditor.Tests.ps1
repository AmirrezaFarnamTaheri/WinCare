. "$PSScriptRoot\..\..\..\src\WinCare\Providers\120-FirmwareBootkitAuditor.ps1"

Describe 'WinCare Firmware Bootkit Auditor Module' {
    It 'Should export UEFI DBX and TPM PCR auditor cmdlets' {
        (Get-Command 'Audit-WinCareUefiDbxSignatures' -ErrorAction SilentlyContinue) | Should Not Be $null
        (Get-Command 'Get-WinCareTpmPcrReport' -ErrorAction SilentlyContinue) | Should Not Be $null
    }

    It 'Should audit UEFI DBX signatures and return TPM PCR report' {
        $uefi = Audit-WinCareUefiDbxSignatures
        $uefi.Status | Should Be 'Secure'
        $tpm = Get-WinCareTpmPcrReport
        $tpm.BootIntegrity | Should Be 'Verified'
    }
}
