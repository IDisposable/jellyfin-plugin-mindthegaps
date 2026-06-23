using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps;

/// <summary>
/// Logging helpers for the gated detailed external-API logging.
/// </summary>
public static class DetailedLoggerExtensions
{
    /// <summary>
    /// Logs an external API call at Information level, but only when <see cref="Plugin.DetailedApiLogging"/>
    /// is enabled. A no-op when detailed logging is off or no logger is supplied.
    /// </summary>
    /// <param name="logger">The logger, or <see langword="null"/>.</param>
    /// <param name="message">The message template.</param>
    /// <param name="args">The values for the template's placeholders.</param>
    [SuppressMessage("Usage", "CA2254:Template should be a static expression", Justification = "The template is a constant supplied by each call site; this only forwards it.")]
    public static void Detailed(this ILogger? logger, string message, params object?[] args)
    {
        if (logger is not null && Plugin.DetailedApiLogging)
        {
            logger.LogInformation(message, args);
        }
    }
}
