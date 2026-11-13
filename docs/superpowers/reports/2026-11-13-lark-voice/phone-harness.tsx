import { render } from 'preact';
import { Dialogue } from './src/games/tactics/story';
import './src/fonts';
import './src/theme.css';
import './src/games/tactics/tactics.css';
const data=await fetch('/review-dialogue.json').then(r=>r.json());
const send=(action:string,args:unknown)=>(window as any).reviewActions.push({action,args});
(window as any).reviewActions=[];
render(<main class="screen" data-game="tactics"><Dialogue data={data} viewId="lark-greeting" send={send} stream={()=>{}}/></main>,document.getElementById('app')!);
