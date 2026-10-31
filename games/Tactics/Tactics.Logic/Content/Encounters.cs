using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>A player's (or absent player's) character in the party: the unit id it fights under, its record and who controls it.</summary>
public sealed record PartyMember(UnitId Id, CharacterRecord Character, Controller Controller);

/// <summary>Sets up the battle an encounter describes (spec §4.8).</summary>
public static class Encounters
{
    private const string PartySpawn = "party_spawn";

    /// <summary>
    /// The <see cref="BattleSetup"/> for the encounter <paramref name="encounterId"/> with this party:
    /// <list type="bullet">
    /// <item>The party takes the map's <c>party_spawn</c> tiles in order, each under its <see cref="PartyMember.Id"/>, with the
    /// sheet <see cref="SheetBuilder.ForCharacter"/> builds and the bag of its record.</item>
    /// <item>Enemies follow in the encounter's order, then its NPCs (allies on the party's side, protected). A unit on a tile
    /// takes that tile; one on a zone takes the zone's first free tiles in zone order, never a tile a fixed (<c>at</c>) unit
    /// of the encounter claims, wherever that unit is listed, so fixed units are placed before zones fill. Its id is
    /// <c>"&lt;monster&gt;.&lt;n&gt;"</c>, numbering each monster from 1 in placement order across enemies and NPCs, unless the
    /// placement names an <see cref="EncounterUnitDef.Id"/>. Its sheet is <see cref="SheetBuilder.ForMonster"/> at the placement's level, with
    /// the placement's AI profile (else the monster's), the sprite's variant if it names one (the third part of the key,
    /// <c>"beasts.wolf.dire"</c>) and its tag added to the sheet's tags, where objectives and triggers look for it. Enemies
    /// and NPCs are AI-controlled by that profile.</item>
    /// <item>The encounter's objectives, triggers, XP and loot, and <paramref name="d"/>, go into the setup unchanged, except
    /// that each trigger <c>phase</c> action gets its phase from the tagged monster's <c>phases</c>.</item>
    /// </list>
    /// Throws <see cref="ContentException"/> for an unknown encounter or map, a placement onto an unwalkable or taken tile, a
    /// zone with too few free tiles, or a unit id in use twice; and <see cref="ArgumentException"/> for a party the encounter
    /// doesn't take (the wrong size, or the same id twice).
    /// </summary>
    public static BattleSetup Setup(string encounterId, ImmutableArray<PartyMember> party, Difficulty d, RulesPack rules)
    {
        var problems = new ContentProblems($"encounter '{encounterId}'");
        if (!rules.Encounters.TryGetValue(encounterId, out var def))
            problems.Add("", $"Unknown encounter '{encounterId}'.");
        problems.ThrowIfAny();
        if (party.Length < def!.Party.Min || party.Length > def.Party.Max)
            throw new ArgumentException(
                $"The encounter '{encounterId}' takes {def.Party.Min} to {def.Party.Max} party members, not {party.Length}.", nameof(party));
        if (party.Select(m => m.Id).Distinct().Count() != party.Length)
            throw new ArgumentException("Two party members have the same id.", nameof(party));

        var map = rules.MapFor(def.Map);
        if (map is null)
            problems.Add("/map", $"Unknown map '{def.Map}'.");
        else if (!map.Zones.ContainsKey(PartySpawn))
            problems.Add("/map", $"The map '{map.Id}' has no \"{PartySpawn}\" zone.");
        problems.ThrowIfAny();

        var placer = new Placer(map!, problems, [.. def.Enemies.Concat(def.Npcs).Select(u => u.At).OfType<Tile>()]);
        var spawn = map!.Zones[PartySpawn];
        for (var i = 0; i < party.Length; i++)
        {
            if (i >= spawn.Length)
                problems.Add("/party", $"The map '{map.Id}' has {spawn.Length} {PartySpawn} tiles for {party.Length} party members.");
            else if (placer.Claim(party[i].Id, spawn[i], "/party", "/party"))
                placer.Units.Add(new UnitPlacement(party[i].Id, SheetBuilder.ForCharacter(party[i].Character, rules), party[i].Controller,
                    spawn[i], party[i].Character.Bag));
        }

        var numbers = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (units, name, isNpc) in new[] { (def.Enemies, "enemies", false), (def.Npcs, "npcs", true) })
            for (var i = 0; i < units.Length; i++)
                PlaceUnits(units[i], $"/{name}/{i}", isNpc, rules, placer, numbers);
        problems.ThrowIfAny();

