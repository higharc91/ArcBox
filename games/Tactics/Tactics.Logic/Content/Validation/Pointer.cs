using System.Globalization;

namespace Tactics.Logic;

/// <summary>JSON pointers (RFC 6901) for content errors.</summary>
internal static class Pointer
{
    /// <summary>The pointer to property <paramref name="name"/> of the value at <paramref name="ptr"/>.</summary>
    public static string Append(string ptr, string name) => ptr + "/" + name.Replace("~", "~0").Replace("/", "~1");

    /// <summary>The pointer to item <paramref name="index"/> of the value at <paramref name="ptr"/>.</summary>
    public static string Append(string ptr, int index) => ptr + "/" + index.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// <paramref name="ptr"/> followed by a System.Text.Json path inside it (<c>$.target.range</c>, <c>$.effects[0]</c>,
    /// <c>$['odd.name']</c>).
    /// </summary>
    public static string Combine(string ptr, string? path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '$')
            return ptr;
        var i = 1;
        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                var end = path.IndexOfAny(['.', '['], i + 1);
                end = end < 0 ? path.Length : end;
                ptr = Append(ptr, path[(i + 1)..end]);
                i = end;
            }
            else if (path[i] == '[' && i + 1 < path.Length && path[i + 1] == '\'')
            {
                var end = path.IndexOf("']", i + 2, StringComparison.Ordinal);
                if (end < 0)
                    break;
                ptr = Append(ptr, path[(i + 2)..end]);
                i = end + 2;
            }
            else if (path[i] == '[')
            {
                var end = path.IndexOf(']', i + 1);
                if (end < 0)
                    break;
                ptr += "/" + path[(i + 1)..end];
                i = end + 1;
            }
            else
                break;
        }
        return ptr;
    }

    /// <summary>
    /// A System.Text.Json message without its trailing <c>Path: … | LineNumber: …</c>, which the pointer replaces. Its
    /// generic "could not be converted to &lt;type&gt;" names the enclosing record rather than the value, so it is replaced, and
    /// its "could not be mapped" for an unknown property becomes "Unknown property '…'".
    /// </summary>
    public static string CleanMessage(string message)
    {
        if (message.StartsWith("The JSON value could not be converted to ", StringComparison.Ordinal))
            return "The value has the wrong type, or isn't one of the allowed values.";
        const string unmapped = "The JSON property '";
        if (message.StartsWith(unmapped, StringComparison.Ordinal) && message.Contains("could not be mapped", StringComparison.Ordinal)
            && message.IndexOf('\'', unmapped.Length) is var end and > 0)
            return $"Unknown property '{message[unmapped.Length..end]}'.";
        var cut = message.IndexOf(" Path: ", StringComparison.Ordinal);
        return cut > 0 ? message[..cut] : message;
    }
}
