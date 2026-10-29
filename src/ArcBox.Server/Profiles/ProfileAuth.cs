namespace ArcBox.Server.Profiles;

/// <summary>Authenticates portal requests by the profile key a phone holds (spec §3.1, §3.4).</summary>
public static class ProfileAuth
{
    private const string Scheme = "Profile ";

    // Keys are 128-bit base64url tokens (22 characters); anything much longer can't be one.
    private const int MaxKeyLength = 128;

    /// <summary>
    /// The profile that <c>Authorization: Profile &lt;key&gt;</c> names, or null. The scheme is case-sensitive and is
    /// followed by exactly one space and the key, and there must be exactly one Authorization header. Counts as a use of
    /// the key. The key is never logged or returned.
    /// </summary>
    public static ProfileRecord? Authenticate(HttpContext http, ProfileService profiles)
    {
        var headers = http.Request.Headers.Authorization;
        if (headers.Count != 1 || headers[0] is not { } header) return null;
        if (!header.StartsWith(Scheme, StringComparison.Ordinal)) return null;

        var key = header.AsSpan(Scheme.Length);
        if (key.IsEmpty || key.Length > MaxKeyLength) return null;
        foreach (var c in key)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) return null;
        }

        return profiles.Resolve(key.ToString());
    }
}
