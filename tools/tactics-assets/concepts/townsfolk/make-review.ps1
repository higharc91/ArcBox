param(
    [string]$Concept = (Join-Path $PSScriptRoot 'bree-concept.png'),
    [string]$Preset = 'innkeeper',
    [string]$Name = 'Bree Tallow',
    [string]$ReviewStem = 'bree',
    [string]$DesignStatus = 'approved'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$manifest = Get-Content (Join-Path $repoRoot 'content/tactics/base/tactical/manifest.json') -Raw | ConvertFrom-Json
$legacy = Get-Content (Join-Path $repoRoot 'content/tactics/base/characters/manifest.json') -Raw | ConvertFrom-Json
$heroAtlas = [Drawing.Bitmap]::new((Join-Path $repoRoot 'content/tactics/base/tactical/human_fighter.png'))
$creatures = Get-Content (Join-Path $repoRoot 'content/tactics/base/creatures/manifest.json') -Raw | ConvertFrom-Json
$creature = @($creatures.sprites.PSObject.Properties | Where-Object { $_.Value.keys -contains "characters.$Preset" } | Select-Object -Last 1)
$npcFrame = $legacy.frame
$npcPixelScale = 1.0
$npcPath = "content/tactics/base/characters/presets/$Preset.png"
if ($creature.Count -gt 0) {
    $npcFrame = $creature[0].Value.frame
    $npcPixelScale = $creature[0].Value.pixelScale
    $npcPath = 'content/tactics/base/creatures/' + $creature[0].Value.file
} elseif ($creatures.legacyOverrides.PSObject.Properties["characters.$Preset"]) {
    $npcPixelScale = $creatures.legacyOverrides.PSObject.Properties["characters.$Preset"].Value.pixelScale
}
$npcAtlas = [Drawing.Bitmap]::new((Join-Path $repoRoot $npcPath))
$hero = $heroAtlas.Clone([Drawing.Rectangle]::new(0,0,$manifest.frame.width,$manifest.frame.height), [Drawing.Imaging.PixelFormat]::Format32bppArgb)
$npc = $npcAtlas.Clone([Drawing.Rectangle]::new(0,0,$npcFrame.width,$npcFrame.height), [Drawing.Imaging.PixelFormat]::Format32bppArgb)
$card = [Drawing.Bitmap]::new(1200,780)
$g = [Drawing.Graphics]::FromImage($card)
$g.Clear([Drawing.Color]::FromArgb(35,32,40))
$g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::Half
$font = [Drawing.Font]::new('Segoe UI',20)
$small = [Drawing.Font]::new('Segoe UI',15)
$g.DrawString('Current production sprites at the same world scale', $font, [Drawing.Brushes]::White, 36,30)
$g.DrawString("Current $Preset", $small, [Drawing.Brushes]::White, 180,130)
$g.DrawString('Shipped human fighter', $small, [Drawing.Brushes]::White, 730,130)
$g.DrawString('6x enlargement; source pixel scales preserved; shared foot baseline', $small, [Drawing.Brushes]::LightGray, 36,710)
$base = 620
$npcScale = 6 * $npcPixelScale
$heroScale = 6 * $manifest.pixelScale
$g.DrawLine([Drawing.Pens]::DimGray,80,$base,1120,$base)
$g.DrawImage($npc, [Drawing.Rectangle]::new(350-$npcFrame.anchor[0]*$npcScale,$base-$npcFrame.anchor[1]*$npcScale,$npc.Width*$npcScale,$npc.Height*$npcScale))
$g.DrawImage($hero, [Drawing.Rectangle]::new(850-$manifest.frame.anchor[0]*$heroScale,$base-$manifest.frame.anchor[1]*$heroScale,$hero.Width*$heroScale,$hero.Height*$heroScale))
$scaleName = if ($ReviewStem -eq 'bree') { 'current-scale.png' } else { "$ReviewStem-scale.png" }
$card.Save((Join-Path $PSScriptRoot $scaleName), [Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $card.Dispose(); $npc.Dispose(); $hero.Dispose(); $npcAtlas.Dispose(); $heroAtlas.Dispose()
if (Test-Path $Concept) {
    $conceptImage = [Drawing.Bitmap]::new($Concept)
    $scaleImage = [Drawing.Bitmap]::new((Join-Path $PSScriptRoot $scaleName))
    $sheet = [Drawing.Bitmap]::new(2840,1240)
    $g = [Drawing.Graphics]::FromImage($sheet)
    $g.Clear([Drawing.Color]::FromArgb(35,32,40))
    $g.DrawString("$Name - Scenario concept - design $DesignStatus", $font,[Drawing.Brushes]::White,36,20)
    $ratio = [Math]::Min(1520.0/$conceptImage.Width,1020.0/$conceptImage.Height)
    $g.DrawImage($conceptImage,[Drawing.Rectangle]::new(40,100,[int]($conceptImage.Width*$ratio),[int]($conceptImage.Height*$ratio)))
    $g.DrawImageUnscaled($scaleImage,1600,200)
    $g.DrawString('Concept painting is a design proposal. PixelLab sprites and their scale require a later review.', $small,[Drawing.Brushes]::LightGray,36,1160)
    $sheet.Save((Join-Path $PSScriptRoot "$ReviewStem-review.jpg"),[Drawing.Imaging.ImageFormat]::Jpeg)
    $g.Dispose(); $sheet.Dispose(); $scaleImage.Dispose(); $conceptImage.Dispose()
}
$font.Dispose(); $small.Dispose()
