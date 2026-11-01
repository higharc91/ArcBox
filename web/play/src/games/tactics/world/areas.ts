/**
 * The flyover's tour of the Vale: one battle map per region, in story order (lore/arcs.json chapters I–V). Each area
 * names its sky and mountain ranges (backgrounds pairings), its colour grade, and set dressing: the battle maps carry
 * only the props that matter to the rules, so the title screen adds trees, reeds and fires around them. Dressing never
 * changes the map itself.
 */

export interface Dressing {
  id: string;
  variant?: string;
  /** Map tile [x, y]. */
  at: [number, number];
}

export interface Area {
  /** The battle map, `rules/maps/<map>.json`. */
  map: string;
  /** The region in lore/regions.json, whose name is the caption. */
  region: string;
  /** Fallback caption when the lore can't load. */
  caption: string;
  /** A smaller line under the caption: the place within the region. */
  place?: string;
  chapter: number;
  sky: string;
  ranges: string;
  grade: string;
  /** How much of the grade to apply (0–1). */
  gradeStrength: number;
  /** Ambient light: sky and ground colours and how bright; dark areas lean on their own lights. */
  ambient: { sky: string; ground: string; intensity: number };
  /** The sun (from the top-left of the screen): colour and intensity; 0 for interiors lit by fire. */
  sun: { colour: string; intensity: number };
  dressing: Dressing[];
  /** Title-screen art direction: map materials drawn as others ("grass/lush" → "sand/desert" in the Sunscar). */
  retexture?: Record<string, string>;
}

