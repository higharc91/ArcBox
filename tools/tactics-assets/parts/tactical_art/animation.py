"""Articulate the tactical layers using the existing, timed production pose rig.

The larger padded frame keeps raised weapons, jumps and lying poses inside the
atlas. Pixels are nearest-neighbour sampled, never filtered. Atlas dimensions
are independent of the character's world footprint.
"""
from functools import lru_cache
import math
import numpy as np
from PIL import Image
from arcpix.canvas import Canvas,FLAG_OUTLINE
from arcpix.palette import OUTLINE
from parts.characters import SLOTS
from parts.rig import Pose
from .roster import character
from .attachments import ATTACHMENTS,HAND_POINTS,SHOULDERS
from .races import fit_point
from .materials import SHADOW_LAYERS,contact_shadow

FRAME=(160,160)
ANCHOR=(80,123)
OFFSET=(48,48)
COLUMNS=24

def translate(x,y):
    return np.array([[1.,0,x],[0,1.,y],[0,0,1.]])

def rotate(degrees,pivot):
    a=math.radians(degrees);c,s=math.cos(a),math.sin(a)
    return translate(*pivot)@np.array([[c,-s,0],[s,c,0],[0,0,1.]])@translate(-pivot[0],-pivot[1])

@lru_cache(maxsize=264)
def rest(role,race,back):
    c,p,layers=character(role,race,back)
    return p,{name:layer.ramp_map() for name,layer in layers.items() if layer.slot.any()}

def transform(name,pose,race=None):
    drop=(6 if pose.kneel else 10 if pose.sit else 2*pose.crouch)
    torso=(pose.lean*2,pose.bob*2+drop-pose.rise*2)
    head=(torso[0]+pose.head[0]*2,torso[1]+pose.head[1]*2)
    if name in ('head','hair','headgear','class-headwear','race-details'):
        motion=translate(*head)
        # The dwarf's cropped hair is attached to the skull. Applying cloth
        # trailing to the whole hair layer made it slide against the face/beard.
        if name=='hair' and race!='dwarf':motion=motion@translate(pose.drag[0],pose.drag[1])
    elif name in ('weapon','near-hand','far-arm','offhand'):
        near=name in ('weapon','near-hand')
        offset=pose.hand_n if near else pose.hand_f
        rest_offset=(1,0) if near else (-1,0)
        hand=(torso[0]+(offset[0]-rest_offset[0])*1.5,torso[1]+offset[1]*1.5)
        pivot=(44,51) if near else (19,48)
        if name=='weapon':motion=translate(*hand)@rotate(pose.weapon-25,pivot)
        elif name=='offhand':motion=translate(*hand)
        else:
            shoulder=(40,35) if near else (23,34)
            dx,dy=pivot[0]-shoulder[0],pivot[1]-shoulder[1]
            tx,ty=dx+hand[0]-torso[0],dy+hand[1]-torso[1]
            angle=math.degrees(math.atan2(ty,tx)-math.atan2(dy,dx))
            length=math.hypot(tx,ty)/math.hypot(dx,dy)
            motion=translate(*torso)@translate(*shoulder)@rotate(angle,(0,0))@np.diag([1,length,1])@translate(-shoulder[0],-shoulder[1])
    elif name=='legs':
        motion=translate(0,-pose.rise*2)
    else:
        # Torso leans while the hip stays planted; cloth trails the movement.
        shear=pose.lean/10
        motion=translate(0,torso[1])@translate(32,50)@np.array([[1.,-shear,0],[0,1.,0],[0,0,1.]])@translate(-32,-50)
        if 'cape' in name or 'tail' in name:motion=translate(*pose.drag)@motion
    whole=translate(*OFFSET)@translate(pose.shift*2,0)@rotate(-pose.tilt,(32,75))
    return whole@motion

def warp(image,matrix):
    a,b,c=matrix[0];d,e,f=matrix[1]
    det=a*e-b*d
    inverse=(e/det,-b/det,(b*f-e*c)/det,-d/det,a/det,(d*c-a*f)/det)
    return image.transform(FRAME,Image.Transform.AFFINE,inverse,Image.Resampling.NEAREST)

