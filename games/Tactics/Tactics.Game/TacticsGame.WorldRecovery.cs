using ArcBox.Sdk;

namespace Tactics.Game;

public sealed partial class TacticsGame
{
    /// <summary>A test seam for failures after a world operation has changed or saved state.</summary>
    internal Action<string>? WorldActionCompleted { get; set; }
    private string? _worldRecoveryNotice;

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
            WorldActionCompleted?.Invoke(path);
            // Keep the header stable while the thumb is held; clear it on the next deliberate action.
            if (path.StartsWith("input:", StringComparison.Ordinal) && path != "input:walk") _worldRecoveryNotice = null;
        }
        catch (Exception error) when (IsEngineFailure(error))
        {
            if (before.Battle is { } battle) Fault(path, battle.State, null, error, tv: false);
            try
            {
                var message = $"TACTICS world recovery ({path}): {error}";
                if (StoryLogged is { } logged) logged(message);
                else Console.Error.WriteLine(message);
            }
            catch (Exception loggingError) when (IsEngineFailure(loggingError)) { /* Logging cannot prevent recovery. */ }
            foreach (var timer in new[] { Timers.Step, Timers.Save, Timers.Lead, Timers.Line, Timers.Vote, Timers.Pace, Timers.Turn, Timers.React, CampTimerKey, "questResult", "townBark" })
                _ctx.CancelTimer(timer);
            // Roll back purchases/rewards as well as the cursor. Never reopen a broken active cursor.
            _doc = before with { Battle = null, World = before.World with { Story = null, Shop = null, Camp = null } };
            (_dir, _blocked, _owed, _latched) = (null, false, false, false);
            _legs = [];
            (_saveBy, _saveAt) = (null, null);
            EndTelling();
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
            _questObservedFlags = StoryFlags();
            _phase = TacticsPhase.Exploration;
            EnterWorld(); // validates the saved tile, or uses the region's start
            _epoch = Math.Max(epoch, _epoch) + 1;
            NoteAppeared();
            _sessionLead = sessionLead ?? before.World.LastLeader;
            var candidates = _players.Where(p => _online.Contains(p.Id) && p.Profile is { } member && before.For(member) is not null).ToArray();
            _lead = candidates.FirstOrDefault(p => p.Profile?.Value == (lead ?? _sessionLead))?.Profile?.Value
                ?? candidates.FirstOrDefault()?.Profile?.Value;
            _sessionLead ??= _lead;
            _silent.Clear();
            _electionOpen = false;
            (_heardAt, _settledAt) = (_ctx.Now, _ctx.Now);
            KeepWorld();
            // Do not re-enter quest/reward processing while recovering from its exception.
            _lease.Save(_doc.ToJson());
            LeadClock();
            const string notice = "The story has changed. The party is safely back on the road; try again.";
            _worldRecoveryNotice = before.Battle is null ? notice : BattleCalledOff;
            Note(_worldRecoveryNotice);
        }
    }
}
