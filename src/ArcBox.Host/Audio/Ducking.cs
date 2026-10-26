using ArcBox.Host.Core.Audio;
using Godot;

namespace ArcBox.Host.Audio;

/// <summary>
/// Dips Music (−12 dB) and Sfx (−6 dB) while the narrator speaks, with a 200 ms attack and 600 ms release
/// (<see cref="DuckEnvelope"/>), including while the built-in voice speaks (the narrator follows its utterance callbacks).
/// </summary>
public partial class Ducking : Node
{
    private readonly DuckEnvelope _envelope = new();

    /// <summary>True while a voice is audible.</summary>
    public Func<bool> VoiceActive { get; set; } = () => false;

    public override void _Ready() => Name = "Ducking";

    public override void _Process(double delta)
    {
        _envelope.Step(VoiceActive(), delta);
        AudioBuses.SetDuck(_envelope.MusicDb, _envelope.SfxDb);
    }
}