def world_transform(pose):
    return translate(*OFFSET)@translate(pose.shift*2,0)@rotate(-pose.tilt,(32,75))

def hand_target(role,side,pose,race,back=False):
    """World position of the actual palm socket, including race fit and pose."""
    if role=='bard' and side=='far':
        # The plucking hand follows the same rigid lute as the neck hand.
        layer='instrument-back' if back else 'weapon'
        return item_transform(role,layer,pose,race,back)@np.r_[fit_point((27,53),race),1.]
    definitions=ATTACHMENTS[role]
    layer='weapon' if side=='near' else 'offhand'
    if role=='bard' and back and side=='near':layer='instrument-back'
    definition=definitions.get(layer)
    socket=definition.grip if definition else HAND_POINTS[side]
    offset=pose.hand_n if side=='near' else pose.hand_f
    default=(1,0) if side=='near' else (-1,0)
    drop=6 if pose.kneel else 10 if pose.sit else 2*pose.crouch
    delta=np.array([pose.lean*2+(offset[0]-default[0])*1.5,
        pose.bob*2+drop-pose.rise*2+offset[1]*1.5])
    return world_transform(pose)@np.r_[fit_point(socket,race)+delta,1.]

@lru_cache(maxsize=32768)
def arm_transform(role,side,pose,race,back=False):
    shoulder=fit_point(SHOULDERS[side],race)
    grip=fit_point(HAND_POINTS[side],race)
    target_world=hand_target(role,side,pose,race,back)
    world=world_transform(pose)
    # Undo the whole-character rotation before solving the local limb.
    target=np.linalg.solve(world,target_world)[:2]
    drop=6 if pose.kneel else 10 if pose.sit else 2*pose.crouch
    shifted=shoulder+np.array([pose.lean*2,pose.bob*2+drop-pose.rise*2])
    v=grip-shoulder;w=target-shifted
    length=max(.01,np.linalg.norm(w))/np.linalg.norm(v)
    angle=math.atan2(w[1],w[0])-math.atan2(v[1],v[0])
    c,s=math.cos(angle),math.sin(angle)
    turn=np.array([[c,-s],[s,c]])
    axis=v/np.linalg.norm(v)
    # Stretch along the original arm axis, preserving its thickness. The old
    # screen-Y scaling did not map the wrist exactly onto the moving grip.
    linear=turn@(np.eye(2)+(length-1)*np.outer(axis,axis))
    affine=np.eye(3);affine[:2,:2]=linear;affine[:2,2]=shifted-linear@shoulder
    return world@affine

@lru_cache(maxsize=32768)
def item_transform(role,name,pose,race,back=False):
    definition=ATTACHMENTS[role][name]
    source=fit_point(definition.grip,race)
    target=hand_target(role,definition.hand,pose,race,back)
    angle=(pose.weapon-25 if definition.rotate else 0)-pose.tilt
    return translate(target[0],target[1])@rotate(angle,(0,0))@translate(-source[0],-source[1])

def layer_transform(role,name,pose,race,back=False):
    if name in ATTACHMENTS[role]:return item_transform(role,name,pose,race,back)
    if name in ('near-hand','far-arm'):
        return arm_transform(role,'near' if name=='near-hand' else 'far',pose,race,back)
    if name in ('near-palm','far-palm'):
        side='near' if name=='near-palm' else 'far'
        source=fit_point(HAND_POINTS[side],race)
        v=source-fit_point(SHOULDERS[side],race)
        moved=arm_transform(role,side,pose,race,back)[:2,:2]@v
        angle=math.degrees(math.atan2(moved[1],moved[0])-math.atan2(v[1],v[0]))
        target=hand_target(role,side,pose,race,back)
        return translate(target[0],target[1])@rotate(angle,(0,0))@translate(-source[0],-source[1])
    return transform(name,pose,race)

