using System.Net;
using AllSky.YouTube;
using Xunit;

namespace AllSky.YouTube.Tests;

public sealed class YouTubeOAuthServiceTests
{
    private const string TokenJson =
        "{\"access_token\":\"access-prova\",\"expires_in\":3600,\"refresh_token\":\"refresh-prova\",\"token_type\":\"Bearer\"}";

    private static readonly DateTimeOffset Now = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

    private static OAuthClientCredentials Credentials() =>
        new("client-di-prova", new OAuthSecret("segreto-di-prova"));

    /// <summary>
    /// Browser simulato: legge redirect_uri e state dall'URL di autorizzazione e invia al loopback la risposta indicata.
    /// La richiesta parte senza attendere: il listener la accoda finche' il login non la legge.
    /// </summary>
    private static Func<Uri, CancellationToken, Task> FakeBrowser(
        HttpClient http,
        Func<string, string, string> callbackQuery,
        Action<Uri, Task<HttpResponseMessage>> onRequest)
    {
        return (uri, _) =>
        {
            var query = QueryParser.ParseQuery(uri.Query);
            string target = query["redirect_uri"] + "/?" + callbackQuery(query["state"], query["redirect_uri"]);
            onRequest(uri, http.GetAsync(target));
            return Task.CompletedTask;
        };
    }

    [Fact]
    public async Task Login_Successo_PkceCoerenteESalvataggioDelRefreshToken()
    {
        using var dir = new TempDir();
        var store = new YouTubeAuthStore(dir.File("youtube-auth.json"), new FakeProtector());
        var handler = new FakeHandler(HttpStatusCode.OK, TokenJson);
        var service = new YouTubeOAuthService(new GoogleOAuthClient(new HttpClient(handler), new FixedClock(Now)), store);

        using HttpClient browser = LocalHttp.Create();
        Uri? authorizationUrl = null;
        Task<HttpResponseMessage>? pending = null;
        Func<Uri, CancellationToken, Task> open = FakeBrowser(
            browser,
            (state, _) => "code=codice-prova&state=" + Uri.EscapeDataString(state),
            (uri, response) =>
            {
                authorizationUrl = uri;
                pending = response;
            });

        OAuthTokens tokens = await service.LoginAsync(Credentials(), open, TimeSpan.FromSeconds(10));

        using HttpResponseMessage page = await pending!;
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("access-prova", tokens.AccessToken.Reveal());

        // URL di autorizzazione: scope upload, S256, nessun segreto.
        var authQuery = QueryParser.ParseQuery(authorizationUrl!.Query);
        Assert.Equal("S256", authQuery["code_challenge_method"]);
        Assert.Equal("https://www.googleapis.com/auth/youtube.upload", authQuery["scope"]);
        Assert.DoesNotContain("segreto-di-prova", authorizationUrl.ToString());

        // Scambio: il code_verifier inviato deve corrispondere al code_challenge dell'URL e il redirect_uri deve coincidere.
        RecordedRequest request = Assert.Single(handler.Requests);
        var form = QueryParser.ParseForm(request.Body);
        Assert.Equal("codice-prova", form["code"]);
        Assert.Equal(authQuery["redirect_uri"], form["redirect_uri"]);
        Assert.Equal(authQuery["code_challenge"], OAuthPkce.ComputeChallenge(form["code_verifier"]));

        StoredYouTubeAuth? stored = store.Load();
        Assert.NotNull(stored);
        Assert.Equal("refresh-prova", stored!.RefreshToken.Reveal());
    }

    [Fact]
    public async Task Login_AutorizzazioneNegata_LanciaAccessDeniedSenzaScambio()
    {
        using var dir = new TempDir();
        var store = new YouTubeAuthStore(dir.File("youtube-auth.json"), new FakeProtector());
        var handler = new FakeHandler(HttpStatusCode.OK, TokenJson);
        var service = new YouTubeOAuthService(new GoogleOAuthClient(new HttpClient(handler), new FixedClock(Now)), store);

        using HttpClient browser = LocalHttp.Create();
        Task<HttpResponseMessage>? pending = null;
        Func<Uri, CancellationToken, Task> open = FakeBrowser(
            browser,
            (state, _) => "error=access_denied&state=" + Uri.EscapeDataString(state),
            (_, response) => pending = response);

        OAuthException error = await Assert.ThrowsAsync<OAuthException>(
            () => service.LoginAsync(Credentials(), open, TimeSpan.FromSeconds(10)));

        using HttpResponseMessage page = await pending!;
        Assert.Equal("access_denied", error.ErrorCode);
        Assert.True(error.IsPermanent);
        Assert.Empty(handler.Requests);
        Assert.Null(store.Load());
    }

