"""Lossless 8-bit indexed encoding for fixed-palette sprite atlases."""
from pathlib import Path
import io
import numpy as np
from PIL import Image

def exact_png_bytes(image):
 rgba=image.convert('RGBA');a=np.array(rgba);packed=a.reshape(-1,4).copy().view(np.uint32).reshape(-1);colors,indices=np.unique(packed,return_inverse=True);buf=io.BytesIO()
 if len(colors)<=256:
  values=colors.astype(np.uint32).view(np.uint8).reshape(-1,4);indexed=Image.fromarray(indices.reshape(a.shape[:2]).astype(np.uint8));indexed.putpalette(values[:,:3].flatten().tolist()+[0]*(768-len(values)*3));indexed.info['transparency']=bytes(values[:,3]);indexed.save(buf,format='PNG',bits=8,optimize=True,compress_level=9)
 else:rgba.save(buf,format='PNG',optimize=True,compress_level=9)
 result=buf.getvalue()
 with Image.open(io.BytesIO(result)) as check:
  if check.convert('RGBA').tobytes()!=rgba.tobytes():raise ValueError('Lossless atlas encoding changed pixels.')
 return result

def save_exact_png(image,path):Path(path).write_bytes(exact_png_bytes(image))

def portrait_rect(image):
 """A bounded square around the first rest pose, excluding animation padding."""
 bb=image.convert('RGBA').getbbox();side=min(max(bb[2]-bb[0],bb[3]-bb[1])+4,image.width,image.height);x=max(0,min(image.width-side,(bb[0]+bb[2]-side)//2));y=max(0,min(image.height-side,(bb[1]+bb[3]-side)//2));return [x,y,side,side]
