// Renders the shared Broadcast tower + lanes. Each driver: {name, dist, chip, chipText?, delta?, from?, spun?, mv?}
window.TEAM = { JO: '#3671c6', SAM: '#e8002d', ALEX: '#ff8000', RIA: '#27f4d2', MO: '#b6babd', KAI: '#229971' };
window.FINISH = 20;

window.renderRace = (el, drivers, opts = {}) => {
  const sorted = [...drivers].sort((a, b) => b.dist - a.dist);
  const lead = sorted[0].dist;
  const x = d => 6 + Math.min(d, FINISH) / FINISH * 88;
  el.innerHTML = `
    <div class="tower"><div class="th"><span>POS · DRIVER</span><span>${opts.towerRight || 'GAP · THROTTLE'}</span></div>
      <div class="rows">${sorted.map((d, i) => `
        <div class="trow ${i === 0 ? 'leader' : ''}"><div class="p">${i + 1}</div><div class="c" style="background:${TEAM[d.name]}"></div>
          <div class="n">${d.name}${d.mv ? `<span class="mv" style="color:${d.mv > 0 ? 'var(--good)' : 'var(--bad)'}">${d.mv > 0 ? '▲' : '▼'}${Math.abs(d.mv)}</span>` : ''}</div>
          <div class="gap">${i ? (lead - d.dist ? '−' + (lead - d.dist) : 'TIED') : 'LEAD'}</div><div class="chip ${d.chip}">${d.chipText || d.chip}</div></div>`).join('')}
      </div></div>
    <div class="trackcol"><div class="th"><span>START</span><span>FINISH · ${FINISH}</span></div>
      <div class="lanes"><div class="ticks"></div>${sorted.map(d => `
        <div class="lrow ${d.spun ? 'spun' : ''}">
          <div class="fill" style="width:${x(d.dist) - 4}%;background:${TEAM[d.name]}"></div>
          ${d.from !== undefined && d.from < d.dist ? `<div class="ghost" style="left:${x(d.from) - 4}%;width:${x(d.dist) - x(d.from)}%;background:${TEAM[d.name]}"></div>` : ''}
          <div class="flag"></div>
          <div class="car" style="left:${x(d.dist)}%">${carSvg(TEAM[d.name], { tyre: '#050507', cockpit: '#111' })}</div>
          ${d.delta ? `<div class="delta" style="left:calc(${x(d.dist)}% + 10px);color:${d.delta[0] === '+' ? 'var(--good)' : 'var(--bad)'}">${d.delta}</div>` : ''}
        </div>`).join('')}
      </div></div>`;
};

window.diffBars = n => [1, 2, 3].map(i => `<i class="${i <= n ? '' : 'off'}"></i>`).join('');
