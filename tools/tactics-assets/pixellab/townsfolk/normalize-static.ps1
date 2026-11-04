$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=$PSScriptRoot
$inputs=Get-Content "$root/pilot-inputs.json" -Raw | ConvertFrom-Json
$stats=Get-Content "$root/static-measurements.json" -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Path "$root/normalized" -Force | Out-Null
$records=@()
foreach($i in 1..4){
 $m=$stats[$i]; $bounds=$m.bounds
 $source=[Drawing.Bitmap]::new("$root/static/candidate-$i.png")
 $dest=[Drawing.Bitmap]::new([int]$inputs.heroFrame.width,[int]$inputs.heroFrame.height)
 $g=[Drawing.Graphics]::FromImage($dest)
 try {
  $g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
  $g.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::Half
  $h=$inputs.heroFrame.anchor[1]-$inputs.heroHeadY
  $w=[int][Math]::Round(($bounds[2]-$bounds[0]+1)*$h/$m.visibleHeight)
  $x=$inputs.heroFrame.anchor[0]-[int][Math]::Floor($w/2)
  $y=$inputs.heroFrame.anchor[1]-$h+1
  $g.DrawImage($source,[Drawing.Rectangle]::new($x,$y,$w,$h),[Drawing.Rectangle]::new($bounds[0],$bounds[1],$bounds[2]-$bounds[0]+1,$m.visibleHeight),[Drawing.GraphicsUnit]::Pixel)
  $dest.Save("$root/normalized/candidate-$i.png",[Drawing.Imaging.ImageFormat]::Png)
  $records+=@{candidate=$i;sourceBounds=$bounds;destination=@($x,$y,$w,$h);method='nearest-neighbour; fixed transform before animation';worldHeight=$h*$inputs.heroPixelScale}
 }finally{$g.Dispose();$dest.Dispose();$source.Dispose()}
}
[IO.File]::WriteAllText("$root/normalization.json", (($records|ConvertTo-Json -Depth 8).Replace("`r`n","`n")+"`n"),[Text.UTF8Encoding]::new($false))
