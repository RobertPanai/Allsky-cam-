using AllSky.Publish;
using AllSky.Telegram;
using Xunit;

namespace AllSky.Telegram.Tests;

/// <summary>Il token non deve comparire in nessun messaggio, riga di log, ToString o eccezione prodotta dal modulo.</summary>
public sealed class SecretLeakTests
{
    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(413)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task EsitiDiErrore_NessunMessaggioNeLogNeToStringContieneIlToken(int status)
    {
        var rig = new TelegramRig();
        // Il corpo ripete il token, come farebbe una risposta non sanificata: il modulo non deve riportarlo.
        rig.Handler.Reply(status,
            "{\"ok\":false,\"description\":\"token errato " + FakeToken.Value + "\",\"parameters\":{\"retry_after\":2}}");
        TelegramSettings settings = TelegramRig.Settings(42);
        TelegramSender sender = rig.Sender(settings);

        TelegramResult r = await sender.SendMessageAsync(42, "ciao");
        TelegramResult test = await sender.TestConnectionAsync();

        var outputs = new List<string>
        {
            r.Message,
            r.ErrorKind.ToString() ?? string.Empty,
            test.Message,
            settings.ToString(),
            rig.Client.ToString(),
            sender.ToString() ?? string.Empty,
        };
        outputs.AddRange(rig.LogLines);

        Assert.All(outputs, text => Assert.DoesNotContain(FakeToken.Marker, text));
    }

    [Fact]
    public void Registro_RedigeIlTokenAncheSeArrivaPerErrore()
    {
        var lines = new List<string>();
        var log = new TelegramLog(lines.Add, new Redactor(() => new[] { FakeToken.Value }));

        log.Warn("errore con " + FakeToken.Value + " su https://api.telegram.org/bot" + FakeToken.Value + "/sendMessage?x=1");

        Assert.Single(lines);
        Assert.DoesNotContain(FakeToken.Marker, lines[0]);
    }

    [Fact]
    public void Registro_UnErroreNelDestinatarioNonInterrompeIlLog()
    {
        var log = new TelegramLog(_ => throw new IOException("disco pieno"), new Redactor(() => Array.Empty<string>()));

        log.Info("messaggio");
    }

    [Fact]
    public void Impostazioni_ToStringNonRivelaIlToken()
    {
        TelegramSettings settings = TelegramRig.Settings(42, -100123);

        Assert.DoesNotContain(FakeToken.Marker, settings.ToString());
        Assert.DoesNotContain(FakeToken.Marker, settings.BotToken.ToString());
    }

    [Fact]
    public void Client_ToStringNonRivelaIlToken()
    {
        var client = new TelegramClient(new HttpClient(new ScriptedTelegramHandler()), new Secret(FakeToken.Value));

        Assert.DoesNotContain(FakeToken.Marker, client.ToString());
    }
}
