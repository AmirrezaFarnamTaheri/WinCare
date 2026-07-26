Describe 'WinCare QUIC UDP Desync Native Method' {
    It 'Should expose SendQuicDesyncDatagram native method' {
        $type = [Type]::GetType('WinCare.Native.DpiHelper, WinCare.Native')
        if ($type) {
            $method = $type.GetMethod('SendQuicDesyncDatagram')
            $method | Should -Not -Be $null
        }
    }
}
