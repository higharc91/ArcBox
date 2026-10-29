using Godot;

namespace ArcBox.Host.Audio;

/// <summary>
/// <c>--audio-log</c>: every sound the TV plays prints <c>AUDIO &lt;bus&gt; &lt;name&gt;</c> (narration:
/// <c>AUDIO Voice &lt;theme&gt;: &lt;text&gt;</c>), so runs without a sound card, and <c>tools/tv-e2e.sh</c>, can check that
/// sounds fire and in what order.
/// </summary>
public static class AudioLog
{
    public static bool Enabled { get; set; }

    public static void Play(string bus, string name)
    {
        if (Enabled)
        {
            GD.Print($"AUDIO {bus} {name}");
        }
    }
}
