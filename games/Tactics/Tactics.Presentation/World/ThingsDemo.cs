using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;

namespace Tactics.Presentation.World;

/// <summary>One view of <see cref="ThingsDemo"/>, sent <paramref name="At"/> seconds in, and the shots to take after it.</summary>
/// <param name="Label">What it shows, for the log; the shots are named after it.</param>
/// <param name="At">When it's sent.</param>
/// <param name="View">The host view.</param>
/// <param name="Shots">Seconds after it, and the name of the shot to take then.</param>
/// <param name="Captions">Every thing is labelled with its art key and where its picture came from: the galleries.</param>
public sealed record ThingsDemoStep(string Label, double At, TacticsHostView View, ImmutableArray<(double After, string Name)> Shots, bool Captions);

/// <summary>
/// Dev-only views for <c>--world-demo=things</c>, the art check for things to interact with (interactables design §4): with no server, a
/// generated courtyard holding every kind in every state the art brief draws, each labelled with its art key and whether it was drawn from its
/// own art, a stand-in or the plain marker, on two gallery screens; then, one by one, a chest opening, a hidden cache found, traps seen, set
/// off and disarmed, a lever working a gate both ways, a locked door unlocked and opened, a secret door found and opened, and a pickup, a
/// note and a shrine used; then the hidden things of design §7: tracks noticed that lead to a cache, a rune sensed by Arcana, the other
/// notices in a row, a hidden path through a hedge found, and an active Search with its hidden DC. The views are the server's own shapes (<see cref="ThingView.Of"/>), so what's drawn is what a session would draw.
/// The user's art AI drops its set into <c>content/tactics/base/interactables/</c> and runs this to see it.
/// </summary>
public static class ThingsDemo
{
    /// <summary>The demo's region id: the TV builds <see cref="Ground"/> for it, as no pack has it.</summary>
    public const string Region = "things_demo";

    private const int Width = 88;
    private const int Height = 140;
    private const int FloorHeight = 1;
    private const int WallHeight = 4;
    private static readonly string[] Materials = ["grass/lush", "flagstone/grey", "rock/grey"];
    private const byte Grass = 0, Flagstone = 1, Rock = 2;

    /// <summary>The kinds in the order the galleries show them, by family as the design's table has them.</summary>
    public static readonly ImmutableArray<string> Kinds =
        ["chest", "crate", "barrel", "sack", "cache", "door", "gate", "drawbridge", "secret_door", "lever", "note", "plaque", "book", "gravestone",
         "signpost", "pickup", "shrine", "well", "altar", "ambient", "trap", "hidden_path",
         "tracks", "scratches", "disturbed_earth", "bloodstain", "draft", "carving", "rune", "remains", "stall", "campfire"];

    /// <summary>The first 21 kinds fill the first three galleries as they always have; these go where there's room.</summary>
    private static readonly ImmutableDictionary<string, (int Gallery, int Column, int Row)> Placed = new Dictionary<string, (int, int, int)>
    {
        ["hidden_path"] = (2, 1, 3), ["stall"] = (4, 0, 0), ["campfire"] = (4, 0, 1),
        ["tracks"] = (3, 0, 0), ["scratches"] = (3, 0, 1), ["disturbed_earth"] = (3, 0, 2), ["bloodstain"] = (3, 0, 3),
        ["draft"] = (3, 1, 0), ["carving"] = (3, 1, 1), ["rune"] = (3, 1, 2), ["remains"] = (3, 1, 3),
    }.ToImmutableDictionary(StringComparer.Ordinal);

    /// <summary>The galleries: the tile where each starts. Each is apart from the rest, so a shot of one shows none of another.</summary>
    private static readonly Tile[] Galleries = [new(14, 22), new(12, 50), new(14, 74), new(14, 100), new(14, 126)];

    /// <summary>Kinds to a column, and columns to a gallery.</summary>
    private const int Rows = 4, Columns = 2;

