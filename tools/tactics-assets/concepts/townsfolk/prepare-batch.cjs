const fs = require('node:fs');
const path = require('node:path');
const read = name => JSON.parse(fs.readFileSync(path.join(__dirname, name), 'utf8'));
const queue = read('briefs.json');
const approvals = read('approvals.json');
const approval = approvals[queue.anchor];
if (approval?.status !== 'approved') throw Error('The series anchor needs owner approval.');
const anchorRequest = read('bree-request.json');
const candidates = [...queue.named.filter(b => b.id !== queue.anchor), ...queue.townsfolk];
const requests = candidates.map(brief => ({
  id: brief.id,
  preset_reference: brief.preset_reference,
  provider: 'Scenario',
  model_id: anchorRequest.model_id,
  team_id: anchorRequest.team_id,
  project_id: anchorRequest.project_id,
  seed: null,
  seed_note: anchorRequest.seed_note,
  design_approval: 'pending',
  parameters: {
    ...anchorRequest.parameters,
    prompt: brief.prompt + ' Use the supplied approved Bree concept only as the rendering-style reference: match its restrained painted material planes, top-left lighting, clear silhouettes and muted parchment background. Preserve this brief\'s own face, age, gender, species, proportions, outfit, equipment and identity; do not copy Bree\'s face, apron, notebook or hair. If a child or goblin is specified, keep its distinct scale and anatomy. All full-body views share one ground baseline and consistent size.',
    referenceImages: [approval.asset_id]
  }
}));
if (requests.length !== new Set(requests.map(r => r.id)).size) throw Error('Duplicate concept request.');
fs.writeFileSync(path.join(__dirname, 'batch-requests.json'), JSON.stringify({
  issue: 80, anchor: queue.anchor, anchor_asset_id: approval.asset_id,
  spend_approval: approvals.concept_batch_2026_11_03?.status ?? 'pending', named_sheets: queue.named.length - 1,
  paired_townsfolk_sheets: queue.townsfolk.length, requests
}, null, 2) + '\n');
console.log(`${requests.length} requests prepared; generation spend ${approvals.concept_batch_2026_11_03?.status ?? 'pending'}.`);
