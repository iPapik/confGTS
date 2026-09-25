$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$assets = Join-Path $root "Installer\Assets"
New-Item -ItemType Directory -Force -Path $assets | Out-Null
$icoPath = Join-Path $assets "ConfGTS.ico"

$size = 128
$bmp = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)

$rect = [System.Drawing.Rectangle]::new(5, 5, 118, 118)
$path = [System.Drawing.Drawing2D.GraphicsPath]::new()
$radius = 28
$diameter = $radius * 2
$path.AddArc($rect.X, $rect.Y, $diameter, $diameter, 180, 90)
$path.AddArc($rect.Right - $diameter, $rect.Y, $diameter, $diameter, 270, 90)
$path.AddArc($rect.Right - $diameter, $rect.Bottom - $diameter, $diameter, $diameter, 0, 90)
$path.AddArc($rect.X, $rect.Bottom - $diameter, $diameter, $diameter, 90, 90)
$path.CloseFigure()

$start = [System.Drawing.Color]::FromArgb(255, 22, 140, 184)
$end = [System.Drawing.Color]::FromArgb(255, 22, 182, 194)
$gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new($rect, $start, $end, 45.0)
$g.FillPath($gradient, $path)

$overlay = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(42, 11, 47, 91))
$g.FillEllipse($overlay, -12, 65, 152, 92)

$whitePen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(245, 255, 255, 255), 7)
$whitePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$whitePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$whitePen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

foreach ($x in @(43, 64, 85)) {
    $wave = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $wave.AddBezier($x, 38, $x - 7, 48, $x + 7, 58, $x, 68)
    $wave.AddBezier($x, 68, $x - 7, 78, $x + 7, 88, $x, 98)
    $g.DrawPath($whitePen, $wave)
    $wave.Dispose()
}

$underlinePen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(235, 255, 255, 255), 6)
$underlinePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$underlinePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawLine($underlinePen, 42, 106, 86, 106)

$hIcon = $bmp.GetHicon()
try {
    $icon = [System.Drawing.Icon]::FromHandle($hIcon)
    $stream = [System.IO.File]::Create($icoPath)
    try { $icon.Save($stream) } finally { $stream.Dispose() }
} finally {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ConfGTSNativeIcon {
    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool DestroyIcon(IntPtr hIcon);
}
'@ -ErrorAction SilentlyContinue
    [ConfGTSNativeIcon]::DestroyIcon($hIcon) | Out-Null
}

$underlinePen.Dispose()
$whitePen.Dispose()
$overlay.Dispose()
$gradient.Dispose()
$path.Dispose()
$g.Dispose()
$bmp.Dispose()

if (!(Test-Path $icoPath)) { throw "Failed to generate ConfGTS.ico" }
Write-Host "Generated ConfGTS icon: $icoPath" -ForegroundColor Cyan
