"""Bard gestures keep the lute against its rigged hand and articulate the playing arm."""
from parts.rig import Pose,frame_list
CASTS={
 'bard_play':(20,False,[Pose(hand_n=(0,-2),hand_f=(4,-2),weapon=-5),Pose(crouch=1,hand_n=(-1,-3),hand_f=(6,-5),weapon=-8),Pose(lean=1,hand_n=(1,-2),hand_f=(5,1),weapon=4),Pose(lean=1,hand_n=(1,-2),hand_f=(6,-3),weapon=6),Pose(hand_n=(0,-2),hand_f=(4,-1),weapon=0)]),
 'bard_verse':(20,False,[Pose(lean=-1,hand_n=(-1,-2),hand_f=(2,-3),weapon=-5),Pose(hand_n=(-1,-3),hand_f=(4,-8),head=(0,-1),weapon=-8),Pose(lean=1,hand_f=(8,-9),head=(1,-1),weapon=5),Pose(lean=2,hand_f=(10,-5),weapon=8),Pose(lean=1,hand_f=(6,-3),weapon=4)]),
 'bard_crescendo':(16,False,[Pose(crouch=1,hand_f=(4,-2),weapon=-8),Pose(crouch=2,hand_n=(-1,-4),hand_f=(6,-8),weapon=-12),Pose(hand_n=(1,-6),hand_f=(8,-12),head=(0,-1),weapon=5),Pose(lean=2,hand_n=(2,-4),hand_f=(10,-8),weapon=12),Pose(lean=1,hand_f=(6,-5),weapon=6)]),
 'bard_lore':(20,False,[Pose(hand_n=(-1,-2),hand_f=(3,-3),weapon=-5),Pose(hand_f=(5,-9),head=(0,-1),weapon=-10),Pose(lean=1,hand_f=(9,-11),weapon=0),Pose(lean=2,hand_f=(11,-5),weapon=8),Pose(lean=1,hand_f=(7,-4),weapon=4)]),
 'bard_valor':(20,False,[Pose(lean=-1,hand_n=(-2,-2),hand_f=(4,-2),weapon=-12),Pose(crouch=2,hand_n=(-2,-4),hand_f=(6,-5),weapon=-18),Pose(lean=2,hand_n=(3,-2),hand_f=(7,1),weapon=16,expression='angry'),Pose(lean=2,hand_n=(4,0),hand_f=(8,-2),weapon=22),Pose(lean=1,hand_n=(2,-1),hand_f=(5,-2),weapon=10)])}
FALLBACKS={'bard_play':'channel','bard_verse':'talk','bard_crescendo':'summon','bard_lore':'point','bard_valor':'attack'}
def poses_for(role):
 base=frame_list()
 return base+[(name,i,p) for name,(_,_,poses) in CASTS.items() for i,p in enumerate(poses)] if role=='bard' else base
def manifest():
 poses=poses_for('bard')
 return {'framesPerDirection':len(poses),'anims':{name:{'frames':[i for i,(a,_,_) in enumerate(poses) if a==name],'fps':fps,'loop':loop} for name,(fps,loop,_) in CASTS.items()}}
def classic_aliases(anims):
 for name,fallback in FALLBACKS.items():anims[name]=dict(anims[fallback])
 return anims
