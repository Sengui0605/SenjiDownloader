param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\SenjiDownloader\assets'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
function RoundPath([float]$X, [float]$Y, [float]$Size, [float]$Radius) {
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $diameter = $Radius * 2
    $path.AddArc($X,$Y,$diameter,$diameter,180,90)
    $path.AddArc($X+$Size-$diameter,$Y,$diameter,$diameter,270,90)
    $path.AddArc($X+$Size-$diameter,$Y+$Size-$diameter,$diameter,$diameter,0,90)
    $path.AddArc($X,$Y+$Size-$diameter,$diameter,$diameter,90,90)
    $path.CloseFigure(); return $path
}
$images = @()
foreach ($size in @(16,24,32,48,64,128,256)) {
    $bitmap = New-Object Drawing.Bitmap $size,$size
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size/256.0,$size/256.0)
    $background = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(181,235,200))
    $path = RoundPath 12 12 232 60
    $graphics.FillPath($background,$path)
    $pen = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(17,42,26)),16
    $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $graphics.DrawLine($pen,128,65,128,145)
    $graphics.DrawLines($pen,[Drawing.PointF[]]@([Drawing.PointF]::new(91,109),[Drawing.PointF]::new(128,146),[Drawing.PointF]::new(165,109)))
    $graphics.DrawLines($pen,[Drawing.PointF[]]@([Drawing.PointF]::new(73,163),[Drawing.PointF]::new(73,188),[Drawing.PointF]::new(183,188),[Drawing.PointF]::new(183,163)))
    $memory = New-Object IO.MemoryStream
    $bitmap.Save($memory,[Drawing.Imaging.ImageFormat]::Png)
    $images += [PSCustomObject]@{Size=$size;Data=$memory.ToArray()}
    if ($size -eq 256) { $bitmap.Save((Join-Path $OutputDirectory 'senji.png'),[Drawing.Imaging.ImageFormat]::Png) }
    $memory.Dispose(); $pen.Dispose(); $background.Dispose(); $path.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$file = [IO.File]::Create((Join-Path $OutputDirectory 'senji.ico'))
$writer = New-Object IO.BinaryWriter $file
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($item in $images) {
    $dimension = if ($item.Size -eq 256) { 0 } else { $item.Size }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$item.Data.Length); $writer.Write([uint32]$offset)
    $offset += $item.Data.Length
}
foreach ($item in $images) { $writer.Write([byte[]]$item.Data) }
$writer.Dispose()
