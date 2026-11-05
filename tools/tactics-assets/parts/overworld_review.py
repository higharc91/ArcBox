"""Authored art-review decisions. Unknown families deliberately require a new review.

These are placement recommendations, not new canon or live prop placements.
Region lore, scenes and the painted region remain the sources of truth.
"""
POLICIES={}


def group(section,names,where,note):
    for name in names.split():
        key=(section,name)
        if key in POLICIES:raise ValueError(f'Duplicate art review: {key}')
        POLICIES[key]=(where,note)


group('props','tree tree_fruit bush bush_berry stump log tall_grass fern',
    'Emberwick fields and autumn woods',
    'Keep the clustered foliage and wood shading. Prefer autumn/red away from the warm fields; blossom is a seasonal library option, not Chapter I default.')
group('props','pine dead_tree', 'Scarp and burned bank',
    'Keep the open silhouettes. Mix sparse pines with dead trees; the burned bank should not look like a lush orchard.')
group('props','pine_snowy ice_spikes','Frostpeak',
    'Keep the cold highlights. Reserve snow and ice for the frozen pass, not Emberwick warm ground.')
group('props','rock_small rock_medium rock_large rock_mossy boulder crag rock_cluster',
    'Scarp, banks and regional geology',
    'Keep faceted top-left lighting. Moss belongs on wet banks; bare/basalt on burned ground, sandstone in Sunscar, snow in Frostpeak.')
group('props','reeds lily_pads swamp_tree will_o_wisp','Wet banks / Mirewood',
    'Reeds fit the ford margins. Cypress, lily pools and floating wisps belong chiefly to Mirewood; keep the ford crossing and shore readable.')
group('props','mushrooms mushroom_cluster crystals crystal_large', 'Forest / ember-adjacent caves',
    'Natural red/brown mushrooms fit woods. Glowing caps and crystals need a specific magical site; do not scatter them as ordinary village decorations.')
group('props','crate barrel sack cart chest chest_open','Store, granary, docks and earned loot',
    'Wood and metal match the town kit. Keep access lanes clear. Crates/sacks support the grain shortage; gold-filled chests are rewards, not street furniture.')
group('props','maud_ash_garden maud_stool_pots',"Old Maud's burned-bank home",
    'Owner-approved kitchen-herb bed in cold ash and knee-high stool/pots. Keep the cottage approach and reed track clear; no magical glow or occult trophies.')
group('props','fence fence_stone fence_picket fence_iron palisade','Fields, town boundary and defensible sites',
    'Match segments to their axis and material. Plain wood suits fields, stone suits Warden work, iron suits a formal courtyard, palisades suit a guarded camp.')
group('props','campfire torch lamp_post lantern','Occupied camp, street and shrine',
    'Keep local warm light. Smoke and flame must start at the actual emitter. Use sparse practical lights; blue ambient light is not ordinary ember-fire.')
group('props','banner signpost','Road junctions and faction-owned places',
    'Signs need a route or destination. Palette-only chevrons are generic heraldry, not canonical faction crests; avoid assigning a false faction identity.')
group('props','well','Emberwick square',
    'Refined masonry courses, rim and bucket. Anchor on the paving and leave a walkable approach; the water glint is reflected light, not a magical source.')
group('props','tent','Skarrow winter camp / traveller shelter',
    'Added tension folds, seam shadows, repairs and a hide palette. Use mended hide/canvas for the hungry clan, not a rich crimson military camp.')
group('props','haystack','Warm fields and granary edge',
    'Straw clusters read well. Use modest harvested piles; a landscape full of abundant grain would contradict the failing harvest.')
group('props','gravestone grave_cross tomb','Cemetery / Warden burial sites',
    'Keep weathered masonry. A generic cross is an optional old marker, not the Church of the Hearth emblem; do not dress the Skarrow camp as a graveyard.')
group('props','pillar pillar_broken ruin_wall ruin_arch rubble','Old Warden structures',
    'Keep the shared stone palette and chips. Use broken forms at actual ruins, and retain clear arch openings; intact village houses should not all be ruined.')
group('props','altar shrine','Church of the Hearth / specific ritual sites',
    'Refined altar cloth depth and stone joints. Warm votive light fits the Hearth; dark/blood palettes belong to an identified hostile ritual, not Maud by default.')
group('props','ladder','Reachable raised platform',
    'Rungs and rails read well. A visible ladder must have a reachable base and a supported climb interaction; a picture alone does not make the route traversable.')
group('props','cage barricade mantlet skull_pike ribcage','Specific siege, captivity or burial story',
    'Keep distinct silhouettes but reserve their placement. Hunger explains the Skarrow toll; skull pikes and cages must not imply unearned cruelty.')
group('props','cactus cactus_pear','Sunscar',
    'Keep green planes and pale spines. Reserve these for the dry uplands; they do not belong beside the steaming ford or the autumn town.')
