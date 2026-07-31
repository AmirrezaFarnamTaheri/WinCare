#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$Root = (Split-Path $PSScriptRoot -Parent),
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
try {
    Add-Type -AssemblyName System.Drawing.Common -ErrorAction Stop
} catch {
    Add-Type -AssemblyName System.Drawing -ErrorAction Stop
}

function New-WinCareRoundedRectanglePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][Drawing.RectangleF]$Rectangle,
        [Parameter(Mandatory)][single]$Radius
    )
    $diameter = [single]($Radius * 2)
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($Rectangle.X,$Rectangle.Y,$diameter,$diameter,180,90)
    $path.AddArc($Rectangle.Right-$diameter,$Rectangle.Y,$diameter,$diameter,270,90)
    $path.AddArc(
        $Rectangle.Right-$diameter,
        $Rectangle.Bottom-$diameter,
        $diameter,
        $diameter,
        0,
        90
    )
    $path.AddArc($Rectangle.X,$Rectangle.Bottom-$diameter,$diameter,$diameter,90,90)
    $path.CloseFigure()
    $path
}

function Set-WinCareGraphicsQuality {
    [CmdletBinding()]
    param([Parameter(Mandatory)][Drawing.Graphics]$Graphics)
    $Graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $Graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
    $Graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $Graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $Graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::ClearTypeGridFit
}

function Add-WinCareShieldPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][Drawing.Drawing2D.GraphicsPath]$Path,
        [Parameter(Mandatory)][single]$Scale
    )
    $points = @(
        [Drawing.PointF]::new(512*$Scale,150*$Scale),
        [Drawing.PointF]::new(634*$Scale,150*$Scale),
        [Drawing.PointF]::new(757*$Scale,190*$Scale),
        [Drawing.PointF]::new(842*$Scale,253*$Scale),
        [Drawing.PointF]::new(842*$Scale,472*$Scale),
        [Drawing.PointF]::new(842*$Scale,681*$Scale),
        [Drawing.PointF]::new(714*$Scale,829*$Scale),
        [Drawing.PointF]::new(512*$Scale,902*$Scale),
        [Drawing.PointF]::new(310*$Scale,829*$Scale),
        [Drawing.PointF]::new(182*$Scale,681*$Scale),
        [Drawing.PointF]::new(182*$Scale,472*$Scale),
        [Drawing.PointF]::new(182*$Scale,253*$Scale),
        [Drawing.PointF]::new(267*$Scale,190*$Scale),
        [Drawing.PointF]::new(390*$Scale,150*$Scale)
    )
    $Path.AddClosedCurve($points,0.18)
}

