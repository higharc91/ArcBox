using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Godot;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// Dev only: <c>--world-demo</c>. Opens a whole region the way the TV will (<see cref="WorldCanvas"/>) and walks a stand-in the length of it at twice
/// walking pace, the camera on its heels from the moment the fade starts to lift, so the blocks still to record are recorded during the walk. It prints what
/// each step cost in <c>WORLD</c> lines. Then, with <c>--shots</c>, it goes to each of <see cref="WorldSpots"/> and saves <c>world-&lt;spot&gt;.png</c>,
/// and it quits. The spec is <c>&lt;region&gt;|hilly[:&lt;block tiles&gt;]</c>.
/// </summary>
internal sealed partial class WorldDemo : Node
{
    private const string Stress = "hilly";
    private const string WoodSpec = "wood";
    private const string TavernSpec = "tavern";
    private const string TownSpec = "town";
    private const string SwatchSpec = "swatches";
    private const float StepsPerSecond = 2 / 0.3f;      // twice the walking pace of a tile per 0.3 s
    private const double RegularFrameMs = 16;           // the task's line: a frame over this, regularly, stops the work
    private const double ColdSeconds = 1;               // frames this close to the start have the JIT in them
    private const int FadeFrames = 40;                  // before a shot, for a fade to have finished, which also has the overlay drawn
    private const double SecondLook = 0.3;              // between two shots of water, to see that it moved

    private enum Stage { Loading, Crossing, ChangingParty, RestoringParty, Spots, Done }

    private readonly record struct Frame(double Seconds, double Ms, double RenderCpu, double RenderGpu, double Record, double Query, double Draw, int Items, double DrawCalls);

    private readonly string _spec;
    private readonly string _pack;
    private readonly string? _shots;
    private readonly string? _looks;
    private readonly string? _walk;
    private readonly List<Frame> _frames = [];

    private WorldCanvas _canvas = null!;
    private WorldPeople? _people;
    private readonly HashSet<string> _storyFlags = (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--world-story-flags=", StringComparison.Ordinal))?["--world-story-flags=".Length..] ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
    private Task<ImmutableArray<LitItem>>? _scenery;
    private bool _scenerySet;
    private readonly bool _partyWalk = OS.GetCmdlineUserArgs().Contains("--world-party-walk");
    private float WalkSpeed => _partyWalk ? 1 / .3f : StepsPerSecond;
    private WalkShow? _proof;
    private int _proofTarget = -1;
    private bool _proofHeld;
    private ImmutableArray<string> _proofRoster = [];
    private float _proofSteps;
    private double _frameDelta;

    private UnitArt _art = null!;
    private WorldGround? _ground;
    private ImmutableArray<Tile> _route = [];
    private ImmutableArray<WorldSpot> _spots = [];
    private Stage _stage = Stage.Loading;
    private long _baselineManaged;
    private double _baselineEngine;
    private double _clock;
    private double _crossing;
    private float _steps;
    private int _spot;
    private int _settling;
    private double _waited;
    private bool _secondShot;
    private bool _logged;
    private readonly bool _perfGate;
    private double _peakTextures;
    private bool _gateFailed;
    private ImmutableArray<string> _roster = WorldTextureBudget.Party;
    private int _rosterFrames;
    private bool _mobReview;

    public WorldDemo(string spec, string packRoot, string? shots, string? looks = null, string? walk = null, bool perfGate = false)
    {
        _shots = shots;
        _looks = looks;
        _walk = walk;
        _spec = spec;
        _pack = packRoot;
        _perfGate = perfGate;
    }

    public override void _Ready()
    {
        var size = GetWindow().Size;
        Log($"window {size.X}x{size.Y} at {GetWindow().Position}, spec {_spec}, optimised {Optimised()}, adapter {RenderingServer.GetVideoAdapterName()}");
        _baselineManaged = GC.GetTotalMemory(true);
        _baselineEngine = Performance.GetMonitor(Performance.Monitor.MemoryStatic);
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        _art = UnitArt.Load(_pack);
        var parts = _spec.Split(':');
        var kindled = parts.Contains("kindled", StringComparer.Ordinal);
        var roadAndHost = parts.Contains("road-and-host", StringComparer.Ordinal);
        var goblinRoles = parts.Contains("goblin-roles", StringComparer.Ordinal);
        _mobReview = kindled || roadAndHost || goblinRoles || parts.Contains("mob-refresh", StringComparer.Ordinal);
        if (kindled)
            _roster = ["characters.class_fighter", "characters.bandit", "characters.cultist", "characters.cult_leader", "characters.hobgoblin"];
        else if (roadAndHost)
            _roster = ["characters.class_fighter", "characters.goblin_cutthroat", "characters.bandit", "characters.hobgoblin", "characters.goblin_spear"];
        else if (goblinRoles)
            _roster = ["characters.class_fighter", "characters.goblin_cutthroat", "characters.goblin_archer", "characters.goblin_shaman", "characters.goblin_spear"];
        else if (_mobReview)
            _roster = ["characters.class_fighter", "characters.goblin_cutthroat", "characters.orc_brute", "beasts.drake.red", "characters.kobold"];
        var blocks = parts.Length > 1 && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var b) ? b : WorldTuning.BlockSize;
        _canvas = new WorldCanvas(_pack, () => Ground(parts[0]), blocks);
        AddChild(_canvas);
    }

