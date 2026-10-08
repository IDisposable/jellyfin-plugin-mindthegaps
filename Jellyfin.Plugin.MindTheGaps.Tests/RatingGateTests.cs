using System;
using System.Collections.Generic;
using System.Reflection;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// The gate asks core's own IsParentalAllowed, so these run core's comparison against a small stand-in for its
// rating table: US ratings by their usual score, and a German one to show a prefixed rating is read too.
[Collection(nameof(RatingGateTests))]
public class RatingGateTests
{
    private static readonly Dictionary<string, int> _scores = new(StringComparer.OrdinalIgnoreCase)
    {
        ["G"] = 0,
        ["PG"] = 10,
        ["PG-13"] = 13,
        ["R"] = 17,
        ["TV-14"] = 14,
        ["TV-MA"] = 17,
        ["FSK-12"] = 12,
    };

    public RatingGateTests()
    {
        BaseItem.LocalizationManager = RatingTable.Create();

        // 10.11 walks an item's library folders for inherited tags; a pathless item with no parent is in none.
        BaseItem.LibraryManager ??= NoLibraryFolders.Create();

        // 10.11 logs a debug line on the way; the server sets this, the test host does not.
        BaseItem.Logger ??= NullLogger<BaseItem>.Instance;
    }

    private static User Restricted(int maxScore, params UnratedItem[] blockUnrated)
    {
        var user = new User("kid", "provider", "reset") { MaxParentalRatingScore = maxScore };
        user.SetPreference(PreferenceKind.BlockUnratedItems, blockUnrated);
        return user;
    }

    [Theory]
    [InlineData("US", "PG", true)]
    [InlineData("US", "PG-13", true)]
    [InlineData("US", "R", false)]
    [InlineData("DE", "FSK-12", true)]
    public void AllowsAMovieRatedWithinTheLimit(string country, string rating, bool allowed)
        => Assert.Equal(allowed, RatingGate.Allows(Restricted(13), isSeries: false, new TmdbCertification(country, rating)));

    [Theory]
    [InlineData("TV-14", true)]
    [InlineData("TV-MA", false)]
    public void AllowsASeriesRatedWithinTheLimit(string rating, bool allowed)
        => Assert.Equal(allowed, RatingGate.Allows(Restricted(14), isSeries: true, new TmdbCertification("US", rating)));

    // On 12.0 core reads a certification with the rating table of the item's country, which is the country the
    // certification came from. 10.11 passes no country and goes by the rating's own prefix, as it does for a
    // library item.
    [Fact]
    public void ReadsTheCertificationWithItsOwnCountrysTable()
    {
        RatingTable.Countries.Clear();
        RatingGate.Allows(Restricted(13), isSeries: false, new TmdbCertification("DE", "FSK-12"));

#if NET10_0_OR_GREATER
        Assert.Contains("DE", RatingTable.Countries);
#else
        Assert.Contains(null, RatingTable.Countries);
#endif
    }

    // The user's own "block items with no rating" choice decides an unrated title, for movies and series apart.
    [Fact]
    public void AnUnratedTitleFollowsTheUsersOwnChoiceForItsKind()
    {
        var blocksMovies = Restricted(13, UnratedItem.Movie);

        Assert.False(RatingGate.Allows(blocksMovies, isSeries: false, null));
        Assert.True(RatingGate.Allows(blocksMovies, isSeries: true, null));
        Assert.True(RatingGate.Allows(Restricted(13), isSeries: false, null));
    }

    [Fact]
    public void AUserWithNoLimitIsAllowedEverything()
    {
        var user = new User("adult", "provider", "reset");

        Assert.True(RatingGate.Allows(user, isSeries: false, new TmdbCertification("US", "R")));
        Assert.True(RatingGate.Allows(user, isSeries: true, null));
    }

    // A library with no folders: every list it is asked for is empty, every lookup finds nothing.
    public class NoLibraryFolders : DispatchProxy
    {
        public static ILibraryManager Create() => DispatchProxy.Create<ILibraryManager, NoLibraryFolders>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var type = targetMethod?.ReturnType;
            if (type is null || type == typeof(void))
            {
                return null;
            }

            if (type.IsArray)
            {
                return Array.CreateInstance(type.GetElementType()!, 0);
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                return Activator.CreateInstance(type);
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() is var open
                && (open == typeof(IEnumerable<>) || open == typeof(IReadOnlyList<>) || open == typeof(IList<>) || open == typeof(IReadOnlyCollection<>)))
            {
                return Array.CreateInstance(type.GetGenericArguments()[0], 0);
            }

            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }

    // Core's rating table, reduced to the one call the comparison makes, noting which country's table it asked.
    public class RatingTable : DispatchProxy
    {
        public static List<string?> Countries { get; } = [];

        public static ILocalizationManager Create() => DispatchProxy.Create<ILocalizationManager, RatingTable>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ILocalizationManager.GetRatingScore))
            {
                Countries.Add(args?.Length > 1 ? args[1] as string : null);
                return args?[0] is string rating && _scores.TryGetValue(rating, out var score) ? new ParentalRatingScore(score, null) : null;
            }

            return targetMethod?.ReturnType.IsValueType == true && targetMethod.ReturnType != typeof(void)
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
        }
    }
}
