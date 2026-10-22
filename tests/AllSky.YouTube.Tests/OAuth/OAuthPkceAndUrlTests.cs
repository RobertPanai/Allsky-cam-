using AllSky.YouTube;
using Xunit;

namespace AllSky.YouTube.Tests;

public sealed class OAuthPkceAndUrlTests
{
    [Fact]
    public void ComputeChallenge_VettoreRfc7636_Appendice_B()
    {
        // RFC 7636, Appendice B: code_verifier di esempio e code_challenge S256 attesi.
        string challenge = OAuthPkce.ComputeChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk");

        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", challenge);
    }

    [Fact]
    public void NewVerifier_RispettaLaRfc_ELeChiamateSonoDiverse()
    {
        string first = OAuthPkce.NewVerifier();
        string second = OAuthPkce.NewVerifier();

        Assert.Equal(43, first.Length);
        Assert.True(OAuthPkce.IsValidVerifier(first));
        Assert.True(first != second);
    }

    [Fact]
    public void NewState_EBase64UrlSenzaPadding_EDiverso()
    {
        string first = OAuthPkce.NewState();
        string second = OAuthPkce.NewState();

        Assert.DoesNotContain("+", first);
        Assert.DoesNotContain("/", first);
        Assert.DoesNotContain("=", first);
        Assert.True(first != second);
    }

    [Fact]
    public void ComputeChallenge_RifiutaVerifierFuoriDalleRegole()
    {
        Assert.Throws<ArgumentException>(() => OAuthPkce.ComputeChallenge("troppo-corto"));
        Assert.Throws<ArgumentException>(() => OAuthPkce.ComputeChallenge(new string('a', 42) + "+"));
    }

    [Fact]
    public void BuildAuthorizationUrl_ParametriDellaGuida_ScopeUpload_NessunSegreto()
    {
        Uri url = GoogleOAuth.BuildAuthorizationUrl("client-di-prova", "http://127.0.0.1:53000", "stato-x", "challenge-x");

        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", url.ToString());
        var query = QueryParser.ParseQuery(url.Query);
        Assert.Equal(7, query.Count);
        Assert.Equal("client-di-prova", query["client_id"]);
        Assert.Equal("http://127.0.0.1:53000", query["redirect_uri"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("https://www.googleapis.com/auth/youtube.upload", query["scope"]);
        Assert.Equal("stato-x", query["state"]);
        Assert.Equal("challenge-x", query["code_challenge"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.DoesNotContain("client_secret", url.ToString());
    }
}
