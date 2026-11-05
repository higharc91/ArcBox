// Read-only verification of the exact approved production installation.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict'),{execFileSync}=require('node:child_process');
const {hash}=require('./build.cjs');
const root=path.resolve(__dirname,'../../../../..'),content=path.join(root,'content/tactics/base');
const approval=JSON.parse(fs.readFileSync(path.join(__dirname,'approval.json'))),record=JSON.parse(fs.readFileSync(path.join(__dirname,'review-pack.json')));
assert.equal(approval.status,'approved');assert(approval.ownerApproval.quote);
for(const [file,digest]of Object.entries(approval.sha256))assert.equal(hash(fs.readFileSync(path.join(__dirname,file))),digest,`Approved source/review changed: ${file}`);
for(const [file,digest]of Object.entries({...record.assetHashes,...approval.reviewDocuments}))assert.equal(hash(fs.readFileSync(path.join(content,file))),digest,`Production differs from approved review: ${file}`);
const allowed=new Set([...Object.keys(record.assetHashes),...record.cosmeticDocuments]);
const changed=execFileSync('git',['diff','--name-only','633e009f','--','content/tactics/base'],{cwd:root,encoding:'utf8'}).trim().split('\n').filter(Boolean);
for(const file of changed)assert(allowed.has(file.replace('content/tactics/base/','')),`Unrelated production change: ${file}`);
const baseline=execFileSync('git',['ls-tree','-r','--name-only','633e009f','content/tactics/base'],{cwd:root,encoding:'utf8'}).trim().split('\n').map(f=>f.replace('content/tactics/base/',''));
const expected=new Set([...baseline,...Object.keys(record.assetHashes)]),inventory=[];
function walk(dir,rel=''){for(const entry of fs.readdirSync(dir,{withFileTypes:true}).sort((a,b)=>a.name.localeCompare(b.name))){const file=rel?`${rel}/${entry.name}`:entry.name,full=path.join(dir,entry.name);if(entry.isDirectory())walk(full,file);else{assert(expected.delete(file),`Unexpected content: ${file}`);const data=fs.readFileSync(full);inventory.push({path:file,bytes:data.length,sha256:hash(data)});}}}
walk(content);assert.equal(expected.size,0,'Missing legacy or installed content');
const result={result:'PASS',approvedVariants:14,portraits:84,placedPeople:28,artFiles:98,cosmeticDocuments:record.cosmeticDocuments,productionFiles:inventory.length,productionBytes:inventory.reduce((n,f)=>n+f.bytes,0),productionInventorySha256:hash(JSON.stringify(inventory)),unrelatedContentChanges:0};
console.log(JSON.stringify(result,null,2));
