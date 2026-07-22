. "$PSScriptRoot\..\..\..\src\WinCare\Providers\128-WasiPluginRuntime.ps1"

Describe 'WinCare WASI Plugin Runtime Module' {
    It 'Should export WASI plugin execution and catalog cmdlets' {
        (Get-Command 'Invoke-WinCareWasiPlugin' -ErrorAction SilentlyContinue) | Should Not Be $null
        (Get-Command 'Get-WinCareWasiMarketplaceCatalog' -ErrorAction SilentlyContinue) | Should Not Be $null
    }

    It 'Should execute WASI plugin and return catalog' {
        $wasm = Invoke-WinCareWasiPlugin -PluginPath 'diag.wasm'
        $wasm.Status | Should Be 'Success'
        $cat = Get-WinCareWasiMarketplaceCatalog
        $cat.Count | Should Be 2
    }
}