export const AREAS: Area[] = [
  {
    map: "river_crossing",
    region: "ashen_ford",
    caption: "The Ashen Ford",
    chapter: 1,
    sky: "day",
    ranges: "hills",
    grade: "golden_hour",
    gradeStrength: 0.55,
    ambient: { sky: "#b9c8e6", ground: "#4a3a2c", intensity: 0.55 },
    sun: { colour: "#fff1d6", intensity: 0.6 },
    dressing: [
      { id: "tree", variant: "autumn", at: [1, 1] },
      { id: "tree", variant: "green", at: [3, 3] },
      { id: "tree", variant: "light", at: [0, 6] },
      { id: "tree", variant: "autumn", at: [2, 10] },
      { id: "bush", variant: "autumn", at: [4, 1] },
      { id: "bush", variant: "green", at: [0, 9] },
      { id: "tall_grass", variant: "green", at: [2, 5] },
      { id: "tall_grass", variant: "dry", at: [4, 11] },
      { id: "reeds", at: [5, 2] },
      { id: "reeds", at: [5, 10] },
      { id: "reeds", at: [8, 1] },
      { id: "campfire", at: [10, 7] },
      { id: "tent", variant: "red", at: [11, 6] },
      { id: "banner", variant: "goblin", at: [9, 5] },
      { id: "torch", at: [8, 6] },
      { id: "torch", at: [8, 9] },
      { id: "barrel", at: [12, 8] },
      { id: "pine", at: [12, 1] },
      { id: "pine", at: [13, 3] },
      { id: "rock_medium", variant: "grey", at: [10, 0] },
      { id: "signpost", at: [5, 7] },
      { id: "log", at: [11, 10] },
    ],
  },
  {
    map: "forest_ruins",
    region: "mirewood",
    caption: "Mirewood",
    place: "The drowned ruins",
    chapter: 2,
    sky: "swamp",
    ranges: "marsh",
    grade: "swamp",
    gradeStrength: 0.6,
    ambient: { sky: "#8fb3a8", ground: "#2a3328", intensity: 0.42 },
    sun: { colour: "#d9f0d0", intensity: 0.38 },
    retexture: { "grass/lush": "forest_floor/mossy", "dirt/dark": "mud/swamp" },
    dressing: [
      { id: "swamp_tree", at: [0, 3] },
      { id: "swamp_tree", at: [3, 0] },
      { id: "swamp_tree", at: [12, 10] },
      { id: "dead_tree", variant: "dark", at: [13, 1] },
      { id: "will_o_wisp", variant: "blue", at: [5, 3] },
      { id: "will_o_wisp", variant: "green", at: [11, 8] },
      { id: "will_o_wisp", variant: "blue", at: [2, 7] },
      { id: "lantern", variant: "moss", at: [6, 5] },
      { id: "lantern", variant: "moss", at: [10, 6] },
      { id: "mushroom_cluster", variant: "glow", at: [1, 9] },
      { id: "mushroom_cluster", variant: "glow", at: [13, 7] },
      { id: "fern", variant: "green", at: [0, 0] },
      { id: "fern", variant: "green", at: [6, 10] },
      { id: "reeds", at: [3, 11] },
      { id: "reeds", at: [0, 11] },
      { id: "ruin_wall", variant: "grey", at: [12, 4] },
    ],
  },
  {
    map: "warlord_bridge",
    region: "sunscar",
    caption: "The Sunscar",
    place: "The warlord's bridge",
    chapter: 3,
    sky: "desert",
    ranges: "dunes",
    grade: "desert_heat",
    gradeStrength: 0.55,
    ambient: { sky: "#e8d2b0", ground: "#5a3f28", intensity: 0.5 },
    sun: { colour: "#fff0d0", intensity: 0.72 },
    retexture: { "grass/lush": "sand/desert", "dirt/dark": "path/packed", "rock/grey": "sandstone/red" },
    dressing: [
      { id: "cactus", at: [1, 1] },
      { id: "cactus", at: [12, 10] },
      { id: "cactus", at: [3, 11] },
      { id: "cactus_pear", at: [10, 1] },
      { id: "cactus_pear", at: [0, 9] },
      { id: "ribcage", at: [2, 2] },
      { id: "skull_pike", at: [9, 4] },
      { id: "skull_pike", at: [9, 7] },
      { id: "rock_cluster", variant: "sandstone", at: [13, 8] },
      { id: "tent", variant: "canvas", at: [11, 9] },
      { id: "banner", variant: "crimson", at: [10, 5] },
      { id: "torch", at: [4, 4] },
      { id: "torch", at: [4, 7] },
      { id: "campfire", at: [11, 6] },
    ],
  },
  {
    map: "crystal_cave",
    region: "frostpeak",
    caption: "Frostpeak Pass",
    place: "The crystal workings",
    chapter: 4,
    sky: "overcast",
    ranges: "peaks",
    grade: "frost",
    gradeStrength: 0.6,
    ambient: { sky: "#9fb4d8", ground: "#262a3a", intensity: 0.34 },
    sun: { colour: "#dfe8ff", intensity: 0.3 },
    dressing: [
      { id: "stalagmite", variant: "cool", at: [1, 1] },
      { id: "stalagmite", variant: "cool", at: [12, 9] },
      { id: "stalagmite_large", variant: "cool", at: [13, 0] },
      { id: "stalagmite_small", variant: "cool", at: [0, 10] },
      { id: "crystal_big", variant: "blue", at: [2, 8] },
      { id: "crystal_big", variant: "violet", at: [10, 10] },
      { id: "glow_mushrooms", variant: "teal", at: [1, 4] },
      { id: "glow_mushrooms", variant: "violet", at: [9, 1] },
      { id: "pine_snowy", at: [3, 0] },
      { id: "pine_snowy", at: [13, 11] },
      { id: "ice_spikes", at: [12, 2] },
    ],
  },
  {
    map: "keep_gate",
    region: "ember_keep",
    caption: "The Ember Keep",
    place: "The gate",
    chapter: 5,
    sky: "ashfall",
    ranges: "volcano",
    grade: "infernal",
    gradeStrength: 0.45,
    ambient: { sky: "#c08070", ground: "#2a1614", intensity: 0.34 },
    sun: { colour: "#ffb08a", intensity: 0.32 },
    dressing: [
      { id: "torch", at: [5, 3] },
      { id: "torch", at: [5, 8] },
      { id: "banner", variant: "crimson", at: [8, 3] },
      { id: "banner", variant: "crimson", at: [8, 8] },
      { id: "vent", at: [3, 2] },
      { id: "dead_tree", variant: "dark", at: [0, 0] },
      { id: "dead_tree", variant: "dark", at: [1, 11] },
      { id: "skull_pike", at: [4, 10] },
      { id: "brazier", variant: "fire", at: [11, 4] },
      { id: "brazier", variant: "fire", at: [11, 7] },
      { id: "rock_cluster", variant: "obsidian", at: [0, 6] },
    ],
  },
  {
    map: "hall_of_the_flame",
    region: "ember_keep",
    caption: "The Ember Keep",
    place: "The Hall of the Flame",
    chapter: 5,
    sky: "ashfall",
    ranges: "volcano",
    grade: "infernal",
    gradeStrength: 0.6,
    ambient: { sky: "#a06058", ground: "#1a0c0c", intensity: 0.26 },
    sun: { colour: "#ff9a70", intensity: 0.14 },
    dressing: [
      { id: "brazier", variant: "fire", at: [1, 1] },
      { id: "brazier", variant: "fire", at: [1, 10] },
      { id: "candelabra", variant: "gold", at: [12, 10] },
      { id: "treasure_hoard", at: [13, 5] },
      { id: "statue_gargoyle", variant: "grey", at: [12, 3] },
      { id: "statue_gargoyle", variant: "grey", at: [12, 8] },
      { id: "chains", variant: "rusty", at: [4, 1] },
    ],
  },
];
