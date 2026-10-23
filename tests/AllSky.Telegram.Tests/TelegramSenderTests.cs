using AllSky.Telegram;
using Xunit;

namespace AllSky.Telegram.Tests;

public sealed class TelegramSenderTests
{
    private const long Authorized = 42;
    private const long Stranger = 999;

    private static TelegramSettings Settings(bool enabled = true, OversizeVideoPolicy policy = OversizeVideoPolicy.SendStillsWithLink)
        => TelegramRig.Settings(Authorized) with { Enabled = enabled, OversizeVideoPolicy = policy };

    [Fact]
    public async Task ChatAutorizzata_MessaggioInviato()
    {
        var rig = new TelegramRig();
        rig.Handler.Ok();

        TelegramResult r = await rig.Sender(Settings()).SendMessageAsync(Authorized, "Notte buona");

        Assert.True(r.Ok);
        Assert.Equal(1, rig.Handler.Count);
        Assert.Contains("\"text\":\"Notte buona\"", rig.Handler.Requests[0].Body);
    }

    [Fact]
    public async Task ChatNonAutorizzata_RifiutataPrimaDellaRete()
    {
        var rig = new TelegramRig();

        TelegramResult r = await rig.Sender(Settings()).SendMessageAsync(Stranger, "ciao");

        Assert.Equal(TelegramErrorKind.Unauthorized, r.ErrorKind);
        Assert.Equal(0, rig.Handler.Count);
    }

    [Fact]
    public async Task InvioDisattivato_RifiutatoSenzaRete()
    {
        var rig = new TelegramRig();

        TelegramResult r = await rig.Sender(Settings(enabled: false)).SendMessageAsync(Authorized, "ciao");

        Assert.Equal(TelegramErrorKind.Unauthorized, r.ErrorKind);
        Assert.Equal(0, rig.Handler.Count);
    }

    [Fact]
    public async Task TokenMancante_RifiutatoSenzaRete()
    {
        var rig = new TelegramRig(token: new AllSky.Publish.Secret(""));
        TelegramSettings settings = Settings() with { BotToken = new AllSky.Publish.Secret("") };

        TelegramResult r = await rig.Sender(settings).SendMessageAsync(Authorized, "ciao");

        Assert.Equal(TelegramErrorKind.Unauthorized, r.ErrorKind);
        Assert.Equal(0, rig.Handler.Count);
    }

    [Fact]
    public async Task Didascalia_TroppoLunga_RifiutataSenzaRete()
    {
        var rig = new TelegramRig();

        TelegramResult r = await rig.Sender(Settings())
            .SendDocumentAsync(Authorized, TelegramRig.Bytes(10), "a.txt", new string('x', 1025));

        Assert.Equal(TelegramErrorKind.InvalidInput, r.ErrorKind);
        Assert.Equal(0, rig.Handler.Count);
    }

    [Fact]
    public async Task Foto_NelleSoglie_InviataComeFoto()
    {
        var rig = new TelegramRig();
        rig.Handler.Ok();

        TelegramResult r = await rig.Sender(Settings())
            .SendPhotoAsync(Authorized, TelegramRig.Bytes(2_000_000), "keogram.jpg", "Keogram", 4096, 1500);

        Assert.True(r.Ok);
        string body = rig.Handler.Requests[0].Body;
        Assert.Contains("name=photo", body);
        Assert.DoesNotContain("name=document", body);
    }

    [Fact]
    public async Task Foto_RapportoVentuno_InviataComeDocumento()
    {
        var rig = new TelegramRig();
        rig.Handler.Ok();

        TelegramResult r = await rig.Sender(Settings())
            .SendPhotoAsync(Authorized, TelegramRig.Bytes(1_000_000), "striscia.jpg", null, 6300, 300);

        Assert.True(r.Ok);
        Assert.Equal(1, rig.Handler.Count);
        string body = rig.Handler.Requests[0].Body;
        Assert.Contains("name=document; filename=striscia.jpg", body);
        Assert.DoesNotContain("name=photo", body);
        Assert.Contains("[INFO] La foto supera i limiti di sendPhoto", string.Join("\n", rig.LogLines));
    }

    [Fact]
    public async Task Video_60MB_PoliticaRicodifica_RifiutatoConSuggerimento()
    {
        var rig = new TelegramRig();

        TelegramResult r = await rig.Sender(Settings(policy: OversizeVideoPolicy.Reencode))
            .SendVideoAsync(Authorized, TelegramRig.Bytes(60_000_000), "notte.mp4", null);

        Assert.Equal(TelegramErrorKind.TooLarge, r.ErrorKind);
        Assert.Contains("ricodifica", r.Message);
        Assert.Equal(0, rig.Handler.Count);
    }

    [Fact]
    public async Task Video_60MB_PoliticaStills_RifiutatoConKeogramELink()
    {
        var rig = new TelegramRig();

        TelegramResult r = await rig.Sender(Settings(policy: OversizeVideoPolicy.SendStillsWithLink))
            .SendVideoAsync(Authorized, TelegramRig.Bytes(60_000_000), "notte.mp4", null);

        Assert.Equal(TelegramErrorKind.TooLarge, r.ErrorKind);
        Assert.Contains("keogram", r.Message);
        Assert.Contains("link", r.Message);
        Assert.Equal(0, rig.Handler.Count);
    }

    [Fact]
    public async Task Video_Sotto50MB_InviatoConStreaming()
    {
        var rig = new TelegramRig();
        rig.Handler.Ok();

        TelegramResult r = await rig.Sender(Settings())
            .SendVideoAsync(Authorized, TelegramRig.Bytes(1000), "notte.mp4", "Timelapse");

        Assert.True(r.Ok);
        string body = rig.Handler.Requests[0].Body;
        Assert.Contains("name=video", body);
        Assert.Contains("\r\n\r\ntrue\r\n", body);
    }

    [Fact]
    public async Task ProvaDiInvio_GetMe_SenzaMessaggi()
    {
        var rig = new TelegramRig();
        rig.Handler.Ok("{\"id\":1,\"is_bot\":true}");

        TelegramResult r = await rig.Sender(Settings(enabled: false)).TestConnectionAsync();

        Assert.True(r.Ok);
        Assert.Equal("GET", rig.Handler.Requests[0].Method);
        Assert.EndsWith("/getMe", rig.Handler.Requests[0].Path);
    }

    [Fact]
    public async Task ProvaDiInvio_SenzaToken_NessunaRete()
    {
        var rig = new TelegramRig();
        TelegramSettings settings = Settings() with { BotToken = new AllSky.Publish.Secret("") };

        TelegramResult r = await rig.Sender(settings).TestConnectionAsync();

        Assert.Equal(TelegramErrorKind.Unauthorized, r.ErrorKind);
        Assert.Equal(0, rig.Handler.Count);
    }

    [Fact]
    public async Task LimiteGiornalieroDelSender_ScartaSenzaRete()
    {
        var rig = new TelegramRig(dailyLimit: 1);
        rig.Handler.Ok();
        TelegramSender sender = rig.Sender(Settings());

        TelegramResult first = await sender.SendMessageAsync(Authorized, "uno");
        TelegramResult second = await sender.SendMessageAsync(Authorized, "due");

        Assert.True(first.Ok);
        Assert.Equal(TelegramErrorKind.RateLimited, second.ErrorKind);
        Assert.Equal(1, rig.Handler.Count);
    }
}