group('props','training_dummy archery_target','Guard training yard',
    'Keep readable targets, stable bases and worn equipment. Place in a small guarded yard with approach space, not on public travel lanes.')
group('props','vent','Active volcanic ground / Ember Keep',
    'Keep the hot core and basalt rim. This is active heat; do not use it as a substitute for century-old cold ash.')

group('terrain','grass grass_lip','Warm fields / cooling autumn verge',
    'Reduced grass tuft density and isolated white tips. Preserve broad colour masses; autumn/dry palettes carry the cooling land while green/lush remain near warm fields.')
group('terrain','dirt path dirt_side','Fields and travelled roads',
    'Keep worn earth and pebbles. Fixed runtime edges so packed paths stand on earth rather than default cliff rock.')
group('terrain','flagstone cobble pavers ashlar_side brick_side','Town square / Warden masonry',
    'Keep readable stone courses. Warm paving now uses warm masonry sides; avoid mixing warm tops with unrelated cold foundations.')
group('terrain','rock cliff_side basalt basalt_side','Scarp / burned geology',
    'Keep rock planes and vertical structure. Basalt belongs to the burned geology; preserve visible height breaks and navigable passes.')
group('terrain','sand dunes sandstone sandstone_side','Sunscar / small river sediment deposits',
    'Keep layered strata and sand flow. Dunes and extensive sandstone are Sunscar; a small sandy bank is not a desert biome.')
group('terrain','snow snow_lip ice ice_side','Frostpeak',
    'Keep pale planes and blue fracture shadows. Fixed snowy edges and ice sides; reserve continuous snow cover for the cold region.')
group('terrain','planks plank_side','Docks, wooden walkways and roofs in the draft',
    'Fixed deck recognition and matching dark/oak sides. Structural walkways need thin deck edges, not a stone column; region building blocks remain placeholders.')
group('terrain','water','Ford and regional waterways',
    'Keep looped current and shallow/deep contrast. The ford must remain legible; steam is local to the warm river, not blanket fog over the whole map.')
group('terrain','mud bog forest_floor forest_needles moss_lip','Damp woods / Mirewood',
    'Keep roots and clustered litter. Fixed earth foundations; reserve duckweed and broad bog pools for Mirewood, not the Emberwick square.')
group('terrain','lava lava_side','Active infernal sites',
    'Keep animated channels and emissive light. This visibly active material is distinct from the old Ashen Ford burn scar.')
group('terrain','ash','Century-old burned bank',
    'Removed glowing coals and automatic firelight. The Fall was a century ago; warm soil does not make every ash tile a burning hazard.')
group('terrain','flowers_0 flowers_1 flowers_2 pebbles_0 pebbles_1 leaves_0 leaves_1','Sparse ground dressing',
    'Keep small clustered marks. Prefer fallen autumn leaves in Chapter I; leave quiet ground around characters and interactable locations.')
group('terrain','bones blood cracks puddle scorch','Localized history / recent action',
    'Keep these as local decals with a cause. Blood and fresh scorch belong to recent violence or spells; puddles fit the wet banks, not every paved tile.')

group('town','timber_frame whitewash fieldstone dressed_stone log_wall plank_shed', 'Village walls and region-appropriate structures',
    'Keep material volume, age and top-left lighting. Timber/plaster suits ordinary Emberwick homes; dressed stone identifies Warden masonry. Avoid uniform pristine facades.')
group('town','slate clay_tiles thatch shakes', 'Village roofs',
    'Keep courses, eaves and ridge direction. Worn thatch/shakes suit modest homes; slate and clay can distinguish civic or prosperous buildings.')
group('town','floorboards tavern_flags rug doormat','Interior and doorstep',
    'Keep scale and surface wear. Rugs stay inside/under cover; village roads should not use indoor floorboards by accident.')
group('town','door door_open window window_shuttered window_lit', 'House facades',
    'Keep hinges, sills and state variants. Doors need a clear approach; lit windows belong to occupied buildings and must agree with time of day.')
group('town','lancet lancet_lit rose_window rose_window_lit belfry hearth', 'Hearth chapel / Last Coal hearth',
    'Keep coloured glass, bell and fire detail. Warm inhabited light fits the lore; the art does not itself imply a memory-burning magical service.')
group('town','sign_bracket sign_mug sign_anvil sign_bag smoke','Inn, smithy and general store attachments',
    'Keep readable trade symbols. Attach signs at their bracket and chimney smoke at its base; avoid independent drifting signs or smoke from a cold roof.')
group('town','ivy crack wall_lantern arrow_slit notices', 'Purposeful facade detail',
    'Keep wall-local scale and anchoring. Ivy fits damp/older facades, arrow slits defensive masonry, notices the square; do not cover every wall with every decal.')
group('town','cottage house tavern smithy store stall windmill', 'Emberwick / river settlements',
    'Keep the detailed modular buildings. The Last Coal needs its mug sign; smithy its forge/anvil; the store its bag sign. Modest market stock fits the shortage.')
