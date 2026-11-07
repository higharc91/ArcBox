"""Authored art-review decisions. Unknown families deliberately require a new review.

These are placement recommendations, not new canon or live prop placements.
Region lore, scenes and the painted region remain the sources of truth.
"""
POLICIES={}
NONVISUAL_PROPS=frozenset({'warden_gate_collision'})


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
group('props','candle_stubs','Occupied kobold passages in Cinder Hollow',
    'Existing approved cave dressing: small practical wax candles with local warm light. Preserve their floor anchors and keep traversal and encounter staging clear; these are not ember-fire.')
group('props','animal_bones dried_blood','Ashmaw nest and cave feeding sites',
    'Existing approved signs of predation. Keep bones and old blood localized to the cave and nest, grounded at their native scale; they do not belong at Maud\'s cared-for cottage.')
group('props','fishing_skiff','Ashen Ford riverbank and working docks',
    'A small practical fishing boat, grounded or moored at water. Keep boarding and pedestrian routes clear; its artwork does not create a traversable crossing.')
group('props','bridge_span_damaged bridge_span_stone bridge_span_timber','Authored ford crossings',
    'Keep spans aligned to the river and their road. Damaged timber and old stone must match the actual stamp and bridge state; a decorative sprite alone is not walkable ground.')
group('props','fence_join','Ashen Ford fields and livestock pens',
    'Corner/tee pieces complete the matching fence segments. Preserve the correct facing and gate gaps; do not place joins as freestanding posts on roads.')
group('props','crop_wheat_stubble','Cooling Southfield farmland',
    'Sparse harvested stubble supports the grain shortage. Keep the field subdued rather than suggesting an abundant unharvested crop.')
group('props','lantern_iron','Occupied town, docks and rural doorsteps',
    'Practical iron lantern with local warm light. Keep it attached to its support and avoid treating it as unexplained magical illumination.')
group('props','farm_goat farm_goat_anim farm_sheep farm_sheep_anim farm_dog farm_dog_anim pack_mule pack_mule_anim',
    'Ashen Ford farms, pens and working streets',
    'Existing approved animal art and directional animations. Keep species scale and harness details consistent, ground feet on their anchors, and leave entrances clear. These are local animals, not hostile mobs.')
group('props','field_crow field_crow_anim red_fox red_fox_anim river_duck river_duck_anim','Fields, woodland edge and river',
    'Existing approved wildlife art and animation. Crows remain small, foxes belong at the woodland/field edge, ducks by water. Preserve their established world scale and motion anchors.')
group('props','wild_crow wild_crow_anim','Retained pre-overhaul wildlife source',
    'Unreferenced legacy crow sheets, retained unchanged for provenance. Do not place these alongside the current approved field_crow sprites; they use different scale and animation metadata.')
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

# Explicit reviewed families from the approved #82 installation; unknown names still fail.
group('terrain',
    'warden_deck',
    'Approved Warden bridge at the ford',
    'Owner-approved timber deck over the supported arches. Preserve its walkable surface, fuel classification and tile-owned structure; the broken northern bridge remains impassable.')

group('terrain',
    'warden_ramp road_ramp',
    'Approved bridge approaches and authored road elevation changes',
    'Supported sloped masonry joins road and deck heights. Keep shared corner heights, character surface sampling and clear travel lanes aligned with the saved geometry.')

group('terrain',
    'warden_edge',
    'Approved Warden bridge parapets',
    'Stone edge surfaces retain blocking parapets beside the cart lane. These tiles belong to the authored structure, not an extra river crossing.')

group('props',
    'warden_fortified_10_10 warden_fortified_10_6 warden_fortified_10_7 warden_fortified_10_8 '
    'warden_fortified_10_9 warden_fortified_11_6 warden_fortified_11_7 warden_fortified_2_10 '
    'warden_fortified_2_9 warden_fortified_3_10 warden_fortified_3_11 warden_fortified_3_9 '
    'warden_fortified_4_10 warden_fortified_4_11 warden_fortified_4_9 warden_fortified_5_10 '
    'warden_fortified_5_11 warden_fortified_5_8 warden_fortified_5_9 warden_fortified_6_10 '
    'warden_fortified_6_11 warden_fortified_6_8 warden_fortified_6_9 warden_fortified_7_10 '
    'warden_fortified_7_11 warden_fortified_7_8 warden_fortified_7_9 warden_fortified_8_10 '
    'warden_fortified_8_11 warden_fortified_8_8 warden_fortified_8_9 warden_fortified_9_10 '
    'warden_fortified_9_11 warden_fortified_9_6 warden_fortified_9_7 warden_fortified_9_8 '
    'warden_fortified_9_9',
    'Owner-approved Ford bridge: fortified',
    'Closed defenses, hides, stakes and the modest gatehouse follow the fortified story flag state. Each approved piece belongs to one physical tile. Preserve its native anchor, density and sorting; do not combine the pieces into a single overlay.')

