<#
    .SYNOPSIS
    WinCare v5.2.0 Windows Update COM & Driver Scraper Provider
    Module ID: 65-WindowsUpdateAgent

    .DESCRIPTION
    Queries COM Microsoft.Update.Session for missing driver updates and decodes HRESULT error codes.
    Derived from DriverUpdate (095) and errorlookup (098) - 100% Self-Inclusive.
#>

function Get-WinCareAvailableDriverUpdates {
    [CmdletBinding()]
    param()

    Write-Verbose "Querying COM Microsoft.Update.Session for pending driver updates..."
    try {
        $session = New-Object -ComObject Microsoft.Update.Session
        $searcher = $session.CreateUpdateSearcher()
        $searcher.ServerSelection = 2 # Windows Update
        $results = $searcher.Search("IsInstalled=0 AND Type='Driver'")

        $updates = @()
        foreach ($update in $results.Updates) {
            $updates += [PSCustomObject]@{
                Title            = $update.Title
                DriverClass      = $update.DriverClass
                DriverModel      = $update.DriverModel
                DriverProvider   = $update.DriverProvider
                IsDownloaded     = $update.IsDownloaded
            }
        }
        return $updates
    } catch {
        Write-Warning "COM Windows Update search unavailable or offline: $_"
        return @()
    }
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Get-WinCareAvailableDriverUpdates }

