// The mockups' sample party, enemies and NPCs, shared so every screen shows the same characters.
// Players and colours follow join order, like ArcBox's player colours.

export const PLAYERS = {
  jess:   { name: 'Jess',   color: '#ff8a3d' },
  dave:   { name: 'Dave',   color: '#3ec7ff' },
  priya:  { name: 'Priya',  color: '#c08cff' },
  sam:    { name: 'Sam',    color: '#5fe08a' },
  marcus: { name: 'Marcus', color: '#ffd447' },
  alex:   { name: 'Alex',   color: '#ff7fb6' },
};

export const PARTY = {
  kara: {
    name: 'Kara', player: 'jess', line: 'Human Fighter · Vanguard', level: 3,
    hp: [31, 34], ac: 18, spd: 7, move: 4, jump: 2,
    sprite: { head: 'helmet', helm: '#aab4c2', plume: '#c23b3b', skin: '#e8b48c', outfit: '#4d6f9e', accent: '#c23b3b', legs: '#38394a', weapon: 'sword', shield: '#2f4f86' },
  },
  aldric: {
    name: 'Aldric', player: 'dave', line: 'Dwarf Cleric · Domain unchosen', level: 3,
    hp: [27, 29], ac: 16, spd: 5, move: 3, jump: 1,
    sprite: { hair: '#b8642e', beard: true, skin: '#e2a982', outfit: '#e9e2cf', accent: '#d9a93a', legs: '#5a4636', weapon: 'mace' },
  },
  wren: {
    name: 'Wren', player: 'priya', line: 'Halfling Rogue · Shadow', level: 3,
    hp: [22, 24], ac: 15, spd: 9, move: 5, jump: 3,
    sprite: { head: 'hood', hood: '#2e4a3c', skin: '#d79c74', outfit: '#3a3f4a', accent: '#6f8f5a', legs: '#2a2c34', weapon: 'dagger' },
  },
  ilyra: {
    name: 'Ilyra', player: 'sam', line: 'Elf Wizard · Chronomancer', level: 3,
    hp: [18, 19], ac: 12, spd: 6, move: 3, jump: 2,
    sprite: { hair: '#e9e3f5', longHair: true, skin: '#f3d4c0', outfit: '#5b3d8f', accent: '#e3c35a', legs: '#3a2c55', weapon: 'staff', orb: '#9fe6ff', hat: '#4a2f7a', hatBand: '#e3c35a', ears: true },
  },
  tamsin: {
    name: 'Tamsin', player: 'marcus', line: 'Half-Elf Ranger · Sharpshooter', level: 3,
    hp: [25, 28], ac: 14, spd: 8, move: 4, jump: 2,
    sprite: { hair: '#a8432a', longHair: true, skin: '#eab58d', outfit: '#4c6b34', accent: '#8a5a2b', legs: '#3c3226', weapon: 'bow' },
  },
};

export const RECRUIT = {
  sefa: {
    name: 'Sefa', player: 'alex', line: 'Tiefling Warlock', level: 1,
    sprite: { hair: '#1f1a2e', longHair: true, skin: '#b4546a', eye: '#ffd76a', horns: true, hornColor: '#2a2030', outfit: '#3a2342', accent: '#b58cff', legs: '#241a2a', weapon: 'tome', tome: '#5a2a6a' },
  },
};

export const ENEMIES = {
  cutthroat: { name: 'Goblin Cutthroat', line: 'Enemy · Skirmisher', hp: [9, 16], ac: 13,
    sprite: { skin: '#7fa04a', ears: true, eye: '#ffd84a', hair: '#3a2a1c', outfit: '#6b4a2e', accent: '#a33a2a', legs: '#4a3a28', weapon: 'dagger' } },
  goblin: { name: 'Goblin Spear', line: 'Enemy', hp: [14, 14], ac: 13,
    sprite: { skin: '#86a64e', ears: true, eye: '#ffd84a', head: 'hood', hood: '#5a3a28', outfit: '#5a4a32', accent: '#8a3a2a', legs: '#3a2e22', weapon: 'sword' } },
  shaman: { name: 'Goblin Shaman', line: 'Enemy · Caster', hp: [12, 12], ac: 11,
    sprite: { skin: '#79a050', ears: true, eye: '#ff6a5e', hair: '#d9d0b0', outfit: '#6a2f3a', accent: '#e3c35a', legs: '#3a2228', weapon: 'staff', orb: '#ff7a5a' } },
  brute: { name: 'Orc Brute', line: 'Enemy · Bruiser', hp: [30, 30], ac: 14,
    sprite: { skin: '#6d8a6a', ears: true, eye: '#ff5a4a', hair: '#1e1a18', outfit: '#4a3a32', accent: '#8a8a92', legs: '#2e2622', weapon: 'axe' } },
};

export const NPCS = {
  mayor: { name: 'Mayor Halden', sprite: { hair: '#c9c4bb', beard: true, skin: '#e8b897', outfit: '#7a2f36', accent: '#e3c35a', legs: '#3a2c2a' } },
  smith: { name: 'Brenna', sprite: { hair: '#2a1a12', skin: '#c98d66', outfit: '#5a4a3a', accent: '#8a8a92', legs: '#3a2e22', weapon: 'axe' } },
  child: { name: 'Pip', sprite: { hair: '#e0b04a', skin: '#f0c49a', outfit: '#3e7ab0', accent: '#e3c35a', legs: '#3a3346' } },
};

/** Fills every <canvas data-portrait="kara"> on the page (party, recruit, enemies or NPCs). */
export function fillPortraits(portrait) {
  const all = { ...PARTY, ...RECRUIT, ...ENEMIES, ...NPCS };
  for (const c of document.querySelectorAll('canvas[data-portrait]')) {
    const who = all[c.dataset.portrait];
    const bg = who.player ? PLAYERS[who.player].color : c.dataset.portrait in ENEMIES ? '#a0443c' : '#8a7a5a';
    portrait(c, who.sprite, c.dataset.bg || bg);
  }
}
