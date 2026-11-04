$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$queue = Get-Content (Join-Path $PSScriptRoot 'briefs.json') -Raw | ConvertFrom-Json
$approvals = Get-Content (Join-Path $PSScriptRoot 'approvals.json') -Raw | ConvertFrom-Json
$groups = @(@{Name='named'; Items=$queue.named}, @{Name='townsfolk'; Items=$queue.townsfolk})
$index = @('# Ashen Ford concept review', '', 'Concept designs require owner approval before PixelLab work. The comparison cards show current production people beside the shipped hero at a shared world scale.', '')
foreach ($group in $groups) {
    $cards = @()
    foreach ($item in $group.Items) {
        $id = $item.id
        $concept = if ($id -eq 'character_bree_tallow') { Join-Path $PSScriptRoot 'bree-concept.png' } else { Join-Path $PSScriptRoot "generated/$id.png" }
        if (!(Test-Path $concept)) { continue }
        $name = if ($item.title) { $item.title } else { $item.preset_reference + ' - variants A and B' }
        $state = if ($approvals.PSObject.Properties[$id]) { $approvals.PSObject.Properties[$id].Value.status } else { 'pending' }
        & (Join-Path $PSScriptRoot 'make-review.ps1') -Concept $concept -Preset $item.preset_reference -Name $name -ReviewStem $id -DesignStatus $state
        $cards += "$id-review.jpg"
        $relativeConcept = if ($id -eq 'character_bree_tallow') { 'bree-concept.png' } else { "generated/$id.png" }
        $index += "- **$name** ($state): [comparison]($id-review.jpg), [full concept]($relativeConcept)."
    }
    for ($offset=0; $offset -lt $cards.Count; $offset+=6) {
        $page = [int]($offset/6)+1
        $count = [Math]::Min(6,$cards.Count-$offset)
        $rows = [int][Math]::Ceiling($count/2.0)
        $sheet = [Drawing.Bitmap]::new(2840,100+$rows*620)
        $g = [Drawing.Graphics]::FromImage($sheet)
        $g.Clear([Drawing.Color]::FromArgb(35,32,40))
        $font = [Drawing.Font]::new('Segoe UI',30)
        $g.DrawString("Wardenflame - $($group.Name) concept review - page $page",$font,[Drawing.Brushes]::White,32,24)
        for ($j=0; $j -lt $count; $j++) {
            $image = [Drawing.Bitmap]::new((Join-Path $PSScriptRoot $cards[$offset+$j]))
            $g.DrawImage($image,[Drawing.Rectangle]::new(($j%2)*1420,100+[int][Math]::Floor($j/2.0)*620,1420,620))
            $image.Dispose()
        }
        $filename = "$($group.Name)-contact-$page.jpg"
        $sheet.Save((Join-Path $PSScriptRoot $filename),[Drawing.Imaging.ImageFormat]::Jpeg)
        $font.Dispose(); $g.Dispose(); $sheet.Dispose()
        $index += @('', "![$($group.Name) concepts, page $page]($filename)", '')
    }
}
if (Test-Path (Join-Path $PSScriptRoot 'supplements/child-b.png')) {
    & (Join-Path $PSScriptRoot 'make-review.ps1') -Concept (Join-Path $PSScriptRoot 'supplements/child-b.png') -Preset child -Name 'Child variant B - missing body views completed' -ReviewStem child-b-supplement -DesignStatus $approvals.townsfolk_child_b_body.status
    $index += @('', '## Child B body-view supplement', '', 'The original paired child sheet supplied only the face of variant B. This study completes the body views of that same character, using the original image as its reference. Its design is approved; pixel art still requires owner review.', '', '![Child B supplement](child-b-supplement-review.jpg)')
}
[System.IO.File]::WriteAllText((Join-Path $PSScriptRoot 'review-index.md'), ([string]::Join([char]10,$index) + [char]10), [System.Text.UTF8Encoding]::new($false))
