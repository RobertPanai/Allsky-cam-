using System.Text.Json;
using AllSky.Publish;
using AllSky.Telegram;
using Xunit;

namespace AllSky.Telegram.Tests;

public sealed class TelegramClientTests
{
    private readonly ScriptedTelegramHandler _handler = new();

    private TelegramClient Client(string token = FakeToken.Value)
        => new(new HttpClient(_handler), new Secret(token));

    [Fact]
    public async Task SendMessage_Ok_PostJsonSulPercorsoDelBot()
    {
        _handler.Ok();

        TelegramResult r = await Client().SendMessageAsync(-100123, "Ciao dall'osservatorio");

        Assert.True(r.Ok);
        RecordedRequest req = Assert.Single(_handler.Requests);
        Assert.Equal("POST", req.Method);
        Assert.Equal("/bot" + FakeToken.Value + "/sendMessage", req.Path);
        // Il corpo e' JSON: lo decodifico (System.Text.Json scrive l'apostrofo come \u0027, che e' equivalente).
        JsonElement json = JsonDocument.Parse(req.Body).RootElement;
        Assert.Equal(-100123L, json.GetProperty("chat_id").GetInt64());
        Assert.Equal("Ciao dall'osservatorio", json.GetProperty("text").GetString());
    }

    [Fact]
    public async Task SendPhoto_Multipart_CampiEFileNelCorpo()
    {
        _handler.Ok();
        byte[] jpeg = TelegramRig.Bytes(1234);

        TelegramResult r = await Client().SendPhotoAsync(42, jpeg, "keogram.jpg", "Keogram notte", 4096, 1500);

        Assert.True(r.Ok);
        string body = Assert.Single(_handler.Requests).Body;
        Assert.Contains("name=chat_id", body);
        Assert.Contains("\r\n\r\n42\r\n", body);
        Assert.Contains("name=caption", body);
        Assert.Contains("Keogram notte", body);
        Assert.Contains("name=photo; filename=keogram.jpg", body);
        Assert.Contains("Content-Type: image/jpeg", body);
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public async Task SendVideo_Stream_CampoSupportsStreaming(bool streaming, string expectedValue)
    {
        _handler.Ok();
        using var video = new MemoryStream(TelegramRig.Bytes(5000));

        TelegramResult r = await Client().SendVideoAsync(7, video, "notte.mp4", null, streaming);

        Assert.True(r.Ok);
        string body = Assert.Single(_handler.Requests).Body;
        Assert.Contains("name=video; filename=notte.mp4", body);
        Assert.Contains("Content-Type: video/mp4", body);
        Assert.Contains("name=supports_streaming", body);
        Assert.Contains("\r\n\r\n" + expectedValue + "\r\n", body);
        Assert.DoesNotContain("name=caption", body);
    }

    [Fact]
    public async Task SendVideo_Bytes_RiusaLoStessoCampo()
    {
        _handler.Ok();

        TelegramResult r = await Client().SendVideoAsync(7, TelegramRig.Bytes(100), "notte.mp4", "Notte", true);

        Assert.True(r.Ok);
        Assert.Contains("name=supports_streaming", Assert.Single(_handler.Requests).Body);
    }

    [Fact]
    public async Task SendDocument_Stream_Ok()
    {
        _handler.Ok();
        using var doc = new MemoryStream(TelegramRig.Bytes(2000));

        TelegramResult r = await Client().SendDocumentAsync(9, doc, "startrails.tif", "Startrails");

        Assert.True(r.Ok);
        string body = Assert.Single(_handler.Requests).Body;
        Assert.Contains("name=document; filename=startrails.tif", body);
        Assert.Contains("Startrails", body);
    }

    [Fact]
    public async Task Video_60MB_TooLarge_SenzaChiamataDiRete()
    {
        TelegramResult r = await Client().SendVideoAsync(7, TelegramRig.Bytes(60_000_000), "notte.mp4", null);

        Assert.Equal(TelegramErrorKind.TooLarge, r.ErrorKind);
        Assert.Equal(0, _handler.Count);
    }

    [Fact]
    public async Task Video_Stream_60MB_TooLarge_SenzaLeggereIlFlusso()
    {
        using var video = new MemoryStream(TelegramRig.Bytes(60_000_000));

        TelegramResult r = await Client().SendVideoAsync(7, video, "notte.mp4", null);

        Assert.Equal(TelegramErrorKind.TooLarge, r.ErrorKind);
        Assert.Equal(0, _handler.Count);
    }

    [Fact]
    public async Task Documento_50MBEsatti_Accettato()
    {
        _handler.Ok();

        TelegramResult r = await Client().SendDocumentAsync(1, TelegramRig.Bytes(50_000_000), "dati.bin", null);

        Assert.True(r.Ok);
        Assert.Equal(1, _handler.Count);
    }

    [Fact]
    public async Task Documento_50MBPiuUnByte_TooLarge_SenzaRete()
    {
        TelegramResult r = await Client().SendDocumentAsync(1, TelegramRig.Bytes(50_000_001), "dati.bin", null);

        Assert.Equal(TelegramErrorKind.TooLarge, r.ErrorKind);
        Assert.Equal(0, _handler.Count);
    }

    [Fact]
    public async Task Foto_Oltre10MB_TooLarge_SenzaRete()
    {
        TelegramResult r = await Client().SendPhotoAsync(1, TelegramRig.Bytes(10_000_001), "a.jpg", null);

        Assert.Equal(TelegramErrorKind.TooLarge, r.ErrorKind);
        Assert.Equal(0, _handler.Count);
    }

    [Fact]
    public async Task Foto_Rapporto21_InvalidInput_SenzaRete()
    {
        TelegramResult r = await Client().SendPhotoAsync(1, TelegramRig.Bytes(1000), "a.jpg", null, 6300, 300);

        Assert.Equal(TelegramErrorKind.InvalidInput, r.ErrorKind);
        Assert.Equal(0, _handler.Count);
    }

    [Fact]
    public async Task Didascalia_Oltre1024_InvalidInput_SenzaRete()
    {
        TelegramResult r = await Client().SendDocumentAsync(1, TelegramRig.Bytes(10), "a.txt", new string('x', 1025));

        Assert.Equal(TelegramErrorKind.InvalidInput, r.ErrorKind);
        Assert.Equal(0, _handler.Count);
    }

    [Fact]
    public async Task Testo_Vuoto_InvalidInput_SenzaRete()
    {
        TelegramResult r = await Client().SendMessageAsync(1, "   ");

        Assert.Equal(TelegramErrorKind.InvalidInput, r.ErrorKind);
        Assert.Equal(0, _handler.Count);
    }

    [Fact]
    public async Task GetMe_Get_VerificaIlToken()
    {
        _handler.Ok("{\"id\":1,\"is_bot\":true}");

        TelegramResult r = await Client().GetMeAsync();

        Assert.True(r.Ok);
        RecordedRequest req = Assert.Single(_handler.Requests);
        Assert.Equal("GET", req.Method);
        Assert.Equal("/bot" + FakeToken.Value + "/getMe", req.Path);
    }

    [Fact]
    public async Task Risposta401_Unauthorized_UnSoloTentativo()
    {
        _handler.Reply(401, "{\"ok\":false,\"error_code\":401,\"description\":\"Unauthorized\"}");

        TelegramResult r = await Client().SendMessageAsync(1, "ciao");

        Assert.Equal(TelegramErrorKind.Unauthorized, r.ErrorKind);
        Assert.Equal(1, _handler.Count);
    }

    [Fact]
    public async Task Risposta429_LeggeRetryAfterDalCorpo()
    {
        _handler.Reply(429, "{\"ok\":false,\"error_code\":429,\"description\":\"Too Many Requests: retry after 3\",\"parameters\":{\"retry_after\":3}}");

        TelegramResult r = await Client().SendMessageAsync(1, "ciao");

        Assert.Equal(TelegramErrorKind.RateLimited, r.ErrorKind);
        Assert.Equal(3, r.RetryAfterSeconds);
        Assert.DoesNotContain("retry after", r.Message);
    }

    [Fact]
    public async Task Risposta429_SenzaParameters_RetryAfterNullo()
    {
        _handler.Reply(429, "{\"ok\":false,\"error_code\":429}");

        TelegramResult r = await Client().SendMessageAsync(1, "ciao");

        Assert.Equal(TelegramErrorKind.RateLimited, r.ErrorKind);
        Assert.Null(r.RetryAfterSeconds);
    }

    [Theory]
    [InlineData(400, "Rejected")]
    [InlineData(403, "Rejected")]
    [InlineData(413, "TooLarge")]
    [InlineData(500, "Network")]
    [InlineData(502, "Network")]
    public async Task AltriCodici_MappatiSenzaRitentativi(int status, string expected)
    {
        _handler.Reply(status, "{\"ok\":false}");

        TelegramResult r = await Client().SendMessageAsync(1, "ciao");

        Assert.Equal(expected, r.ErrorKind.ToString());
        Assert.Equal(1, _handler.Count);
    }

    [Fact]
    public async Task RispostaOk_CorpoNonLeggibile_Network()
    {
        _handler.Reply(200, "<html>proxy</html>");

        TelegramResult r = await Client().SendMessageAsync(1, "ciao");

        Assert.Equal(TelegramErrorKind.Network, r.ErrorKind);
    }

    [Fact]
    public async Task EccezioneDiRete_NonRiportaUrlNeToken()
    {
        _handler.Throw(new HttpRequestException("Errore su https://api.telegram.org/bot" + FakeToken.Value + "/sendMessage"));

        TelegramResult r = await Client().SendMessageAsync(1, "ciao");

        Assert.Equal(TelegramErrorKind.Network, r.ErrorKind);
        Assert.DoesNotContain(FakeToken.Marker, r.Message);
        Assert.DoesNotContain("https://", r.Message);
    }

    [Fact]
    public async Task Timeout_DiRete_Network()
    {
        _handler.Throw(new TaskCanceledException("scaduto"));

        TelegramResult r = await Client().SendMessageAsync(1, "ciao");

        Assert.Equal(TelegramErrorKind.Network, r.ErrorKind);
    }

    [Fact]
    public async Task AnnullamentoDelChiamante_SiPropaga()
    {
        // Il gestore reale, annullato dal token del chiamante, lancia OperationCanceledException: deve uscire.
        _handler.Throw(new OperationCanceledException());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client().SendMessageAsync(1, "ciao", cts.Token));
    }