    [Fact]
    public async Task Login_StateErrato_NonAccettaESiScadeAlTimeout()
    {
        using var dir = new TempDir();
        var store = new YouTubeAuthStore(dir.File("youtube-auth.json"), new FakeProtector());
        var handler = new FakeHandler(HttpStatusCode.OK, TokenJson);
        var service = new YouTubeOAuthService(new GoogleOAuthClient(new HttpClient(handler), new FixedClock(Now)), store);

        using HttpClient browser = LocalHttp.Create();
        Task<HttpResponseMessage>? pending = null;
        Func<Uri, CancellationToken, Task> open = FakeBrowser(
            browser,
            (_, _) => "code=codice-falso&state=stato-falsificato",
            (_, response) => pending = response);

        await Assert.ThrowsAsync<TimeoutException>(
            () => service.LoginAsync(Credentials(), open, TimeSpan.FromMilliseconds(400)));

        using HttpResponseMessage page = await pending!;
        Assert.Equal(HttpStatusCode.BadRequest, page.StatusCode);
        Assert.Empty(handler.Requests);
        Assert.Null(store.Load());
    }

    [Fact]
    public async Task Refresh_SenzaNuovoRefreshToken_ConservaQuelloSalvato()
    {
        using var dir = new TempDir();
        var store = new YouTubeAuthStore(dir.File("youtube-auth.json"), new FakeProtector());
        store.Save(new StoredYouTubeAuth("client-di-prova", new OAuthSecret("segreto-di-prova"), new OAuthSecret("refresh-vecchio")));
        var handler = new FakeHandler(HttpStatusCode.OK, "{\"access_token\":\"access-nuovo\",\"expires_in\":3600}");
        var service = new YouTubeOAuthService(new GoogleOAuthClient(new HttpClient(handler), new FixedClock(Now)), store);

        OAuthTokens tokens = await service.RefreshAccessTokenAsync();

        Assert.Equal("access-nuovo", tokens.AccessToken.Reveal());
        Assert.Equal("refresh-vecchio", store.Load()!.RefreshToken.Reveal());
        Assert.Equal("refresh_token", QueryParser.ParseForm(Assert.Single(handler.Requests).Body)["grant_type"]);
    }

    [Fact]
    public async Task Refresh_ConNuovoRefreshToken_LoSostituisce()
    {
        using var dir = new TempDir();
        var store = new YouTubeAuthStore(dir.File("youtube-auth.json"), new FakeProtector());
        store.Save(new StoredYouTubeAuth("client-di-prova", new OAuthSecret("segreto-di-prova"), new OAuthSecret("refresh-vecchio")));
        var handler = new FakeHandler(HttpStatusCode.OK, TokenJson);
        var service = new YouTubeOAuthService(new GoogleOAuthClient(new HttpClient(handler), new FixedClock(Now)), store);

        await service.RefreshAccessTokenAsync();

        Assert.Equal("refresh-prova", store.Load()!.RefreshToken.Reveal());
    }

    [Fact]
    public async Task Refresh_SenzaLogin_LanciaNotLoggedIn()
    {
        using var dir = new TempDir();
        var store = new YouTubeAuthStore(dir.File("youtube-auth.json"), new FakeProtector());
        var handler = new FakeHandler(HttpStatusCode.OK, TokenJson);
        var service = new YouTubeOAuthService(new GoogleOAuthClient(new HttpClient(handler), new FixedClock(Now)), store);

        OAuthException error = await Assert.ThrowsAsync<OAuthException>(() => service.RefreshAccessTokenAsync());

        Assert.Equal("not_logged_in", error.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Logout_RevocaELiminaIlFile()
    {
        using var dir = new TempDir();
        var store = new YouTubeAuthStore(dir.File("youtube-auth.json"), new FakeProtector());
        store.Save(new StoredYouTubeAuth("client-di-prova", new OAuthSecret("segreto-di-prova"), new OAuthSecret("refresh-vecchio")));
        var handler = new FakeHandler(HttpStatusCode.OK, string.Empty);
        var service = new YouTubeOAuthService(new GoogleOAuthClient(new HttpClient(handler), new FixedClock(Now)), store);

        await service.LogoutAsync();

        Assert.Equal("refresh-vecchio", QueryParser.ParseForm(Assert.Single(handler.Requests).Body)["token"]);
        Assert.Null(store.Load());
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public async Task Logout_RevocaFallita_RimuoveComunqueIlFileEPropagaringErrore()
    {
        using var dir = new TempDir();
        var store = new YouTubeAuthStore(dir.File("youtube-auth.json"), new FakeProtector());
        store.Save(new StoredYouTubeAuth("client-di-prova", new OAuthSecret("segreto-di-prova"), new OAuthSecret("refresh-vecchio")));
        var handler = new FakeHandler(HttpStatusCode.BadRequest, "{\"error\":\"invalid_token\"}");
        var service = new YouTubeOAuthService(new GoogleOAuthClient(new HttpClient(handler), new FixedClock(Now)), store);

        OAuthException error = await Assert.ThrowsAsync<OAuthException>(() => service.LogoutAsync());

        Assert.Equal("invalid_token", error.ErrorCode);
        Assert.Null(store.Load());
    }
}
