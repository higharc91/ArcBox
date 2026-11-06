using System.Globalization;
using System.Text;
using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using ArcBox.Sdk.Testing;
using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>
/// The wait between one player's turns in a big party (issue #113), measured at the Game level on a fake clock: six bot phones play a
/// battle against about ten foes, and the run reports seconds per round and the longest wait between one player's turns, before the
/// reaction preferences and the short clock (every reaction asked, a full minute whatever is left to do) and after them.
/// <para>The phones are people in a hurry or not: <see cref="Bot.ThinkSeconds"/> to start a turn, a second between taps, and a reaction
/// prompt answered after <see cref="Bot.ReactSeconds"/>. A "brisk" bot taps End turn once it is done; a "dawdler" never does, so its
/// clock runs out (the worst case the short clock is for). Set <c>TURN_WAIT_REPORT</c> to a file to have the numbers written there.</para>
/// </summary>
public sealed class TurnWaitTests
{
    private static readonly string[] Classes = ["fighter", "cleric", "rogue", "wizard", "fighter", "rogue"];

    private sealed record Bot(double ThinkSeconds, double ReactSeconds, bool Dawdles);

    private sealed record Result(string Name, int Rounds, double Seconds, int Prompts, int Foes, double Longest, string LongestWho, List<double> Waits, int Turns, double PromptSeconds)
    {
        public double Mean => Waits.Count == 0 ? 0 : Waits.Average();
    }

    /// <summary>The same table over several seeds as one run: the time and rounds add up, the longest wait is the longest of them.</summary>
    private static Result Over(string name, TacticsTiming timing, Bot bot, bool prefs, params int[] seeds)
    {
        var runs = seeds.Select(s => Play(name, timing, Encounter, bot, prefs, Rounds, s)).ToList();
        var worst = runs.MaxBy(r => r.Longest)!;
        return new Result(name, runs.Sum(r => r.Rounds), runs.Sum(r => r.Seconds), runs.Sum(r => r.Prompts), runs[0].Foes, worst.Longest, worst.LongestWho,
            [.. runs.SelectMany(r => r.Waits)], runs.Sum(r => r.Turns), runs.Sum(r => r.PromptSeconds));
    }

    private sealed class Table
    {
        public Table(TacticsTiming timing, string encounter, int seed = 1)
        {
            Profiles = [.. Enumerable.Range(1, 6).Select(i => (ProfileId?)new ProfileId($"pr_member{i}"))];
            Game = new TacticsGame(Rules, timing, battleList: true);
            Harness = new GameHarness<TacticsGame>(Game, playerCount: 6, seed: seed, profiles: Profiles);
            var info = new CampaignInfo("c1", "ABCD", TacticsGame.Id, "Embers", Profiles[0]!.Value,
                [.. Profiles.Select((p, i) => new CampaignMember(p!.Value, $"Player{i + 1}", i == 0 ? CampaignRole.Owner : CampaignRole.Player, DateTimeOffset.UnixEpoch))], 1);
            Lease = new FakeCampaignLease(info, new TacticsCampaignRules(Rules).NewDocument(info));
            Harness.StartCampaign(Lease);
            for (var i = 0; i < 6; i++)
                Send(i, "pickClass", new { @class = Classes[i] });
            Send(0, "battles");
            Send(0, "pickEncounter", new { encounter });
            if (Game.Phase == TacticsPhase.Battle && Host.Battle!.Stage is not null)
                Harness.HostSignal("shown", new { seq = Host.Battle.Beats[^1].Seq });
        }

        public IReadOnlyList<ProfileId?> Profiles { get; }
        public TacticsGame Game { get; }
        public GameHarness<TacticsGame> Harness { get; }
        public FakeCampaignLease Lease { get; }
        public TacticsHostView Host => Harness.HostView<TacticsHostView>();
        public void Send(int player, string action, object? data = null) => Harness.Input(player, new { action, data });
        public BattleState State => Game.Document.Battle!.State;
    }

    private static string Tag(UnitId id) => id.Value;

