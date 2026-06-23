using System;
using System.Text;

namespace Jellyfin.Plugin.MindTheGaps.Services.Http;

/// <summary>
/// Scrubs secrets out of a URL before it reaches a log. Almost every client carries its key in a header
/// (never logged), but a few put it in the query string (MDBList's <c>apikey</c>, the TMDB availability
/// fetch's <c>api_key</c>), so a warning or a detailed-logging line would otherwise print the key verbatim.
/// The request and the cache key keep the real URL; only what is logged passes through here.
/// </summary>
internal static class LogSafe
{
    // Query-parameter names whose value is a secret. Matched case-insensitively; the value is replaced, the
    // name is kept so a log still shows which parameter was present.
    private static readonly string[] _secretParams =
    {
        "apikey", "api_key", "key", "token", "access_token", "client_secret", "secret"
    };

    /// <summary>
    /// Returns the URL with any secret query-parameter value replaced by <c>***</c>, safe to log.
    /// </summary>
    /// <param name="url">The URL to redact.</param>
    /// <returns>The redacted URL.</returns>
    public static string Redact(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return url ?? string.Empty;
        }

        var queryStart = url.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0)
        {
            return url;
        }

        var builder = new StringBuilder(url.Length);
        builder.Append(url, 0, queryStart + 1);

        var pairs = url.Substring(queryStart + 1).Split('&');
        for (var i = 0; i < pairs.Length; i++)
        {
            if (i > 0)
            {
                builder.Append('&');
            }

            var pair = pairs[i];
            var eq = pair.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0 && IsSecret(pair.AsSpan(0, eq)))
            {
                builder.Append(pair, 0, eq + 1);
                builder.Append("***");
            }
            else
            {
                builder.Append(pair);
            }
        }

        return builder.ToString();
    }

    private static bool IsSecret(ReadOnlySpan<char> name)
    {
        foreach (var secret in _secretParams)
        {
            if (name.Equals(secret, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
