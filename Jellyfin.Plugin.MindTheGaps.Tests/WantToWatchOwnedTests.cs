using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

/// <summary>
/// The want-to-watch row with owned titles from the user's playlist, and taking a title off once watched.
/// </summary>
public class WantToWatchOwnedTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly OwnershipIndex OwnsNothing = new(new HashSet<string>());

    private static TodoEntry Missing(string id, string tmdb, string added = "2026-01-01T00:00:00Z") => new()
    {
        Id = id,
        Name = id,
        TargetKindName = "Movie",
        ProviderIds = new Dictionary<string, string> { ["Tmdb"] = tmdb },
        AddedUtc = added
    };

    private static Movie OwnedMovie(string name, string? tmdb = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        ProductionYear = 2001,
        PremiereDate = new DateTime(2001, 5, 4, 0, 0, 0, DateTimeKind.Utc),
        ProviderIds = tmdb is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["Tmdb"] = tmdb }
    };

    [Fact]
    public void WantedRow_PutsOwnedTitlesFirst_InTheirOwnOrder_ThenTheMissingOnes()
    {
        var a = OwnedMovie("Owned A", "10");
        var b = OwnedMovie("Owned B", "11");

        var row = WantedRowBuilder.Build(
            [Missing("old", "1", "2026-01-01T00:00:00Z"), Missing("new", "2", "2026-03-01T00:00:00Z")],
            OwnsNothing,
            [b, a],
            10,
            Now);

        Assert.Equal(["Owned B", "Owned A", "new", "old"], row.Select(c => c.Title));
        Assert.Equal([b.Id, a.Id, null, null], row.Select(c => c.ItemId));
    }

    [Fact]
    public void WantedRow_OwnedTitlesShareTheLimit_AndAppearOnce()
    {
        var a = OwnedMovie("Owned A");
        var b = OwnedMovie("Owned B");

        var row = WantedRowBuilder.Build([Missing("m", "1")], OwnsNothing, [a, b, a], 2, Now);

        Assert.Equal(["Owned A", "Owned B"], row.Select(c => c.Title));
    }

    [Fact]
    public void WantedRow_WithNoOwnedTitles_IsJustTheMissingOnes()
    {
        var withOverload = WantedRowBuilder.Build([Missing("m", "1")], OwnsNothing, 10, Now);
        var withEmpty = WantedRowBuilder.Build([Missing("m", "1")], OwnsNothing, [], 10, Now);

        Assert.Equal(withOverload.Select(c => c.GapId), withEmpty.Select(c => c.GapId));
        Assert.Null(Assert.Single(withEmpty).ItemId);
    }

    [Fact]
    public void WantedRow_ShowsOwnedTitles_EvenWithAnEmptyList()
    {
        var row = WantedRowBuilder.Build([], OwnsNothing, [OwnedMovie("Owned")], 10, Now);

        Assert.Equal("Owned", Assert.Single(row).Title);
    }

    [Fact]
    public void OwnedCard_CarriesItsLibraryItem_AndIsOnTheList()
    {
        var movie = OwnedMovie("Heat", "949");

        var card = WantedRowBuilder.OwnedCard(movie);

        Assert.Equal("owned:" + movie.Id.ToString("N"), card.GapId);
        Assert.Equal(movie.Id, card.ItemId);
        Assert.Equal("Heat", card.Title);
        Assert.Equal(2001, card.Year);
        Assert.Equal("Movie", card.Kind);
        Assert.Equal(949, card.TmdbId);
        Assert.True(card.OnList);
        Assert.False(card.Upcoming);
        Assert.Null(card.ImageUrl);
    }

    [Fact]
    public void OwnedCard_ForASeries_AndWithoutATmdbId()
    {
        var card = WantedRowBuilder.OwnedCard(new Series { Id = Guid.NewGuid(), Name = "A Show" });

        Assert.Equal("Series", card.Kind);
        Assert.Equal(0, card.TmdbId);
    }

    [Theory]
    [InlineData(UserDataSaveReason.PlaybackFinished, true, true)]
    [InlineData(UserDataSaveReason.TogglePlayed, true, true)]
    [InlineData(UserDataSaveReason.TogglePlayed, false, false)]
    [InlineData(UserDataSaveReason.PlaybackProgress, true, false)]
    [InlineData(UserDataSaveReason.PlaybackStart, true, false)]
    [InlineData(UserDataSaveReason.PlaybackFinished, false, false)]
    public void IsWatchedSave_OnlyFinishedOrToggledToPlayed(UserDataSaveReason reason, bool played, bool expected)
        => Assert.Equal(expected, WatchedAutoRemover.IsWatchedSave(reason, played));

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    public void RemoveWatched_NeedsWantToWatch_ItsPlaylist_AndItsOwnOption(bool wantToWatch, bool playlist, bool removeWatched, bool expected)
    {
        var config = new PluginConfiguration
        {
            WantToWatchEnabled = wantToWatch,
            WantToWatchPlaylistEnabled = playlist,
            WantToWatchRemoveWatched = removeWatched
        };

        Assert.Equal(expected, WatchedAutoRemover.IsOn(config));
        Assert.False(WatchedAutoRemover.IsOn(null));
    }

    [Fact]
    public void BothOptionsAreOffByDefault()
    {
        var config = new PluginConfiguration();

        Assert.False(config.WantToWatchRowIncludesOwned);
        Assert.False(config.WantToWatchRemoveWatched);
    }
}
