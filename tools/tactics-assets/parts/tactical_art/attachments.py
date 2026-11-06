"""Explicit carried-item contracts. New equipment must declare its hand and grip.

Coordinates are authored before race fitting. Held objects require pixel contact;
spell effects intentionally float relative to their own hand and are marked as such.
"""
from dataclasses import dataclass
import math

@dataclass(frozen=True)
class Attachment:
    hand: str
    grip: tuple[float,float]
    rotate: bool = True
    kind: str = 'held'

    def __post_init__(self):
        if self.hand not in ('near','far') or self.kind not in ('held','effect'):
            raise ValueError('Attachments require a near/far hand and held/effect kind')
        if len(self.grip)!=2 or not all(math.isfinite(v) for v in self.grip):
            raise ValueError('Attachments require a finite 2D grip point')

ATTACHMENTS={
 'fighter':{'weapon':Attachment('near',(44,52)), 'offhand':Attachment('far',(18,49),False)},
 'rogue':{'weapon':Attachment('near',(44,51)), 'offhand':Attachment('far',(19,48))},
 'wizard':{'weapon':Attachment('near',(50,51))},
 'cleric':{'weapon':Attachment('near',(49,51)), 'offhand':Attachment('far',(16,49),False)},
 'ranger':{'weapon':Attachment('near',(56,44))},
 'paladin':{'weapon':Attachment('near',(48,53))},
 'bard':{'weapon':Attachment('near',(43,40),False), 'instrument-back':Attachment('near',(43,40),False)},
 'druid':{'weapon':Attachment('near',(50,51))},
 'warlock':{'weapon':Attachment('near',(44,51),False,'effect'), 'offhand':Attachment('far',(17,51),False)},
 'monk':{},
 'barbarian':{'weapon':Attachment('near',(48,51))},
 'sorcerer':{'weapon':Attachment('near',(44,51),False,'effect'), 'offhand':Attachment('far',(19,48),False,'effect')},
}

HAND_POINTS={'near':(44,51),'far':(19,48)}
SHOULDERS={'near':(40,35),'far':(23,34)}
EQUIPMENT_LAYERS=('weapon','offhand','instrument-back')

def validate_attachments(role,layers):
    definitions=ATTACHMENTS[role]
    for name in EQUIPMENT_LAYERS:
        layer=layers.get(name)
        if layer is not None and layer.slot.any() and name not in definitions:
            raise ValueError(f'{role}/{name} has artwork but no hand/grip attachment')
