using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// How the report and the per-user lists are written to disk, which is not how the API sends them. A link is
/// never written: every link is built from the ids beside it (see <see cref="ExternalLinkEnricher.Fill"/>), so
/// storing it would only keep an old copy of what the ids already say. An image or offer address on a host the
/// plugin knows is written as a short token (see <see cref="StoredUrls"/>). Reading still accepts both as they
/// were written before, which is what lets <see cref="LegacyLinkIds"/> recover the ids an old file only kept in
/// a link.
/// </summary>
internal static class StoredJson
{
    /// <summary>
    /// Creates the options.
    /// </summary>
    /// <returns>Fresh options; the caller keeps one instance.</returns>
    public static JsonSerializerOptions Create() => new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { Trim } }
    };

    private static void Trim(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        foreach (var property in info.Properties)
        {
            var name = (property.AttributeProvider as PropertyInfo)?.Name;
            if (IsBuiltLink(info.Type, name))
            {
                property.ShouldSerialize = static (_, _) => false;
            }
            else if (property.PropertyType == typeof(string) && IsAddress(info.Type, name))
            {
                property.CustomConverter = CompactUrlConverter.Instance;
            }
        }
    }

    private static bool IsBuiltLink(Type type, string? name)
        => (type == typeof(GapItem) && name is nameof(GapItem.Links) or nameof(GapItem.SourceLinks))
            || (type == typeof(TodoEntry) && name is nameof(TodoEntry.Links));

    private static bool IsAddress(Type type, string? name)
        => (type == typeof(GapItem) && name is nameof(GapItem.ImageUrl))
            || (type == typeof(TodoEntry) && name is nameof(TodoEntry.ImageUrl))
            || (type == typeof(NotInterestedEntry) && name is nameof(NotInterestedEntry.ImageUrl))
            || (type == typeof(AvailabilityOffer) && name is nameof(AvailabilityOffer.Url) or nameof(AvailabilityOffer.LogoUrl));

    private sealed class CompactUrlConverter : JsonConverter<string>
    {
        public static readonly CompactUrlConverter Instance = new();

        public override bool HandleNull => false;

        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => StoredUrls.Expand(reader.GetString());

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
            => writer.WriteStringValue(StoredUrls.Compact(value));
    }
}
