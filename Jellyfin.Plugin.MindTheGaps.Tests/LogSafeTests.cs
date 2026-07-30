using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class LogSafeTests
{
    [Fact]
    public void Redact_ReplacesApiKeyQueryValue()
    {
        Assert.Equal(
            "https://api.mdblist.com/lists/1/items?apikey=***",
            LogSafe.Redact("https://api.mdblist.com/lists/1/items?apikey=SECRET"));
    }

    [Fact]
    public void Redact_ReplacesEverySecretParamButKeepsTheRest()
    {
        Assert.Equal(
            "https://x/y?query=star&apikey=***&page=2",
            LogSafe.Redact("https://x/y?query=star&apikey=topsecret&page=2"));
    }

    [Fact]
    public void Redact_HandlesApiUnderscoreKeyAndToken()
    {
        Assert.Equal("https://x?api_key=***", LogSafe.Redact("https://x?api_key=abc"));
        Assert.Equal("https://x?token=***", LogSafe.Redact("https://x?token=abc"));
    }

    [Fact]
    public void Redact_LeavesUrlWithNoSecretsUnchanged()
    {
        Assert.Equal("https://x/y?page=2&q=star", LogSafe.Redact("https://x/y?page=2&q=star"));
        Assert.Equal("https://x/y", LogSafe.Redact("https://x/y"));
    }

    [Fact]
    public void Redact_HandlesNullAndEmpty()
    {
        Assert.Equal(string.Empty, LogSafe.Redact(null));
        Assert.Equal(string.Empty, LogSafe.Redact(string.Empty));
    }

    [Theory]
    // An acquisition target is a URL the user typed, so it can carry basic-auth credentials. Uri.ToString
    // prints them in full, which is how they would otherwise reach the log.
    [InlineData("http://user:pass@radarr:7878/api/v3/movie", "http://***@radarr:7878/api/v3/movie")]
    [InlineData("https://user:pass@host/path", "https://***@host/path")]
    [InlineData("http://user@host:8989/api", "http://***@host:8989/api")]
    // No userinfo, so nothing to strip.
    [InlineData("http://radarr:7878/api/v3/movie", "http://radarr:7878/api/v3/movie")]
    // An "@" past the authority is part of the path or query, not a credential.
    [InlineData("https://host/users/me@example.com", "https://host/users/me@example.com")]
    [InlineData("https://host/x?q=a@b", "https://host/x?q=a@b")]
    public void Redact_StripsBasicAuthCredentialsFromTheAuthority(string url, string expected)
        => Assert.Equal(expected, LogSafe.Redact(url));

    [Fact]
    public void Redact_StripsUserInfoAndQuerySecretsTogether()
        => Assert.Equal(
            "https://***@host/x?apikey=***&page=2",
            LogSafe.Redact("https://user:pass@host/x?apikey=SECRET&page=2"));
}
