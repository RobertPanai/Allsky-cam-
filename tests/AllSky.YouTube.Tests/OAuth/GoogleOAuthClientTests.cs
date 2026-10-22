using System.Net;
using AllSky.YouTube;
using Xunit;

namespace AllSky.YouTube.Tests;

public sealed class GoogleOAuthClientTests
{
    private const string Verifier = "verifier-di-prova-di-quarantatre-caratteri-xx";
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

    private static OAuthClientCredentials Credentials() =>
        new("client-di-prova", new OAuthSecret("segreto-di-prova"));

    [Fact]
    public async Task Exchange_InviaParametriEPrendeIToken()
    {
        var handler = new FakeHandler(HttpStatusCode.OK,
            "{\"access_token\":\"access-prova\",\"expires_in\":3600,\"refresh_token\":\"refresh-prova\"," +
            "\"scope\":\"https://www.googleapis.com/auth/youtube.upload\",\"token_type\":\"Bearer\"}");
        var client = new GoogleOAuthClient(new HttpClient(handler), new FixedClock(Now));

        OAuthTokens tokens = await client.ExchangeCodeAsync(
            Credentials(), "codice-prova", "http://127.0.0.1:53000", Verifier, CancellationToken.None);

        RecordedRequest request = Assert.Single(handler.Requests);
        Assert.Equal(GoogleOAuth.TokenEndpoint, request.Url.ToString());
        var form = QueryParser.ParseForm(request.Body);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("codice-prova", form["code"]);
        Assert.Equal("http://127.0.0.1:53000", form["redirect_uri"]);
        Assert.Equal("client-di-prova", form["client_id"]);
        Assert.Equal("segreto-di-prova", form["client_secret"]);
        Assert.Equal(Verifier, form["code_verifier"]);

        Assert.Equal("access-prova", tokens.AccessToken.Reveal());
        Assert.Equal("refresh-prova", tokens.RefreshToken.Reveal());
        Assert.Equal(Now.AddSeconds(3600), tokens.AccessTokenExpiresAtUtc);
    }

    [Fact]
    public async Task Refresh_SenzaNuovoRefreshToken_LaRispostaNeNonContiene()
    {
        var handler = new FakeHandler(HttpStatusCode.OK,
            "{\"access_token\":\"access-2\",\"expires_in\":3599,\"token_type\":\"Bearer\"}");
        var client = new GoogleOAuthClient(new HttpClient(handler), new FixedClock(Now));

        OAuthTokens tokens = await client.RefreshAsync(Credentials(), new OAuthSecret("refresh-prova"), CancellationToken.None);

        RecordedRequest request = Assert.Single(handler.Requests);
        var form = QueryParser.ParseForm(request.Body);
        Assert.Equal("refresh_token", form["grant_type"]);
        Assert.Equal("refresh-prova", form["refresh_token"]);
        Assert.False(tokens.HasRefreshToken);
        Assert.Equal("access-2", tokens.AccessToken.Reveal());
        Assert.Equal(Now.AddSeconds(3599), tokens.AccessTokenExpiresAtUtc);
    }

    [Fact]
    public async Task InvalidGrant_EPermanente_EIlTokenNonCompareNeiMessaggi()
    {
        var handler = new FakeHandler(HttpStatusCode.BadRequest,
            "{\"error\":\"invalid_grant\",\"error_description\":\"Bad Request\"}");
        var client = new GoogleOAuthClient(new HttpClient(handler), new FixedClock(Now));

        OAuthException error = await Assert.ThrowsAsync<OAuthException>(
            () => client.RefreshAsync(Credentials(), new OAuthSecret("refresh-prova"), CancellationToken.None));

        Assert.Equal("invalid_grant", error.ErrorCode);
        Assert.Equal(400, error.HttpStatus!.Value);
        Assert.True(error.IsPermanent);
        Assert.DoesNotContain("refresh-prova", error.ToString());
    }

    [Fact]
    public async Task ErroreDelServer_SenzaJson_NonPermanente()
    {
        var handler = new FakeHandler(HttpStatusCode.InternalServerError, "<html>errore interno</html>");
        var client = new GoogleOAuthClient(new HttpClient(handler), new FixedClock(Now));

        OAuthException error = await Assert.ThrowsAsync<OAuthException>(
            () => client.RefreshAsync(Credentials(), new OAuthSecret("refresh-prova"), CancellationToken.None));

        Assert.Equal("http_500", error.ErrorCode);
        Assert.False(error.IsPermanent);
    }

    [Fact]
    public async Task RispostaSenzaAccessToken_EInvalida()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "{\"expires_in\":3600}");
        var client = new GoogleOAuthClient(new HttpClient(handler), new FixedClock(Now));

        OAuthException error = await Assert.ThrowsAsync<OAuthException>(
            () => client.RefreshAsync(Credentials(), new OAuthSecret("refresh-prova"), CancellationToken.None));

        Assert.Equal("invalid_response", error.ErrorCode);
    }

    [Fact]
    public async Task Revoca_TokenNelCorpoNonNellUrl()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, string.Empty);
        var client = new GoogleOAuthClient(new HttpClient(handler), new FixedClock(Now));

        await client.RevokeAsync(new OAuthSecret("refresh-prova"), CancellationToken.None);

        RecordedRequest request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethodPost, request.Method.Method);
        Assert.Equal(GoogleOAuth.RevocationEndpoint, request.Url.ToString());
        Assert.DoesNotContain("refresh-prova", request.Url.ToString());
        Assert.Equal("refresh-prova", QueryParser.ParseForm(request.Body)["token"]);
    }

    [Fact]
    public async Task Revoca_TokenNonValido_LanciaErroreConCodice()
    {
        var handler = new FakeHandler(HttpStatusCode.BadRequest, "{\"error\":\"invalid_token\"}");
        var client = new GoogleOAuthClient(new HttpClient(handler), new FixedClock(Now));

        OAuthException error = await Assert.ThrowsAsync<OAuthException>(
            () => client.RevokeAsync(new OAuthSecret("refresh-prova"), CancellationToken.None));

        Assert.Equal("invalid_token", error.ErrorCode);
        Assert.Empty(handler.Requests.Where(r => r.Url.ToString() != GoogleOAuth.RevocationEndpoint));
    }

    [Fact]
    public void ToString_NonMostraTokenNeSegreti()
    {
        var tokens = new OAuthTokens(new OAuthSecret("access-prova"), new OAuthSecret("refresh-prova"), Now, "scope");

        Assert.DoesNotContain("access-prova", tokens.ToString());
        Assert.DoesNotContain("refresh-prova", tokens.ToString());
        Assert.DoesNotContain("segreto-di-prova", Credentials().ToString());
        Assert.DoesNotContain("segreto-di-prova", new OAuthSecret("segreto-di-prova").ToString());
    }

    [Fact]
    public void Credenziali_RifiutanoClientIdVuoto()
    {
        Assert.Throws<ArgumentException>(() => new OAuthClientCredentials(" ", new OAuthSecret("x")));
    }

    private const string HttpMethodPost = "POST";
}
