// Technical review composition: whole native scene crops, no creative pixel edits.
const fs=require('fs'),path=require('path'),{chromium}=require('playwright');
(async()=>{
 const out=path.resolve('docs/art/mob-sprite-review/wyrm-triad');
 const shots=['attack-contact','breath-impact','roar-impact','wingbeat-impact'].flatMap(action=>['black','blue','green'].map(color=>({name:color+'-'+action,url:'data:image/png;base64,'+fs.readFileSync(path.join(out,color+'-'+action+'.png')).toString('base64')})));
 const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
 try{
  const page=await browser.newPage();
  const url=await page.evaluate(async shots=>{
   const c=document.createElement('canvas');c.width=1440;c.height=1360;const x=c.getContext('2d');x.imageSmoothingEnabled=false;x.fillStyle='#111923';x.fillRect(0,0,c.width,c.height);
   for(let i=0;i<shots.length;i++){
    const image=new Image();image.src=shots[i].url;await image.decode();const dx=i%3*480,dy=Math.floor(i/3)*340;
    x.fillStyle='#eee9da';x.font='bold 19px Arial';x.fillText(shots[i].name.replaceAll('-',' '),dx+12,dy+28);
    x.drawImage(image,540,500,960,560,dx,dy+40,480,280);
   }
   return c.toDataURL();
  },shots);
  fs.writeFileSync(path.join(out,'actions-in-game.png'),Buffer.from(url.split(',')[1],'base64'));
 }finally{await browser.close()}
})().catch(error=>{console.error(error);process.exit(1)});
