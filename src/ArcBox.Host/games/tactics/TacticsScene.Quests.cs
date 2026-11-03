using Tactics.Game;
using Tactics.Presentation.World;

namespace ArcBox.Host.Games.Tactics;

public partial class TacticsScene
{
    private bool _questSoundSeen, _questBookSound;
    private string? _questPinSound, _talkSound;
    private (int Epoch,int Seq)? _questEventSound;

    private void QuestAudio(TacticsHostView view)
    {
        LoadSound();
        var explore=view.Explore;
        var open=explore?.QuestLog is not null;
        var pin=explore?.Pinned?.Id;
        var talk=view.Story?.Scene.StartsWith("talk_",StringComparison.Ordinal)==true?view.Story.Scene:null;
        void Play(string cue)
        {
            var wanted=QuestSounds.Wanted(cue).FirstOrDefault(id=>_sound?.Has(id)==true);
            if(wanted is not null)_audio.Play(wanted,-10);
        }
        if(open!=_questBookSound)Play(open?"open":"close");
        if(_questSoundSeen&&pin is not null&&pin!=_questPinSound&&explore?.QuestEvent?.Kind!="start")Play("pin");
        if(talk is not null&&talk!=_talkSound)Play("talk");
        if(view.Phase==nameof(TacticsPhase.Exploration)&&explore?.QuestEvent is { } ev&&ev.Until>DateTimeOffset.UtcNow
            &&_questEventSound!=(explore.Epoch,ev.Seq))
        {
            _questEventSound=(explore.Epoch,ev.Seq);Play(ev.Kind);
        }
        (_questSoundSeen,_questBookSound,_questPinSound,_talkSound)=(true,open,pin,talk);
    }
}
