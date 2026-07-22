#requires -Version 7.2
BeforeAll { Import-Module (Join-Path (Split-Path $PSScriptRoot -Parent) 'src\WinCare\WinCare.psd1') -Force }
Describe 'Declarative extension admission' {
  InModuleScope WinCare {
    BeforeEach {$script:WinCareVersion='4.0.0'}
    It 'rejects traversal and duplicate paths' {
      $m=@{schemaVersion=1;id='test.extension';name='Test';version='1.0.0';publisher='Test';description='';minimumWinCareVersion='4.0.0';files=@(@{path='../bad.json';sha256='0'*64;bytes=1});catalogFiles=@();knowledgeFiles=@();sourceRecords=@('TEST')}
      {Test-WinCareExtensionManifest $m}|Should -Throw
    }
    It 'accepts a strict non-executable data manifest' {
      $m=@{schemaVersion=1;id='test.extension';name='Test';version='1.0.0';publisher='Test';description='';minimumWinCareVersion='4.0.0';files=@(@{path='catalog/rules.json';sha256='0'*64;bytes=1});catalogFiles=@('catalog/rules.json');knowledgeFiles=@();sourceRecords=@('TEST')}
      Test-WinCareExtensionManifest $m|Should -BeTrue
    }
  }
}

Describe 'Authenticated local broker protocol' {
  InModuleScope WinCare {
    It 'signs requests deterministically and detects mutation' {
      $token=[byte[]](1..32);$e=[ordered]@{schemaVersion=2;channel='wincare.local.readonly';requestId='a'*32;timestamp=[datetime]::UtcNow.ToString('o');nonce='b'*64;command='system';arguments=@{}}
      $e.signature=Get-WinCareBrokerSignature $e $token -Fields @('schemaVersion','channel','requestId','timestamp','nonce','command','arguments')
      Test-WinCareBrokerSignature $e $token @('schemaVersion','channel','requestId','timestamp','nonce','command','arguments')|Should -BeTrue
      $e.command='applications';Test-WinCareBrokerSignature $e $token @('schemaVersion','channel','requestId','timestamp','nonce','command','arguments')|Should -BeFalse
    }
  }
}

Describe 'Uninstall command safety' {
  InModuleScope WinCare {
    It 'blocks shell and script hosts' { Test-WinCareUnsafeCommandHost cmd.exe|Should -BeTrue;Test-WinCareUnsafeCommandHost pwsh.exe|Should -BeTrue;Test-WinCareUnsafeCommandHost vendor-uninstall.exe|Should -BeFalse }
    It 'normalizes MSI uninstall to exact product removal' {
      $app=[pscustomobject]@{ProductCode='{11111111-2222-3333-4444-555555555555}';UninstallString='MsiExec.exe /I{11111111-2222-3333-4444-555555555555}';QuietUninstallString='';InstallerType='MSI';Scope='Machine'}
      $i=ConvertTo-WinCareUninstallInvocation $app;$i.FilePath|Should -Be msiexec.exe;$i.Arguments[0]|Should -Be /x;$i.SuccessExitCodes|Should -Contain 3010
    }
  }
}