    /// <summary>Where gallery <paramref name="gallery"/>'s leader stands: in the gap between its two columns, between its middle rows.</summary>
    private static Tile LeaderOf(int gallery) => new(Galleries[gallery].X + 9, Galleries[gallery].Y - 3);

    // The scenes, on flagstone in the east, each with a wall where it needs one.
    private static readonly Tile ChestAt = new(48, 18);
    private static readonly Tile CacheAt = new(49, 31);
    private static readonly Tile PlateAt = new(60, 17);
    private static readonly Tile PitAt = new(61, 20);
    private static readonly Tile TrappedAt = new(62, 18);
    private static readonly int GateWallX = 56;
    private static readonly Tile GateAt = new(56, 34);
    private static readonly Tile LeverAt = new(54, 32);
    private static readonly int DoorWallY = 46;
    private static readonly Tile DoorAt = new(48, 46);
    private static readonly Tile SecretAt = new(60, 46);
    private static readonly Tile KeyAt = new(48, 58);
    private static readonly Tile NoteAt = new(50, 60);
    private static readonly Tile ShrineAt = new(52, 57);

    // The hidden things (design §7), further south.
    private static readonly Tile TracksAt = new(47, 70);
    private static readonly Tile TrackCacheAt = new(53, 68);
    private static readonly Tile RuneAt = new(62, 70);
    private static readonly int HedgeY = 84;
    private static readonly Tile PathAt = new(56, 84);
    private static readonly Tile[] NoticeRow = [new(46, 96), new(49, 97), new(52, 96), new(55, 97), new(58, 96), new(61, 97)];
    private static readonly Tile SearchedAt = new(67, 92);

    /// <summary>The states the art brief draws for a kind: its own, but a floor trap's are its trap's.</summary>
    public static ImmutableArray<string> StatesOf(InteractableKind kind)
        => kind.Family == InteractableFamily.FloorTrap ? ["armed", InteractableSave.Disarmed, InteractableSave.Sprung] : kind.States;

    /// <summary>A thing of <paramref name="kind"/> as content would write it, with what it needs to load (words, a lever's gate, a hidden DC).</summary>
    private static InteractableDef Def(string id, string kind, Tile at, string facing = "sw")
    {
        var k = InteractableKinds.Of(kind)!;
        return new InteractableDef(id, kind, at, facing, Art: kind == "ambient" ? "props.cart" : null, Name: kind == "ambient" ? "cart" : null,
            Text: k.Family is InteractableFamily.Readable or InteractableFamily.Shrine or InteractableFamily.Ambient ? ["Words."] : [],
            Requires: [], Unless: [], Sets: [], Loot: [], Targets: [],
            Hidden: k.MustHide ? new HiddenDef(10) : null, Trap: k.Family == InteractableFamily.FloorTrap ? new TrapDef(10, 10) : null);
    }

    /// <summary>The save that puts <paramref name="def"/> in the art state <paramref name="state"/>, found if it's hidden, a floor trap seen.</summary>
    private static InteractableSave SaveFor(InteractableDef def, string state)
    {
        var floor = InteractableKinds.Of(def.Kind)!.Family == InteractableFamily.FloorTrap;
        return floor
            ? new InteractableSave(InteractableKinds.Idle, Found: true, TrapSpotted: true, Trap: state == "armed" ? null : state)
            : new InteractableSave(state, Found: true);
    }

    private static readonly IReadOnlySet<string> NoFlags = ImmutableHashSet<string>.Empty;

    private static ThingView View(InteractableDef def, string state) => ThingView.Of(def, SaveFor(def, state), NoFlags);

    /// <summary>Where gallery <paramref name="gallery"/> puts column <paramref name="column"/>, row <paramref name="row"/>, state <paramref name="state"/>.</summary>
    /// <remarks>
    /// States go across the screen two tiles apart, so their labels clear each other; rows go down it, two tiles apart; the second column is
    /// a gap's width to the right, where the leader stands.
    /// </remarks>
    private static Tile Cell(int gallery, int column, int row, int state)
    {
        var o = Galleries[gallery];
        return new Tile(o.X + 8 * column + 2 * state + 2 * row, o.Y - 8 * column - 2 * state + 2 * row);
    }

