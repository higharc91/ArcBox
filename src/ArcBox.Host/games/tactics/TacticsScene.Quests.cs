using Tactics.Game;
using Tactics.Presentation.Audio;
using Tactics.Presentation.World;

namespace ArcBox.Host.Games.Tactics;

public partial class TacticsScene
{
    private bool _questSoundSeen, _questBookSound;
    private string? _questPinSound, _talkSound;
    private (int Epoch,int Seq)? _questEventSound;
    private string? _questRegionSound;
    private bool _bountyAcceptedSound;
    private (int Epoch, QuestEventView Notice)? _giftNotice;
    private (int Epoch, int Seq, int Index)? _giftSound;

    private void QuestCue(string cue)
    {
        var wanted = QuestSounds.Wanted(cue).FirstOrDefault(id => _sound?.Has(id) == true);
        if (wanted is not null) _audio.Play(wanted, -10);
    }

    private void GiftAudio()
    {
        if (_giftNotice is not { } current || current.Notice.Gifts is not { Length: > 0 } gifts) return;
        var remaining = (current.Notice.Until - DateTimeOffset.UtcNow).TotalSeconds;
        if (remaining <= 0) return;
        var elapsed = Math.Max(0, Math.Max(7, gifts.Length * 2.4 + 1) - remaining);
        var key = (current.Epoch, current.Notice.Seq, Math.Min(gifts.Length - 1, (int)(elapsed / 2.4)));
        if (_giftSound == key) return;
        _giftSound = key;
        QuestCue("present");
    }

    private void QuestAudio(TacticsHostView view)
    {
        LoadSound();
        _audio.CreatureAmbience(MusicChoice.CreatureAmbienceOf(view));
        var explore=view.Explore;
        var open=explore?.QuestLog is not null;
        var pin=explore?.Pinned?.Id;
        var talk=view.Story?.Scene.StartsWith("talk_",StringComparison.Ordinal)==true?view.Story.Scene:null;
        void Play(string cue) => QuestCue(cue);
        var accepted = explore?.PeopleFlags?.Contains("sq_ashmaw_accepted") == true;
        if (_questSoundSeen && accepted && !_bountyAcceptedSound) Play("accept");
        if (_questRegionSound is { } prior && explore is { } next && prior != next.Region
            && (prior == "cinder_hollow" || next.Region == "cinder_hollow")) Play("enter");
        if (explore is not null) _questRegionSound = explore.Region;
        _bountyAcceptedSound = accepted;
        _giftNotice = view.Phase == nameof(TacticsPhase.Exploration) && explore?.QuestEvent is { Gifts: { Length: > 0 } } gift
            ? (explore.Epoch, gift) : null;
        if(open!=_questBookSound)Play(open?"open":"close");
        if(_questSoundSeen&&pin is not null&&pin!=_questPinSound&&explore?.QuestEvent?.Kind!="start")Play("pin");
        if(talk is not null&&talk!=_talkSound)Play("talk");
        if(view.Phase==nameof(TacticsPhase.Exploration)&&explore?.QuestEvent is { } ev&&ev.Until>DateTimeOffset.UtcNow
            &&_questEventSound!=(explore.Epoch,ev.Seq))
        {
            _questEventSound=(explore.Epoch,ev.Seq);
            if (ev.Gifts is not { Length: > 0 }) Play(ev.Kind);
        }
        (_questSoundSeen,_questBookSound,_questPinSound,_talkSound)=(true,open,pin,talk);
    }
}
