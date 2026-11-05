import subprocess,os,sys,select,json
from pathlib import Path
production='--production' in sys.argv[2:]
root=Path('/workspace/ArcBox');report=root/'docs/superpowers/reports/2026-11-03-townsfolk-art/old-maud-imagegen';report=report/'production' if production else report;report.mkdir(exist_ok=True)
region=sys.argv[1];env=os.environ.copy();env.update(VK_ICD_FILENAMES='/workspace/.arcbox/vulkan/lvp-local.json',LP_NUM_THREADS='2',LD_LIBRARY_PATH='/workspace/.arcbox/system/root/usr/lib/x86_64-linux-gnu:'+env.get('LD_LIBRARY_PATH',''),ARCBOX_TACTICS_ART_ROOT=str(root/'content/tactics/base') if production else '/workspace/artifacts/issue80-old-maud-review')
r,w=os.pipe();xlog=open(report/(region+'-xvfb.log'),'w');x=subprocess.Popen(['/workspace/.arcbox/bin/Xvfb','-displayfd',str(w),'-screen','0','1280x720x24','-nolisten','tcp'],env=env,pass_fds=(w,),stdout=xlog,stderr=xlog);os.close(w)
try:
 if not select.select([r],[],[],10)[0]:raise RuntimeError('Xvfb did not become ready')
 display=os.read(r,100).decode().strip();env['DISPLAY']=':'+display
 shots=report/(region+'-shots');shots.mkdir(exist_ok=True)
 if region=='ashen_ford':looks='old-maud@130,90@128,90'
 else:
  start=json.load(open(root/f'content/tactics/base/rules/world/{region}.json'))['spawns']['start'];looks=f'start@{start[0]},{start[1]}@{start[0]},{start[1]}'
 args=['/workspace/.arcbox/bin/godot','--path',str(root/'src/ArcBox.Host'),'--rendering-method','mobile','--rendering-driver','vulkan','--audio-driver','Dummy','--','--world-demo='+region,'--world-looks='+looks,'--shots='+str(shots),'--perf','--preset=low','--res=1280x720','--quit-after=55']
 with open(report/(region+'-native.log'),'w') as f:result=subprocess.run(args,env=env,stdout=f,stderr=subprocess.STDOUT,timeout=100)
 print(json.dumps({'region':region,'exit':result.returncode,'log':str(report/(region+'-native.log'))}),flush=True)
 sys.exit(result.returncode)
finally:x.terminate();x.wait(timeout=5)
