using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Services.Acquisition;

/// <summary>
/// The outcome of one handoff send: whether it worked and a short human-readable message. A send never
/// throws; an unreachable service or a non-success status is reported here as a failure so the caller can
/// surface it without aborting a batch.
/// </summary>
public sealed class AcquisitionResult
{
    private AcquisitionResult(bool success, string message)
    {
        Success = success;
        Message = message;
    }

    /// <summary>
    /// Gets a value indicating whether the send succeeded.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Gets the human-readable outcome message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <param name="message">The outcome message.</param>
    /// <returns>A successful result.</returns>
    public static AcquisitionResult Ok(string message = "Sent.") => new(true, message);

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    /// <param name="message">The failure message.</param>
    /// <returns>A failed result.</returns>
    public static AcquisitionResult Fail(string message) => new(false, message);

    /// <summary>
    /// Collapses a response body to a single line capped at 200 characters, for an error message that does
    /// not dump a whole HTML/JSON body into a toast.
    /// </summary>
    /// <param name="body">The raw response body (may be null).</param>
    /// <param name="logger">The logger.</param>
    /// <returns>A one-line, length-capped summary.</returns>
    public static string Summarize(string? body, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        // Radarr and Sonarr answer a rejected add with a JSON array of validation failures; the user wants
        // the messages ("This movie has already been added"), not the envelope around them.
        var messages = ValidationMessages(body, logger);
        if (messages.Count > 0)
        {
            return string.Join(" ", messages);
        }

        // Collapse every run of whitespace (a CRLF, indentation in a JSON/HTML body) to one space.
        var oneLine = string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return oneLine.Length <= 200
            ? oneLine
            : string.Create(CultureInfo.InvariantCulture, $"{oneLine[..200]}...");
    }

    /// <summary>
    /// Reads the <c>errorMessage</c> of every entry in an arr validation-failure array.
    /// </summary>
    /// <param name="body">The response body.</param>
    /// <param name="logger">The logger.</param>
    /// <returns>The messages.</returns>
    public static IReadOnlyList<string> ValidationMessages(string? body, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(body) || !body.TrimStart().StartsWith('['))
        {
            logger.LogWarning("Expected an array but got non-array response body {Body}.", body);
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                logger.LogWarning("Expected an array but failed to parse validation messages from response body {Body}.", body);
                return [];
            }

            var messages = new List<string>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty("errorMessage", out var message)
                    && message.ValueKind == JsonValueKind.String
                    && message.GetString() is { Length: > 0 } text)
                {
                    text = text.Trim();
                    if (!messages.Contains(text))
                    {
                        messages.Add(text);
                    }
                }
            }

            return messages;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse validation messages from response body {Body}.", body);
            return [];
        }
    }
}
