<#
.SYNOPSIS
Builds a collision-safe, hash-bound file organization plan.
#>
function Invoke-WinCareSmartFileOrganizer {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$SourceDirectory,
        [ValidateRange(1,10000)][int]$MaximumFiles=2000,
        [switch]$Apply,[switch]$Approved,[switch]$PreviewOnly
    )
    try{$root=(Resolve-Path -LiteralPath $SourceDirectory -ErrorAction Stop).Path}catch{return New-WinCareResult -Success $false -Status Failed -Code 'SourceDirectoryMissing' -Message $_.Exception.Message -ExitCode 2}
    $rootItem=Get-Item -LiteralPath $root -Force -ErrorAction Stop
    if(-not $rootItem.PSIsContainer -or ($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint)){return New-WinCareResult -Success $false -Status Blocked -Code 'UnsafeOrganizerRoot' -Message 'The organizer root must be a real directory, not a reparse point.' -ExitCode 22}
    $categories=[ordered]@{
        Documents=@('.pdf','.docx','.xlsx','.pptx','.txt','.csv','.md','.odt','.ods')
        Installer_Packages=@('.msi','.msix','.appx','.iso')
        Media_Assets=@('.mp4','.mkv','.mov','.png','.jpg','.jpeg','.gif','.mp3','.wav','.flac')
        Archive_Files=@('.zip','.tar','.gz','.7z','.rar')
    }
    $files=@(Get-ChildItem -LiteralPath $root -File -Force -ErrorAction Stop | Where-Object {-not($_.Attributes -band [IO.FileAttributes]::ReparsePoint)} | Sort-Object FullName)
    if($files.Count -gt $MaximumFiles){return New-WinCareResult -Success $false -Status Blocked -Code 'OrganizerFileLimitExceeded' -Message "The root contains $($files.Count) files; limit is $MaximumFiles." -ExitCode 5}
    $actions=[Collections.Generic.List[object]]::new();$skipped=[Collections.Generic.List[object]]::new()
    foreach($file in $files){
        $category='Uncategorized';foreach($entry in $categories.GetEnumerator()){if($entry.Value -contains $file.Extension.ToLowerInvariant()){$category=$entry.Key;break}}
        $destination=Join-Path (Join-Path $root $category) $file.Name
        if(Test-Path -LiteralPath $destination){$skipped.Add([pscustomobject]@{Source=$file.FullName;Destination=$destination;Reason='DestinationExists'});continue}
        $hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
        $actions.Add((New-WinCareAction -Type 'MoveFile' -Label "Move $($file.Name) to $category" -Risk Moderate -Parameters @{Source=$file.FullName;Destination=$destination;ExpectedSourceHash=$hash;AllowedRoots=@($root)} -Reversible $true -Compensator @{Type='MoveFile';Parameters=@{Source=$destination;Destination=$file.FullName;ExpectedSourceHash=$hash;AllowedRoots=@($root)}} -EstimatedBytes ([long]$file.Length) -Verification 'The destination hash must match the source hash and the source must be absent.'))
    }
    $plan=New-WinCarePlan -Title "Organize files in $root" -Description 'Moves only direct child files, never overwrites destinations, and binds every move to the previewed SHA-256.' -Actions @($actions) -RollbackOnFailure $true -Metadata @{Root=$root;Scanned=$files.Count;Planned=$actions.Count;Skipped=@($skipped);EvidenceType='FileInventoryAndSha256'}
    if(-not $Apply -and -not $PreviewOnly){return $plan};Invoke-WinCarePlan -Plan $plan -Approved:$Approved -PreviewOnly:$PreviewOnly
}
if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Invoke-WinCareSmartFileOrganizer }