def validate_contacts(role,race,back,pose,posed):
    """Fail a bake on a detached held object, using actual rasterized coverage.

    One adjacent pixel is allowed for nearest-neighbour rotation/outline coverage;
    the check is local to the declared palm, not a blade touching some other limb.
    """
    for name,definition in ATTACHMENTS[role].items():
        if name not in posed or definition.kind!='held':continue
        arm='near-palm' if definition.hand=='near' else 'far-palm'
        target=hand_target(role,definition.hand,pose,race,back)
        x,y=np.rint(target[:2]).astype(int)
        x0,x1=max(0,x-4),min(FRAME[0],x+5)
        y0,y1=max(0,y-4),min(FRAME[1],y+5)
        palm=posed[arm][y0:y1,x0:x1]
        item=posed[name][y0:y1,x0:x1]
        padded=np.pad(item,1)
        touching=np.zeros_like(item)
        for dy in range(3):
            for dx in range(3):touching|=padded[dy:dy+item.shape[0],dx:dx+item.shape[1]]
        if not np.any(palm&touching):
            raise ValueError(f'Detached {race}/{role}/{name}, back={back}, grip=({x},{y}), pose={pose}')

@lru_cache(maxsize=132)
def colour_table(role,race):
    palette,_=rest(role,race,False)
    table=np.zeros((16,8,2,4),np.uint8)
    outline=np.array([int(OUTLINE[i:i+2],16) for i in (1,3,5)])
    for name,colours in palette.items():
        for shade,colour in enumerate(colours):
            rgb=np.array([int(colour[i:i+2],16) for i in (1,3,5)])
            table[SLOTS.id(name),shade,0]=[*rgb,255]
            table[SLOTS.id(name),shade,1]=[*(rgb*.4+outline*.6).astype(np.uint8),255]
    return table

def frame(role,race,back,pose):
    palette,layers=rest(role,race,back)
    out=Canvas(*FRAME,SLOTS)
    posed={}
    for name,image in layers.items():
        if name=='head' and not back and (pose.blink or pose.expression in ('shut','pain') or pose.kneel):
            data=np.array(image).copy()
            mask=data[:,:,0]==SLOTS.id('eyes')*16
            data[mask,0]=SLOTS.id('skin')*16;data[mask,1]=2*32
            image=Image.fromarray(data)
        if name=='legs':
            # Separate legs follow their own foot controls instead of sliding as a block.
            left=image.copy();right=image.copy()
            a=np.array(left).copy();a[:,32:]=0;left=Image.fromarray(a)
            a=np.array(right).copy();a[:,:32]=0;right=Image.fromarray(a)
            image=Image.new('RGBA',FRAME)
            for limb,offset,default in ((left,pose.foot_f,-1),(right,pose.foot_n,1)):
                image.alpha_composite(warp(limb,transform(name,pose,race)@translate((offset[0]-default)*1.5,-offset[1]*1.5)))
        else:
            motion=layer_transform(role,name,pose,race,back)
            image=warp(image,motion)
        data=np.array(image)
        mask=data[:,:,3]>0
        if name in SHADOW_LAYERS:
            opaque=mask&(data[:,:,0]!=SLOTS.id('magic')*16)
            contact_shadow(out,opaque)
        if name in ATTACHMENTS[role] or name in ('near-hand','far-arm','near-palm','far-palm'):
            posed[name]=mask
        out.slot[mask]=data[:,:,0][mask]//16
        out.shade[mask]=data[:,:,1][mask]//32
        out.flags[mask]=0
    validate_contacts(role,race,back,pose,posed)
    # Same selective-outline neighbor priority as Canvas.outline, vectorized for
    # the ~50,000 runtime frames rather than scanning the padded empty pixels.
    rim=np.zeros_like(out.slot)
    for dx,dy in ((1,0),(-1,0),(0,1),(0,-1)):
        neighbor=np.roll(out.slot,(-dy,-dx),(0,1))
        if dx==1:neighbor[:,-1]=0
        if dx==-1:neighbor[:,0]=0
        if dy==1:neighbor[-1]=0
        if dy==-1:neighbor[0]=0
        mask=(out.slot==0)&(rim==0)&(neighbor>0)
        rim[mask]=neighbor[mask]
    mask=rim>0;out.slot[mask]=rim[mask];out.shade[mask]=0;out.flags[mask]=FLAG_OUTLINE
    return Image.fromarray(colour_table(role,race)[out.slot,out.shade,out.flags&1])
