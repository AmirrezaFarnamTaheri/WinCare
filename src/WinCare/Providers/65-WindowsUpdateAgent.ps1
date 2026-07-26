<#
.SYNOPSIS
Queries the Windows Update Agent for missing driver updates with explicit availability evidence.
#>
function Get-WinCareAvailableDriverUpdates {
    [CmdletBinding()]
    param([ValidateRange(1,1000)][int]$MaximumUpdates = 200)

    if (-not $IsWindows) {
        return New-WinCareResult -Success $false -Status Blocked -Code 'WindowsUpdateAgentUnsupported' -Message 'Windows Update Agent is available only on Windows.' -ExitCode 78
    }
    try {
        $session = New-Object -ComObject Microsoft.Update.Session -ErrorAction Stop
        $searcher = $session.CreateUpdateSearcher()
        $searcher.ServerSelection = 2
        $search = $searcher.Search("IsInstalled=0 AND Type='Driver'")
        $items = [Collections.Generic.List[object]]::new()
        for ($index = 0; $index -lt $search.Updates.Count -and $items.Count -lt $MaximumUpdates; $index++) {
            $update = $search.Updates.Item($index)
            $identity = $update.Identity
            $items.Add([pscustomobject]@{
                UpdateId=[string]$identity.UpdateID;RevisionNumber=[int]$identity.RevisionNumber
                Title=[string]$update.Title;DriverClass=[string]$update.DriverClass
                DriverModel=[string]$update.DriverModel;DriverProvider=[string]$update.DriverProvider
                IsDownloaded=[bool]$update.IsDownloaded;IsMandatory=[bool]$update.IsMandatory
                RebootRequired=[bool]$update.RebootRequired;MsrcSeverity=[string]$update.MsrcSeverity
            })
        }
        New-WinCareResult -Success $true -Code 'DriverUpdatesObserved' -Message 'Windows Update Agent driver search completed.' -Data @{
            Updates=@($items);Count=$items.Count;TotalResultCount=[int]$search.Updates.Count
            Truncated=[int]$search.Updates.Count -gt $items.Count;ResultCode=[string]$search.ResultCode
            EvidenceType='WindowsUpdateAgentSearchResult'
        }
    } catch {
        $hresult = ('0x{0:X8}' -f ($_.Exception.HResult -band 0xffffffffL))
        New-WinCareResult -Success $false -Status Failed -Code 'WindowsUpdateAgentSearchFailed' -Message $_.Exception.Message -ExitCode 1 -Data @{HResult=$hresult;EvidenceType='ComException'}
    }
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Get-WinCareAvailableDriverUpdates }
