using AllSky.Core.YouTube;
using Xunit;

namespace AllSky.Core.Tests.YouTube;

public sealed class YouTubeLinksTests
{
    [Fact]
    public void WatchUrl_IdValido_LinkCompleto()
    {
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", YouTubeLinks.WatchUrl("dQw4w9WgXcQ"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc&evil=1")]
    [InlineData("a/b")]
    [InlineData("spazio qui")]
    public void WatchUrl_IdNonValido_Null(string? id)
    {
        Assert.Null(YouTubeLinks.WatchUrl(id));
    }
}
