Add-Type -AssemblyName System.Drawing
$output=Join-Path $PSScriptRoot 'gpt-usage.ico'
$bitmap=[Drawing.Bitmap]::new(256,256)
$g=[Drawing.Graphics]::FromImage($bitmap);$g.SmoothingMode='AntiAlias';$g.TextRenderingHint='AntiAliasGridFit';$g.Clear([Drawing.Color]::Transparent)
$brush=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(91,76,219));$g.FillEllipse($brush,8,8,240,240)
$font=[Drawing.Font]::new('Segoe UI',80,[Drawing.FontStyle]::Bold,[Drawing.GraphicsUnit]::Pixel)
$format=[Drawing.StringFormat]::new();$format.Alignment='Center';$format.LineAlignment='Center'
$g.DrawString('GPT',$font,[Drawing.Brushes]::White,[Drawing.RectangleF]::new(0,0,256,246),$format)
$icon=[Drawing.Icon]::FromHandle($bitmap.GetHicon());$file=[IO.File]::Create($output);$icon.Save($file);$file.Dispose();$icon.Dispose();$format.Dispose();$font.Dispose();$brush.Dispose();$g.Dispose();$bitmap.Dispose()
