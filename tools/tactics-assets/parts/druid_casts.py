"""Druid root-weaving, wind, healing, lash and animal-coil gestures."""
from parts.rig import Pose,frame_list
CASTS={
 'druid_roots':(14,False,[Pose(crouch=1,hand_n=(-3,0),weapon=-15),Pose(crouch=3,lean=1,hand_n=(3,4),hand_f=(-3,3),weapon=25),Pose(crouch=1,hand_n=(-6,-6),hand_f=(6,-6),weapon=0),Pose(hand_n=(-8,-10),hand_f=(8,-10),head=(0,-1),weapon=-20),Pose(hand_n=(-6,-7),hand_f=(6,-7),weapon=-10)]),
 'druid_gale':(14,False,[Pose(lean=-2,hand_n=(-5,-3),hand_f=(-1,-2),weapon=-25),Pose(hand_n=(-6,-10),hand_f=(6,-8),weapon=-35),Pose(lean=2,hand_n=(9,-8),hand_f=(-6,-7),weapon=55),Pose(lean=2,hand_n=(10,-4),hand_f=(-8,-3),weapon=70),Pose(lean=1,hand_n=(6,-4),weapon=40)]),
 'druid_mend':(16,False,[Pose(hand_n=(-3,-2),hand_f=(3,-2),weapon=-15),Pose(crouch=1,hand_n=(-3,-6),hand_f=(3,-6),head=(0,1),weapon=-25),Pose(lean=1,hand_n=(5,-7),hand_f=(-1,-4),weapon=30),Pose(lean=2,hand_n=(8,-4),hand_f=(-3,-2),weapon=55),Pose(lean=1,hand_n=(6,-3),weapon=35)]),
 'druid_lash':(18,False,[Pose(lean=-2,hand_n=(-5,-3),weapon=-30),Pose(crouch=1,hand_n=(-5,-8),hand_f=(2,-3),weapon=-55),Pose(lean=2,hand_n=(9,-6),hand_f=(-3,-2),weapon=70),Pose(lean=2,hand_n=(10,-2),weapon=100),Pose(lean=1,hand_n=(5,-3),weapon=40)]),
 'druid_shape':(14,False,[Pose(crouch=1,hand_n=(-3,-2),hand_f=(3,-2),weapon=-15),Pose(crouch=4,lean=-1,hand_n=(-5,1),hand_f=(5,1),head=(0,2),weapon=-30),Pose(crouch=3,lean=1,hand_n=(-7,-3),hand_f=(7,-3),weapon=15,expression='angry'),Pose(crouch=1,lean=2,hand_n=(8,-6),hand_f=(-7,-5),head=(0,-1),weapon=60),Pose(lean=1,hand_n=(6,-4),hand_f=(-5,-3),weapon=45)])}
FALLBACKS={'druid_roots':'summon','druid_gale':'cast','druid_mend':'buff','druid_lash':'point','druid_shape':'channel'}
def poses_for(role):
 base=frame_list()
 return base+[(name,i,p) for name,(_,_,poses) in CASTS.items() for i,p in enumerate(poses)] if role=='druid' else base
def manifest():
 poses=poses_for('druid')
 return {'framesPerDirection':len(poses),'anims':{name:{'frames':[i for i,(a,_,_) in enumerate(poses) if a==name],'fps':fps,'loop':loop} for name,(fps,loop,_) in CASTS.items()}}
def classic_aliases(anims):
 for name,fallback in FALLBACKS.items():anims[name]=dict(anims[fallback])
 return anims
