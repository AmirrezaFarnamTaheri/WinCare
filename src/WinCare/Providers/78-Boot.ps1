function Get-WinCareBootDiagnostics {
    [CmdletBinding()]param()
    $bcd=if(Get-Command bcdedit.exe -ErrorAction SilentlyContinue){Invoke-WinCareProcess -FilePath bcdedit.exe -ArgumentList @('/enum','{current}') -TimeoutSeconds 30}else{$null}
    $winre=if(Get-Command reagentc.exe -ErrorAction SilentlyContinue){Invoke-WinCareProcess -FilePath reagentc.exe -ArgumentList @('/info') -TimeoutSeconds 30}else{$null}
    $secure=$null;try{$secure=Confirm-SecureBootUEFI}catch { Write-Verbose 'A best-effort operation was unavailable.' }
    [pscustomobject]@{Bcd=if($bcd){$bcd.Data.StdOut}else{$null};WinRE=if($winre){$winre.Data.StdOut}else{$null};SecureBoot=$secure;SystemDrive=$env:SystemDrive;FirmwareType=$env:firmware_type;ModificationPolicy='WinCare never replaces the Windows boot loader. Critical BCD changes are disabled by default.'}
}

function New-WinCareBcdExportPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$Destination)
    $destination=Assert-WinCareSafePath -LiteralPath $Destination -AllowMissing;if(Test-Path -LiteralPath $destination){throw 'BCD backup destination already exists.'}
    $action=New-WinCareAction -Type BcdExport -Label 'Export the Boot Configuration Data store' -Risk Moderate -Parameters @{Destination=$destination} -RequiresAdmin $true -Reversible $false -Tags @('Boot','Backup') -SourceRecords @('D15-BOOT-GUARDRAIL') -RecoveryDescription 'The export is a backup artifact; WinCare does not automatically import it.'
    New-WinCarePlan -Title 'Export BCD backup' -Actions @($action) -RollbackOnFailure $false -SourceRecords @('D15-BOOT-GUARDRAIL')
}

function Invoke-WinCareBcdExportAction {
    param([object]$Action)
    $destination=Assert-WinCareSafePath -LiteralPath ([string]$Action.Parameters.Destination) -AllowMissing;if(Test-Path -LiteralPath $destination){return New-WinCareResult -Success $false -Message 'BCD backup destination already exists.' -ExitCode 17}
    $parent=Split-Path -Parent $destination;$null=New-Item -ItemType Directory -Path $parent -Force
    $result=Invoke-WinCareProcess -FilePath bcdedit.exe -ArgumentList @('/export',$destination) -RequireAdmin -TimeoutSeconds 60
    if(-not $result.Success){return $result}
    New-WinCareResult -Success (Test-Path -LiteralPath $destination) -Message 'BCD store exported.' -Data @{Path=$destination;Sha256=Get-WinCareSha256 $destination}
}


