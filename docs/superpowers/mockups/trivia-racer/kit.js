// Shared mock data and a top-down race car SVG for the style mockups.
window.RACE = {
  lap: 2, laps: 3, q: 7, finish: 20,
  category: 'Science & Nature', difficulty: 3,
  question: 'Which planet has the most known moons?',
  answers: ['Jupiter', 'Saturn', 'Uranus', 'Neptune'],
  seconds: 9,
  drivers: [
    { name: 'JO',   pos: 13, throttle: 'CRUISE'  },
    { name: 'SAM',  pos: 10, throttle: 'REDLINE' },
    { name: 'ALEX', pos: 8,  throttle: 'PUSH'    },
    { name: 'RIA',  pos: 11, throttle: 'PUSH'    },
    { name: 'MO',   pos: 4,  throttle: 'REDLINE' },
    { name: 'KAI',  pos: 7,  throttle: 'REDLINE' },
  ],
};

window.carSvg = (color, opts = {}) => `
<svg viewBox="0 0 64 28" class="car-svg" style="color:${color}">
  <rect x="7" y="0" width="12" height="6" rx="1.5" fill="${opts.tyre || '#111'}"/>
  <rect x="7" y="22" width="12" height="6" rx="1.5" fill="${opts.tyre || '#111'}"/>
  <rect x="42" y="1" width="10" height="5" rx="1.5" fill="${opts.tyre || '#111'}"/>
  <rect x="42" y="22" width="10" height="5" rx="1.5" fill="${opts.tyre || '#111'}"/>
  <rect x="0" y="3" width="5" height="22" rx="1.5" fill="currentColor"/>
  <polygon points="4,9 30,6.5 55,11 63,14 55,17 30,21.5 4,19" fill="currentColor"/>
  <rect x="56" y="4" width="5" height="20" rx="1.5" fill="currentColor"/>
  <ellipse cx="29" cy="14" rx="7" ry="3.4" fill="${opts.cockpit || 'rgba(0,0,0,.55)'}"/>
  ${opts.number ? `<text x="44" y="16.5" font-size="8" font-weight="900" text-anchor="middle" fill="${opts.numberInk || '#fff'}" font-family="sans-serif">${opts.number}</text>` : ''}
</svg>`;

// Ranks drivers by position for P1..Pn labels without reordering lanes.
window.ranks = () => {
  const sorted = [...RACE.drivers].sort((a, b) => b.pos - a.pos);
  return Object.fromEntries(RACE.drivers.map(d => [d.name, sorted.indexOf(d) + 1]));
};
