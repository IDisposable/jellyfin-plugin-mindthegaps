using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;

/// <summary>
/// OpenLibrary represents a "text with an optional wiki-style type" value, such as a work's description,
/// as either a plain JSON string or an object of the form <c>{"type": "/type/text", "value": "..."}</c>
/// depending on how the record was authored. This reads either shape into a plain string.
/// </summary>
internal sealed class OpenLibraryTextConverter : JsonConverter<string?>
{
    /// <inheritdoc />
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString();
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return null;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}
