"""Class pose registry; base production frame indices stay stable."""
from . import wizard_casts,sorcerer_casts,warlock_casts,cleric_casts,druid_casts,paladin_casts,bard_casts,ranger_casts
MODULES={'wizard':wizard_casts,'sorcerer':sorcerer_casts,'warlock':warlock_casts,'cleric':cleric_casts,'druid':druid_casts,'paladin':paladin_casts,'bard':bard_casts,'ranger':ranger_casts}
def poses_for(role):
    from .rig import frame_list
    return MODULES[role].poses_for(role) if role in MODULES else frame_list()
def manifests():return {role:module.manifest() for role,module in MODULES.items()}
def classic_aliases(anims):
    for module in MODULES.values():module.classic_aliases(anims)
    return anims
