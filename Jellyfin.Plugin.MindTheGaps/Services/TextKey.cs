using System.Text;

namespace Jellyfin.Plugin.MindTheGaps.Services;

/// <summary>
/// Shared text normalization for matching: lowercase, letters and digits only, so punctuation, spacing, and
/// case differences do not block a comparison. Used to key titles and names for matching and de-duplication
/// (a title's match key, an author's name key, and so on).
/// </summary>
public static class TextKey
{
    /// <summary>
    /// Reduces a string to a comparison key: lowercase letters and digits only, everything else dropped.
    /// </summary>
    /// <param name="value">The text to normalize.</param>
    /// <returns>The normalized key, or an empty string when the input is null or empty.</returns>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }

        return sb.ToString();
    }
}
