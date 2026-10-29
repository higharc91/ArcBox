// Line icons for the phone mockups: <i data-icon="sword"></i> is swapped for an inline SVG.
const P = {
  sword: '<path d="M14.5 3.5H20.5V9.5L9 21 6 18 17.5 6.5"/><path d="M4 15l5 5M3 21l2.5-2.5"/>',
  shield: '<path d="M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6z"/><path d="M12 7v10"/>',
  bash: '<path d="M12 3l7 3v5c0 4-3 7-7 8-4-1-7-4-7-8V6z"/><path d="M20 15l2 2M20 19l3-1M17 20l1 3"/>',
  heart: '<path d="M12 20s-7-4.5-7-10a4 4 0 0 1 7-2.5A4 4 0 0 1 19 10c0 5.5-7 10-7 10z"/><path d="M9 11h6M12 8v6"/>',
  fort: '<path d="M3 20h18M5 20V8h3v2h3V8h2v2h3V8h3v12"/><path d="M10 20v-5h4v5"/>',
  boot: '<path d="M6 3v10l-3 5h11l6-2v-3l-6-1-2-9z"/>',
  bag: '<path d="M5 8h14l-1 12H6z"/><path d="M9 8a3 3 0 0 1 6 0"/>',
  sheet: '<path d="M6 3h12v18H6z"/><path d="M9 8h6M9 12h6M9 16h3"/>',
  hourglass: '<path d="M6 3h12M6 21h12"/><path d="M7 3c0 6 10 5 10 9s-10 3-10 9M17 3c0 6-10 5-10 9s10 3 10 9"/>',
  up: '<path d="M12 19V5M5 12l7-7 7 7"/>',
  rotate: '<path d="M20 12a8 8 0 1 1-2.4-5.7"/><path d="M20 4v5h-5"/>',
  check: '<path d="M4 12l5 5L20 6"/>',
  swap: '<path d="M4 8h14l-3-3M20 16H6l3 3"/>',
  surge: '<path d="M13 2L4 14h7l-1 8 9-12h-7z"/>',
};

export function icons(root = document) {
  for (const el of root.querySelectorAll('i[data-icon]')) {
    const rot = el.dataset.rot ? ` style="transform:rotate(${el.dataset.rot}deg)"` : '';
    el.outerHTML = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"${rot}>${P[el.dataset.icon]}</svg>`;
  }
}
