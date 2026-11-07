"""Warlock pact signs, lash, breach and blade gestures; stable base indices."""
from parts.rig import Pose,frame_list
CASTS={
 'warlock_pact':(14,False,[Pose(hand_n=(-4,-2),hand_f=(2,-2),weapon=-10),Pose(lean=-1,hand_n=(-4,-7),hand_f=(4,-6),weapon=-20),Pose(hand_n=(4,-10),hand_f=(-2,-5),head=(0,-1),weapon=20),Pose(lean=1,hand_n=(7,-6),hand_f=(-2,-3),weapon=45),Pose(hand_n=(4,-4),hand_f=(-1,-2),weapon=30)]),
 'warlock_lash':(16,False,[Pose(lean=-2,hand_n=(-5,-3),hand_f=(-2,-2),weapon=-25),Pose(crouch=1,hand_n=(-3,-8),hand_f=(2,-4),weapon=-35),Pose(lean=2,hand_n=(8,-5),hand_f=(-3,-3),weapon=65,expression='angry'),Pose(lean=2,hand_n=(9,-4),hand_f=(-3,-2),weapon=70),Pose(lean=1,hand_n=(7,-4),weapon=50)]),
 'warlock_rift':(12,False,[Pose(crouch=2,hand_n=(-3,-2),hand_f=(3,-2),weapon=-15),Pose(lean=-1,hand_n=(-6,-8),hand_f=(6,-8),weapon=-25),Pose(hand_n=(-7,-12),hand_f=(7,-12),head=(0,-1),weapon=10),Pose(lean=2,hand_n=(9,-7),hand_f=(-9,-7),weapon=65,expression='angry'),Pose(lean=1,hand_n=(7,-5),hand_f=(-7,-5),weapon=50)]),
 'warlock_blade':(16,False,[Pose(lean=-1,hand_n=(-4,-2),weapon=-30),Pose(crouch=1,hand_n=(-5,-7),hand_f=(2,-3),weapon=-65),Pose(lean=2,hand_n=(8,-7),hand_f=(-3,-1),weapon=75,expression='angry'),Pose(lean=2,hand_n=(8,1),weapon=125,drag=(-2,1)),Pose(lean=1,hand_n=(5,-2),weapon=65)]),
 'warlock_release':(16,False,[Pose(lean=2,hand_n=(9,-4),hand_f=(-3,-2),weapon=70)])}
FALLBACKS={'warlock_pact':'channel','warlock_lash':'point','warlock_rift':'summon','warlock_blade':'attack','warlock_release':'point'}
def poses_for(role):
 base=frame_list()
 return base+[(name,i,p) for name,(_,_,poses) in CASTS.items() for i,p in enumerate(poses)] if role=='warlock' else base
def manifest():
 poses=poses_for('warlock')
 return {'framesPerDirection':len(poses),'anims':{name:{'frames':[i for i,(a,_,_) in enumerate(poses) if a==name],'fps':fps,'loop':loop} for name,(fps,loop,_) in CASTS.items()}}
def classic_aliases(anims):
 for name,fallback in FALLBACKS.items():anims[name]=dict(anims[fallback])
 return anims
