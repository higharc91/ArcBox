param([switch]$Normalized)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$inputs = Get-Content (Join-Path $PSScriptRoot 'pilot-inputs.json') -Raw | ConvertFrom-Json
$sources = @(@{Name='Shipped human fighter'; File='current-hero-idle.png'})
$folder = if ($Normalized) { 'normalized' } else { 'static' }
1..4 | ForEach-Object { $sources += @{Name="Bree candidate $_"; File="$folder/candidate-$_.png"} }
$card = [Drawing.Bitmap]::new(1500,600)
$g = [Drawing.Graphics]::FromImage($card)
$font = [Drawing.Font]::new('Segoe UI',13)
$small = [Drawing.Font]::new('Segoe UI',11)
$measurements = @()
try {
    $g.Clear([Drawing.Color]::FromArgb(35,32,40))
    $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::Half
    $g.DrawString('Bree static pilot - pixel art awaiting owner review', $font, [Drawing.Brushes]::White, 30,20)
    $caption = if ($Normalized) { 'Sources normalized once to 36 world pixels; shared foot anchor; 6x world enlargement' } else { 'Raw outputs; shared production foot anchor; 6x world enlargement' }
    $g.DrawString($caption, $small, [Drawing.Brushes]::LightGray, 30,52)
    $scale = 6 * $inputs.heroPixelScale
    $baseline = 460
    $g.DrawLine([Drawing.Pens]::DimGray,20,$baseline,1480,$baseline)
    for ($i=0; $i -lt $sources.Count; $i++) {
        $source = $sources[$i]
        $image = [Drawing.Bitmap]::new((Join-Path $PSScriptRoot $source.File))
        try {
            if ($image.Width -ne $inputs.heroFrame.width -or $image.Height -ne $inputs.heroFrame.height) { throw "Unexpected canvas: $($source.File)" }
            $minX=$image.Width; $minY=$image.Height; $maxX=-1; $maxY=-1; $colours=[Collections.Generic.HashSet[int]]::new()
            for ($y=0; $y -lt $image.Height; $y++) { for ($x=0; $x -lt $image.Width; $x++) {
                $pixel=$image.GetPixel($x,$y)
                if ($pixel.A -gt 0) { $minX=[Math]::Min($minX,$x); $minY=[Math]::Min($minY,$y); $maxX=[Math]::Max($maxX,$x); $maxY=[Math]::Max($maxY,$y); [void]$colours.Add($pixel.ToArgb()) }
            } }
            if ($maxX -lt 0) { throw "Empty image: $($source.File)" }
            $center=150+300*$i
            $g.DrawImage($image,[Drawing.Rectangle]::new($center-$inputs.heroFrame.anchor[0]*$scale,$baseline-$inputs.heroFrame.anchor[1]*$scale,$image.Width*$scale,$image.Height*$scale))
            $g.DrawString($source.Name,$font,[Drawing.Brushes]::White,24+300*$i,495)
            $height=$maxY-$minY+1
            $g.DrawString("Visible height: $height raw / $($height*$inputs.heroPixelScale) world",$small,[Drawing.Brushes]::LightGray,24+300*$i,525)
            $measurements += [ordered]@{file=$source.File; bounds=@($minX,$minY,$maxX,$maxY); visibleHeight=$height; visibleWorldHeight=$height*$inputs.heroPixelScale; colours=$colours.Count; sha256=(Get-FileHash (Join-Path $PSScriptRoot $source.File) -Algorithm SHA256).Hash.ToLowerInvariant()}
        } finally { $image.Dispose() }
    }
    $reviewFile = if ($Normalized) { 'bree-normalized-review.png' } else { 'bree-static-review.png' }
    $measureFile = if ($Normalized) { 'normalized-measurements.json' } else { 'static-measurements.json' }
    $card.Save((Join-Path $PSScriptRoot $reviewFile),[Drawing.Imaging.ImageFormat]::Png)
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot $measureFile), (($measurements | ConvertTo-Json -Depth 8).Replace("`r`n","`n") + "`n"), [Text.UTF8Encoding]::new($false))
} finally { $g.Dispose(); $card.Dispose(); $font.Dispose(); $small.Dispose() }