    /// <summary>Plays the table until round <paramref name="rounds"/> is over or the battle ends; the bots tap on the fake clock.</summary>
    private static Result Play(string name, TacticsTiming timing, string encounter, Bot bot, bool prefs, int rounds, int seed)
    {
        var t = new Table(timing, encounter, seed);
        Assert.Equal(TacticsPhase.Battle, t.Game.Phase);
        if (prefs)
            for (var p = 0; p < 6; p++)
                t.Send(p, "reactionPrefs", new { opportunity = "always", defence = "miss" });

        var foes = t.State.Units.Values.Count(u => !u.Id.Value.StartsWith("hero"));
        var started = t.Harness.Now;
        var seenSince = new Dictionary<int, DateTimeOffset>();    // when a player's turn screen first showed this turn
        var lastAct = new Dictionary<int, DateTimeOffset>();       // when the bot last tapped
        var endedAt = new Dictionary<UnitId, DateTimeOffset>();    // when each hero's last turn ended
        UnitId? previous = null;
        var longest = 0.0;
        var longestWho = "";
        var waits = new List<double>();
        var prompts = 0;
        var promptSeconds = 0.0;
        var turns = 0;

        for (var step = 0; step < 200_000 && t.Game.Phase == TacticsPhase.Battle && t.State.Round <= rounds; step++)
        {
            var now = t.Harness.Now;
            var state = t.State;

            // Who is up, and the wait since the same player last finished a turn (heroes only: a summon is its owner's phone).
            if (state.Active != previous)
            {
                if (previous is { } was && was.Value.StartsWith("hero"))
                    endedAt[was] = now;
                if (state.Active is { } up && up.Value.StartsWith("hero"))
                {
                    turns++;
                    if (endedAt.TryGetValue(up, out var ended))
                    {
                        var wait = (now - ended).TotalSeconds;
                        waits.Add(wait);
                        if (wait > longest)
                            (longest, longestWho) = (wait, $"{Tag(up)} in round {state.Round}");
                    }
                }
                previous = state.Active;
            }

            for (var p = 0; p < 6; p++)
            {
                var screen = t.Game.Phase == TacticsPhase.Battle ? t.Harness.PlayerView(p) as CustomView : null;
                if (screen is null)
                    continue;
                if (screen.Screen == TacticsGame.Screens.React)
                {
                    if (seenSince.TryAdd(-1 - p, now))
                        prompts++;
                    var since = seenSince[-1 - p];
                    if ((now - since).TotalSeconds >= bot.ReactSeconds)
                    {
                        var data = screen.Data;
                        var options = data.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("id").GetString()!).ToList();
                        t.Send(p, "react", new { choice = options.Contains("opportunity") ? "opportunity" : "pass" });
                        promptSeconds += (now - since).TotalSeconds;
                        seenSince.Remove(-1 - p);
                    }
                    continue;
                }
                seenSince.Remove(-1 - p);
                if (screen.Screen != TacticsGame.Screens.Turn)
                {
                    seenSince.Remove(p);
                    continue;
                }
                if (!seenSince.TryGetValue(p, out var shown))
                {
                    seenSince[p] = now;
                    lastAct[p] = now;
                    continue;
                }
                var unit = state.Active!.Value;
                var started1 = (now - shown).TotalSeconds >= bot.ThinkSeconds;
                if (!started1 || (now - lastAct[p]).TotalSeconds < 1)
                    continue;
                lastAct[p] = now;
                if (Tap(t, p, unit, bot) is false && !bot.Dawdles)
                    t.Send(p, "endTurn");
            }
            t.Harness.Advance(TimeSpan.FromSeconds(0.5));
        }

