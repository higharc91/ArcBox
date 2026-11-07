"""Five-frame Sorcerer gestures: gather, fling, focus and draconic release."""
from parts.rig import Pose,frame_list
CASTS={
 'sorcerer_fling':(14,False,[Pose(lean=-1,hand_n=(-4,-2),hand_f=(-2,-3),weapon=-15),Pose(crouch=1,hand_n=(-3,-7),hand_f=(3,-5),weapon=-25),Pose(lean=2,hand_n=(8,-6),hand_f=(-3,-2),weapon=65,expression='angry'),Pose(lean=1,hand_n=(6,-4),hand_f=(-2,-1),weapon=50,drag=(-1,1)),Pose(hand_n=(4,-3),weapon=35)]),
 'sorcerer_surge':(12,False,[Pose(crouch=2,hand_n=(-3,-3),hand_f=(3,-3),weapon=-10),Pose(hand_n=(-5,-10),hand_f=(5,-10),weapon=-20,head=(0,-1)),Pose(lean=1,hand_n=(6,-12),hand_f=(-6,-12),weapon=30,expression='angry'),Pose(lean=2,hand_n=(8,-7),hand_f=(-8,-7),weapon=60,drag=(-1,1)),Pose(hand_n=(5,-5),hand_f=(-5,-5),weapon=40)]),
 'sorcerer_focus':(12,False,[Pose(hand_n=(-3,-3),hand_f=(3,-3),weapon=0),Pose(hand_n=(-4,-7),hand_f=(4,-7),weapon=-12),Pose(hand_n=(1,-10),hand_f=(-1,-10),weapon=5,head=(0,-1)),Pose(hand_n=(5,-7),hand_f=(-5,-7),weapon=35),Pose(hand_n=(3,-4),hand_f=(-3,-4),weapon=20)]),
 'sorcerer_breath':(12,False,[Pose(crouch=1,lean=-1,hand_n=(-3,-2),hand_f=(-1,-3),head=(-1,-1),weapon=-15),Pose(lean=-2,hand_n=(-4,-6),hand_f=(3,-6),head=(-2,-2),weapon=-25),Pose(lean=2,hand_n=(5,-7),hand_f=(-5,-7),head=(2,-1),weapon=45,expression='angry'),Pose(lean=2,hand_n=(8,-4),hand_f=(-7,-4),head=(2,0),weapon=65),Pose(lean=1,hand_n=(5,-3),hand_f=(-4,-3),weapon=40)])}
FALLBACKS={'sorcerer_fling':'point','sorcerer_surge':'summon','sorcerer_focus':'channel','sorcerer_breath':'cast'}
def poses_for(role):
    base=frame_list()
    return base+[(name,i,p) for name,(_,_,poses) in CASTS.items() for i,p in enumerate(poses)] if role=='sorcerer' else base

def manifest():
    poses=poses_for('sorcerer')
    return {'framesPerDirection':len(poses),'anims':{name:{'frames':[i for i,(a,_,_) in enumerate(poses) if a==name],'fps':fps,'loop':loop} for name,(fps,loop,_) in CASTS.items()}}
def classic_aliases(anims):
    for name,fallback in FALLBACKS.items():anims[name]=dict(anims[fallback])
    return anims
