# Draws the Wecker's icon - the orange tile of the Plugin Hub with a white "on" sign - as icon.png,
# 180 x 180, the size an iPhone wants for a symbol on its home screen (it rounds the corners itself).
#
#   powershell -ExecutionPolicy Bypass -File wecker\make_icon.ps1      (from the hub's folder)

Add-Type -AssemblyName System.Drawing

$size   = 180
$target = Join-Path $PSScriptRoot 'icon.png'
$orange = [System.Drawing.Color]::FromArgb(255, 0xE0, 0x80, 0x20)

$bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode   = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.Clear($orange)

$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 15
$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round

# The ring, open at the top, and the stroke through the opening
$r = 46
$cx = 90
$cy = 98
$g.DrawArc($pen, $cx - $r, $cy - $r, 2 * $r, 2 * $r, -55, 290)
$g.DrawLine($pen, $cx, 36, $cx, 94)

$g.Dispose()
$bmp.Save($target, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "icon.png: $size x $size"
