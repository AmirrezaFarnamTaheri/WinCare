$ErrorActionPreference = 'Stop';
$packageName = 'wincare'
$fileType = 'msi'
$silentArgs = '/qn /norestart'
Uninstall-ChocolateyPackage -PackageName $packageName -FileType $fileType -SilentArgs $silentArgs
