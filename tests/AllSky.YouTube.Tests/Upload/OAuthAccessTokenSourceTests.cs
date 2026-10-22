using System.Net;
using AllSky.YouTube;
using Xunit;

namespace AllSky.YouTube.Tests;

public sealed class OAuthAccessTokenSourceTests
{
    private const string TokenJson =
        "{\"access_token\":\"access-prova\",\"expires_in\":3600,\"token_type\":\"Bearer\"}";

    [Fact]
    public async Task Token_RiusatoFinoAScadenzaERinnovatoAlleSoglie()
    {
        using var dir = new TempDir();
        var store = new YouTubeAuthStore(dir.File("youtube-auth.json"), new FakeProtector());
        store.Save(new StoredYouTubeAuth("client-prova", new OAuthSecret("segreto-prova"), new OAuthSecret("refresh-prova")));
        var start = UploadHarness.Local(2026, 10, 22, 8, 0);
        var handler = new FakeHandler(HttpStatusCode.OK, TokenJson);
        var service = new YouTubeOAuthService(
            new GoogleOAuthClient(new HttpClient(handler), new FixedClock(start)),
            store);
        var clock = new TestClock { UtcNow = start };
        var source = new OAuthAccessTokenSource(service, clock);

        OAuthSecret first = await source.GetAccessTokenAsync(false, CancellationToken.None);
        Assert.Equal("access-prova", first.Reveal());
        Assert.Equal(1, handler.Requests.Count);

        // Ancora valido: nessun nuovo rinnovo.
        clock.UtcNow = start.AddMinutes(30);
        await source.GetAccessTokenAsync(false, CancellationToken.None);
        Assert.Equal(1, handler.Requests.Count);

        // Dentro il margine di 2 minuti prima della scadenza (3600 s): si rinnova.
        clock.UtcNow = start.AddSeconds(3600 - 60);
        await source.GetAccessTokenAsync(false, CancellationToken.None);
        Assert.Equal(2, handler.Requests.Count);

        // Dopo un 401 il rinnovo e' forzato anche se il token e' in cache.
        clock.UtcNow = start.AddSeconds(60);
        await source.GetAccessTokenAsync(true, CancellationToken.None);
        Assert.Equal(3, handler.Requests.Count);
        Assert.DoesNotContain("access-prova", first.ToString());
    }
}
