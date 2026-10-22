using AllSky.YouTube;
using Xunit;

namespace AllSky.YouTube.Tests;

public sealed class YouTubeAuthStoreTests
{
    private static StoredYouTubeAuth Sample() =>
        new("client-di-prova", new OAuthSecret("segreto-di-prova"), new OAuthSecret("refresh-prova"));

    [Fact]
    public void SaveLoad_RoundTrip_SenzaSegretiInChiaroNelFile()
    {
        using var dir = new TempDir();
        var store = new YouTubeAuthStore(dir.File("youtube-auth.json"), new FakeProtector());

        store.Save(Sample());

        string text = File.ReadAllText(store.FilePath);
        Assert.DoesNotContain("refresh-prova", text);
        Assert.DoesNotContain("segreto-di-prova", text);

        StoredYouTubeAuth? loaded = store.Load();
        Assert.NotNull(loaded);
        Assert.Equal("client-di-prova", loaded!.ClientId);
        Assert.Equal("segreto-di-prova", loaded.ClientSecret.Reveal());
        Assert.Equal("refresh-prova", loaded.RefreshToken.Reveal());
        Assert.DoesNotContain("refresh-prova", loaded.ToString());
        Assert.DoesNotContain("segreto-di-prova", loaded.ToString());
    }

    [Fact]
    public void Load_FileAssente_RestituisceNull()
    {
        using var dir = new TempDir();
        var store = new YouTubeAuthStore(dir.File("assente.json"), new FakeProtector());

        Assert.Null(store.Load());
    }

    [Fact]
    public void Load_JsonCorrotto_RestituisceNullSenzaEccezioni()
    {
        using var dir = new TempDir();
        string path = dir.File("youtube-auth.json");
        File.WriteAllText(path, "{ non e' json");
        var store = new YouTubeAuthStore(path, new FakeProtector());

        Assert.Null(store.Load());
    }

    [Fact]
    public void Load_RefreshTokenNonDecifrabile_RestituisceNull()
    {
        using var dir = new TempDir();
        string path = dir.File("youtube-auth.json");
        new YouTubeAuthStore(path, new FakeProtector()).Save(Sample());

        var store = new YouTubeAuthStore(path, new FakeProtector { FailUnprotect = true });

        Assert.Null(store.Load());
    }

    [Fact]
    public void Save_ProtezioneNonDisponibile_LanciaESenzaScrivere()
    {
        using var dir = new TempDir();
        string path = dir.File("youtube-auth.json");
        var store = new YouTubeAuthStore(path, new FakeProtector { NotSupported = true });

        Assert.Throws<InvalidOperationException>(() => store.Save(Sample()));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Delete_RimuoveIlFile()
    {
        using var dir = new TempDir();
        var store = new YouTubeAuthStore(dir.File("youtube-auth.json"), new FakeProtector());
        store.Save(Sample());

        store.Delete();

        Assert.False(File.Exists(store.FilePath));
        Assert.Null(store.Load());
    }
}