    /// <summary>
    /// The galleries: every kind in every state, four kinds to a column and two columns to a screen, states left to right; then the last
    /// gallery's second column shows the facings and the trapped warning.
    /// </summary>
    public static ImmutableArray<ThingView> Gallery()
    {
        var all = ImmutableArray.CreateBuilder<ThingView>();
        for (var i = 0; i < Kinds.Length; i++)
        {
            var kind = InteractableKinds.Of(Kinds[i])!;
            var (gallery, column, row) = Placed.TryGetValue(kind.Id, out var placed) ? placed : (i / (Rows * Columns), i % (Rows * Columns) / Rows, i % Rows);
            var states = StatesOf(kind);
            for (var s = 0; s < states.Length; s++)
                all.Add(View(Def($"{kind.Id}.{states[s]}", kind.Id, Cell(gallery, column, row, s)), states[s]));
        }
        // The facings: one sprite, mirrored for se and nw (art brief §4.1), and a passage's wall turning with it.
        string[] facings = ["se", "ne", "nw"];
        for (var s = 0; s < facings.Length; s++)
            all.Add(View(Def($"door.closed.{facings[s]}", "door", Cell(2, 1, 1, s), facings[s]), "closed"));
        // A trap seen on a chest and a door: the TV's warning, not the art's.
        var chest = Def("chest.closed.trapped", "chest", Cell(2, 1, 2, 0)) with { Trap = new TrapDef(10, 10) };
        var door = Def("door.closed.trapped", "door", Cell(2, 1, 2, 1)) with { Trap = new TrapDef(10, 10) };
        all.Add(ThingView.Of(chest, new InteractableSave("closed", TrapSpotted: true), NoFlags));
        all.Add(ThingView.Of(door, new InteractableSave("closed", TrapSpotted: true), NoFlags));
        all.Add(View(Def("chest.closed.ne", "chest", Cell(2, 1, 2, 2), "ne"), "closed"));
        return all.ToImmutable();
    }

