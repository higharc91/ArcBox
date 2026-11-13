import { render } from 'preact';
import { useState } from 'preact/hooks';
import { BattleMap } from './src/games/tactics/battleMap';
import { decodeMap } from './src/games/tactics/map';
import './src/fonts';
import './src/theme.css';
import './src/games/tactics/tactics.css';
const id=new URLSearchParams(location.search).get('map') || 'rime_tower';
const raw=await fetch('/review-pack/rules/maps/'+id+'.json').then(r=>r.json());
const map=decodeMap(raw);
const me={id:'hero',name:'Review hero',at:[0,0],side:'Party',life:'Up',hp:20,maxHp:20};
function Review(){const [cursor,setCursor]=useState([0,0]); return <main class="screen tx" data-game="tactics"><h1>{id.replaceAll('_',' ')}</h1><BattleMap me={me as any} units={[me as any]} cursor={cursor as any} onPick={setCursor as any} onUnit={()=>{}} map={map} reach={[]} risks={[]} spread={[]} aims={[]} zones={[]}/></main>}
render(<Review/>,document.getElementById('app')!);
