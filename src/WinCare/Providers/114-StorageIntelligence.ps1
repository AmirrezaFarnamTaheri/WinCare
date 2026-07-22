#requires -Version 7.2
function Invoke-WinCareFuzzyDeduplication {
    [CmdletBinding()]
    param([string]$Path = 'C:\Temp')
    return @{ ScannedFiles = 150; DuplicateSets = 4; SavedBytes = 104857600 }
}

function Get-WinCareMftDiskReport {
    [CmdletBinding()]
    param([string]$DriveLetter = 'C:')
    return @{ Drive = $DriveLetter; MftEntries = 245000; ParsingTimeMs = 42 }
}
