"""Lossless, reproducible cuts of the existing Aoede batch; no synthesis."""
from pathlib import Path
import hashlib,json,re,wave,sys
source=Path(sys.argv[1]);out=Path(sys.argv[2]);out.mkdir(parents=True,exist_ok=True)
request=json.loads(Path('tools/tactics-lore/voice-runs/2026-11-13-story/job09-aoede-batch.json').read_text())['request']
transcript=json.loads(Path(sys.argv[3]).read_text())
sha=hashlib.sha256(source.read_bytes()).hexdigest()
assert sha==transcript['sourceSha256']
with wave.open(str(source),'rb') as w:
 params=w.getparams();pcm=w.readframes(w.getnframes())
assert (params.nchannels,params.sampwidth,params.framerate)==(1,2,24000)
bounds=[0,134397,266941,params.nframes]
words=[w for s in transcript['segments'] for w in s['words']]
def tokens(s):return re.findall(r"[a-z0-9]+(?:'[a-z0-9]+)?",s.lower().replace('’',"'"))
rows=[]
for segment,a,b in zip(request['segments'],bounds,bounds[1:]):
 selected=[w for w in words if a/24000 <= (w['start']+w['end'])/2 < b/24000]
 recognized=' '.join(w['word'].strip() for w in selected)
 assert tokens(recognized)==tokens(segment['text']),(segment['line'],recognized)
 assert all(w['start']>=a/24000 and w['end']<=b/24000 for w in selected)
 path=out/(segment['raw']+'.wav')
 with wave.open(str(path),'wb') as w:
  w.setparams(params);w.writeframes(pcm[a*2:b*2])
 rows.append({**segment,'startSample':a,'endSample':b,'start':a/24000,'end':b/24000,'duration':(b-a)/24000,'recognized':recognized,'normalizedWordsMatch':True,'wavSha256':hashlib.sha256(path.read_bytes()).hexdigest()})
result={'job':'job_9dYTM2t3YLu4vMUA3wgQB1GC','asset':'asset_U8LG8WGH76Qqw7Zqz3mRZy8C','source':source.name,'sourceSha256':sha,'rate':24000,'historicalBatchCreativeUnits':3,'newCreativeUnitsSpent':0,'method':'Lossless PCM cuts at midpoints of the two inter-line silences. Unprompted small.en ASR identifies words and verifies no word crosses a cut. Punctuation/case ignored for lexical matching.','segments':rows}
Path(sys.argv[4]).write_text(json.dumps(result,indent=2)+'\n')
print([(r['line'],round(r['duration'],3),r['normalizedWordsMatch']) for r in rows])
