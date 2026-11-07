"""Remove only empty pixels; preserve world anchors, beam reach and projectile centres."""
def pack(images, anchor, attach, padding=2):
    w,h=images[0].size
    boxes=[im.getchannel('A').getbbox() for im in images]
    boxes=[b for b in boxes if b]
    if not boxes:return images,tuple(anchor),(0,0)
    left=max(0,min(b[0] for b in boxes)-padding);top=max(0,min(b[1] for b in boxes)-padding)
    right=min(w,max(b[2] for b in boxes)+padding);bottom=min(h,max(b[3] for b in boxes)+padding)
    if attach=='projectile':
        # Flight centres its frame, so trimming must be symmetric on both axes.
        left=min(left,w-right);right=w-left
        top=min(top,h-bottom);bottom=h-top
    elif anchor[0]*6<=w:
        # Runtime beam lighting uses width minus anchor X. Keep that reach exactly.
        left,right=0,w
    elif (anchor[0]-left)*6<=right-left:
        left,right=0,w
    return [im.crop((left,top,right,bottom)) for im in images],(anchor[0]-left,anchor[1]-top),(left,top)
