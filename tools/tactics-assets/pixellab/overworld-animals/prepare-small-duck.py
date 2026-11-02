import base64,io,json
from pathlib import Path
from PIL import Image
R=Path(__file__).resolve().parent
def main():
 data=json.loads((R/'canonical-layout.json').read_text());c=next(c for c in data['families'] if c['id']=='duck');im=Image.open(R/'duck/swim-front.png').convert('RGBA');im=im.crop(im.getbbox());im=im.crop((0,0,min(32,im.width),min(24,im.height)));buf=io.BytesIO();im.save(buf,format='PNG');qs=[]
 for d in data['directions']:
  face={'south':'TRUE front view toward viewer; yellow bill centered.','north':'TRUE REAR view away from viewer: back of green head and folded back feathers, bill and face hidden.','west':'Left-facing side profile, yellow bill points LEFT.','east':'Right-facing side profile, yellow bill points RIGHT.','south-west':'Three-quarter front view looking down-left.','south-east':'Three-quarter front view looking down-right.','north-west':'Three-quarter REAR view looking away up-left, back and folded wings visible.','north-east':'Three-quarter REAR view looking away up-right, back and folded wings visible.'}[d]
  desc='ONE very SMALL swimming male mallard duck, '+d+' view. '+face+' Low top-down pixel RPG camera. Green head, small yellow bill, chestnut chest, cream collar, folded brown/grey feathered wings with a blue patch. WHOLE duck only 18 pixels TALL and 26 pixels LONG maximum in this 32x24 frame. At least 3 transparent pixels above and below. Horizontal swimming pose, BOTH FEET HIDDEN. Small compact body, not an upright goose or giant. Fine 1-pixel outline, stepped top-left shading. No actual water, ripples, shadow, bubbles, scenery, other animals, symbols or effects. ONE complete bird with intact bill, folded wings and tail.'
  qs.append({'key':'duck/small/'+d,'name':'create_image_pro_flash','arguments':{'description':desc,'width':32,'height':24,'no_background':True,'style_image':{'base64':base64.b64encode(buf.getvalue()).decode(),'usage_description':'Duck identity, palette and native pixel style only. Redraw a whole SMALL bird in the requested facing.'},'palette':c['palette'],'max_colors':32,'seed':212000+len(qs)}})
 Path('/workspace/requests-duck-small.json').write_text(json.dumps(qs));print('Prepared eight small duck facings.')
if __name__=='__main__':main()
