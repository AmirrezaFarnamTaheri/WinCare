#requires -Version 7.2
function Get-WinCareSupportWallets {
    [CmdletBinding()]
    param()
    return @{
        BTC  = 'bc1q68g4m4denjw4smhvwmnz5fychuj3ge2vupx07w'
        ETH  = '0xbd5af5d1517317111db9523d6bb42fceae887abb'
        TRON = 'TRjFLA1Dd32Bw1i3FxjZW5dmVub5UfXFSS'
    }
}

function Show-WinCareSupportWalletDialog {
    [CmdletBinding()]
    param()
    $wallets = Get-WinCareSupportWallets
    Write-Host "==========================================" -ForegroundColor Cyan
    Write-Host " WinCare v1.0.0 Support Crypto Wallets" -ForegroundColor Yellow
    Write-Host "==========================================" -ForegroundColor Cyan
    Write-Host " Bitcoin (BTC):     $($wallets.BTC)" -ForegroundColor Green
    Write-Host " Ethereum (ETH/EVM): $($wallets.ETH)" -ForegroundColor Green
    Write-Host " TRON (TRX/USDT):   $($wallets.TRON)" -ForegroundColor Green
    Write-Host "==========================================" -ForegroundColor Cyan
    return $wallets
}
