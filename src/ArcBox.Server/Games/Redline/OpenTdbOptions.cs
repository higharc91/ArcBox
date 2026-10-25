namespace ArcBox.Server.Games.Redline;

/// <summary>Settings for the OpenTDB client and question pool; binds from <c>Redline:OpenTdb</c>.</summary>
public sealed class OpenTdbOptions
{
    public string BaseUrl { get; set; } = "https://opentdb.com";
    public int Target { get; set; } = 150;
    public int LowWater { get; set; } = 60;
    public TimeSpan MinRequestSpacing { get; set; } = TimeSpan.FromSeconds(5.5);
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(5);
    public int DedupeWindow { get; set; } = 500;
    public bool Enabled { get; set; } = true;
}
