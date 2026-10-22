using AllSky.Core.LogView;
using Xunit;

namespace AllSky.Core.Tests.LogView;

public sealed class LogSecretMaskTests
{
    [Theory]
    [InlineData("Authorization: Bearer abc.def-123", "Authorization: ***")]
    [InlineData("Authorization: Basic cm9iZXJ0OnNlZ3JldG8=", "Authorization: ***")]
    [InlineData("richiesta con Bearer eyJhbGciOi.payload.sig fine", "richiesta con Bearer *** fine")]
    public void Intestazioni_VengonoMascherate(string input, string expected)
    {
        Assert.Equal(expected, LogSecretMask.Apply(input));
    }

    [Theory]
    [InlineData("token=ab12cd34", "token=***")]
    [InlineData("password: hunter2", "password: ***")]
    [InlineData("\"refresh_token\": \"1//0gAbCdEfGhIj\"", "\"refresh_token\": \"***\"")]
    [InlineData("client_secret='ssh-secret-9'", "client_secret='***'")]
    [InlineData("client secret: topsecretvalue", "client secret: ***")]
    [InlineData("https://example.test/cb?access_token=zz99&state=1", "https://example.test/cb?access_token=***&state=1")]
    [InlineData("api_key=KEY123;next=1", "api_key=***;next=1")]
    public void ChiaviSensibili_VengonoMascherate(string input, string expected)
    {
        Assert.Equal(expected, LogSecretMask.Apply(input));
    }

    [Fact]
    public void FormatiGoogle_VengonoMascherati()
    {
        Assert.Equal("secret *** fine", LogSecretMask.Apply("secret GOCSPX-abc_DEF-123 fine"));
        Assert.Equal("access ***", LogSecretMask.Apply("access ya29.a0AfH6SMB-xyz"));
        Assert.Equal("refresh ***", LogSecretMask.Apply("refresh 1//0gAbCdEfGhIjKl"));
    }

    [Theory]
    [InlineData("Avvio di AllSky Host. Dati: 'C:\\Users\\robert\\AllSky'.")]
    [InlineData("Porta 5810 occupata: provo la successiva.")]
    [InlineData("Latitudine 45.07, longitudine 7.69, quota 240 m.")]
    public void TestoSenzaSegreti_RestaInvariato(string input)
    {
        Assert.Equal(input, LogSecretMask.Apply(input));
    }

    [Fact]
    public void ChiaveSenzaValore_NonMangiaLaRigaSuccessiva()
    {
        string input = "Password per la rete locale:\nAvvio completato";

        Assert.Equal(input, LogSecretMask.Apply(input));
    }

    [Fact]
    public void Idempotente()
    {
        string once = LogSecretMask.Apply("token=abc password: xyz Bearer tok123");

        Assert.Equal(once, LogSecretMask.Apply(once));
    }

    [Fact]
    public void Null_RestituisceVuoto()
    {
        Assert.Equal(string.Empty, LogSecretMask.Apply(null));
    }
}