        var seconds = (t.Harness.Now - started).TotalSeconds;
        var played = Math.Max(1, Math.Min(rounds, t.Game.Phase == TacticsPhase.Battle ? t.State.Round - 1 : rounds));
        return new Result(name, played, seconds, prompts, foes, longest, longestWho, waits, turns, promptSeconds);
    }

    /// <summary>One tap for the unit that's up: an attack if one is legal, else a step toward the nearest foe. False when there's nothing left to do.</summary>
    private static bool Tap(Table t, int player, UnitId unit, Bot bot)
    {
        var state = t.State;
        var choices = Choices.For(state, unit, Rules);
        var attack = choices.Abilities.Where(a => a.Targets.Length > 0 && a.Cost.Action > 0 && a.Cost.Resource?.IsEmpty != false)
            .Where(a => a.Targets.Any(tc => tc.Target.Unit is { } u && state.Units[u].Controller is AiControlled))
            .FirstOrDefault();
        if (attack is not null)
        {
            var target = attack.Targets.First(tc => tc.Target.Unit is { } u && state.Units[u].Controller is AiControlled);
            t.Send(player, "use", new { ability = attack.Ability.Value, unit = target.Target.Unit!.Value.Value });
            return true;
        }
        var foes = state.Units.Values.Where(u => u.Controller is AiControlled && DownedRules.InBattle(u)).ToList();
        var me = state.Units[unit];
        if (choices.Moves.Length > 0 && foes.Count > 0)
        {
            int D(Tile a, Tile b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
            var best = choices.Moves.OrderBy(m => foes.Min(f => D(m.To, f.Pos))).First();
            if (foes.Min(f => D(best.To, f.Pos)) < foes.Min(f => D(me.Pos, f.Pos)))
            {
                t.Send(player, "move", new { path = best.Path.Select(x => new[] { x.X, x.Y }).ToArray() });
                return true;
            }
        }
        return false;
    }

    private static TacticsTiming Before => TacticsTiming.Default with { SpentTurn = TimeSpan.FromSeconds(60) };

    private static TacticsTiming After => TacticsTiming.Default;

    private const string Encounter = "crypt_hall";     // 10 foes against a party of 6
    private const int Rounds = 4;
    private static readonly int[] Seeds = [1, 2, 3, 4];

    private static readonly Bot Brisk = new(ThinkSeconds: 12, ReactSeconds: 6, Dawdles: false);
    private static readonly Bot Dawdler = new(ThinkSeconds: 12, ReactSeconds: 6, Dawdles: true);

    [Fact]
    public void Six_phones_against_ten_foes_wait_less_between_their_turns()
    {
        var runs = new List<(Result Before, Result After)>
        {
            (Over("brisk", Before, Brisk, prefs: false, Seeds), Over("brisk", After, Brisk, prefs: true, Seeds)),
            (Over("dawdler", Before, Dawdler, prefs: false, Seeds), Over("dawdler", After, Dawdler, prefs: true, Seeds)),
        };

        var report = new StringBuilder();
        foreach (var (before, after) in runs)
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"{before.Name}: foes {before.Foes}, rounds {before.Rounds}/{after.Rounds}, " +
                $"s/round {before.Seconds / before.Rounds:F1} -> {after.Seconds / after.Rounds:F1}, " +
                $"longest wait {before.Longest:F1} -> {after.Longest:F1} s ({before.LongestWho} / {after.LongestWho}), " +
                $"mean wait {before.Mean:F1} -> {after.Mean:F1} s, prompts {before.Prompts} -> {after.Prompts} " +
                $"({before.PromptSeconds:F0} -> {after.PromptSeconds:F0} s the table stood still for them), turns {before.Turns}/{after.Turns}");
        }
        if (Environment.GetEnvironmentVariable("TURN_WAIT_REPORT") is { Length: > 0 } path)
            File.WriteAllText(path, report.ToString());

        Assert.All(runs, r => Assert.Equal(10, r.Before.Foes));
        Assert.All(runs, r => Assert.Equal(0, r.After.Prompts));          // nothing paused the game for a player who opted in
        Assert.All(runs, r => Assert.True(r.After.Longest <= r.Before.Longest, report.ToString()));
        Assert.True(runs[1].After.Seconds / runs[1].After.Rounds < runs[1].Before.Seconds / runs[1].Before.Rounds, report.ToString());
    }
}
