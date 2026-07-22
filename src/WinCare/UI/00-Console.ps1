function Get-WinCareColor {
    param([ValidateSet('Primary','Accent','Success','Warning','Danger','Muted','Text','SelectedText','SelectedBackground')][string]$Role)
    $theme = 'Default'
    try {
        if (Get-Command Ensure-WinCareState -ErrorAction SilentlyContinue) { Ensure-WinCareState }
        if (Get-Command Get-WinCareConfig -ErrorAction SilentlyContinue) {
            $val = Get-WinCareConfig 'Theme'
            if ($val) { $theme = [string]$val }
        }
    } catch {}

    if ($theme -eq 'Monochrome') {
        switch ($Role) {
            'SelectedText' { return 'Black' }
            'SelectedBackground' { return 'Gray' }
            default { return 'Gray' }
        }
    }
    if ($theme -eq 'HighContrast') {
        switch ($Role) {
            'Primary' { return 'White' }
            'Accent' { return 'Yellow' }
            'Success' { return 'Green' }
            'Warning' { return 'Yellow' }
            'Danger' { return 'Red' }
            'Muted' { return 'Gray' }
            'Text' { return 'White' }
            'SelectedText' { return 'Black' }
            'SelectedBackground' { return 'Yellow' }
            default { return 'White' }
        }
    }
    switch ($Role) {
        'Primary' { return 'Cyan' }
        'Accent' { return 'Blue' }
        'Success' { return 'Green' }
        'Warning' { return 'Yellow' }
        'Danger' { return 'Red' }
        'Muted' { return 'DarkGray' }
        'Text' { return 'Gray' }
        'SelectedText' { return 'Black' }
        'SelectedBackground' { return 'Cyan' }
        default { return 'Gray' }
    }
}

function Get-WinCareConsoleSize {
    try {
        [pscustomobject]@{
            Width=[math]::Max(20,[Console]::WindowWidth)
            Height=[math]::Max(10,[Console]::WindowHeight)
        }
    } catch { [pscustomobject]@{ Width=100; Height=30 } }
}

function Clear-WinCareScreen {
    try { [Console]::Clear() } catch { Clear-Host }
}

function Write-WinCareText {
    param(
        [AllowEmptyString()][string]$Text = '',
        [string]$Role = 'Text',
        [switch]$NoNewline
    )
    $params = @{ Object=$Text; ForegroundColor=(Get-WinCareColor $Role); NoNewline=[bool]$NoNewline }
    Write-Host @params
}

function Get-WinCareGlyph {
    param([string]$Unicode,[string]$Ascii)
    try {
        if (Get-Command Get-WinCareConfig -ErrorAction SilentlyContinue) {
            if (Get-WinCareConfig 'Ascii') { return $Ascii }
        }
    } catch {}
    return $Unicode
}

function Write-WinCareHeader {
    param([string]$Title)
    $size = Get-WinCareConsoleSize
    $line = [string]::new('='[0], $size.Width)
    Write-WinCareText -Text $line -Role 'Primary'
    Write-WinCareText -Text "  $Title" -Role 'Accent'
    Write-WinCareText -Text $line -Role 'Primary'
}
