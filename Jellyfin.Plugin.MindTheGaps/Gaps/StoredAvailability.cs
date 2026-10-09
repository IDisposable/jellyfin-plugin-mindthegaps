using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// How a gap's offers are stored: <c>{ "url": ..., "offers": [...] }</c>, the watch page once for the gap (every
/// offer of a title carries the same TMDB watch page) and each offer as its service, monetization and quality. An
/// offer whose page differs keeps its own. No offer stores its service's logo: the report's meta file keeps one
/// per service (see <see cref="GapReportFiles"/>). Reading also accepts the plain array every earlier version
/// wrote. The three short strings an offer is made of repeat across hundreds of thousands of offers and take a
/// few hundred values, so each read shares one copy of each.
/// </summary>
internal sealed class StoredAvailability : JsonConverter<IReadOnlyList<AvailabilityOffer>>
{
    /// <summary>
    /// Gets the instance.
    /// </summary>
    public static readonly StoredAvailability Instance = new();

    /// <inheritdoc />
    public override IReadOnlyList<AvailabilityOffer> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var listed = JsonSerializer.Deserialize<List<AvailabilityOffer>>(ref reader, options) ?? [];
            foreach (var offer in listed)
            {
                Share(offer);
            }

            return listed;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected the offers of a gap.");
        }

        string? shared = null;
        var offers = new List<AvailabilityOffer>();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("url"u8))
            {
                reader.Read();
                shared = StoredUrls.Expand(reader.GetString());
            }
            else if (reader.ValueTextEquals("offers"u8))
            {
                reader.Read();
                while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
                {
                    offers.Add(ReadOffer(ref reader));
                }
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }

        foreach (var offer in offers)
        {
            offer.Url ??= shared;
        }

        return offers;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, IReadOnlyList<AvailabilityOffer> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        if (value.Count == 0)
        {
            writer.WriteStartArray();
            writer.WriteEndArray();
            return;
        }

        var shared = value[0].Url;
        foreach (var offer in value)
        {
            if (!string.Equals(offer.Url, shared, StringComparison.Ordinal))
            {
                shared = null;
                break;
            }
        }

        writer.WriteStartObject();
        if (shared is not null)
        {
            writer.WriteString("url"u8, StoredUrls.Compact(shared));
        }

        writer.WriteStartArray("offers"u8);
        foreach (var offer in value)
        {
            writer.WriteStartObject();
            writer.WriteString("provider"u8, offer.Provider);
            WriteIfSet(writer, "monetizationType"u8, offer.MonetizationType);
            WriteIfSet(writer, "quality"u8, offer.Quality);
            if (shared is null && offer.Url is not null)
            {
                writer.WriteString("url"u8, StoredUrls.Compact(offer.Url));
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static AvailabilityOffer ReadOffer(ref Utf8JsonReader reader)
    {
        var offer = new AvailabilityOffer();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("provider"u8))
            {
                reader.Read();
                offer.Provider = reader.GetString() ?? string.Empty;
            }
            else if (reader.ValueTextEquals("monetizationType"u8))
            {
                reader.Read();
                offer.MonetizationType = reader.GetString();
            }
            else if (reader.ValueTextEquals("quality"u8))
            {
                reader.Read();
                offer.Quality = reader.GetString();
            }
            else if (reader.ValueTextEquals("url"u8))
            {
                reader.Read();
                offer.Url = StoredUrls.Expand(reader.GetString());
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }

        Share(offer);
        return offer;
    }

    private static void WriteIfSet(Utf8JsonWriter writer, ReadOnlySpan<byte> name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }

    // A service name, a monetization type and a quality each take a few hundred values across every offer, so
    // one copy of each is kept for the life of the process rather than one per offer.
    private static void Share(AvailabilityOffer offer)
    {
        offer.Provider = string.Intern(offer.Provider);
        offer.MonetizationType = offer.MonetizationType is null ? null : string.Intern(offer.MonetizationType);
        offer.Quality = offer.Quality is null ? null : string.Intern(offer.Quality);
    }
}