    private WorldGround Ground(string region)
    {
        var ground = region switch
        {
            Stress => DemoRegion.Hilly(160, 120),
            WoodSpec => DemoRegion.Wood(160, 120),
            TavernSpec => DemoRegion.Tavern(_pack),
            TownSpec => DemoRegion.Town(_pack),
            SwatchSpec => DemoRegion.Swatches(_pack),
            _ => DemoRegion.FromPack(_pack, region),
        };
        var start = region switch
        {
            Stress => new Tile(80, 60),
            WoodSpec => DemoRegion.WoodStart(160, 120),
            TavernSpec => DemoRegion.TavernWalk()[0],
            TownSpec => DemoRegion.TownWalk()[0],
            SwatchSpec => DemoRegion.SwatchLooks()[0].Stand,
            _ when _walk is not null => WorldSpots.Route(_walk)[0],
            _ => RegionStart(region),
        };
        _route = region switch
        {
            _ when _walk is not null => WorldSpots.Route(_walk),
            _ when _looks is not null || region == SwatchSpec => [start, start],
            TavernSpec => DemoRegion.TavernWalk(),
            TownSpec => DemoRegion.TownWalk(),
            _ => WorldRoute.Across(ground.With([], _storyFlags), start, ClassDef.DefaultJump),
        };
        _spots = region switch
        {
            _ when _looks is not null => WorldSpots.Parse(_looks),
            SwatchSpec => DemoRegion.SwatchLooks(),
            TavernSpec => DemoRegion.TavernSpots(),
            TownSpec => DemoRegion.TownSpots(),
            _ => WorldSpots.Of(new BoardTerrain(ground.Map, null), ground.Terrain),
        };
        _ground = ground;
        return ground;
    }

