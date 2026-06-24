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
}