function New-WinCareMarkBitmap {
    [CmdletBinding()]
    param([ValidateRange(16,2048)][int]$Size)
    $scale = [single]($Size / 1024.0)
    $bitmap = [Drawing.Bitmap]::new($Size,$Size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        Set-WinCareGraphicsQuality -Graphics $graphics
        $graphics.Clear([Drawing.Color]::Transparent)
        $backgroundPath = New-WinCareRoundedRectanglePath -Rectangle (
            [Drawing.RectangleF]::new(64*$scale,64*$scale,896*$scale,896*$scale)
        ) -Radius (224*$scale)
        $backgroundBrush = [Drawing.Drawing2D.LinearGradientBrush]::new(
            [Drawing.PointF]::new(64*$scale,64*$scale),
            [Drawing.PointF]::new(960*$scale,960*$scale),
            [Drawing.ColorTranslator]::FromHtml('#10283B'),
            [Drawing.ColorTranslator]::FromHtml('#06111D')
        )
        try { $graphics.FillPath($backgroundBrush,$backgroundPath) }
        finally { $backgroundBrush.Dispose(); $backgroundPath.Dispose() }

        $shield = [Drawing.Drawing2D.GraphicsPath]::new()
        Add-WinCareShieldPath -Path $shield -Scale $scale
        $shieldColor = [Drawing.Color]::FromArgb(
            46,
            [Drawing.ColorTranslator]::FromHtml('#A7F8FF')
        )
        $shieldPen = [Drawing.Pen]::new($shieldColor,[single](28*$scale))
        $shieldPen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
        try { $graphics.DrawPath($shieldPen,$shield) }
        finally { $shieldPen.Dispose(); $shield.Dispose() }

        $accentBrush = [Drawing.Drawing2D.LinearGradientBrush]::new(
            [Drawing.PointF]::new(246*$scale,334*$scale),
            [Drawing.PointF]::new(778*$scale,704*$scale),
            [Drawing.ColorTranslator]::FromHtml('#20D5E8'),
            [Drawing.ColorTranslator]::FromHtml('#50F2A4')
        )
        $accentPen = [Drawing.Pen]::new($accentBrush,[single](92*$scale))
        $accentPen.StartCap = [Drawing.Drawing2D.LineCap]::Round
        $accentPen.EndCap = [Drawing.Drawing2D.LineCap]::Round
        $accentPen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
        $wPoints = @(
            [Drawing.PointF]::new(246*$scale,334*$scale),
            [Drawing.PointF]::new(362*$scale,704*$scale),
            [Drawing.PointF]::new(512*$scale,424*$scale),
            [Drawing.PointF]::new(662*$scale,704*$scale),
            [Drawing.PointF]::new(778*$scale,334*$scale)
        )
        try { $graphics.DrawLines($accentPen,$wPoints) }
        finally { $accentPen.Dispose(); $accentBrush.Dispose() }

        $carePen = [Drawing.Pen]::new(
            [Drawing.ColorTranslator]::FromHtml('#F5FCFF'),
            [single](28*$scale)
        )
        $carePen.StartCap = [Drawing.Drawing2D.LineCap]::Round
        $carePen.EndCap = [Drawing.Drawing2D.LineCap]::Round
        $carePen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
        $carePoints = @(
            [Drawing.PointF]::new(418*$scale,540*$scale),
            [Drawing.PointF]::new(470*$scale,540*$scale),
            [Drawing.PointF]::new(500*$scale,486*$scale),
            [Drawing.PointF]::new(542*$scale,600*$scale),
            [Drawing.PointF]::new(572*$scale,540*$scale),
            [Drawing.PointF]::new(624*$scale,540*$scale)
        )
        try {
            $graphics.DrawLines($carePen,$carePoints)
            $circleBrush = [Drawing.SolidBrush]::new(
                [Drawing.ColorTranslator]::FromHtml('#F5FCFF')
            )
            try {
                $graphics.FillEllipse(
                    $circleBrush,
                    482*$scale,
                    394*$scale,
                    60*$scale,
                    60*$scale
                )
            } finally { $circleBrush.Dispose() }
        } finally { $carePen.Dispose() }
    } finally { $graphics.Dispose() }
    $bitmap
}

function New-WinCareLogoBitmap {
    [CmdletBinding()]
    param()
    $bitmap = [Drawing.Bitmap]::new(1600,520,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        Set-WinCareGraphicsQuality -Graphics $graphics
        $graphics.Clear([Drawing.Color]::Transparent)
        $panel = New-WinCareRoundedRectanglePath -Rectangle (
            [Drawing.RectangleF]::new(0,0,1600,520)
        ) -Radius 64
        $panelBrush = [Drawing.SolidBrush]::new(
            [Drawing.ColorTranslator]::FromHtml('#07131F')
        )
        try { $graphics.FillPath($panelBrush,$panel) }
        finally { $panelBrush.Dispose(); $panel.Dispose() }

        $mark = New-WinCareMarkBitmap -Size 480
        try { $graphics.DrawImage($mark,[Drawing.Rectangle]::new(40,20,480,480)) }
        finally { $mark.Dispose() }

        $titleFont = [Drawing.Font]::new(
            'Segoe UI',
            170,
            [Drawing.FontStyle]::Bold,
            [Drawing.GraphicsUnit]::Pixel
        )
        $taglineFont = [Drawing.Font]::new(
            'Segoe UI',
            38,
            [Drawing.FontStyle]::Regular,
            [Drawing.GraphicsUnit]::Pixel
        )
        $titleBrush = [Drawing.SolidBrush]::new(
            [Drawing.ColorTranslator]::FromHtml('#F5FCFF')
        )
        $taglineBrush = [Drawing.SolidBrush]::new(
            [Drawing.ColorTranslator]::FromHtml('#75E9DA')
        )
        try {
            $graphics.DrawString('WinCare',$titleFont,$titleBrush,550,85)
            $graphics.DrawString(
                'SYSTEM HEALTH · SECURITY · CONTROL',
                $taglineFont,
                $taglineBrush,
                570,
                315
            )
        } finally {
            $taglineBrush.Dispose()
            $titleBrush.Dispose()
            $taglineFont.Dispose()
            $titleFont.Dispose()
        }
    } finally { $graphics.Dispose() }
    $bitmap
}

