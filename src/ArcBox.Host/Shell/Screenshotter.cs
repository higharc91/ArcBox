using Godot;

namespace ArcBox.Host.Shell;

/// <summary>
/// <c>--shots=&lt;dir&gt;</c>: saves the viewport as <c>&lt;seq&gt;-&lt;label&gt;.png</c> after the next drawn frame.
/// Add it to the tree before calling <see cref="Capture"/>.
/// </summary>
public partial class Screenshotter : Node
{
    private int _seq;

    public Screenshotter()
    {
        Dir = "";
    }

    public Screenshotter(string dir)
    {
        Dir = ProjectSettings.GlobalizePath(dir);
        Name = "Screenshotter";
        System.IO.Directory.CreateDirectory(Dir);
    }

    public string Dir { get; }

    /// <summary>Last saved file, for logs and tests.</summary>
    public string? LastPath { get; private set; }

    /// <summary>Saves the frame after this one (so layout changes made this frame are visible) as <c>NNN-label.png</c>.</summary>
    public async void Capture(string label)
    {
        var name = $"{++_seq:000}-{Sanitise(label)}.png";
        // Two frames: containers settle their sizes one frame after a screen is swapped in.
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (!IsInsideTree())
        {
            return;
        }

        var image = GetViewport().GetTexture().GetImage();
        var path = System.IO.Path.Combine(Dir, name);
        var err = image.SavePng(path);
        if (err == Error.Ok)
        {
            LastPath = path;
            GD.Print($"SHOT {path}");
        }
        else
        {
            GD.PushWarning($"Screenshot {path} failed: {err}");
        }
    }

    private static string Sanitise(string label)
    {
        var chars = label.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '-').ToArray();
        return new string(chars).Trim('-');
    }
}
