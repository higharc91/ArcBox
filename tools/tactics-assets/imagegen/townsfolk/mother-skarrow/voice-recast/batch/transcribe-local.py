from pathlib import Path
import json,sys,time,hashlib,os
from faster_whisper import WhisperModel
base=Path(__file__).resolve().parent.parent
repo=next(p for p in base.parents if (p/'tools/tactics-lore/voices.py').is_file())
model_name=sys.argv[2] if len(sys.argv)>2 else 'small.en'
model=WhisperModel(model_name,device='cpu',compute_type='int8',cpu_threads=4,num_workers=1,download_root=os.environ.get('ARCBOX_VOICE_QA_MODELS',str(repo/'.cache/voice-qa/models')))
if sys.argv[1]=='audition':
 sources=[('audition',base/'audition-kore-source.wav')];dest=base/'batch/local-transcripts'
else:
 rows=json.loads((base/'batch/lines.json').read_text());sources=list({j['raw']:(j['raw'],base/'batch/raw'/(j['raw']+'.wav')) for j in rows}.values());dest=base/'batch/local-transcripts'
if model_name!='small.en':dest=base/('batch/local-transcripts-'+model_name)
if sys.argv[1]=='confirm':
 selected=json.loads((base/'batch/confirmation-lines.json').read_text());sources=[(raw,p) for raw,p in sources if raw in selected]
dest.mkdir(exist_ok=True)
for i,(raw,source) in enumerate(sources,1):
 if not source.exists():print('PENDING source',raw,flush=True);continue
 sha=hashlib.sha256(source.read_bytes()).hexdigest();out=dest/(raw+'.json')
 if out.exists() and json.loads(out.read_text()).get('sourceSha256')==sha:continue
 start=time.monotonic();segments,info=model.transcribe(str(source),language='en',beam_size=5,temperature=0,vad_filter=False,condition_on_previous_text=False)
 parts=[dict(start=s.start,end=s.end,text=s.text) for s in segments]
 d=dict(model='Systran/faster-whisper-'+model_name,engine='faster-whisper1.2.1 / CTranslate2 CPU int8',beamSize=5,temperature=0,vadFilter=False,conditionOnPreviousText=False,initialPrompt=None,source=source.name,sourceSha256=sha,duration=info.duration,segments=parts,text=' '.join(s['text'].strip() for s in parts),secondsToTranscribe=round(time.monotonic()-start,3))
 out.write_text(json.dumps(d,indent=2)+'\n');print(i,len(sources),raw,d['text'],flush=True)
