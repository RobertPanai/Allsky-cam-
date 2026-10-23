using AllSky.Publish;
using AllSky.Telegram;
using Xunit;

namespace AllSky.Telegram.Tests;

public sealed class TelegramSettingsStoreTests : IDisposable
{
    private readonly TempFolder _dir = new();

    public void Dispose() => _dir.Dispose();

    private static TelegramSettings Sample() => new()
    {
        Enabled = true,
        BotToken = new Secret(FakeToken.Value),
        AuthorizedChatIds = new long[] { 42, -100123 },
        DailyLimit = 20,
        OversizeVideoPolicy = OversizeVideoPolicy.Reencode,
    };

    [Fact]
    public void RoundTrip_TokenCifratoSuDisco()
    {
        string path = _dir.File("telegram.json");
        var store = new TelegramSettingsStore(path, new ReversibleProtector());

        store.Save(Sample());

        string onDisk = File.ReadAllText(path);
        Assert.DoesNotContain(FakeToken.Marker, onDisk);
        Assert.Contains("botTokenProtected", onDisk);

        TelegramSettings loaded = store.Load();
        Assert.True(loaded.Enabled);
        Assert.Equal(FakeToken.Value, loaded.BotToken.Reveal());
        Assert.Equal(new long[] { 42, -100123 }, loaded.AuthorizedChatIds.ToArray());
        Assert.Equal(20, loaded.DailyLimit);
        Assert.Equal(OversizeVideoPolicy.Reencode, loaded.OversizeVideoPolicy);
    }

    [Fact]
    public void FileAssente_ValoriDiDefault()
    {
        TelegramSettings loaded = new TelegramSettingsStore(_dir.File("manca.json"), new ReversibleProtector()).Load();

        Assert.False(loaded.Enabled);
        Assert.True(loaded.BotToken.IsEmpty);
        Assert.Empty(loaded.AuthorizedChatIds);
        Assert.Equal(SendQueue.DefaultDailyLimit, loaded.DailyLimit);
        Assert.Equal(OversizeVideoPolicy.SendStillsWithLink, loaded.OversizeVideoPolicy);
    }

    [Fact]
    public void ProtezioneNonDisponibile_NonSalvaNulla()
    {
        string path = _dir.File("telegram.json");
        var protector = new ReversibleProtector { NotSupported = true };
        var store = new TelegramSettingsStore(path, protector);

        Assert.Throws<InvalidOperationException>(() => store.Save(Sample()));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void JsonCorrotto_SpostatoInBad_ValoriDiDefault()
    {
        string path = _dir.File("telegram.json");
        File.WriteAllText(path, "{non e' json");

        TelegramSettings loaded = new TelegramSettingsStore(path, new ReversibleProtector()).Load();

        Assert.False(loaded.Enabled);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(path + ".bad"));
    }

    [Fact]
    public void LimiteGiornalieroNonValido_TornaAlDefault()
    {
        string path = _dir.File("telegram.json");
        File.WriteAllText(path, "{\"enabled\":true,\"dailyLimit\":0}");

        TelegramSettings loaded = new TelegramSettingsStore(path, new ReversibleProtector()).Load();

        Assert.True(loaded.Enabled);
        Assert.Equal(SendQueue.DefaultDailyLimit, loaded.DailyLimit);
    }

    [Fact]
    public void TokenNonDecifrabile_DiventaVuoto()
    {
        string path = _dir.File("telegram.json");
        File.WriteAllText(path, "{\"enabled\":true,\"botTokenProtected\":\"non-base64!!\"}");

        TelegramSettings loaded = new TelegramSettingsStore(path, new ReversibleProtector()).Load();

        Assert.True(loaded.Enabled);
        Assert.True(loaded.BotToken.IsEmpty);
    }
}
