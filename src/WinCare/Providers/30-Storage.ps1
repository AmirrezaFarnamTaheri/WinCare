<#
    .SYNOPSIS
    WinCare v5.2.0 Smart File Organizer & Storage Management Provider
    Module ID: 30-Storage

    .DESCRIPTION
    Automated file categorization and content hash caching provider.
    Derived from FileWizardAI (171) - 100% Self-Inclusive.
#>

function Invoke-WinCareSmartFileOrganizer {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory=$true)]
        [string]$SourceDirectory,

        [switch]$DryRun = $false
    )

    if (-not (Test-Path $SourceDirectory)) {
        Write-Error "Source directory does not exist: $SourceDirectory"
        return
    }

    $categories = @{
        "Documents"          = @(".pdf", ".docx", ".xlsx", ".pptx", ".txt", ".csv")
        "Installer_Packages" = @(".msi", ".exe", ".iso")
        "Media_Assets"       = @(".mp4", ".png", ".jpg", ".jpeg", ".mp3", ".wav")
        "Archive_Files"      = @(".zip", ".tar", ".gz", ".7z", ".rar")
    }

    $files = Get-ChildItem -Path $SourceDirectory -File
    Write-Host "Scanning $($files.Count) files in $SourceDirectory for smart organization..." -ForegroundColor Yellow

    foreach ($file in $files) {
        $ext = $file.Extension.ToLower()
        $targetCategory = "Uncategorized"

        foreach ($cat in $categories.Keys) {
            if ($categories[$cat] -contains $ext) {
                $targetCategory = $cat
                break
            }
        }

        $targetFolder = Join-Path $SourceDirectory $targetCategory
        if (-not $DryRun -and -not (Test-Path $targetFolder)) {
            New-Item -Path $targetFolder -ItemType Directory -Force | Out-Null
        }

        $destPath = Join-Path $targetFolder $file.Name
        if ($PSCmdlet.ShouldProcess($file.Name, "Move to $targetCategory")) {
            if (-not $DryRun) {
                Move-Item -Path $file.FullName -Destination $destPath -Force
                Write-Host "Moved $($file.Name) -> $targetCategory" -ForegroundColor Green
            } else {
                Write-Host "[DRY-RUN] Would move $($file.Name) -> $targetCategory" -ForegroundColor Gray
            }
        }
    }
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Invoke-WinCareSmartFileOrganizer
 }

