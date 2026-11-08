const fs=require('fs'),path=require('path'),{chromium}=require('playwright');
(async()=>{
 const out=path.resolve('docs/art/mob-sprite-review/gilded-and-rime');
 const names=['gold-attack-contact','frost-attack-contact','ogre-attack-contact',
  'gold-breath-impact','frost-boulder-release','ogre-boulder-release',
  'gold-roar-impact','frost-roar-impact','ogre-roar-impact','gold-wingbeat-impact'];
 const shots=names.map((name,i)=>({name,column:i===9?1:i%3,row:Math.floor(i/3),url:'data:image/png;base64,'+fs.readFileSync(path.join(out,name+'.png')).toString('base64')}));
 const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
 try{
  const page=await browser.newPage();const url=await page.evaluate(async shots=>{
   const c=document.createElement('canvas');c.width=1440;c.height=1360;const x=c.getContext('2d');x.imageSmoothingEnabled=false;x.fillStyle='#111923';x.fillRect(0,0,c.width,c.height);
   for(const shot of shots){const image=new Image();image.src=shot.url;await image.decode();const dx=shot.column*480,dy=shot.row*340;x.fillStyle='#eee9da';x.font='bold 19px Arial';x.fillText(shot.name.replaceAll('-',' '),dx+12,dy+28);x.drawImage(image,540,500,960,560,dx,dy+40,480,280)}return c.toDataURL();
  },shots);fs.writeFileSync(path.join(out,'actions-in-game.png'),Buffer.from(url.split(',')[1],'base64'));
 }finally{await browser.close()}
})().catch(error=>{console.error(error);process.exit(1)});
