using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Entities.Movies;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class ProviderIdExtensionsTests
{
    private static Movie Item(params (string Key, string Value)[] ids)
    {
        var item = new Movie { Id = Guid.NewGuid() };
        var dict = new Dictionary<string, string>();
        foreach (var (key, value) in ids)
        {
            dict[key] = value;
        }

        item.ProviderIds = dict;
        return item;
    }

    [Theory]
    [InlineData("123", true)]
    [InlineData("0", false)]
    [InlineData("-5", false)]
    [InlineData("not-a-number", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParseProviderId_OnlyAcceptsAPositiveInteger(string? value, bool expected)
        => Assert.Equal(expected, value.TryParseProviderId(out _));

    [Fact]
    public void TryParseProviderId_SetsTheIdOnSuccess()
    {
        Assert.True("123".TryParseProviderId(out var id));
        Assert.Equal(123, id);
    }

    [Fact]
    public void TryGetProviderIdAsInt_OnAnItem_ReadsAValidId()
    {
        var item = Item((ProviderIds.Tmdb, "603"));

        Assert.True(item.TryGetProviderIdAsInt(ProviderIds.Tmdb, out var id));
        Assert.Equal(603, id);
    }

    [Fact]
    public void TryGetProviderIdAsInt_OnAnItem_FalseWhenAbsentBlankOrNotPositive()
    {
        Assert.False(Item().TryGetProviderIdAsInt(ProviderIds.Tmdb, out _));
        Assert.False(Item((ProviderIds.Tmdb, string.Empty)).TryGetProviderIdAsInt(ProviderIds.Tmdb, out _));
        Assert.False(Item((ProviderIds.Tmdb, "0")).TryGetProviderIdAsInt(ProviderIds.Tmdb, out _));
        Assert.False(Item((ProviderIds.Tmdb, "not-a-number")).TryGetProviderIdAsInt(ProviderIds.Tmdb, out _));
    }

    [Fact]
    public void TryGetProviderIdAsInt_OnADictionary_ReadsAValidId()
    {
        IReadOnlyDictionary<string, string> ids = new Dictionary<string, string> { [ProviderIds.Tmdb] = "603" };

        Assert.True(ids.TryGetProviderIdAsInt(ProviderIds.Tmdb, out var id));
        Assert.Equal(603, id);
    }

    [Fact]
    public void TryGetProviderIdAsInt_OnADictionary_IsCaseInsensitiveOnTheKey()
    {
        IReadOnlyDictionary<string, string> ids = new Dictionary<string, string>(StringComparer.Ordinal) { ["TMDB"] = "603" };

        Assert.True(ids.TryGetProviderIdAsInt(ProviderIds.Tmdb, out var id));
        Assert.Equal(603, id);
    }

    [Fact]
    public void TryGetProviderIdAsInt_OnADictionary_FalseWhenAbsentOrNotPositive()
    {
        IReadOnlyDictionary<string, string> empty = new Dictionary<string, string>();
        IReadOnlyDictionary<string, string> zero = new Dictionary<string, string> { [ProviderIds.Tmdb] = "0" };

        Assert.False(empty.TryGetProviderIdAsInt(ProviderIds.Tmdb, out _));
        Assert.False(zero.TryGetProviderIdAsInt(ProviderIds.Tmdb, out _));
    }

    [Theory]
    [InlineData("123", true)]
    [InlineData("0", false)]
    [InlineData("-5", false)]
    [InlineData("not-a-number", false)]
    [InlineData(null, false)]
    public void TryParseProviderIdAsLong_OnlyAcceptsAPositiveInteger(string? value, bool expected)
        => Assert.Equal(expected, value.TryParseProviderIdAsLong(out _));

    [Fact]
    public void TryGetProviderIdAsLong_OnAnItem_ReadsAValidId()
    {
        // TheTVDB and Discogs ids are typed long in this plugin (matching their own client signatures),
        // even though real values are well within int range.
        var item = Item((ProviderIds.Tvdb, "75565"));

        Assert.True(item.TryGetProviderIdAsLong(ProviderIds.Tvdb, out var id));
        Assert.Equal(75565L, id);
    }

    [Fact]
    public void TryGetProviderIdAsLong_OnAnItem_FalseWhenAbsentOrNotPositive()
    {
        Assert.False(Item().TryGetProviderIdAsLong(ProviderIds.Tvdb, out _));
        Assert.False(Item((ProviderIds.Tvdb, "0")).TryGetProviderIdAsLong(ProviderIds.Tvdb, out _));
    }
}