group('town','granary maud_hut','Ashen Ford lore places, owner-approved issue #81',
    'Locked grain barn with central hasp and modest stores; Maud retains her cottage footprint, attached herbs/charms and ordinary chimney soot. Shared town materials, top-left light and shadow projection. No ember-fire or new story disclosure.')
group('town','chapel','Emberwick Church of the Hearth',
    'Replaced the generic cross finial with a gilded flame/brazier shape. Keep stone buttresses and warm windows; the finial is metal, not a magical beacon.')
group('town','watchtower wall_u wall_v wall_tower gate_u gate_v', 'Town defences / old Warden sites',
    'Keep masonry and paired orientations. Gates need an aligned road and an actual traversable opening; decorative gate art alone does not alter collision.')
group('town','steps_u steps_v stairs_u stairs_v bridge_u bridge_v', 'Height transitions and crossing modules',
    'Keep both orientations and worn edges. Match rise/drop metadata to ground heights. These stone bridge modules are not Vorga\'s rope-and-timber bridge.')
group('town','notice_board_u notice_board_v clutter_crates clutter_barrels clutter_produce tavern_table', 'Square, dockside and inn',
    'Keep readable props at shared texel density. Anchor furniture on the ground; use produce sparingly, and keep public lanes free of clutter.')

group('ambient','halo light_pool','A real local emitter / explicit magic',
    'Keep soft additive falloff. Fire is warm; arcane/fel colours require the corresponding source. A halo is not a standalone floating prop.')
group('ambient','campfire_smoke chimney_smoke','Lit hearth, forge or camp; steam at the ford',
    'Keep looped puffs. Match grey/soot to combustion and steam to warm water; bottom anchors stay at the emitter while the plume drifts.')
group('ambient','river_steam','Warm water at the Ashen Ford',
    'Added low translucent curls with a stationary water anchor and limited opacity. Use sparse phase-offset patches; this is neither chimney smoke nor an emissive magical cloud.')
group('ambient','waterfall waterfall_lip waterfall_foam waterfall_spray shore_foam shore_foam_corner ripple splash', 'Actual water edges and height drops',
    'Keep matching foam/current variants. Waterfalls need a real drop, shore foam a real bank; steam spray belongs near the warm ford, not unrelated dry ground.')
group('ambient','lava_bubble swamp_bubbles','Active lava / Mirewood pools',
    'Keep loops local to their liquid. Do not scatter lava bubbles over cold ash or swamp bubbles on the paved square.')
group('ambient','chest_anim door lever spike_trap','Supported interactive objects',
    'Keep state sequences and stable anchors. Visual open/closed states must follow game state; a decorative chest, lever or trap does not create an interaction.')
group('ambient','portal rune_stone','Authored magical / Warden story site',
    'Keep clear activation states. Reserve these for named story purposes; casual town portals would undermine the grounded journey and visible cost of magic.')
group('ambient','fireflies leaf snowflake rain_splash motes embers','Sparse local particles',
    'Keep small silhouettes and loop timing. Prefer autumn leaves/ordinary motes here; snow belongs to cold weather, fire embers to a real nearby flame.')
group('ambient','bird flock butterfly fish','Regional wildlife',
    'Keep readable motion at low density. Crows/swallow/silver fish fit the ford; koi and blossom palettes are library alternatives, not default local fauna.')
group('ambient','rain snow fog light_shafts','Regional weather and atmosphere',
    'Keep the map and party readable underneath. Use thin local mist, autumn light and occasional rain at the ford; snow is Frostpeak, miasma Mirewood, falling ash Ember Keep.')

group('worldmap','town village castle dungeon ruins tower camp port shrine lair forest mountain swamp oasis volcano glacier party quest',
    'Generic parchment map vocabulary',
    'Keep ink-and-wash style distinct from the pixel world. Generic symbols are fallback categories; use the literal place override for bridges, Maud\'s hut, the Heartpool and flame hall.')
group('worldmap','stone_bridge rope_bridge hut inn crater heartpool flame_hall ice_cave','Named lore landmarks',
    'Added literal location art: Warden stone bridge, Vorga timber/rope bridge, Maud\'s charm-hung hut, Last Coal inn, black-glass crater, dark heartpool, ruined flame hall and ice cave entrance. Inherit lore visibility gates.')
group('worldmap','parchment sample_map','Illustration library / style specimen',
    'Paper fibres and muted wash fit. The sample map is a generic style demonstration, not canonical Vale geography or the playable Ashen Ford region.')


def assessment(section,name,variant='',replaces=None):
    if section=='ambient' and replaces:
        return assessment('props',replaces,variant)
    where,note=POLICIES[(section,name)]
    # Palette choices have specific consequences even when they share geometry.
    if variant in ('blossom','snow','snowy','frost','glacier'):
        note+=' Seasonal constraint: do not use this palette as ordinary autumn Emberwick dressing.'
    return where,note