function ConvertTo-WinCarePngBytes {
    [CmdletBinding()]
    param([Parameter(Mandatory)][Drawing.Bitmap]$Bitmap)
    $stream = [IO.MemoryStream]::new()
    try {
        $Bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
        $stream.ToArray()
    } finally { $stream.Dispose() }
}

function Write-WinCareIco {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [Parameter(Mandatory)][int[]]$Sizes
    )
    $frames = [Collections.Generic.List[byte[]]]::new()
    try {
        foreach ($size in $Sizes) {
            $bitmap = New-WinCareMarkBitmap -Size $size
            try { $frames.Add((ConvertTo-WinCarePngBytes -Bitmap $bitmap)) }
            finally { $bitmap.Dispose() }
        }
        $stream = [IO.FileStream]::new(
            $LiteralPath,
            [IO.FileMode]::Create,
            [IO.FileAccess]::Write,
            [IO.FileShare]::None
        )
        $writer = [IO.BinaryWriter]::new($stream,[Text.Encoding]::UTF8,$false)
        try {
            $writer.Write([uint16]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]$frames.Count)
            $offset = 6 + (16 * $frames.Count)
            for ($index = 0; $index -lt $frames.Count; $index++) {
                $size = $Sizes[$index]
                $writer.Write([byte](if ($size -eq 256) { 0 } else { $size }))
                $writer.Write([byte](if ($size -eq 256) { 0 } else { $size }))
                $writer.Write([byte]0)
                $writer.Write([byte]0)
                $writer.Write([uint16]1)
                $writer.Write([uint16]32)
                $writer.Write([uint32]$frames[$index].Length)
                $writer.Write([uint32]$offset)
                $offset += $frames[$index].Length
            }
            foreach ($frame in $frames) { $writer.Write($frame) }
            $writer.Flush()
            $stream.Flush($true)
        } finally { $writer.Dispose() }
    } finally {
        foreach ($frame in $frames) {
            if ($frame.Length) { [Array]::Clear($frame,0,$frame.Length) }
        }
    }
}

function Write-WinCareUtf8NoBom {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [Parameter(Mandatory)][string]$Text
    )
    [IO.File]::WriteAllText(
        $LiteralPath,
        $Text,
        [Text.UTF8Encoding]::new($false)
    )
}

