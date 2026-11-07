"""Physical cuts, thrusts, sweeps, shield braces, orders and recovery. Base indices stay fixed."""
from parts.rig import Pose,frame_list
CASTS={
 'fighter_cut':(20,False,[Pose(lean=-1,weapon=-50,hand_n=(-2,-4)),Pose(lean=-2,crouch=1,weapon=-80,hand_n=(-3,-7)),Pose(lean=3,weapon=65,hand_n=(7,-6),hand_f=(1,-2),expression='angry'),Pose(lean=1,weapon=105,hand_n=(5,0))]),
 'fighter_thrust':(20,False,[Pose(lean=-1,weapon=20,hand_n=(-2,-2)),Pose(crouch=1,lean=-2,weapon=25,hand_n=(-4,-5)),Pose(lean=3,weapon=90,hand_n=(10,-5),hand_f=(1,-2)),Pose(lean=1,weapon=65,hand_n=(5,-3))]),
 'fighter_sweep':(20,False,[Pose(crouch=1,lean=-1,weapon=-55,hand_n=(-2,-4)),Pose(crouch=2,lean=-2,weapon=-100,hand_n=(-4,-6)),Pose(crouch=1,lean=3,weapon=105,hand_n=(10,-4),smear=-30),Pose(lean=1,weapon=140,hand_n=(4,1))]),
 'fighter_guard':(20,False,[Pose(crouch=1,weapon=25,hand_f=(4,-2)),Pose(crouch=2,weapon=35,hand_f=(8,-5)),Pose(crouch=2,lean=1,weapon=45,hand_f=(10,-7)),Pose(crouch=1,weapon=25,hand_f=(6,-4))]),
 'fighter_command':(20,False,[Pose(weapon=-15,hand_n=(1,-3),hand_f=(0,-2)),Pose(weapon=-45,hand_n=(2,-7),hand_f=(3,-5)),Pose(lean=1,weapon=-65,hand_n=(3,-10),hand_f=(9,-8),head=(0,-1)),Pose(lean=1,weapon=-35,hand_n=(2,-7),hand_f=(6,-5))]),
 'fighter_breath':(20,False,[Pose(crouch=2,head=(0,1),weapon=30,hand_f=(-2,-1)),Pose(crouch=2,weapon=35,hand_f=(-1,-4)),Pose(head=(0,-1),weapon=20,hand_f=(0,-6)),Pose(weapon=10,hand_f=(0,-3))])}
CASTS['fighter_release']=(20,False,[CASTS['fighter_cut'][2][-1]])
FALLBACKS={'fighter_cut':'attack','fighter_thrust':'thrust','fighter_sweep':'spin','fighter_guard':'block','fighter_command':'wave','fighter_breath':'buff','fighter_release':'attack'}
def poses_for(role):
 base=frame_list();return base+[(name,i,p) for name,(_,_,poses) in CASTS.items() for i,p in enumerate(poses)] if role=='fighter' else base
def manifest():
 poses=poses_for('fighter');return {'framesPerDirection':len(poses),'anims':{name:{'frames':[i for i,(a,_,_) in enumerate(poses) if a==name],'fps':fps,'loop':loop} for name,(fps,loop,_) in CASTS.items()}}
def classic_aliases(anims):
 for name,fallback in FALLBACKS.items():anims[name]=dict(anims[fallback])
 return anims
