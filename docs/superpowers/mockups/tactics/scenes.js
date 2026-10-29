// The mockups' two locations, shared by the TV screens that show them.
import { makeMap } from './kit.js';
import { PARTY, NPCS, PLAYERS } from './cast.js';

/** Emberwick's square at dusk, with the party walking in behind its leader. */
export function village() {
  const m = makeMap([
    'g g g s G g g g s g g g G G',
    'g g g s g g g g s g g g g G',
    'g g g s s s s s s s s s g g',
    'g g s s s s s s s s s s s g',
    'g g s s s s s s s s s s s d',
    'g g s s s s s s s s s s d d',
    'g g s s s s s s s s s d d g',
    'g g g s s s s s s s d d g g',
    'G g g g d d d d d d d g g g',
    'G G g g g d g g g g g g g G',
    'G G g g g d g g g g g g G G',
    'G G G g g d g g g g g g G G',
  ], [
    '1 1 1 1 1 1 1 1 1 1 1 1 2 2',
    '1 1 1 1 1 1 1 1 1 1 1 1 1 2',
    '1 1 1 1 1 1 1 1 1 1 1 1 1 1',
    '1 1 1 1 1 1 1 1 1 1 1 1 1 1',
    '1 1 1 1 1 1 1 1 1 1 1 1 1 1',
    '1 1 1 1 1 1 1 1 1 1 1 1 1 1',
    '1 1 1 1 1 1 1 1 1 1 1 1 1 1',
    '1 1 1 1 1 1 1 1 1 1 1 1 1 1',
    '1 1 1 1 1 1 1 1 1 1 1 1 1 1',
    '2 1 1 1 1 1 1 1 1 1 1 1 1 1',
    '2 2 1 1 1 1 1 1 1 1 1 1 1 2',
    '2 2 2 1 1 1 1 1 1 1 1 1 2 2',
  ], 960, 470);

  m.props = [
    { kind: 'house', x: 2, y: 1, x0: 0, y0: 0, x1: 2, y1: 1, roof: '#9a3d33' },
    { kind: 'house', x: 7, y: 1, x0: 4, y0: 0, x1: 7, y1: 1, roof: '#3d5f8f', walls: 4 },
    { kind: 'house', x: 12, y: 1, x0: 9, y0: 0, x1: 12, y1: 1, roof: '#56763c' },
    { kind: 'house', x: 1, y: 6, x0: 0, y0: 4, x1: 1, y1: 6, roof: '#7a4a2a', plaster: '#d8c7a4' },
    { kind: 'well', x: 7, y: 4 },
    { kind: 'lantern', x: 3, y: 3 }, { kind: 'lantern', x: 12, y: 3 }, { kind: 'lantern', x: 3, y: 7 }, { kind: 'lantern', x: 12, y: 5 },
    { kind: 'crate', x: 12, y: 2 }, { kind: 'crate', x: 13, y: 3 },
    { kind: 'tree', x: 13, y: 0, variant: 'pine' }, { kind: 'tree', x: 0, y: 9, variant: 'autumn' }, { kind: 'tree', x: 1, y: 11 },
    { kind: 'tree', x: 13, y: 10, variant: 'autumn' }, { kind: 'tree', x: 12, y: 11, variant: 'pine' },
  ];
  const col = k => PLAYERS[PARTY[k].player].color;
  m.units = [
    { id: 'mayor', x: 5, y: 5, sprite: NPCS.mayor.sprite, scale: 5 },
    { id: 'smith', x: 10, y: 2, sprite: NPCS.smith.sprite, scale: 5 },
    { id: 'pip', x: 9, y: 3, sprite: NPCS.child.sprite, scale: 3.5 },
    { id: 'ilyra', x: 7, y: 6, sprite: PARTY.ilyra.sprite, ring: col('ilyra'), crown: true },
    { id: 'kara', x: 8, y: 6, sprite: PARTY.kara.sprite, ring: col('kara') },
    { id: 'wren', x: 9, y: 6, sprite: PARTY.wren.sprite, ring: col('wren'), scale: 4 },
    { id: 'tamsin', x: 10, y: 6, sprite: PARTY.tamsin.sprite, ring: col('tamsin') },
    { id: 'aldric', x: 11, y: 7, sprite: PARTY.aldric.sprite, ring: col('aldric'), scale: 4.5 },
  ];
  return m;
}
