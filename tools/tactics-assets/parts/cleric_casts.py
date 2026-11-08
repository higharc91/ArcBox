"""Cleric prayer, restoration, judgement and consecrated weapon poses."""
from parts.rig import Pose,frame_list
CASTS={
 'cleric_prayer':(14,False,[Pose(hand_n=(-3,-3),hand_f=(3,-3),weapon=-10),Pose(crouch=1,hand_n=(-2,-7),hand_f=(2,-7),head=(0,1),weapon=-15),Pose(hand_n=(-5,-11),hand_f=(5,-11),head=(0,-1),weapon=-20),Pose(hand_n=(-7,-9),hand_f=(7,-9),weapon=20),Pose(hand_n=(-4,-6),hand_f=(4,-6),weapon=10)]),
 'cleric_restore':(16,False,[Pose(lean=-1,hand_n=(-3,-2),weapon=-20),Pose(crouch=1,hand_n=(-2,-6),hand_f=(2,-4),weapon=-10),Pose(lean=2,hand_n=(7,-5),hand_f=(-2,-3),weapon=45),Pose(lean=2,hand_n=(9,-3),hand_f=(-3,-2),weapon=55),Pose(lean=1,hand_n=(6,-3),weapon=35)]),
 'cleric_judgement':(12,False,[Pose(crouch=1,hand_n=(-3,-4),weapon=-20),Pose(hand_n=(-4,-11),hand_f=(3,-6),head=(0,-1),weapon=-35),Pose(hand_n=(-2,-14),hand_f=(6,-9),head=(0,-1),weapon=0),Pose(lean=2,hand_n=(9,-8),hand_f=(-4,-5),weapon=65,expression='angry'),Pose(lean=1,hand_n=(7,-5),weapon=45)]),
 'cleric_strike':(16,False,[Pose(lean=-1,hand_n=(-4,-3),weapon=-35),Pose(crouch=1,hand_n=(-5,-8),hand_f=(2,-3),weapon=-75),Pose(lean=2,hand_n=(7,-7),hand_f=(-3,-2),weapon=70,expression='angry'),Pose(lean=2,hand_n=(9,1),weapon=130,drag=(-2,1)),Pose(lean=1,hand_n=(5,-2),weapon=65)])}
FALLBACKS={'cleric_prayer':'pray','cleric_restore':'buff','cleric_judgement':'summon','cleric_strike':'attack'}
def poses_for(role):
 base=frame_list()
 return base+[(name,i,p) for name,(_,_,poses) in CASTS.items() for i,p in enumerate(poses)] if role=='cleric' else base
def manifest():
 poses=poses_for('cleric')
 return {'framesPerDirection':len(poses),'anims':{name:{'frames':[i for i,(a,_,_) in enumerate(poses) if a==name],'fps':fps,'loop':loop} for name,(fps,loop,_) in CASTS.items()}}
def classic_aliases(anims):
 for name,fallback in FALLBACKS.items():anims[name]=dict(anims[fallback])
 return anims
