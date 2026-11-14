// The CustomView data of each Meridian phone screen. Mirrors games/Meridian/Meridian.Game/Views/Screens.cs; the
// fixtures in test/fixtures/meridian are real views from the game, so a drift between the two fails the tests.

export type Deck = "command" | "habitat" | "engineering";
export type Side = "n" | "e" | "s" | "w";
export type Trait = "brawn" | "agility" | "tech" | "nerve";
export type CardKind = "incident" | "salvage" | "anomaly";
export type Cell = [Deck, number, number];

export interface CompartmentCard {
  id: string; tile: string; name?: string; at: Cell; rotation: number; hatches: Side[]; sealed?: Side[]; symbol?: CardKind;
}
export interface CrewMarker { id: string; name: string; at: string; colour: number }
export interface MapData { compartments: CompartmentCard[]; crew: CrewMarker[] }
export interface TrackCard { trait: Trait; values: number[]; step: number }
export interface CrewCard { id: string; name: string; role: string; tracks: TrackCard[]; taken: boolean; mine: boolean }
export interface ItemCard { id: string; kind: CardKind; title: string; text: string; use: string | null; usable: boolean }

export interface CrewPickData { crew: CrewCard[]; picked: number; seats: number }
export interface BriefingCard { role: string; name: string; briefing: string; objectives: string[] }
export interface AttackTarget { id: string; name: string }
export interface ScenarioAction { id: string; label: string; data: string | null }
export interface MonsterCard { id: string; kind: string; at: string; moves: string[]; targets: AttackTarget[] }
export interface TurnData {
  map: MapData; at: string; moves: number; reach: { id: string; cost: number }[]; opens: { at: string; side: Side }[];
  items: ItemCard[]; room: string | null; neighbours: { seat: string; name: string; items: ItemCard[] }[]; me: CrewCard;
  attacks: AttackTarget[]; actions: ScenarioAction[]; monsters: MonsterCard[]; briefing: BriefingCard | null;
}
export interface OrientData { name: string; at: Cell; toward: Side; options: { rotation: number; hatches: Side[] }[]; map: MapData }
export interface CardData {
  id: string; kind: CardKind; title: string; text: string; options: { id: string; label: string; text: string; check: Trait | null }[];
}
export interface RollData {
  reason: "check" | "breach"; trait: Trait | null; dice: number; title: string; text: string;
  bands: { min: number; max: number | null; text: string }[]; needed: number | null;
}
export interface TradeData { from: string; give: ItemCard[]; take: ItemCard[] }
export interface SheetData {
  me: CrewCard; at: string; items: ItemCard[]; map: MapData; active: string; waiting: string | null; briefing: BriefingCard | null;
}
export interface BriefingData { title: string; you: BriefingCard; ready: boolean; readyCount: number; seats: number }
export interface AllocateData { steps: number; tracks: TrackCard[]; by: string | null }
export interface VoteData { prompt: string; options: { id: string; label: string }[]; cast: boolean; remaining: number }
export interface EchoData { targets: { id: string; label: string }[]; lent: boolean }
export interface DebriefData {
  message: string; won: boolean; roles: { seat: string; name: string; role: string; won: boolean }[];
  awards: { title: string; seat: string; name: string }[]; vip: boolean;
}
export interface Presented {
  seq: number; kind: string; seat: string; title: string; text: string; seconds: number; faces: number[]; compartment: string | null;
}
export interface WatchData { now: Presented }
export interface OverData { message: string }
