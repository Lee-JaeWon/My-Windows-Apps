Add-Type -AssemblyName System.Drawing
$assetDirectory = Join-Path $PSScriptRoot 'assets'
New-Item -ItemType Directory -Force -Path $assetDirectory | Out-Null
function RoundedPath([float]$x,[float]$y,[float]$w,[float]$h,[float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x,$y,$d,$d,180,90)
    $p.AddArc(($x+$w-$d),$y,$d,$d,270,90)
    $p.AddArc(($x+$w-$d),($y+$h-$d),$d,$d,0,90)
    $p.AddArc($x,($y+$h-$d),$d,$d,90,90)
    $p.CloseFigure()
    return ,$p
}
$bitmap = New-Object System.Drawing.Bitmap 1024,1024
$g = [System.Drawing.Graphics]::FromImage($bitmap)
$g.SmoothingMode = 'AntiAlias'
$g.TextRenderingHint = 'AntiAliasGridFit'
$g.Clear([System.Drawing.Color]::Transparent)
$tile = RoundedPath 48 48 928 928 208
$gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Point]::new(160,64)),([System.Drawing.Point]::new(864,960)),([System.Drawing.Color]::FromArgb(147,91,255)),([System.Drawing.Color]::FromArgb(65,44,179))
$g.FillPath($gradient,$tile)
$outline = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(80,255,255,255)),8
$g.DrawPath($outline,$tile)
$back = RoundedPath 260 211 534 364 65
$backBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(60,255,255,255))
$g.FillPath($backBrush,$back)
$front = RoundedPath 205 267 534 364 65
$frontBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(248,246,255))
$g.FillPath($frontBrush,$front)
$playBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(105,66,217))
$g.FillPolygon($playBrush,[System.Drawing.PointF[]]@([System.Drawing.PointF]::new(416,357),[System.Drawing.PointF]::new(416,541),[System.Drawing.PointF]::new(572,449)))
$font = New-Object System.Drawing.Font 'Segoe UI',146,([System.Drawing.FontStyle]::Bold),([System.Drawing.GraphicsUnit]::Pixel)
$format = New-Object System.Drawing.StringFormat
$format.Alignment = 'Center'
$format.LineAlignment = 'Center'
$g.DrawString('GIF',$font,[System.Drawing.Brushes]::White,[System.Drawing.RectangleF]::new(130,675,764,186),$format)
$bitmap.Save((Join-Path $assetDirectory 'gif-generator.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$sizes = @(16,24,32,48,64,128,256)
$images = @()
foreach($size in $sizes) {
    $small = New-Object System.Drawing.Bitmap $size,$size
    $sg = [System.Drawing.Graphics]::FromImage($small)
    $sg.InterpolationMode = 'HighQualityBicubic'
    $sg.DrawImage($bitmap,0,0,$size,$size)
    $ms = New-Object System.IO.MemoryStream
    $small.Save($ms,[System.Drawing.Imaging.ImageFormat]::Png)
    $images += ,$ms.ToArray()
    $ms.Dispose(); $sg.Dispose(); $small.Dispose()
}
$file = [System.IO.File]::Create((Join-Path $assetDirectory 'gif-generator.ico'))
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for($i=0;$i -lt $sizes.Count;$i++) {
    $dimension = $sizes[$i] % 256
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach($bytes in $images) { $writer.Write([byte[]]$bytes) }
$writer.Dispose(); $g.Dispose(); $bitmap.Dispose()
foreach($resource in @($tile,$gradient,$outline,$back,$backBrush,$front,$frontBrush,$playBrush,$font,$format)) { $resource.Dispose() }