group('props',
    'warden_force_10_10 warden_force_10_6 warden_force_10_7 warden_force_10_8 warden_force_10_9 '
    'warden_force_11_6 warden_force_11_7 warden_force_2_10 warden_force_2_9 warden_force_3_10 '
    'warden_force_3_11 warden_force_3_9 warden_force_4_10 warden_force_4_11 warden_force_4_7 '
    'warden_force_4_9 warden_force_5_10 warden_force_5_11 warden_force_5_8 warden_force_5_9 '
    'warden_force_6_10 warden_force_6_11 warden_force_6_8 warden_force_6_9 warden_force_7_10 '
    'warden_force_7_11 warden_force_7_8 warden_force_7_9 warden_force_8_10 warden_force_8_11 '
    'warden_force_8_8 warden_force_8_9 warden_force_9_10 warden_force_9_11 warden_force_9_6 '
    'warden_force_9_7 warden_force_9_8 warden_force_9_9',
    'Owner-approved Ford bridge: force',
    'Broken removable defenses are pulled aside after force; the supported bridge remains sound. Each approved piece belongs to one physical tile. Preserve its native anchor, density and sorting; do not combine the pieces into a single overlay.')

group('props',
    'warden_parley_10_10 warden_parley_10_6 warden_parley_10_7 warden_parley_10_8 warden_parley_10_9 '
    'warden_parley_11_6 warden_parley_11_7 warden_parley_2_10 warden_parley_2_9 warden_parley_3_10 '
    'warden_parley_3_11 warden_parley_3_9 warden_parley_4_10 warden_parley_4_11 warden_parley_4_8 '
    'warden_parley_4_9 warden_parley_5_10 warden_parley_5_11 warden_parley_5_8 warden_parley_5_9 '
    'warden_parley_6_10 warden_parley_6_11 warden_parley_6_8 warden_parley_6_9 warden_parley_7_10 '
    'warden_parley_7_11 warden_parley_7_8 warden_parley_7_9 warden_parley_8_10 warden_parley_8_11 '
    'warden_parley_8_8 warden_parley_8_9 warden_parley_9_10 warden_parley_9_11 warden_parley_9_6 '
    'warden_parley_9_7 warden_parley_9_8 warden_parley_9_9',
    'Owner-approved Ford bridge: parley',
    'Open defenses, stacked materials and modest grain delivery follow the parley outcome. Each approved piece belongs to one physical tile. Preserve its native anchor, density and sorting; do not combine the pieces into a single overlay.')

group('props',
    'warden_gate_collision',
    'Ford checkpoint collision',
    'Intentionally invisible one-pixel collision proxy. It blocks the closed checkpoint while the visible gate art supplies its appearance; story flags remove its collision after the crossing is won.')

group('props',
    'road_ramp_q_-1_0_-2_-3_d1 road_ramp_q_-1_0_0_-3_d1 road_ramp_q_-2_0_0_-2_d2 road_ramp_q_0_-2_-2_0_d1 '
    'road_ramp_q_0_-3_-2_0_d1 road_ramp_q_0_0_-1_-2_d1 road_ramp_q_0_0_-1_0_d1 road_ramp_q_0_0_-2_-1_d1 '
    'road_ramp_q_0_0_-2_-3_d1 road_ramp_q_0_0_-3_-1_d1 road_ramp_q_0_0_-3_-2_d1 road_ramp_q_0_0_0_-1_d1 '
    'road_ramp_q_0_0_0_1_d1 road_ramp_q_0_1_0_0_d1 road_ramp_q_0_2_2_0_d1 road_ramp_q_0_3_1_0_d1 '
    'road_ramp_q_1_0_0_0_d1 road_ramp_q_1_0_0_2_d1 road_ramp_q_1_2_0_0_d1 road_ramp_q_2_0_0_2_d1 '
    'road_ramp_q_2_1_0_0_d1 road_ramp_q_2_3_1_0_d1 road_ramp_q_3_1_0_0_d1 road_ramp_q_3_2_0_0_d1',
    'Approved authored road ramps',
    'Tile-owned sloped road meshes match the road material, shared corner heights and character footing. Keep them aligned to the saved road cells; they are structural surfaces, not loose scenery.')

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
group('terrain','path_ford grass_ford dirt_ford rock_ford planks_ford cliff_ford','Authored Ashen Ford ground and crossing',
    'Ford-specific shipped material overlays share the region palette. Keep roads/woodwork aligned to the authored walkable ground, and preserve real heights and cliff transitions.')
group('terrain','ford_water','Ashen Ford river and crossing',
    'Readable water at its authored shallow/deep cells. Local steam belongs to warm river water; the material must not imply a safe crossing where the map is deep.')
group('terrain','ash_ford','Cold burned east bank',
    'Shipped ford ash remains old and cold. Its grey texture is not an active fire or magical emitter; keep it quieter than the party and usable routes.')
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
group('ambient','bird flock butterfly fish squirrel_climb','Regional wildlife',
    'Keep readable motion at low density. Russet squirrels climb authored trunks, not open ground. Crows/swallow/silver fish fit the ford; koi and blossom palettes are library alternatives, not default local fauna.')
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
