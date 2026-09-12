param(
    [string]$Executable = "$PSScriptRoot/../src/WinCare.App/bin/x64/Debug/net8.0-windows10.0.19041.0/win-x64/WinCare.App.exe",
    [string]$NavigationId = 'NavHome',
    [string]$OutputPath,
    [int]$Width = 1440,
    [int]$Height = 1000
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class WinCareReviewWindow {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int height, uint flags);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int command);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attribute, out Rect value, int size);
}
'@
[void][WinCareReviewWindow]::SetProcessDpiAwarenessContext([IntPtr](-4))
[void][WinCareReviewWindow]::SetThreadDpiAwarenessContext([IntPtr](-4))
$exe = (Resolve-Path -LiteralPath $Executable).Path
$process = Get-Process | Where-Object { $_.Path -eq $exe } | Select-Object -First 1
if (-not $process) { $process = Start-Process -FilePath $exe -PassThru -WindowStyle Hidden }
$deadline = [DateTime]::UtcNow.AddSeconds(20)
do {
    $process.Refresh()
    if ($process.HasExited) { throw 'WinCare exited before opening its window.' }
    if ($process.MainWindowHandle -ne 0) { break }
    Start-Sleep -Milliseconds 200
} while ([DateTime]::UtcNow -lt $deadline)
if ($process.MainWindowHandle -eq 0) { throw 'WinCare did not open its window.' }
$handle = $process.MainWindowHandle
[void][WinCareReviewWindow]::ShowWindow($handle, 9)
[void][WinCareReviewWindow]::SetWindowPos($handle, [IntPtr]::Zero, 40, 40, $Width, $Height, 0x0040)
$window = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
$condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $NavigationId)
$deadline = [DateTime]::UtcNow.AddSeconds(20)
do {
    $element = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($element) { break }
    $process.Refresh()
    if ($process.HasExited) { throw 'WinCare exited while loading navigation. Inspect its local crash log.' }
    Start-Sleep -Milliseconds 200
} while ([DateTime]::UtcNow -lt $deadline)
if (-not $element) { throw "Navigation item not found: $NavigationId" }
$pattern = $null
if ($element.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) { $pattern.Select() }
elseif ($element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) { $pattern.Invoke() }
else { throw "Navigation item cannot be activated: $NavigationId" }
Start-Sleep -Milliseconds 700
if ($OutputPath) {
    $rectangle = [WinCareReviewWindow+Rect]::new()
    [void][WinCareReviewWindow]::GetWindowRect($handle, [ref]$rectangle)
    [void][WinCareReviewWindow]::DwmGetWindowAttribute($handle, 9, [ref]$rectangle, 16)
    [void][WinCareReviewWindow]::SetForegroundWindow($handle)
    Start-Sleep -Milliseconds 250
    $bitmap = [Drawing.Bitmap]::new($rectangle.Right - $rectangle.Left, $rectangle.Bottom - $rectangle.Top)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try { $graphics.CopyFromScreen($rectangle.Left, $rectangle.Top, 0, 0, $bitmap.Size) }
    finally { $graphics.Dispose() }
    try { $bitmap.Save([IO.Path]::GetFullPath($OutputPath), [Drawing.Imaging.ImageFormat]::Png) }
    finally { $bitmap.Dispose() }
}
Write-Output "Opened $NavigationId at $Width x $Height."
