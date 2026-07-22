. "$PSScriptRoot\..\..\..\src\WinCare\Providers\121-PostQuantumCrypto.ps1"

Describe 'WinCare Post-Quantum Crypto Module' {
    It 'Should export PQC key exchange and file protection cmdlets' {
        (Get-Command 'Invoke-WinCarePqcKeyExchange' -ErrorAction SilentlyContinue) | Should Not Be $null
        (Get-Command 'Protect-WinCarePqcFile' -ErrorAction SilentlyContinue) | Should Not Be $null
    }

    It 'Should execute PQC key exchange and protect file' {
        $pqc = Invoke-WinCarePqcKeyExchange -Algorithm 'ML-KEM-768'
        $pqc.QuantumSecure | Should Be $true
        $file = Protect-WinCarePqcFile -Path 'C:\Temp\secret.dat'
        $file.Status | Should Be 'Protected'
    }
}
