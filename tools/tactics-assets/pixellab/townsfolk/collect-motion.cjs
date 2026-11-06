// Download only the concrete frame URLs returned by get_character. No API token.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
(async()=>{
  const base=path.join(__dirname,'directional');
  const result=JSON.parse(fs.readFileSync(path.join(base,'motion-result.json'),'utf8'));
  const text=result.content.filter(c=>c.type==='text').map(c=>c.text).join('\n');
  let action=null,downloaded=0;
  for(const line of text.split('\n')) {
    const header=line.match(/^  (bree_(?:idle|walk|talk)) —/);
    if(header){action=header[1].slice(5);continue;}
    const frame=line.match(/^    ([a-z-]+): (https:\/\/\S+) \(i=0\.\.(\d+)\)/);
    if(!frame||!action)continue;
    const [,direction,template,last]=frame;
    const directory=path.join(base,'motion',action,direction);
    fs.mkdirSync(directory,{recursive:true});
    for(let i=0;i<=Number(last);i++) {
      const file=path.join(directory,`${i}.png`);
      if(fs.existsSync(file))continue;
      const response=await fetch(template.replace('{i}',i));
      assert(response.ok,`Frame download HTTP ${response.status}`);
      fs.writeFileSync(file,Buffer.from(await response.arrayBuffer()));downloaded++;
    }
  }
  console.log(JSON.stringify({downloadedFrames:downloaded}));
})().catch(error=>{console.error(error.message);process.exitCode=1;});
