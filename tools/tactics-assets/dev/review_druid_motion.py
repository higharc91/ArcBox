"""Compare the previous Druid decorations with pose-keyed art in synchronized loops."""
import argparse,json,shutil,subprocess,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from PIL import Image
from arcpix.out import PACK
from parts.druid_forms import FORMS
ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'docs/superpowers/reports/combat-vfx/druid/forms/motion'

def main():
 parser=argparse.ArgumentParser();parser.add_argument('--before-ref',default='973287973e1b43ea0d82db5c02e84c4c927adef7');args=parser.parse_args();OUT.mkdir(parents=True,exist_ok=True)
 manifest=json.loads((PACK/'creatures/manifest.json').read_text())['sprites'];entries={}
 for form in FORMS:
  entry=manifest['druid_'+form];entries[form]=entry
  path='content/tactics/base/creatures/'+entry['file']
  (OUT/('before-'+form+'.png')).write_bytes(subprocess.check_output(['git','show',args.before_ref+':'+path],cwd=ROOT))
  shutil.copyfile(PACK/'creatures'/entry['file'],OUT/('after-'+form+'.png'))
 with Image.open(PACK/'tactical/human_druid.png') as hero:hero.crop((0,0,160,160)).save(OUT/'player.png',optimize=True)
 template=Path(__file__).with_name('druid_motion.html').read_text()
 (OUT/'index.html').write_text(template.replace('/* MANIFEST */',json.dumps(entries)).replace('BEFORE_REF',args.before_ref))
 print('Exported synchronized previous/revised loops for all four forms, eight facings and every action.')
if __name__=='__main__':main()