        return new BattleSetup(map, placer.Units.ToImmutable(), def.Objectives ?? ObjectivesDef.Default, WithPhases(def, rules), d,
            def.Xp, def.Loot);
    }

    /// <summary>
    /// The encounter's triggers with every <c>phase</c> action's <see cref="PhaseAction.Def"/> filled in from the monster that
    /// carries its tag (the first placed with it, else the first spawned with it), when that monster has the phase.
    /// </summary>
    private static ImmutableArray<TriggerDef> WithPhases(EncounterDef def, RulesPack rules)
    {
        if (def.Triggers.IsDefaultOrEmpty || !def.Triggers.Any(t => t.Do.Any(a => a.Phase is { Def: null })))
            return def.Triggers.IsDefault ? [] : def.Triggers;
        var monsterByTag = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var unit in def.Enemies.Concat(def.Npcs))
            monsterByTag.TryAdd(unit.UnitTag, unit.Unit);
        foreach (var spawn in def.Triggers.SelectMany(t => t.Do).Select(a => a.Spawn).OfType<SpawnAction>())
            monsterByTag.TryAdd(spawn.UnitTag, spawn.Unit);

        TriggerAction Resolve(TriggerAction action)
        {
            if (action.Phase is not { Def: null } phase || !monsterByTag.TryGetValue(phase.Tag, out var monster)
                || !rules.Monsters.TryGetValue(monster, out var m) || phase.Number < 1 || phase.Number > m.Phases.Length)
                return action;
            return action with { Phase = phase with { Def = m.Phases[phase.Number - 1] } };
        }
        return [.. def.Triggers.Select(t => t with { Do = [.. t.Do.Select(Resolve)] })];
    }

    private static void PlaceUnits(EncounterUnitDef unit, string at, bool isNpc, RulesPack rules, Placer placer,
        Dictionary<string, int> numbers)
    {
        var tiles = placer.TilesFor(unit, at);
        if (tiles.IsDefault)
            return;

        var monster = rules.Monsters[unit.Unit];
        var profile = unit.AiProfile ?? monster.AiProfile;
        foreach (var tile in tiles)
        {
            UnitId id;
            if (unit.Id is { } named)
                id = new UnitId(named);
            else
            {
                var number = numbers.GetValueOrDefault(unit.Unit) + 1;
                numbers[unit.Unit] = number;
                id = new UnitId($"{unit.Unit}.{number}");
            }
            if (!placer.Claim(id, tile, unit.Id is null ? at : Pointer.Append(at, "id"),
                    Pointer.Append(at, unit.At is null ? "zone" : "at")))
                continue;
            var sheet = SheetBuilder.ForMonster(unit.Unit, rules, unit.Level) with
            {
                AiProfile = profile,
                Sprite = unit.Variant is { } variant ? WithVariant(monster.Sprite, variant) : monster.Sprite,
                Tags = SheetTags(monster, unit),
                Side = isNpc ? Side.Party : Side.Enemy,
            };
            placer.Units.Add(new UnitPlacement(id, sheet, new AiControlled(profile), tile, [], Protected: isNpc));
        }
    }

    private static ImmutableSortedSet<string> SheetTags(MonsterDef monster, EncounterUnitDef unit) => monster.Tags.Add(unit.UnitTag);

    /// <summary><c>"beasts.wolf.grey"</c> with the variant <c>"dire"</c> is <c>"beasts.wolf.dire"</c>; a key without a variant gets one.</summary>
    private static string WithVariant(string sprite, string variant)
        => string.Join('.', sprite.Split('.').Take(2).Append(variant));

    /// <summary>
    /// Where units go: the walkable tiles of the map that nobody holds yet, the ids already in use, and the tiles the
    /// encounter's fixed units claim (kept out of zone fills).
    /// </summary>
    private sealed class Placer(BattleMap map, ContentProblems problems, ImmutableArray<Tile> fixedTiles)
    {
        private readonly Terrain _terrain = new(map, ImmutableSortedDictionary<Tile, int>.Empty, ImmutableSortedSet<Tile>.Empty);
        private readonly Dictionary<Tile, UnitId> _taken = [];
        private readonly HashSet<UnitId> _ids = [];

        public ImmutableArray<UnitPlacement>.Builder Units { get; } = ImmutableArray.CreateBuilder<UnitPlacement>();

        /// <summary>
        /// Takes <paramref name="tile"/> and <paramref name="id"/> for a unit. False, with a problem at <paramref name="idAt"/>
        /// or <paramref name="tileAt"/>, when the id is in use or the tile can't be stood on or is taken.
        /// </summary>
        public bool Claim(UnitId id, Tile tile, string idAt, string tileAt)
        {
            if (!_ids.Add(id))
                problems.Add(idAt, $"Unit id '{id}' is already in use.");
            else if (!_terrain.Walkable(tile))
                problems.Add(tileAt, $"Tile {tile} can't be stood on.");
            else if (_taken.TryGetValue(tile, out var holder))
                problems.Add(tileAt, $"Tile {tile} is already taken by '{holder}'.");
            else
            {
                _taken[tile] = id;
                return true;
            }
            return false;
        }

        /// <summary>
        /// The tiles for the units <paramref name="unit"/> places: its <c>at</c> tile once per unit (a second unit finds it
        /// taken), or the first free tiles of its zone that no fixed unit claims; <c>default</c> (and a problem) when the zone
        /// has too few.
        /// </summary>
        public ImmutableArray<Tile> TilesFor(EncounterUnitDef unit, string at)
        {
            if (unit.At is { } tile)
                return [.. Enumerable.Repeat(tile, unit.Count)];
            if (!map.Zones.TryGetValue(unit.Zone ?? "", out var zone))
            {
                problems.Add(Pointer.Append(at, "zone"), $"The map '{map.Id}' has no zone '{unit.Zone}'.");
                return default;
            }
            var free = zone.Where(t => _terrain.Walkable(t) && !_taken.ContainsKey(t) && !fixedTiles.Contains(t))
                .Take(unit.Count).ToImmutableArray();
            if (free.Length >= unit.Count)
                return free;
            problems.Add(Pointer.Append(at, "zone"), $"The zone '{unit.Zone}' has {free.Length} free tiles for {unit.Count} units.");
            return default;
        }
    }
}
