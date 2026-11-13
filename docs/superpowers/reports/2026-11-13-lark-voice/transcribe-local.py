from pathlib import Path
import json,hashlib,time,sys
from faster_whisper import WhisperModel
source=Path(sys.argv[1])
model=WhisperModel('small.en',device='cpu',compute_type='int8',cpu_threads=4,num_workers=1,download_root=str(Path.home()/'.cache/arcbox-voice-qa-models'))
start=time.monotonic()
segments,info=model.transcribe(str(source),language='en',beam_size=5,temperature=0,vad_filter=False,condition_on_previous_text=False,word_timestamps=True)
rows=[]
for s in segments:
 rows.append({'start':s.start,'end':s.end,'text':s.text,'words':[{'start':w.start,'end':w.end,'word':w.word,'probability':w.probability} for w in s.words]})
result={'source':source.name,'sourceSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'model':'Systran/faster-whisper-small.en','engine':'faster-whisper 1.2.1 / CTranslate2 CPU int8','initialPrompt':None,'language':'en','beamSize':5,'temperature':0,'vadFilter':False,'conditionOnPreviousText':False,'wordTimestamps':True,'duration':info.duration,'segments':rows,'secondsToTranscribe':round(time.monotonic()-start,3)}
Path(sys.argv[2]).write_text(json.dumps(result,indent=2)+'\n')
print(' '.join(s['text'].strip() for s in rows))
