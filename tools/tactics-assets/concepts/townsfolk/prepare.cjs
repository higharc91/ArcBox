// Issue #80: derive the concept queue from people actually placed in Ashen Ford.
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../../../..');
const read = p => JSON.parse(fs.readFileSync(path.join(root, p), 'utf8'));
const pack = read('tools/tactics-assets/concepts/scenario-briefs.json');
const world = read('content/tactics/base/rules/world/ashen_ford.json');
const people = world.people;
const named = [...new Set(people.map(p => p.character).filter(Boolean))];
const roles = {
  barmaid: 'An adult member of Bree’s inn staff, practical rolled sleeves, a clean but repeatedly mended apron, towel at the belt and sturdy shoes; capable and busy, without a sexualized tavern costume.',
  child: 'An ordinary Emberwick child, short child proportions, patched wool tunic, practical trousers and soft boots; curious and wary. Distinct from named Pip, without weapons or adult anatomy.',
  farmer: 'An adult working the warm fields as winter approaches, substantial patched wool, practical trousers, weathered boots, a work belt and a modest cap. Worried, experienced and plain-spoken.',
  farmhand: 'A young adult agricultural worker, rolled sleeves over substantial work clothes, simple kerchief and scuffed boots; tired and underpaid, carrying no giant tool that could obscure neighbouring sprites.',
  fisher: 'An adult river worker beside the steaming ford, mended short waterproof coat, rolled trousers, river boots, simple rope belt and small tackle pouch. Practical, superstitious and worn by the weather.',
  guard: 'An ordinary member of Dagna’s town guard, modest worn mail beneath a practical dull-red wool tabard, simple open-faced helmet, leather belt and boots. Loyal but nervous; consistent uniform across variants, without a royal crown or ornate plate.',
  merchant: 'An adult market stallholder, modest layered wool and linen, practical apron or short overcoat, a small coin pouch and sturdy boots. Stock is scarce; no luxurious aristocratic finery or overflowing wealth.'
};
const namedBriefs = named.map(id => {
  const brief = pack.concepts.find(c => c.id === `character_${id}`);
  if (!brief) throw Error(`Missing existing brief: ${id}`);
  return { ...brief, placed_count: people.filter(p => p.character === id).length, approval: 'pending' };
});
const presets = [...new Set(people.filter(p => !p.character && !p.preset.startsWith('goblin_')).map(p => p.preset))].sort();
const townsfolk = presets.map(preset => {
  if (!roles[preset]) throw Error(`A new preset needs an authored brief: ${preset}`);
  return {
    id: `townsfolk_${preset}`, preset_reference: preset,
    placed_count: people.filter(p => !p.character && p.preset === preset).length,
    design_proposal: roles[preset], approval: 'pending',
    variants: [
      { id: 'a', proposal: 'Warm brown skin, dark tightly curled hair, muted ochre and indigo clothing accents.' },
      { id: 'b', proposal: 'Light freckled skin, auburn hair, muted moss and russet clothing accents.' }
    ],
    prompt: `${pack.shared_style} Design two distinct ${preset} townsfolk for Emberwick at the Ashen Ford. ${roles[preset]} The people live in a poor autumn market town whose warm ground is cooling. Variant A has warm brown skin and dark tightly curled hair; variant B has light freckled skin and auburn hair. Distinguish their clothes with muted ochre/indigo versus moss/russet accents while preserving the occupational silhouette and a coherent town palette. For guards keep the same dull-red uniform in both variants. Show each variant in separated matching front, three-quarter and back full-body views, on the same ground line and scale, plus one head study each. Preserve consistent anatomy, clothing construction, handedness and rear garment details. Clear silhouettes for later sprites of about 36 world pixels adult height; children remain visibly shorter. Full feet and head inside every view, generous margins, neutral muted background, no labels or text. These appearances are proposals, not additions to character canon.`
  };
});
const queue = { issue: 80, based_on: '2e64b9df', stage: 'concepts', anchor: 'character_bree_tallow', named: namedBriefs, townsfolk, excluded_goblin_presets: [...new Set(people.filter(p=>!p.character && p.preset.startsWith('goblin_')).map(p=>p.preset))] };
fs.writeFileSync(path.join(__dirname, 'briefs.json'), JSON.stringify(queue, null, 2) + '\n');
const bree = namedBriefs.find(b => b.id === queue.anchor);
const request = {
  provider: 'Scenario', model_id: 'model_google-gemini-nano-banana-2-1',
  team_id: 'team_5nToMAwZ2bBo1gqSuy5933dS', project_id: 'proj_DXXKthLZvfZMp4svC2fRJksp',
  seed: null, seed_note: 'The inspected model schema exposes no seed parameter.', approval: 'pending',
  source_brief: 'scenario-briefs.json:character_bree_tallow',
  parameters: { prompt: bree.prompt + ' Bree is a mature adult woman, warm and capable, with substantial practical work clothes. Plain muted parchment background. Keep the notebook closed and without readable text. This is the series anchor; subsequent sheets will reuse its rendering style. Use restrained large colour planes that remain readable in a detailed 36-pixel-tall world sprite.', aspectRatio: '3:2', resolution: '2K', thinkingLevel: 'HIGH', numOutputs: 1, useGoogleSearch: false }
};
fs.writeFileSync(path.join(__dirname, 'bree-request.json'), JSON.stringify(request, null, 2) + '\n');
console.log(`${namedBriefs.length} named NPCs, ${townsfolk.length} generic presets, ${townsfolk.length * 2} generic variants.`);
