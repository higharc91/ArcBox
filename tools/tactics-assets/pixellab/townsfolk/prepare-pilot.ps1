$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$conceptRoot = Join-Path $repo 'tools/tactics-assets/concepts/townsfolk'
$approvals = Get-Content (Join-Path $conceptRoot 'approvals.json') -Raw | ConvertFrom-Json
if ($approvals.character_bree_tallow.status -ne 'approved') { throw 'Bree concept approval is required.' }
$manifestPath = Join-Path $repo 'content/tactics/base/tactical/manifest.json'
$atlasPath = Join-Path $repo 'content/tactics/base/tactical/human_fighter.png'
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$atlas = [Drawing.Bitmap]::new($atlasPath)
try {
    $frame = $atlas.Clone([Drawing.Rectangle]::new(0,0,$manifest.frame.width,$manifest.frame.height), [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try { $frame.Save((Join-Path $PSScriptRoot 'current-hero-idle.png'), [Drawing.Imaging.ImageFormat]::Png) }
    finally { $frame.Dispose() }
} finally { $atlas.Dispose() }
$metadata = [ordered]@{
    status = 'prepared; generation not submitted'
    character = 'character_bree_tallow'
    approvedConceptAsset = $approvals.character_bree_tallow.asset_id
    conceptSha256 = (Get-FileHash (Join-Path $conceptRoot 'bree-concept.png') -Algorithm SHA256).Hash.ToLowerInvariant()
    heroAtlas = 'content/tactics/base/tactical/human_fighter.png'
    heroAtlasSha256 = (Get-FileHash $atlasPath -Algorithm SHA256).Hash.ToLowerInvariant()
    heroFrame = $manifest.frame
    heroPixelScale = $manifest.pixelScale
    heroHeadY = $manifest.heads.human_fighter
    heroHeadToFootWorldPixels = ($manifest.frame.anchor[1] - $manifest.heads.human_fighter) * $manifest.pixelScale
    pilotScope = 'One static facing, then one short idle or walk only after static inspection. Live schema and price required before submission.'
    generationCost = $null
    integrationApproved = $false
}
$existingPath = Join-Path $PSScriptRoot 'pilot-inputs.json'
if (Test-Path -LiteralPath $existingPath) {
    $existing = Get-Content $existingPath -Raw | ConvertFrom-Json
    if ($existing.staticJobId -and $existing.heroAtlasSha256 -ne $metadata.heroAtlasSha256) { throw 'Hero atlas changed since generation; review pilot provenance before rebuilding.' }
    foreach ($key in @('status','generationCost','staticJobId','idleJobId','previousIdleJobId','selectedCandidate','directionalCharacterId','directionalCoverage','tvReview','pilotScope')) {
        if ($existing.PSObject.Properties[$key]) { $metadata[$key] = $existing.$key }
    }
}
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'pilot-inputs.json'), (($metadata | ConvertTo-Json -Depth 8).Replace("`r`n","`n") + "`n"), [Text.UTF8Encoding]::new($false))
