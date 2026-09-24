# Draws the Plugin Hub icon - the orange tile of wwwroot\favicon.svg with its white hub of
# four nodes - at every size Windows asks for, and packs them into hub.ico (PNG entries).
#
#   powershell -ExecutionPolicy Bypass -File tools\make_icon.ps1      (from the PluginHub folder)

Add-Type -AssemblyName System.Drawing

$target = Join-Path (Split-Path $PSScriptRoot -Parent) 'hub.ico'
$sizes  = 16, 20, 24, 32, 40, 48, 64, 128, 256
$orange = [System.Drawing.Color]::FromArgb(255, 0xE0, 0x80, 0x20)

function Draw-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode   = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $k = $size / 256.0

    # Tile with rounded corners (radius 56 of 256)
    $d = 112 * $k
    $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tile.AddArc(0, 0, $d, $d, 180, 90)
    $tile.AddArc($size - $d, 0, $d, $d, 270, 90)
    $tile.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
    $tile.AddArc(0, $size - $d, $d, $d, 90, 90)
    $tile.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush $orange), $tile)

    # Small sizes get slightly heavier strokes and dots, or the hub dissolves into blur
    $stroke = [Math]::Max(16 * $k, 1.6)
    $node   = [Math]::Max(22 * $k, 2.2)
    $centre = [Math]::Max(32 * $k, 3.0)

    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $stroke
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)

    $cx = 128 * $k
    $cy = 124 * $k
    foreach ($n in @(@(70, 78), @(186, 78), @(128, 196))) {
        $x = $n[0] * $k
        $y = $n[1] * $k
        $g.DrawLine($pen, $cx, $cy, $x, $y)
        $g.FillEllipse($white, $x - $node, $y - $node, 2 * $node, 2 * $node)
    }
    $g.FillEllipse($white, $cx - $centre, $cy - $centre, 2 * $centre, 2 * $centre)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$images = @()
foreach ($s in $sizes) { $images += ,(Draw-Icon $s) }

# ICO container: header, one 16-byte entry per image, then the PNG data
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0)
$w.Write([UInt16]1)
$w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([Byte]$dim)
    $w.Write([Byte]$dim)
    $w.Write([Byte]0)
    $w.Write([Byte]0)
    $w.Write([UInt16]1)
    $w.Write([UInt16]32)
    $w.Write([UInt32]$images[$i].Length)
    $w.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
[System.IO.File]::WriteAllBytes($target, $out.ToArray())
"hub.ico: $($sizes.Count) sizes, $($out.Length) bytes"
