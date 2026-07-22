. "$PSScriptRoot\..\..\..\src\WinCare\Providers\133-SupportCryptoWallet.ps1"

Describe 'WinCare Support Crypto Wallet Module' {
    It 'Should export crypto wallet functions' {
        (Get-Command 'Get-WinCareSupportWallets' -ErrorAction SilentlyContinue) | Should Not Be $null
        (Get-Command 'Show-WinCareSupportWalletDialog' -ErrorAction SilentlyContinue) | Should Not Be $null
    }

    It 'Should return valid Scriptor crypto wallet addresses' {
        $wallets = Get-WinCareSupportWallets
        $wallets.BTC | Should Be 'bc1q68g4m4denjw4smhvwmnz5fychuj3ge2vupx07w'
        $wallets.ETH | Should Be '0xbd5af5d1517317111db9523d6bb42fceae887abb'
        $wallets.TRON | Should Be 'TRjFLA1Dd32Bw1i3FxjZW5dmVub5UfXFSS'
    }
}
