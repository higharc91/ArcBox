using System.Globalization;

namespace Tactics.Presentation;

/// <summary>Turning content ids into words for the TV.</summary>
public static class Words
{
    /// <summary>A content id as a title: <c>river_crossing</c> → "River Crossing".</summary>
    public static string Title(string id)
        => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Replace('_', ' '));
}
