param(
    [Parameter(Mandatory = $true)]
    [string] $OutputPath
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function New-Brush([string] $hex) {
    return [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml($hex))
}

function New-IconPng([int] $size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent
    )
    $graphics.ScaleTransform($size / 256.0, $size / 256.0)

    $greenDark = New-Brush "#267C3A"
    $green = New-Brush "#3E9B45"
    $greenLight = New-Brush "#75B94A"
    $greenDeep = New-Brush "#2B8841"
    $greenLime = New-Brush "#A1CF57"
    $gold = New-Brush "#E99A28"
    $goldLight = New-Brush "#F6B83F"
    $orange = New-Brush "#F15A35"
    $orangeSlice = New-Brush "#FFB638"
    $orangeCore = New-Brush "#FFE18A"
    $red = New-Brush "#F0443D"
    $glass = New-Brush "#EAF1F4"
    $glassLine = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(235, 215, 227, 232), 4)
    $scaleLine = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(220, 201, 119, 26), 3)
    $darkLine = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 30, 109, 53), 3)
    $pick = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 168, 90, 36), 5)
    $shine = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(200, 255, 255, 255), 4)

    $graphics.FillPolygon($greenDark, [System.Drawing.Point[]]@(
        [System.Drawing.Point]::new(154,111), [System.Drawing.Point]::new(130,31),
        [System.Drawing.Point]::new(153,67), [System.Drawing.Point]::new(168,110)))
    $graphics.FillPolygon($green, [System.Drawing.Point[]]@(
        [System.Drawing.Point]::new(160,111), [System.Drawing.Point]::new(170,20),
        [System.Drawing.Point]::new(185,67), [System.Drawing.Point]::new(174,113)))
    $graphics.FillPolygon($greenLight, [System.Drawing.Point[]]@(
        [System.Drawing.Point]::new(164,113), [System.Drawing.Point]::new(218,42),
        [System.Drawing.Point]::new(200,80), [System.Drawing.Point]::new(180,118)))
    $graphics.FillPolygon($greenDeep, [System.Drawing.Point[]]@(
        [System.Drawing.Point]::new(151,112), [System.Drawing.Point]::new(91,54),
        [System.Drawing.Point]::new(121,72), [System.Drawing.Point]::new(157,119)))
    $graphics.FillPolygon($greenLime, [System.Drawing.Point[]]@(
        [System.Drawing.Point]::new(170,117), [System.Drawing.Point]::new(237,91),
        [System.Drawing.Point]::new(203,104), [System.Drawing.Point]::new(180,126)))

    $graphics.FillEllipse($gold, 91, 91, 140, 140)
    $graphics.DrawEllipse([System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255,198,107,22),5), 91, 91, 140, 140)
    $graphics.FillEllipse($goldLight, 112, 89, 98, 39)
    $graphics.DrawEllipse([System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255,217,134,28),3), 112, 89, 98, 39)
    foreach ($y in @(124,154,184,214)) {
        foreach ($x in @(119,149,179,209)) {
            if ($x -gt 96 -and $x -lt 227 -and $y -gt 98 -and $y -lt 225) {
                $graphics.DrawLines($scaleLine, [System.Drawing.Point[]]@(
                    [System.Drawing.Point]::new($x-10,$y),
                    [System.Drawing.Point]::new($x,$y+8),
                    [System.Drawing.Point]::new($x+10,$y)))
            }
        }
    }

    $glassPoints = [System.Drawing.Point[]]@(
        [System.Drawing.Point]::new(31,95), [System.Drawing.Point]::new(103,95),
        [System.Drawing.Point]::new(91,139), [System.Drawing.Point]::new(76,169),
        [System.Drawing.Point]::new(75,199), [System.Drawing.Point]::new(95,211),
        [System.Drawing.Point]::new(37,211), [System.Drawing.Point]::new(57,199),
        [System.Drawing.Point]::new(56,169), [System.Drawing.Point]::new(42,139))
    $graphics.FillPolygon($glass, $glassPoints)
    $graphics.DrawPolygon($glassLine, $glassPoints)
    $graphics.FillPolygon($orange, [System.Drawing.Point[]]@(
        [System.Drawing.Point]::new(36,102), [System.Drawing.Point]::new(98,102),
        [System.Drawing.Point]::new(87,137), [System.Drawing.Point]::new(75,162),
        [System.Drawing.Point]::new(59,137)))
    $graphics.DrawLine([System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255,255,202,112),5), 37,103,97,103)
    $graphics.DrawLine([System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255,215,230,236),8), 66,166,66,201)
    $graphics.FillEllipse($glass, 42,203,48,13)
    $graphics.DrawEllipse([System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255,183,201,210),3),42,203,48,13)
    $graphics.FillEllipse($orangeSlice,27,75,33,33)
    $graphics.DrawEllipse([System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255,217,122,23),3),27,75,33,33)
    $graphics.FillEllipse($orangeCore,33,81,21,21)
    $graphics.DrawLine([System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255,233,152,34),2),43,82,43,101)
    $graphics.DrawLine([System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255,233,152,34),2),34,91,53,91)
    $graphics.DrawLine($pick,49,76,60,45)
    $graphics.FillEllipse($red,50,31,19,19)
    $graphics.DrawEllipse([System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255,201,44,40),3),50,31,19,19)
    $graphics.FillEllipse([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255,255,156,133)),55,35,6,6)
    $graphics.DrawLine($shine,43,111,52,137)

    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $png = $stream.ToArray()
    $stream.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
    return ,$png
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) { $images.Add((New-IconPng $size)) }

$directory = [System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($OutputPath))
[System.IO.Directory]::CreateDirectory($directory) | Out-Null
$stream = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::Create)
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$sizes.Count)
    $offset = 6 + (16 * $sizes.Count)
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $size = $sizes[$i]
        $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$images[$i].Length)
        $writer.Write([UInt32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($image in $images) { $writer.Write($image) }
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}
Write-Host "Generated Fluent pineapple/cocktail icon: $OutputPath"
