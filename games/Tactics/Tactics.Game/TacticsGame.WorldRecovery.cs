using ArcBox.Sdk;

namespace Tactics.Game;

public sealed partial class TacticsGame
{
    /// <summary>A test seam for failures after a world operation has changed or saved state.</summary>
    internal Action<string>? WorldActionCompleted { get; set; }
    /// <summary>A test seam for failures while validating a saved world.</summary>
    internal Action? WorldEntering { get; set; }
    private string? _worldRecoveryNotice;

    /// <summary>Found items kept through a recovery, with no vote under way and no cursor to put back: the next walk asks again.</summary>
    private static LootSave? SetDown(LootSave? loot)
        => loot is null || loot.Items.IsEmpty ? null : loot with { Voting = false, Under = null, Choices = [], Service = null };

    private void WorldCall(string path, Action action)
    {
        var before = _doc;
        var epoch = _epoch;
        var lead = _lead;
        var sessionLead = _sessionLead;
        // Battle commands retain their existing recovery rules. Resume always gets a fallback.
        if (before.World is null || _phase == TacticsPhase.Battle && path != "resume") { action(); return; }
        try
        {
            action();
            LootAfterAction();
            WorldActionCompleted?.Invoke(path);
            // Keep the header stable while the thumb is held; clear it on the next deliberate action.
            if (path.StartsWith("input:", StringComparison.Ordinal) && path != "input:walk") _worldRecoveryNotice = null;
        }
        catch (Exception error) when (IsEngineFailure(error))
        {
            try
            {
                if (before.Battle is { } battle) Fault(path, battle.State, null, error, tv: false);
                try
                {
                    var message = $"TACTICS world recovery ({path}): {error}";
                    if (StoryLogged is { } logged) logged(message);
                    else Console.Error.WriteLine(message);
                }
                catch (Exception loggingError) when (IsEngineFailure(loggingError)) { /* Logging cannot prevent recovery. */ }
                foreach (var timer in RecoveryTimers)
                    _ctx.CancelTimer(timer);
                // Roll back purchases/rewards as well as the cursor. Never reopen a broken active cursor.
                _doc = before with { Battle = null, World = before.World with { Story = null, Shop = null, Camp = null, MobAmbush = null, Loot = SetDown(before.World.Loot) } };
                ResetWorldRecoveryState();
                _questObservedFlags = StoryFlags();
                EnterWorld(); // validates the saved tile, or uses the region's start
                _epoch = Math.Max(epoch, _epoch) + 1;
                NoteAppeared();
                _sessionLead = sessionLead ?? before.World.LastLeader;
                var candidates = _players.Where(p => _online.Contains(p.Id) && p.Profile is { } member && before.For(member) is not null).ToArray();
                _lead = candidates.FirstOrDefault(p => p.Profile?.Value == (lead ?? _sessionLead))?.Profile?.Value
                    ?? candidates.FirstOrDefault()?.Profile?.Value;
                _sessionLead ??= _lead;
                (_heardAt, _settledAt) = (_ctx.Now, _ctx.Now);
                KeepWorld();
                // Do not re-enter quest/reward processing while recovering from its exception.
                _lease.Save(_doc.ToJson());
                LeadClock();
                ArmMobs();
                const string notice = "The story has changed. The party is safely back on the road; try again.";
                _worldRecoveryNotice = before.Battle is null ? notice : BattleCalledOff;
                Note(_worldRecoveryNotice);
            }
            catch (Exception recoveryError) when (IsEngineFailure(recoveryError))
            {
                // No world validation, quest processing, callbacks or timers before this save.
                var world = before.World!;
                var region = Rules.RegionFor(world.Region) ?? Rules.Regions.Values.MinBy(r => r.Def.Chapter);
                var at = region?.Def.Spawns.Start ?? world.At;
                _doc = before with
                {
                    Battle = null,
                    World = world with
                    {
                        Region = region?.Def.Id ?? world.Region, At = at, Facing = new(at.X + 1, at.Y),
                        Story = null, Shop = null, Camp = null, MobAmbush = null, Loot = SetDown(world.Loot),
                    },
                };
                _lease.Save(_doc.ToJson()); // Persist first: even a later host failure cannot reopen the broken cursor.
                ResetWorldRecoveryState();
                _region = region;
                (_at, _facing) = (at, _doc.World.Facing);
                _epoch = Math.Max(epoch, _epoch) + 1;
                _storySeq = Math.Max(world.StorySeq, world.Story?.Seq ?? 0);
                _thingSeq = Math.Max(_thingSeq, world.ThingSeq);
                _questObservedFlags = world.Flags.ToHashSet(StringComparer.Ordinal);
                _sessionLead = sessionLead ?? world.LastLeader;
                _lead = _players.FirstOrDefault(p => _online.Contains(p.Id) && p.Profile is { } member && before.For(member) is not null)?.Profile?.Value;
                _worldRecoveryNotice = "The story has changed. The party is safely back at the road's beginning; try again.";
                _notice = _worldRecoveryNotice;
                // Timers and diagnostic sinks are best effort, after the safe document is durable.
                foreach (var timer in RecoveryTimers)
                    try { _ctx.CancelTimer(timer); }
                    catch (Exception timerError) when (IsEngineFailure(timerError)) { }
                try
                {
                    var message = $"TACTICS minimal world recovery ({path}): original={error}; recovery={recoveryError}";
                    if (StoryLogged is { } logged) logged(message);
                    else Console.Error.WriteLine(message);
                }
                catch (Exception loggingError) when (IsEngineFailure(loggingError)) { }
            }
        }
    }

    private static readonly string[] RecoveryTimers = [MobTimer, MobAmbushTimer, Timers.Step, Timers.Save, Timers.Lead, Timers.Line, Timers.Vote, Timers.Pace, Timers.Turn, Timers.React, CampTimerKey, "questResult", "townBark"];

    // Pure in-memory cleanup, also usable when host callbacks or world initialization have failed.
    private void ResetWorldRecoveryState()
    {
        _mobActors.Clear();
        _mobDirty = false;
        (_dir, _blocked, _owed, _latched) = (null, false, false, false);
        _legs = [];
        (_saveBy, _saveAt) = (null, null);
        ResetQuestion();
        _telling = StoryStates.Lines;
        _card = null;
        _questEvent = null;
        _bark = null;
        _barkNote = null;
        _thingEvents = [];
        _thingNote = null;
        _tradeFeed.Clear();
        _toasts.Clear();
        _notice = null;
        _leadNote = null;
        _faultAt = null;
        _passive = null;
        _walkGround = null;
        _walkReach = null;
        _appeared.Clear();
        _awaited.Clear();
        _questLogLeader = null;
        _silent.Clear();
        _electionOpen = false;
        _phase = TacticsPhase.Exploration;
    }
}