    [Fact]
    public async Task TokenVuoto_Unauthorized_SenzaRete()
    {
        TelegramResult r = await Client(token: "").SendMessageAsync(1, "ciao");

        Assert.Equal(TelegramErrorKind.Unauthorized, r.ErrorKind);
        Assert.Equal(0, _handler.Count);
    }

    [Theory]
    [InlineData("123/456")]
    [InlineData("123 456")]
    [InlineData("123?x=1")]
    public async Task TokenConCaratteriNonAmmessi_Unauthorized_SenzaRete(string token)
    {
        TelegramResult r = await Client(token).SendMessageAsync(1, "ciao");

        Assert.Equal(TelegramErrorKind.Unauthorized, r.ErrorKind);
        Assert.Equal(0, _handler.Count);
    }

    [Fact]
    public void BaseUrlSenzaHttps_Rifiutata()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            _ = new TelegramClient(new HttpClient(_handler), new Secret(FakeToken.Value), new Uri("http://api.example.org"));
        });
    }

    [Fact]
    public void BaseUrlLocaleHttp_Accettata()
    {
        _ = new TelegramClient(new HttpClient(_handler), new Secret(FakeToken.Value), new Uri("http://127.0.0.1:8081"));
    }

    [Fact]
    public void ToString_NonContieneIlToken()
    {
        Assert.DoesNotContain(FakeToken.Marker, Client().ToString());
    }
}
