using System;
using Jellyfin.Plugin.MindTheGaps.Services.Images;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class ImageHostHealthTests
{
    private const string Host = "image.tmdb.org";

    [Fact]
    public void AHostIsBlockedAfterFiveFailuresInARow()
    {
        var health = new ImageHostHealth(() => DateTimeOffset.UtcNow);

        for (var i = 0; i < 4; i++)
        {
            Assert.False(health.Failed(Host));
            Assert.False(health.IsBlocked(Host));
        }

        Assert.True(health.Failed(Host));
        Assert.True(health.IsBlocked(Host));
    }

    [Fact]
    public void AnAnswerClearsTheRun()
    {
        var health = new ImageHostHealth(() => DateTimeOffset.UtcNow);
        for (var i = 0; i < 4; i++)
        {
            health.Failed(Host);
        }

        health.Answered(Host);

        for (var i = 0; i < 4; i++)
        {
            Assert.False(health.Failed(Host));
        }

        Assert.False(health.IsBlocked(Host));
    }

    [Fact]
    public void ABlockLastsTenMinutesAndOneMoreFailureAfterItReopensIt()
    {
        var now = DateTimeOffset.UtcNow;
        var health = new ImageHostHealth(() => now);
        for (var i = 0; i < 5; i++)
        {
            health.Failed(Host);
        }

        now += TimeSpan.FromMinutes(9);
        Assert.True(health.IsBlocked(Host));

        now += TimeSpan.FromMinutes(2);
        Assert.False(health.IsBlocked(Host));
        health.Failed(Host);
        Assert.True(health.IsBlocked(Host));
    }

    [Fact]
    public void AnOutageIsReportedOnceHoweverManyCooldownsItOutlasts()
    {
        var now = DateTimeOffset.UtcNow;
        var health = new ImageHostHealth(() => now);

        var reports = 0;
        for (var cooldown = 0; cooldown < 4; cooldown++)
        {
            for (var i = 0; i < 5; i++)
            {
                if (health.Failed(Host))
                {
                    reports++;
                }
            }

            now += TimeSpan.FromMinutes(11);
        }

        Assert.Equal(1, reports);
    }

    [Fact]
    public void AnAnswerEndsAnOutageOnceAndTheNextOutageIsReportedAgain()
    {
        var now = DateTimeOffset.UtcNow;
        var health = new ImageHostHealth(() => now);
        Assert.False(health.Answered(Host));

        for (var i = 0; i < 5; i++)
        {
            health.Failed(Host);
        }

        now += TimeSpan.FromMinutes(11);
        Assert.True(health.Answered(Host));
        Assert.False(health.Answered(Host));

        var begun = false;
        for (var i = 0; i < 5; i++)
        {
            begun = health.Failed(Host);
        }

        Assert.True(begun);
    }

    [Fact]
    public void AnAnswerBeforeAnOutageBeginsIsNotARecovery()
    {
        var health = new ImageHostHealth(() => DateTimeOffset.UtcNow);
        for (var i = 0; i < 4; i++)
        {
            health.Failed(Host);
        }

        Assert.False(health.Answered(Host));
    }

    [Fact]
    public void AnAnswerAfterTheCooldownClosesTheBlockForGood()
    {
        var now = DateTimeOffset.UtcNow;
        var health = new ImageHostHealth(() => now);
        for (var i = 0; i < 5; i++)
        {
            health.Failed(Host);
        }

        now += TimeSpan.FromMinutes(11);
        health.Answered(Host);

        Assert.False(health.Failed(Host));
        Assert.False(health.IsBlocked(Host));
    }

    [Fact]
    public void HostsAreTrackedApartAndWithoutRegardToCase()
    {
        var health = new ImageHostHealth(() => DateTimeOffset.UtcNow);
        for (var i = 0; i < 5; i++)
        {
            health.Failed("IMAGE.TMDB.ORG");
        }

        Assert.True(health.IsBlocked(Host));
        Assert.False(health.IsBlocked("covers.openlibrary.org"));
    }
}
