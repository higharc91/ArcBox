"""Wizard-only close-up poses, appended after the stable production frame indices."""
from parts.rig import Pose,frame_list

CASTS = {
 'wizard_bolt':(14,False,[
  Pose(crouch=1,hand_n=(-1,-1),hand_f=(-3,-2),weapon=10),
  Pose(lean=-1,hand_n=(-3,-4),hand_f=(-2,-4),weapon=-15,drag=(1,0)),
  Pose(lean=1,hand_n=(4,-5),hand_f=(1,-3),weapon=50),
  Pose(lean=2,hand_n=(7,-5),hand_f=(2,-2),weapon=72,expression='angry'),
  Pose(lean=1,hand_n=(5,-4),hand_f=(0,-1),weapon=55,drag=(-1,1)),Pose()]),
 'wizard_weave':(12,False,[
  Pose(hand_n=(-2,-3),hand_f=(2,-3),weapon=5),
  Pose(hand_n=(-3,-6),hand_f=(3,-5),weapon=-10),
  Pose(crouch=1,hand_n=(-2,-8),hand_f=(2,-7),weapon=-20,head=(0,1)),
  Pose(hand_n=(2,-10),hand_f=(-2,-9),weapon=0,drag=(0,1)),
  Pose(lean=1,hand_n=(5,-8),hand_f=(-4,-7),weapon=35),
  Pose(lean=2,hand_n=(7,-5),hand_f=(-6,-5),weapon=60),
  Pose(lean=1,hand_n=(3,-3),hand_f=(-3,-3),weapon=35,drag=(-1,0)),Pose()]),
 'wizard_ward':(12,False,[
  Pose(crouch=1,hand_n=(-2,-2),hand_f=(1,-2),weapon=0),
  Pose(hand_n=(-4,-5),hand_f=(3,-5),weapon=-12),
  Pose(lean=-1,hand_n=(-3,-7),hand_f=(5,-8),weapon=-15),
  Pose(lean=-1,hand_n=(-2,-5),hand_f=(7,-6),weapon=-8),
  Pose(hand_n=(0,-3),hand_f=(4,-4),weapon=10),Pose()]),
 'wizard_nova':(12,False,[
  Pose(crouch=2,hand_n=(-3,-2),hand_f=(2,-2),head=(0,1),weapon=-15),
  Pose(crouch=1,hand_n=(-4,-6),hand_f=(3,-6),weapon=-25),
  Pose(hand_n=(-3,-10),hand_f=(3,-10),weapon=-10),
  Pose(hand_n=(1,-13),hand_f=(-1,-12),weapon=0,head=(0,-1),drag=(0,1)),
  Pose(lean=1,hand_n=(5,-12),hand_f=(-5,-11),weapon=30),
  Pose(crouch=1,lean=2,hand_n=(8,-7),hand_f=(-7,-7),weapon=65,expression='angry'),
  Pose(hand_n=(4,-4),hand_f=(-4,-4),weapon=40,drag=(-1,1)),Pose()]),
 'wizard_stomp':(14,False,[
  Pose(crouch=1,hand_n=(0,-3),hand_f=(-3,-2),weapon=0),
  Pose(hand_n=(-2,-10),hand_f=(-4,-6),weapon=-10,foot_n=(2,2)),
  Pose(hand_n=(-1,-12),hand_f=(-4,-8),weapon=0,foot_n=(2,3)),
  Pose(crouch=2,lean=1,hand_n=(3,3),hand_f=(-5,-2),weapon=15,expression='angry'),
  Pose(crouch=1,hand_n=(2,1),hand_f=(-3,-1),weapon=20,drag=(0,1)),Pose()]),
}
FALLBACKS={'wizard_bolt':'point','wizard_weave':'channel','wizard_ward':'buff','wizard_nova':'summon','wizard_stomp':'slam'}

def poses_for(role):
    base=frame_list()
    if role!='wizard':return base
    return base+[(name,i,p) for name,(_,_,poses) in CASTS.items() for i,p in enumerate(poses)]

def manifest():
    poses=poses_for('wizard')
    return {'framesPerDirection':len(poses),'anims':{name:{'frames':[i for i,(a,_,_) in enumerate(poses) if a==name],'fps':fps,'loop':loop}
        for name,(fps,loop,_) in CASTS.items()}}

def classic_aliases(anims):
    for name,fallback in FALLBACKS.items():anims[name]=dict(anims[fallback])
    return anims