    private Tile RegionStart(string region)
    {
        var file = System.IO.Path.Combine(_pack, "rules", "world", $"{region}.json");
        using var document = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(file));
        var start = document.RootElement.GetProperty("spawns").GetProperty("start");
        return new Tile(start[0].GetInt32(), start[1].GetInt32());
    }

    private static bool Optimised()
        => typeof(WorldDemo).Assembly.GetCustomAttributes(typeof(DebuggableAttribute), false).OfType<DebuggableAttribute>().All(d => !d.IsJITOptimizerDisabled);

    public override void _Process(double delta)
    {
        _frameDelta = delta;
        _clock += delta;
        if (_perfGate)
            _peakTextures = Math.Max(_peakTextures, Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed));
        switch (_stage)
        {
            case Stage.Loading:
                Open();
                break;
            case Stage.Crossing:
                Cross(delta);
                break;
            case Stage.Spots:
                Visit(delta);
                break;
            case Stage.ChangingParty:
            case Stage.RestoringParty:
                CheckPartyChange();
                break;
            case Stage.Done:
                GetTree().Quit(_gateFailed ? 1 : 0);
                break;
        }
    }

    private void Open()
    {
        if (_canvas.Now == WorldCanvas.Stage.Failed)
        {
            _gateFailed = _perfGate;
            _stage = Stage.Done;
            return;
        }
        if (_canvas.Plan is null)
        {
            return;
        }
        if (!_logged)
        {
            _logged = true;
            LogPlan();
            Walk(0);    // behind the fade, so the view's blocks are the right ones and the walker's art is in
        }
        if (_canvas.Now == WorldCanvas.Stage.Open)
        {
            CheckTextures("open");
            var load = _canvas.LoadMs;
            Log($"textures {_canvas.Plan.Images.Length} in {load.Textures:0} ms, worst {load.Worst:0.0} ms; binding items (and baking glows) {load.Bind:0} ms; {_canvas.Waiting} blocks still to record at reveal");
            _stage = Stage.Crossing;
        }
    }

    private void LogPlan()
    {
        var plan = _canvas.Plan!;
        var lit = plan.Blocks.Select(b => b.Lit.Items.Length).ToArray();
        var tiles = plan.Terrain.Map.Width * plan.Terrain.Map.Height;
        Log($"build {_canvas.BuildTimes}");
        Log($"items {lit.Sum():N0} lit; {tiles:N0} tiles, {plan.Blocks.Length} blocks, block items median {Median(lit.Select(i => (double)i)):0}, most {lit.Max()}; "
            + $"{plan.Lighting.Lights.Length} lights, active {plan.Lighting.Active}; {plan.Paths.Length} images; route {_route.Length} tiles; spots {string.Join(' ', _spots.Select(s => $"{s.Name}@{s.Look.X},{s.Look.Y}"))}");
        Log($"memory after build: {MemoryText()}");
    }

    /// <summary>Puts the walker <paramref name="steps"/> along the route and the camera on it.</summary>
    private UnitPlace Walk(float steps)
    {
        _proofSteps = steps;
        var terrain = _canvas.Plan!.Terrain;
        var place = WorldRoute.At(terrain, _route, steps);
        var heading = WorldRoute.Heading(_route, steps);
        _canvas.Place(WorldView.Centred(place.Feet));
        _canvas.Aim(place.Map, heading * StepsPerSecond);
        Show(terrain, place, heading);
        return place;
    }

    /// <summary>The stand-in, and the region's people round where the camera is, as the TV has them.</summary>
    private void Show(BoardTerrain terrain, UnitPlace place, System.Numerics.Vector2 heading, System.Numerics.Vector2? looking = null)
    {
        if (_scenery is null)
            _scenery = Task.Run(() => LitBoard.Build(BoardPlan.StoryProps(terrain, _canvas.Art!, _storyFlags), _canvas.Plan!.Lighting).Items);
        if (!_scenerySet && _scenery.IsCompletedSuccessfully)
        {
            _canvas.SetPlugs(_scenery.Result);
            _scenerySet = true;
        }
        _people ??= new WorldPeople(_canvas.Ground!.People, terrain, _art);
        var walkers = _mobReview ? _roster.SelectMany((sprite, i) => DemoRegion.StandIn(terrain, _art,
                new UnitPlace(place.Map + new System.Numerics.Vector2(-i * 1.2f, i * 1.2f), place.Height), heading, _clock, sprite, $"mob-review-{i}")).ToImmutableArray()
            : _partyWalk ? ProofWalkers(terrain, place) : _perfGate ? DemoRegion.PerfParty(terrain, _art, place, heading, _clock, _roster)
            : DemoRegion.StandIn(terrain, _art, place, heading, _clock);
        _canvas.PartySheets(walkers.Select(u => u.Sprite.Path));
        ImmutableArray<BoardUnit> units = [.. walkers.Concat(_people.Units(_clock, looking ?? place.Feet, [], flags: _storyFlags)).OrderBy(u => u.Order)];
        _canvas.ShowUnits(units, walkers, _art);
    }

    // Development-only evidence uses the real walking/formation presenter, not
    // four sprites offset at a shared height. Inputs have the server's view shape.
    private ImmutableArray<BoardUnit> ProofWalkers(BoardTerrain terrain, UnitPlace place)
    {
        _proof ??= new WalkShow(new BoardTerrain(terrain.Map.WithStoryFlags(_storyFlags), null), _art, null);
        var epoch = _stage == Stage.Spots ? 100 + _spot : 1;
        var moving = _route.Length > 1 && _route.Zip(_route.Skip(1)).Any(p => p.First != p.Second);
        var target = _stage == Stage.Spots || !moving ? 0 : Math.Clamp((int)MathF.Floor(_proofSteps) + 1, 0, _route.Length - 1);
        var held = moving && _stage != Stage.Spots && _proofSteps < _route.Length - 1;
        var at = _stage == Stage.Spots ? new Tile((int)MathF.Floor(place.Map.X), (int)MathF.Floor(place.Map.Y)) : _route[target];
        ImmutableArray<WalkLeg> legs = _stage == Stage.Spots || !moving ? [] : [.. Enumerable.Range(Math.Max(1, target - 11), Math.Min(12, target))
            .Select(i => new WalkLeg(i, _route[i], (int)(1000 / WalkSpeed)))];
        ImmutableArray<WalkerView> party = [.. _roster.Select((sprite, i) => new WalkerView("proof-" + i, "Hero " + (i + 1), sprite, true, i == 0))];
        if (_proofTarget < 0)
            _proof.Receive(new ExploreView(terrain.Map.Id, "Road proof", 1, 0, _route[0], _route[0] with { X = _route[0].X + 1 }, false, [], party, "proof-0") { PeopleFlags = [.. _storyFlags] });
        if (_stage == Stage.Spots || target != _proofTarget || held != _proofHeld || !_proofRoster.SequenceEqual(_roster))
            _proof.Receive(new ExploreView(terrain.Map.Id, "Road proof", epoch, target, at, at with { X = at.X + 1 }, held, legs, party, "proof-0") { PeopleFlags = [.. _storyFlags] });
        (_proofTarget, _proofHeld, _proofRoster) = (target, held, _roster);
        return _proof.Frame(_frameDelta).Walkers;
    }

    private void Cross(double delta)
    {
        var rid = GetViewport().GetViewportRid();
        var (query, draw) = (_canvas.QueryMs, _canvas.OverDrawMs);
        _frames.Add(new Frame(_crossing, delta * 1000, RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid) + RenderingServer.GetFrameSetupTimeCpu(),
            RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid), _canvas.RecordMs, query, draw, _canvas.OverItems,
            Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)));
        _crossing += delta;
        _steps = (float)_crossing * WalkSpeed;
        Walk(_steps);
        if (_steps < _route.Length - 1)
        {
            return;
        }
        LogCrossing();
        Shoot($"world-{_spec.Replace(':', '-')}.png");
        if (_perfGate)
        {
            _roster = WorldTextureBudget.ChangedParty;
            _stage = Stage.ChangingParty;
            _rosterFrames = 0;
        }
        else _stage = _shots is null ? Stage.Done : Stage.Spots;
    }

    private void CheckPartyChange()
    {
        Walk(_route.Length - 1);
        // Submit the new roster, drain the upload budget, then allow two rendered frames for the monitor to settle.
        if (_canvas.PendingUnitTextures > 0) { _rosterFrames = 0; return; }
        if (++_rosterFrames < 3) return;
        CheckTextures(_stage == Stage.ChangingParty ? "party-change" : "party-restored");
        if (_stage == Stage.ChangingParty)
        {
            _roster = WorldTextureBudget.Party;
            _rosterFrames = 0;
            _stage = Stage.RestoringParty;
        }
        else _stage = _shots is null ? Stage.Done : Stage.Spots;
    }

    private void LogCrossing()
    {
        CheckTextures("crossing");
        var all = _frames;
        var warm = all.Where(f => f.Seconds >= ColdSeconds).ToArray();
        var recording = all.Where(f => f.Record > 0).ToArray();
        var over = all.Where(f => f.Ms > RegularFrameMs).ToArray();
        Log($"crossing {_route.Length} tiles at {StepsPerSecond:0.0} tiles/s, {_crossing:0.0} s, {all.Count} frames: frame ms {Spread(all.Select(f => f.Ms))}");
        Log($"crossing after the first {ColdSeconds:0} s: frame ms {Spread(warm.Select(f => f.Ms))}");
        Log($"frames over {RegularFrameMs:0} ms: {over.Length} of {all.Count}" + (over.Length == 0 ? "" : $", at {string.Join(' ', over.Take(12).Select(f => $"{f.Seconds:0.00}s={f.Ms:0.0}"))}, {over.Count(f => f.Record > 0)} while recording"));
        Log($"recording during the walk: {recording.Length} frames, ms a frame {(recording.Length == 0 ? "none" : Spread(recording.Select(f => f.Record)))}");
        if (recording.Length > 0)
        {
            Log($"frames while recording: frame ms {Spread(recording.Select(f => f.Ms))}");
        }
        Log($"_Draw per block, first time: {Spread(_canvas.BlockMs)}, sum {_canvas.BlockMs.Sum():0.0} ms");
        Log($"render CPU ms {Spread(all.Select(f => f.RenderCpu))}; GPU ms {Spread(all.Select(f => f.RenderGpu))}; at most {all.Max(f => f.DrawCalls):N0} draw calls");
        Log($"overlay query ms {Spread(all.Select(f => f.Query))}; draw ms {Spread(all.Select(f => f.Draw))}; items {Spread(all.Select(f => (double)f.Items))}");
        Log($"blocks recorded once, whatever the walk did: {_canvas.RecordedOnce}; recorded again for a fade: {_canvas.Rerecorded}");
        Log($"memory at end of crossing: {MemoryText()}");
    }

    private void CheckTextures(string stage)
    {
        if (!_perfGate) return;
        var bytes = Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed);
        _peakTextures = Math.Max(_peakTextures, bytes);
        var passed = WorldTextureBudget.Passes(_peakTextures, WorldTextureBudget.Party.Length);
        _gateFailed |= !passed;
        Log(FormattableString.Invariant($"texture-budget region={_spec} stage={stage} party=4 textures-bytes={bytes:0} peak-bytes={_peakTextures:0} limit-bytes={WorldTextureBudget.LimitBytes} result={(passed ? "PASS" : "FAIL")}"));
    }

    private void Visit(double delta)
    {
        if (_spot >= _spots.Length)
        {
            _stage = Stage.Done;
            return;
        }
        var spot = _spots[_spot];
        var terrain = _canvas.Plan!.Terrain;
        var place = UnitPlace.On(terrain, spot.Stand);
        _canvas.Place(WorldView.Centred(terrain.Centre(spot.Look)));
        Show(terrain, place, new System.Numerics.Vector2(1, 0), terrain.Centre(spot.Look));
        if (++_settling < FadeFrames)
        {
            return;
        }
        if (!_secondShot)
        {
            Shoot($"world-{spot.Name}.png");
            _secondShot = true;
            _waited = 0;
            return;
        }
        if (terrain.Look(spot.Look).Water)
        {
            _waited += delta;
            if (_waited < SecondLook)
            {
                return;
            }
            Shoot($"world-{spot.Name}-later.png");
        }
        _spot++;
        _settling = 0;
        _secondShot = false;
    }

    /// <summary>With <c>--shots</c>, the picture. It stalls a frame, so none is taken while the walk is being timed.</summary>
    private void Shoot(string name)
    {
        if (_shots is not null)
        {
            var file = System.IO.Path.Combine(_shots, name);
            GetViewport().GetTexture().GetImage().SavePng(file);
            Log($"shot {file}");
        }
    }

    private string MemoryText()
    {
        GC.Collect();
        var managed = GC.GetTotalMemory(true);
        var engine = Performance.GetMonitor(Performance.Monitor.MemoryStatic);
        var textures = Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed);
        var buffers = Performance.GetMonitor(Performance.Monitor.RenderBufferMemUsed);
        return $"managed {Mb(managed):0.0} MB (+{Mb(managed - _baselineManaged):0.0}), engine {Mb(engine):0.0} MB (+{Mb(engine - _baselineEngine):0.0}), "
            + $"textures {Mb(textures):0.0} MB, buffers {Mb(buffers):0.0} MB, process {Mb(System.Environment.WorkingSet):0} MB";
    }

    private static double Mb(double bytes) => bytes / (1024 * 1024);

    private static double Median(IEnumerable<double> values) => Percentile(values, 0.5);

    private static double Percentile(IEnumerable<double> values, double share)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? 0 : sorted[(int)Math.Min(sorted.Length - 1, sorted.Length * share)];
    }

    private static string Spread(IEnumerable<double> values)
    {
        var all = values.ToArray();
        return $"median {Median(all):0.00}, p95 {Percentile(all, 0.95):0.00}, p99 {Percentile(all, 0.99):0.00}, worst {(all.Length == 0 ? 0 : all.Max()):0.00}";
    }

    private static void Log(string line) => GD.Print($"WORLD {line}");
}
