/** Phone-side reading aids, not rules: exact costs, targets and outcomes still come from the server. */
export const CLASS_GUIDANCE: Record<string, { style: string; complexity: "Easy" | "Moderate" | "Difficult"; beginner?: boolean }> = {
  fighter: { style: "Stand beside enemies, protect friends and fight with weapons.", complexity: "Easy", beginner: true },
  barbarian: { style: "Rush into close combat and use Rage to fight through danger.", complexity: "Easy", beginner: true },
  ranger: { style: "Fight from a distance and use nature skills to support the party.", complexity: "Moderate", beginner: true },
  rogue: { style: "Find a good opening, strike hard and slip away from enemies.", complexity: "Moderate" },
  cleric: { style: "Heal friends and use divine magic to protect or attack.", complexity: "Moderate" },
  paladin: { style: "Fight on the front line with weapons, healing and divine power.", complexity: "Moderate" },
  monk: { style: "Move quickly, land close-range strikes and spend Ki for special moves.", complexity: "Moderate" },
  bard: { style: "Support friends, disrupt enemies and choose the right spell for the moment.", complexity: "Difficult" },
  druid: { style: "Control the battlefield with nature magic and animal forms.", complexity: "Difficult" },
  sorcerer: { style: "Cast powerful spells and spend Sorcery to change how they work.", complexity: "Difficult" },
  warlock: { style: "Use a pact's magic to attack enemies and create unusual advantages.", complexity: "Moderate" },
  wizard: { style: "Choose from many spells to damage enemies or control where they can go.", complexity: "Difficult" },
};

export const RULE_HELP = {
  Move: "The number is how many tiles of movement you have left. Tap a reachable tile, then Move here. You can move and use an ability in the same turn if you have enough left.",
  Action: "Most attacks and spells spend your Action. An available Action does not mean an automatic attack: choose an ability, check its target, then tap Use.",
  Bonus: "A smaller action for abilities marked Bonus. You can use it as well as your Action. If no Bonus ability is available, you can leave it unused.",
  Reaction: "A response to a trigger, often during someone else's turn. A prompt lets you use or pass it. The Reactions panel controls automatic responses.",
  Downed: "At 0 HP, a hero is Downed and cannot act. The number drops at the start of their own turns and when they are hit. Any healing or an adjacent ally's Help revives them. At 0 they are Out for this battle and gain an injury; they are not permanently dead.",
  Delay: "Delay moves your turn one place later in the turn order. It does not grant another Action. End turn finishes your turn instead.",
  Disengage: "Use this before moving away from a nearby enemy to avoid its free parting attack this turn. It does not move you: pick a tile and Move afterwards.",
  Concentration: "Keeping a spell going. You can concentrate on only one spell at a time. Taking damage can break it, and starting another concentration spell ends the first.",
  "Dice and checks": "A d20 is a twenty-sided die. Add your bonus to the roll. AC is the defence an attack must meet; DC is the number a check or saving throw must meet. A saving throw is a chance to resist an effect. The preview shows your chance before you commit.",
  Advantage: "Roll two d20s and use the higher result. Disadvantage uses the lower. They cancel each other; several sources do not add more dice.",
} as const;
export type RuleTopic = keyof typeof RULE_HELP;

/** Short introductions only. The original rules remain available beside the exact server-supplied costs and previews. */
export const ABILITY_GUIDANCE: Record<string, string> = {
  "common.attack": "Attack an enemy with your weapon. Check the target and hit chance before you confirm.",
  "common.dash": "Gain more movement this turn. You still choose where to move.",
  "common.disengage": "Move away safely from nearby enemies this turn, without their parting attacks.",
  "common.dodge": "Focus on defending yourself, making enemy attacks harder to land.",
  "common.help": "Help a nearby friend with their next roll, or get a Downed friend back up.",
  "common.shove": "Try to push a nearby enemy away. They get a chance to resist.",
  "common.shove_prone": "Try to knock a nearby enemy over. They get a chance to resist.",
  "common.extinguish": "Put out flames on yourself or a friend beside you.",
  "fighter.second_wind": "Recover some of your own health without spending your main Action.",
  "fighter.action_surge": "Gain another Action this turn for an extra attack or other action.",
  "wizard.fire_bolt": "Send a fire attack at one enemy.",
  "wizard.ray_of_frost": "Send a cold attack at one enemy and slow them if it hits.",
  "wizard.magic_missile": "Send magical darts at an enemy without an attack roll.",
  "wizard.shield": "React to an incoming attack with a magical defence.",
  "cleric.sacred_flame": "Call down radiant fire. The enemy gets a chance to resist.",
  "cleric.cure_wounds": "Restore health to a friend close enough to touch.",
  "cleric.healing_word": "Restore some health to a friend at a distance using a Bonus action.",
  "cleric.bless": "Help friends succeed at attacks and saving throws while you keep concentrating.",
  "barbarian.rage": "Enter a powerful battle state for close combat.",
  "rogue.cunning_dash": "Gain more movement using your Bonus action instead of your main Action.",
};
