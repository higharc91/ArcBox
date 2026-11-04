param([string]$Concept = (Join-Path $PSScriptRoot 'bree-concept.png'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$manifest = Get-Content (Join-Path $repoRoot 'content/tactics/base/tactical/manifest.json') -Raw | ConvertFrom-Json
$legacy = Get-Content (Join-Path $repoRoot 'content/tactics/base/characters/manifest.json') -Raw | ConvertFrom-Json
$heroAtlas = [Drawing.Bitmap]::new((Join-Path $repoRoot 'content/tactics/base/tactical/human_fighter.png'))
$npcAtlas = [Drawing.Bitmap]::new((Join-Path $repoRoot 'content/tactics/base/characters/presets/innkeeper.png'))
$hero = $heroAtlas.Clone([Drawing.Rectangle]::new(0,0,$manifest.frame.width,$manifest.frame.height), [Drawing.Imaging.PixelFormat]::Format32bppArgb)
$npc = $npcAtlas.Clone([Drawing.Rectangle]::new(0,0,$legacy.frame.width,$legacy.frame.height), [Drawing.Imaging.PixelFormat]::Format32bppArgb)
$card = [Drawing.Bitmap]::new(1200,780)
$g = [Drawing.Graphics]::FromImage($card)
$g.Clear([Drawing.Color]::FromArgb(35,32,40))
$g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::Half
$font = [Drawing.Font]::new('Segoe UI',20)
$small = [Drawing.Font]::new('Segoe UI',15)
$g.DrawString('Current production sprites at the same world scale', $font, [Drawing.Brushes]::White, 36,30)
$g.DrawString('Bree / legacy innkeeper', $small, [Drawing.Brushes]::White, 180,130)
$g.DrawString('Shipped human fighter', $small, [Drawing.Brushes]::White, 730,130)
$g.DrawString('6x enlargement; source pixel scales preserved; shared foot baseline', $small, [Drawing.Brushes]::LightGray, 36,710)
$base = 620
$npcScale = 6
$heroScale = 6 * $manifest.pixelScale
$g.DrawLine([Drawing.Pens]::DimGray,80,$base,1120,$base)
$g.DrawImage($npc, [Drawing.Rectangle]::new(350-$legacy.frame.anchor[0]*$npcScale,$base-$legacy.frame.anchor[1]*$npcScale,$npc.Width*$npcScale,$npc.Height*$npcScale))
$g.DrawImage($hero, [Drawing.Rectangle]::new(850-$manifest.frame.anchor[0]*$heroScale,$base-$manifest.frame.anchor[1]*$heroScale,$hero.Width*$heroScale,$hero.Height*$heroScale))
$card.Save((Join-Path $PSScriptRoot 'current-scale.png'), [Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $card.Dispose(); $npc.Dispose(); $hero.Dispose(); $npcAtlas.Dispose(); $heroAtlas.Dispose()
if (Test-Path $Concept) {
    $conceptImage = [Drawing.Bitmap]::new($Concept)
    $scaleImage = [Drawing.Bitmap]::new((Join-Path $PSScriptRoot 'current-scale.png'))
    $sheet = [Drawing.Bitmap]::new(2840,1240)
    $g = [Drawing.Graphics]::FromImage($sheet)
    $g.Clear([Drawing.Color]::FromArgb(35,32,40))
    $g.DrawString('Bree Tallow - proposed Scenario concept anchor - approval pending', $font,[Drawing.Brushes]::White,36,20)
    $ratio = [Math]::Min(1520.0/$conceptImage.Width,1020.0/$conceptImage.Height)
    $g.DrawImage($conceptImage,[Drawing.Rectangle]::new(40,100,[int]($conceptImage.Width*$ratio),[int]($conceptImage.Height*$ratio)))
    $g.DrawImageUnscaled($scaleImage,1600,200)
    $g.DrawString('Concept painting is a design proposal. PixelLab sprites and their scale require a later review.', $small,[Drawing.Brushes]::LightGray,36,1160)
    $sheet.Save((Join-Path $PSScriptRoot 'bree-review.jpg'),[Drawing.Imaging.ImageFormat]::Jpeg)
    $g.Dispose(); $sheet.Dispose(); $scaleImage.Dispose(); $conceptImage.Dispose()
}
$font.Dispose(); $small.Dispose()