function Get-WinCareSha256 {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$LiteralPath)
    (Get-FileHash -LiteralPath $LiteralPath -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Write-WinCareBrandOutput {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$OutputDirectory)
    [void][IO.Directory]::CreateDirectory($OutputDirectory)
    $markSvg = @'
<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024" viewBox="0 0 1024 1024" role="img" aria-labelledby="title desc">
<title id="title">WinCare app mark</title>
<desc id="desc">A dark protective shield with a continuous cyan-to-mint W and central care pulse.</desc>
<defs><linearGradient id="bg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#10283B"/><stop offset="1" stop-color="#06111D"/></linearGradient><linearGradient id="accent" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#20D5E8"/><stop offset="1" stop-color="#50F2A4"/></linearGradient></defs>
<rect x="64" y="64" width="896" height="896" rx="224" fill="url(#bg)"/>
<path d="M512 150C634 150 757 190 842 253V472C842 681 714 829 512 902C310 829 182 681 182 472V253C267 190 390 150 512 150Z" fill="none" stroke="#A7F8FF" stroke-opacity=".18" stroke-width="28"/>
<path d="M246 334L362 704L512 424L662 704L778 334" fill="none" stroke="url(#accent)" stroke-width="92" stroke-linecap="round" stroke-linejoin="round"/>
<path d="M418 540H470L500 486L542 600L572 540H624" fill="none" stroke="#F5FCFF" stroke-width="28" stroke-linecap="round" stroke-linejoin="round"/>
<circle cx="512" cy="424" r="30" fill="#F5FCFF"/>
</svg>
'@
    $logoSvg = @'
<svg xmlns="http://www.w3.org/2000/svg" width="1600" height="520" viewBox="0 0 1600 520" role="img" aria-labelledby="title desc">
<title id="title">WinCare logo</title>
<desc id="desc">WinCare app mark with a clean wordmark and system health tagline.</desc>
<rect width="1600" height="520" rx="64" fill="#07131F"/>
<image href="WinCare-Mark.svg" x="40" y="20" width="480" height="480"/>
<text x="550" y="260" fill="#F5FCFF" font-family="Segoe UI, Inter, Arial, sans-serif" font-size="170" font-weight="700">WinCare</text>
<text x="570" y="365" fill="#75E9DA" font-family="Segoe UI, Inter, Arial, sans-serif" font-size="38" letter-spacing="8">SYSTEM HEALTH · SECURITY · CONTROL</text>
</svg>
'@
    Write-WinCareUtf8NoBom -LiteralPath (
        Join-Path $OutputDirectory 'WinCare-Mark.svg'
    ) -Text $markSvg
    Write-WinCareUtf8NoBom -LiteralPath (
        Join-Path $OutputDirectory 'WinCare-Logo.svg'
    ) -Text $logoSvg

    $app = New-WinCareMarkBitmap -Size 256
    try {
        $app.Save(
            (Join-Path $OutputDirectory 'WinCare-Logo-256.png'),
            [Drawing.Imaging.ImageFormat]::Png
        )
    } finally { $app.Dispose() }
    $logo = New-WinCareLogoBitmap
    try {
        $logo.Save(
            (Join-Path $OutputDirectory 'WinCare-Logo.png'),
            [Drawing.Imaging.ImageFormat]::Png
        )
    } finally { $logo.Dispose() }
    $frames = @(16,24,32,48,64,128,256)
    Write-WinCareIco -LiteralPath (
        Join-Path $OutputDirectory 'WinCare.ico'
    ) -Sizes $frames

    $manifest = [ordered]@{
        schema = 'wincare.brand/v1'
        name = 'WinCare'
        concept = 'Protective shield, continuous W, and care pulse'
        tagline = 'System health · Security · Control'
        palette = [ordered]@{
            ink = '#07131F'
            surface = '#10283B'
            cyan = '#20D5E8'
            mint = '#50F2A4'
            white = '#F5FCFF'
        }
        assets = [ordered]@{
            markSvg = [ordered]@{
                path = 'WinCare-Mark.svg'
                sha256 = Get-WinCareSha256 (
                    Join-Path $OutputDirectory 'WinCare-Mark.svg'
                )
            }
            logoSvg = [ordered]@{
                path = 'WinCare-Logo.svg'
                sha256 = Get-WinCareSha256 (
                    Join-Path $OutputDirectory 'WinCare-Logo.svg'
                )
            }
            logoPng = [ordered]@{
                path = 'WinCare-Logo.png'
                sha256 = Get-WinCareSha256 (
                    Join-Path $OutputDirectory 'WinCare-Logo.png'
                )
                width = 1600
                height = 520
            }
            appPng = [ordered]@{
                path = 'WinCare-Logo-256.png'
                sha256 = Get-WinCareSha256 (
                    Join-Path $OutputDirectory 'WinCare-Logo-256.png'
                )
                width = 256
                height = 256
            }
            appIcon = [ordered]@{
                path = 'WinCare.ico'
                sha256 = Get-WinCareSha256 (
                    Join-Path $OutputDirectory 'WinCare.ico'
                )
                frames = $frames
            }
        }
    }
    Write-WinCareUtf8NoBom -LiteralPath (
        Join-Path $OutputDirectory 'WinCare.Brand.json'
    ) -Text (($manifest | ConvertTo-Json -Depth 16) + [Environment]::NewLine)
}

$rootPath = (Resolve-Path -LiteralPath $Root -ErrorAction Stop).Path
$target = Join-Path $rootPath 'src\WinCare\Data\Gui'
if (-not $Check) {
    Write-WinCareBrandOutput -OutputDirectory $target
    Write-Host "Generated WinCare brand assets in $target"
    return
}

$temp = Join-Path $env:TEMP ('WinCare-brand-check-' + [guid]::NewGuid().ToString('N'))
try {
    Write-WinCareBrandOutput -OutputDirectory $temp
    foreach ($name in @(
        'WinCare-Mark.svg',
        'WinCare-Logo.svg',
        'WinCare-Logo.png',
        'WinCare-Logo-256.png',
        'WinCare.ico',
        'WinCare.Brand.json'
    )) {
        $expected = Join-Path $target $name
        $actual = Join-Path $temp $name
        if (-not (Test-Path -LiteralPath $expected -PathType Leaf)) {
            throw "Committed WinCare brand asset is missing: $name"
        }
        if ((Get-WinCareSha256 $expected) -ne (Get-WinCareSha256 $actual)) {
            throw "Committed WinCare brand asset is not reproducible: $name"
        }
    }
    Write-Host 'WinCare brand assets are reproducible.'
} finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
