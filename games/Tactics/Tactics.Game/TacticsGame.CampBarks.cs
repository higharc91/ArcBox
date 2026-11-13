using Tactics.Logic;

namespace Tactics.Game;

/// <summary>
/// Camp and level-up barks (lore spec §7): one line from someone present, through the town bark channel (<see cref="BarkView"/>).
/// Present means a placed person with lines for the moment standing near the fire, the inn or the party, a party member in their
/// voice (camp only), or the narrator. The rest count or the party level picks who speaks and the line, so a restart says the same.
/// </summary>
public sealed partial class TacticsGame
{
    /// <summary>How near a placed person must stand to the camp or the party to be there for its barks.</summary>
    private const int PresentWithin = 8;

    private sealed record BarkSpeaker(string Speaker, string? Name, string? Portrait, BarkSet Set, string? Register);

    /// <summary>A line as the party settles to sleep.</summary>
    private void CampBark()
    {
        if (Camp is not { } camp || _doc.World is not { } world) return;
        var speakers = Near(camp.At, "camp").ToList();
        foreach (var actor in camp.Actors)
            if (_doc.Members.GetValueOrDefault(actor)?.Character is { } character
                && PlayerVoices.TryParse(PlayerVoices.Of(character), out var set, out var register)
                && Lore.Barks.ForVoice(set, "camp") is { } lines)
                speakers.Add(new BarkSpeaker(character.Name, character.Name, null, lines, register));
        if (Lore.Barks.ForBattle(TacticsVoices.NarratorSpeaker, "camp") is { } narrator)
            speakers.Add(new BarkSpeaker(TacticsVoices.NarratorSpeaker, null, null, narrator, null));
        Bark("camp", world.RestSeq, speakers);
    }

    /// <summary>A line when a battle has raised the party's level.</summary>
    internal void LevelUpBark()
    {
        var speakers = Near(_at, "level_up").ToList();
        if (Lore.Barks.ForBattle(TacticsVoices.NarratorSpeaker, "level_up") is { } narrator)
            speakers.Add(new BarkSpeaker(TacticsVoices.NarratorSpeaker, null, null, narrator, null));
        Bark("level_up", _doc.PartyLevel, speakers);
    }

    /// <summary>The placed people near <paramref name="at"/> with lines for <paramref name="moment"/>.</summary>
    private IEnumerable<BarkSpeaker> Near(Tile at, string moment)
        => PresentPeople()
            .Where(p => p.Person.Character is not null && Reach.Distance(p.Person.At, at) <= PresentWithin)
            .Select(p => (p.Person, Set: Lore.Barks.ForBattle(p.Person.Character!, moment)))
            .Where(p => p.Set is not null)
            .Select(p => new BarkSpeaker(p.Person.Character!, Lore.Character(p.Person.Character!)?.Name, p.Person.Preset, p.Set!, null));

    private void Bark(string moment, long counter, List<BarkSpeaker> speakers)
    {
        if (speakers.Count == 0) return;
        var flags = StoryFlags();
        for (var k = 0; k < speakers.Count; k++)
        {
            var who = speakers[(int)((counter + k) % speakers.Count)];
            if (who.Set.PickWeighted(flags, counter) is not { } line) continue;
            // A player voice's recording is filed under its register: "<line id>#<low|high>" (VoiceClips).
            var id = who.Register is { } register ? $"{line.Id}#{register}" : line.Id;
            _bark = new BarkView(moment, counter + 1, _ctx.Now.AddSeconds(Math.Max(4, Lore.ClipSeconds(line.Id) ?? line.Text.Length / 14d)),
                new StoryLineView(id, who.Speaker, who.Name, who.Portrait, line.Mood, line.Text));
            _barkNote = (who.Name is { } name ? name + ": " : "") + line.Text;
            Say(_barkNote);
            _ctx.SetTimer("townBark", _bark.Until - _ctx.Now);
            return;
        }
    }
}
