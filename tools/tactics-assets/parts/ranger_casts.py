"""Ranger bow release, commanding hand, living wards and a low retreat."""
from parts.rig import Pose,frame_list
CASTS={
 'ranger_draw':(20,False,[Pose(lean=-1,hand_n=(3,-4),hand_f=(4,-4),weapon=0),Pose(crouch=1,hand_n=(4,-5),hand_f=(7,-7),weapon=0,aim=True),Pose(lean=-1,hand_n=(5,-6),hand_f=(8,-8),weapon=0,aim=True),Pose(lean=-1,crouch=1,hand_n=(5,-6),hand_f=(8,-8),weapon=0,aim=True,expression='angry'),Pose(lean=1,hand_n=(6,-6),hand_f=(0,-5),weapon=0,drag=(-1,0))]),
 'ranger_volley':(20,False,[Pose(crouch=1,hand_n=(3,-5),hand_f=(4,-5),weapon=-12),Pose(hand_n=(4,-8),hand_f=(7,-9),weapon=-16,aim=True),Pose(lean=-1,hand_n=(5,-10),hand_f=(8,-11),weapon=-18,aim=True),Pose(lean=-1,hand_n=(5,-10),hand_f=(8,-11),weapon=-18,aim=True,head=(0,-1)),Pose(lean=1,hand_n=(7,-9),hand_f=(0,-7),weapon=-10,drag=(-1,0))]),
 'ranger_command':(16,False,[Pose(hand_n=(1,-2),hand_f=(1,-3),weapon=10),Pose(hand_n=(1,-3),hand_f=(3,-8),weapon=5,head=(0,-1)),Pose(lean=1,hand_f=(7,-9),weapon=5),Pose(lean=2,hand_f=(10,-6),weapon=10),Pose(lean=1,hand_f=(6,-4),weapon=15)]),
 'ranger_ward':(16,False,[Pose(hand_n=(0,-2),hand_f=(-1,-3),weapon=20),Pose(crouch=1,hand_n=(0,-3),hand_f=(1,-6),weapon=15),Pose(hand_n=(1,-4),hand_f=(4,-10),head=(0,-1),weapon=10),Pose(lean=1,hand_f=(8,-7),weapon=20),Pose(hand_f=(5,-4),weapon=25)]),
 'ranger_vanish':(20,False,[Pose(lean=-1,crouch=1,hand_n=(0,-2),hand_f=(-2,-2),weapon=10),Pose(lean=-2,crouch=3,hand_n=(-2,-4),hand_f=(-3,-5),weapon=-5,drag=(1,-1)),Pose(shift=-1,lean=-2,crouch=4,hand_n=(-3,-5),hand_f=(-4,-5),weapon=-10,drag=(2,-1)),Pose(shift=-1,lean=-1,crouch=2,hand_n=(-1,-3),weapon=0,drag=(1,0)),Pose(crouch=1,hand_n=(0,-2),weapon=15)])}
CASTS['ranger_release']=(20,False,[CASTS['ranger_draw'][2][-1]])
FALLBACKS={'ranger_draw':'shoot','ranger_volley':'shoot','ranger_command':'point','ranger_ward':'buff','ranger_vanish':'dodge','ranger_release':'shoot'}
def poses_for(role):
 base=frame_list();return base+[(name,i,p) for name,(_,_,poses) in CASTS.items() for i,p in enumerate(poses)] if role=='ranger' else base
def manifest():
 poses=poses_for('ranger');return {'framesPerDirection':len(poses),'anims':{name:{'frames':[i for i,(a,_,_) in enumerate(poses) if a==name],'fps':fps,'loop':loop} for name,(fps,loop,_) in CASTS.items()}}
def classic_aliases(anims):
 for name,fallback in FALLBACKS.items():anims[name]=dict(anims[fallback])
 return anims