    /// <summary>
    /// The demo's ground: grass, the galleries and the scenes on flagstone, and the walls the door, the gate and the secret door stand in; and a
    /// hedge, grass that can't be walked, with one tile of it that can: the hidden path, which looks like the rest until it's found.
    /// </summary>
    public static WorldGround Ground()
    {
        var tiles = new byte[Width * Height * 3];
        var floor = Gallery().Select(t => t.At).ToHashSet();
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var at = new Tile(x, y);
                var wall = (x == GateWallX && y is >= 26 and <= 42 && at != GateAt) || (y == DoorWallY && x is >= 42 and <= 70 && at != DoorAt && at != SecretAt);
                var hedge = y == HedgeY && x is >= 42 and <= 70;
                var paved = x >= 40 || Near(floor, at);
                var cell = tiles.AsSpan((y * Width + x) * 3, 3);
                (cell[0], cell[1], cell[2]) = wall ? ((byte)WallHeight, Rock, (byte)TileFlags.Impassable)
                    : hedge ? ((byte)FloorHeight, Grass, at == PathAt ? (byte)0 : (byte)TileFlags.Impassable)
                    : ((byte)FloorHeight, paved ? Flagstone : Grass, (byte)0);
            }
        var (columns, rows) = ((Width + WorldRegion.ChunkSize - 1) / WorldRegion.ChunkSize, (Height + WorldRegion.ChunkSize - 1) / WorldRegion.ChunkSize);
        ImmutableArray<RegionChunk> chunks = [.. Enumerable.Range(0, rows).SelectMany(r => Enumerable.Range(0, columns).Select(c => Chunk(tiles, c, r)))];
        var def = new RegionDef(Region, "Things to interact with", 0, [Width, Height], [.. Materials], chunks, [], [],
            new RegionSpawns(LeaderOf(0), LeaderOf(0)), []);
        return WorldGround.Of(WorldRegion.From(def), new Dictionary<string, BattleMap>(), ArtManifest.None);
    }

    private static bool Near(HashSet<Tile> floor, Tile at)
    {
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
                if (floor.Contains(new Tile(at.X + dx, at.Y + dy)))
                    return true;
        return false;
    }

    private static RegionChunk Chunk(byte[] tiles, int column, int row)
    {
        var bytes = new byte[WorldRegion.ChunkBytes];
        for (var y = 0; y < WorldRegion.ChunkSize; y++)
            for (var x = 0; x < WorldRegion.ChunkSize; x++)
            {
                var (worldX, worldY) = (column * WorldRegion.ChunkSize + x, row * WorldRegion.ChunkSize + y);
                if (worldX < Width && worldY < Height)
                    tiles.AsSpan((worldY * Width + worldX) * 3, 3).CopyTo(bytes.AsSpan((y * WorldRegion.ChunkSize + x) * 3, 3));
            }
        return new RegionChunk(column, row, Convert.ToBase64String(bytes));
    }

    /// <summary>The demo's views, the first at 0 s, long enough for the world to open before its first shot.</summary>
    public static ImmutableArray<ThingsDemoStep> Steps()
    {
        var steps = ImmutableArray.CreateBuilder<ThingsDemoStep>();
        var gallery = Gallery();
        var t = 0.0;
        var epoch = 0;
        var seq = 0;
        var events = ImmutableArray<ThingEventView>.Empty;
        var scene = new Dictionary<string, ThingView>(StringComparer.Ordinal);
        var at = LeaderOf(0);

        InteractableDef chest = Def("chest", "chest", ChestAt), cache = Def("cache", "cache", CacheAt),
            plate = Def("plate", "trap", PlateAt) with { Name = "pressure plate" }, pit = Def("pit", "trap", PitAt),
            trapped = Def("trapped_crate", "crate", TrappedAt) with { Trap = new TrapDef(10, 10) },
            gate = Def("gate", "gate", GateAt, "se"), lever = Def("lever", "lever", LeverAt) with { Targets = ["gate"] },
            door = Def("door", "door", DoorAt) with { Lock = new LockDef(Pick: 12) }, secret = Def("secret", "secret_door", SecretAt),
            key = Def("key", "pickup", KeyAt) with { Name = "iron key" }, note = Def("note", "note", NoteAt), shrine = Def("shrine", "shrine", ShrineAt),
            path = Def("goat_track", "hidden_path", PathAt) with { Name = "goat track", Hidden = new HiddenDef(13, "survival") };

        var saves = new Dictionary<string, InteractableSave>(StringComparer.Ordinal);
        void Put(InteractableDef def, InteractableSave save) => (scene[def.Id], saves[def.Id]) = (ThingView.Of(def, save, NoFlags), save);
        // The secret door is a plug of wall until it's found, as the server sends it (Interactables.Plugged), and the hidden path one of hedge.
        ImmutableArray<WallPlug> Plugs() => [.. new[] { secret, path }.Where(d => Interactables.Plugged(d, saves.GetValueOrDefault(d.Id) ?? Interactables.Initial(d), NoFlags))
            .Select(d => new WallPlug(d.At, d.Facing ?? "sw"))];
        Put(chest, new InteractableSave("closed"));
        Put(gate, new InteractableSave("closed"));
        Put(lever, new InteractableSave("off"));
        Put(door, new InteractableSave("closed"));
        Put(trapped, new InteractableSave("closed"));
        Put(key, new InteractableSave("here"));
        Put(note, new InteractableSave("unread"));
        Put(shrine, new InteractableSave("ready"));

        TacticsHostView Host(string? notice)
        {
            var explore = new ExploreView(Region, "Things to interact with", epoch, 0, at, new Tile(at.X + 1, at.Y), false, [],
                [new WalkerView("m0", "Ana", "characters.class_rogue", true, true), new WalkerView("m1", "Bo", "characters.class_fighter", true, false)],
                "Ana", 0, Things: [.. gallery, .. scene.Values], ThingEvents: events, Plugs: Plugs());
            return new TacticsHostView(nameof(TacticsPhase.Exploration), "Ana leads the party", notice, 1, ["Ana", "Bo"], null, null, null, null,
                Explore: explore);
        }
        void Add(string label, string? notice, double hold, (double, string)[] shots, bool captions = false)
        {
            steps.Add(new ThingsDemoStep(label, t, Host(notice), [.. shots], captions));
            t += hold;
        }
        void Go(Tile to)
        {
            (at, epoch) = (to, epoch + 1);      // a new epoch: the camera cuts there
        }
        void Happen(params (InteractableDef Def, string What, InteractableSave? Save)[] happened)
        {
            foreach (var (def, what, save) in happened)
            {
                events = events.Add(new ThingEventView(++seq, def.Id, what, "Ana", null));
                if (save is not null)
                    saves[def.Id] = save;
                if (save is null)
                    scene.Remove(def.Id);
                else
                    Put(def, save);
            }
            while (events.Length > 8)
                events = events.RemoveAt(0);
        }
        (double, string)[] Moment(string name) => [(0.05, $"things-{name}-a"), (0.18, $"things-{name}-b"), (0.35, $"things-{name}-c"), (1.4, $"things-{name}-d")];

        // The galleries: every kind in every state, labelled.
        Add("gallery-1", null, 9, [(8, "things-gallery-1")], captions: true);
        for (var g = 1; g < Galleries.Length; g++)
        {
            Go(LeaderOf(g));
            Add($"gallery-{g + 1}", null, 3, [(2.5, $"things-gallery-{g + 1}")], captions: true);
        }

        // A chest opens.
        Go(new Tile(ChestAt.X - 2, ChestAt.Y + 1));
        Add("open", null, 1.5, [(1.2, "things-open-0-before")]);
        Happen((chest, ThingEvents.Opened, new InteractableSave("open")));
        Add("open", "Inside: 2 × Potion of Healing.", 2, Moment("open"));

        // A hidden cache is found as the party passes.
        Go(new Tile(CacheAt.X - 2, CacheAt.Y + 1));
        Add("reveal", null, 1.5, [(1.2, "things-reveal-0-before")]);
        Happen((cache, ThingEvents.Revealed, new InteractableSave("closed", Found: true)));
        Add("reveal", "Ana spots a hidden cache.", 2.2, Moment("reveal"));

        // Traps: one seen from afar, one on a crate, one nobody saw going off, and the first taken apart.
        Go(new Tile(PlateAt.X - 2, PlateAt.Y + 2));
        Add("trap", null, 1.5, [(1.2, "things-trap-0-before")]);
        Happen((plate, ThingEvents.Spotted, new InteractableSave("idle", TrapSpotted: true)),
            (trapped, ThingEvents.Spotted, new InteractableSave("closed", TrapSpotted: true)));
        Add("trap-spotted", "Ana spots a pressure plate. Ana spots a trap on the crate.", 2.2, Moment("trap-spotted"));
        Happen((pit, ThingEvents.Sprung, new InteractableSave("idle", Trap: InteractableSave.Sprung)));
        Add("trap-sprung", "Click. A trap goes off, but it does no harm.", 2, Moment("trap-sprung"));
        Happen((plate, ThingEvents.Disarmed, new InteractableSave("idle", TrapSpotted: true, Trap: InteractableSave.Disarmed)));
        Add("trap-disarmed", "Ana takes the pressure plate apart.", 2, Moment("trap-disarmed"));

        // A lever opens a gate in its wall, and shuts it again.
        Go(new Tile(LeverAt.X - 1, LeverAt.Y + 2));
        Add("lever", null, 1.5, [(1.2, "things-lever-0-before")]);
        Happen((lever, ThingEvents.Toggled, new InteractableSave("on")), (gate, ThingEvents.Toggled, new InteractableSave("open")));
        Add("lever-on", "Clunk.", 2.2, [(0.05, "things-lever-on-a"), (0.3, "things-lever-on-b"), (0.45, "things-lever-on-c"), (1.4, "things-lever-on-d")]);
        Happen((lever, ThingEvents.Toggled, new InteractableSave("off")), (gate, ThingEvents.Toggled, new InteractableSave("closed")));
        Add("lever-off", "Clunk.", 2.2, [(0.45, "things-lever-off-c"), (1.4, "things-lever-off-d")]);

        // A locked door: the lock gives and it opens, in one view.
        Go(new Tile(DoorAt.X + 1, DoorAt.Y + 2));
        Add("door", null, 1.5, [(1.2, "things-door-0-before")]);
        Happen((door, ThingEvents.Unlocked, new InteractableSave("closed", Unlocked: true)), (door, ThingEvents.Opened, new InteractableSave("open", Unlocked: true)));
        Add("door", "The lock gives.", 2.2, [(0.1, "things-door-a"), (0.3, "things-door-b"), (0.45, "things-door-c"), (1.4, "things-door-d")]);

        // A secret door: a gap in the wall until it's found, which the TV isn't told of; then found, and opened.
        Go(new Tile(SecretAt.X - 1, SecretAt.Y + 2));
        Add("secret", null, 1.5, [(1.2, "things-secret-0-unfound")]);
        Happen((secret, ThingEvents.Revealed, new InteractableSave("closed", Found: true)));
        Add("secret-found", "Ana spots a hidden door.", 2.2, Moment("secret-found"));
        Happen((secret, ThingEvents.Opened, new InteractableSave("open", Found: true)));
        Add("secret-open", null, 2, Moment("secret-open"));

        // A key picked up, a note read, a shrine knelt at.
        Go(new Tile(KeyAt.X - 2, KeyAt.Y + 1));
        Add("use", null, 1.5, [(1.2, "things-use-0-before")]);
        Happen((key, ThingEvents.Taken, null));
        Add("taken", "You take the iron key.", 1.5, [(0.05, "things-taken-a"), (0.2, "things-taken-b"), (1.0, "things-taken-d")]);
        Happen((note, ThingEvents.Read, new InteractableSave("read")), (shrine, ThingEvents.Used, new InteractableSave("used")));
        Add("used", "The ember-light warms you.", 2.5, [(0.1, "things-read-a"), (0.35, "things-used-a"), (0.6, "things-used-b"), (2, "things-used-d")]);

        // Hidden things (design §7). Tracks noticed by passive Perception, which lead to a cache: both are found at once.
        InteractableDef tracks = Def("tracks", "tracks", TracksAt) with { Name = "wolf tracks", Reveals = ["track_cache"] },
            trackCache = Def("track_cache", "cache", TrackCacheAt),
            rune = Def("rune", "rune", RuneAt) with { Name = "ward-rune", Hidden = new HiddenDef(12, "arcana") },
            searched = Def("loose_stone", "cache", SearchedAt) with { Name = "loose flagstone", Hidden = new HiddenDef(14, "investigation") };
        Go(new Tile(TracksAt.X - 2, TracksAt.Y + 1));
        Add("notice", null, 2.5, [(1.0, "things-notice-0-before")]);
        Happen((tracks, ThingEvents.Noticed, new InteractableSave(InteractableKinds.Noticed, Found: true)),
            (trackCache, ThingEvents.Revealed, new InteractableSave("closed", Found: true)));
        Add("notice", "Ana notices: Fresh wolf tracks lead east, to a loose stone. Ana spots a hidden cache.", 2.4, Moment("notice"));

        // A rune only Arcana notices: Bo's passive Arcana, not anyone's eyes.
        Go(new Tile(RuneAt.X - 2, RuneAt.Y + 1));
        Add("rune", null, 2.5, [(1.0, "things-rune-0-before")]);
        Happen((rune, ThingEvents.Noticed, new InteractableSave(InteractableKinds.Noticed, Found: true)));
        Add("rune", "Bo notices: A ward-rune, cut into the flagstone, still faintly warm.", 2.2, Moment("rune"));

        // The rest of the notices, one after another down a row, as a party walking past would notice them.
        string[] rest = ["scratches", "disturbed_earth", "bloodstain", "draft", "carving", "remains"];
        var row = rest.Select((kind, i) => Def($"row_{kind}", kind, NoticeRow[i])).ToArray();
        Go(new Tile(53, 99));
        Add("notices", null, 2.5, [(1.0, "things-notices-0-before")]);
        Happen([.. row.Select(d => (d, ThingEvents.Noticed, (InteractableSave?)new InteractableSave(InteractableKinds.Noticed, Found: true)))]);
        Add("notices", "Ana notices: Deep scratches on the stone, claw-wide.", 2.5, [(0.1, "things-notices-a"), (0.6, "things-notices-b"), (2.2, "things-notices-d")]);

        // A hidden path: one tile of the hedge can be walked, but it's in the way until it's found, and drawn as hedge (a plug, as the secret
        // door's wall); then the plug goes and the TV draws the path.
        Go(new Tile(PathAt.X, PathAt.Y + 3));
        Add("path", null, 2.5, [(1.0, "things-path-0-unfound")]);    // longer: a slow TV mustn't take its shot after the find
        Happen((path, ThingEvents.Revealed, new InteractableSave(InteractableKinds.FoundPath, Found: true)));
        Add("path", "Bo spots a goat track.", 2.2, Moment("path"));

        // An active Search: the party picks who rolls Investigation, the DC hidden, and the roll finds the loose flagstone.
        Go(new Tile(SearchedAt.X - 1, SearchedAt.Y + 1));
        Add("search", null, 2.5, [(1.0, "things-search-0-before")]);
        var picking = t;
        StoryCheckView Check(int[] votes, string? chosen = null, RollResult? roll = null) => new(Interactables.SearchSkill, null, "Search the area",
            [new StoryCardView("m0", "Ana", "Ana", "characters.class_rogue", 1, "INT 12", true, votes[0]),
             new StoryCardView("m1", "Bo", "Bo", "characters.class_wizard", 5, "Proficient · INT 16", true, votes[1])],
            chosen, roll, false, null, roll is null ? DateTimeOffset.UnixEpoch.AddSeconds(picking + 20) : null, roll is null ? 20 : null);
        StoryView Story(string state, StoryCheckView check) => new(++seq, InteractScenes.SearchId, "Search", state, null, null, check, null);
        steps.Add(new ThingsDemoStep("search-pick", t, Host(null) with { Phase = nameof(TacticsPhase.Story), Story = Story(TacticsGame.StoryStates.PickRoller, Check([0, 1])) },
            [(1.5, "things-search-pick")], false));
        t += 2.5;
        steps.Add(new ThingsDemoStep("search-rolled", t, Host(null) with { Phase = nameof(TacticsPhase.Story),
            Story = Story(TacticsGame.StoryStates.Rolled, Check([0, 1], "m1", Rolls.Flat(11, 5, 14) with { Vs = 0 })) }, [(3.5, "things-search-rolled")], false));
        t += 4.5;
        Happen((searched, ThingEvents.Revealed, new InteractableSave("closed", Found: true)));
        Add("search-found", "Bo finds a loose flagstone.", 2.2, Moment("search-found"));
        Add("end", null, 0.5, []);
        return steps.ToImmutable();
    }
}
