using AllSky.YouTube;
using Xunit;

namespace AllSky.YouTube.Tests;

public sealed class ResumableUploadClientTests
{
    private const string Session = "https://upload.example/sessione?upload_id=abc";

    private static ResumableUploadClient Client(ScriptedHandler handler, FakeTokens tokens) =>
        new(new HttpClient(handler), tokens, UploadHarness.ChunkBytes);

    private static VideoMetadata Metadata() =>
        new("Titolo di prova", "Descrizione di prova", new[] { "tag1" }, "28", YouTubePrivacy.Private);

    [Fact]
    public async Task StartSession_InviaInizializzazioneResumableEScriveLaSessione()
    {
        var handler = new ScriptedHandler();
        handler.ReplyLocation(Session);

        Uri uri = await Client(handler, new FakeTokens()).StartSessionAsync(Metadata(), 600_000, CancellationToken.None);

        Assert.Equal(Session, uri.AbsoluteUri);
        SentRequest sent = Assert.Single(handler.Sent);
        Assert.Equal("POST", sent.Method);
        Assert.StartsWith(ResumableUploadClient.UploadEndpoint + "?uploadType=resumable&part=snippet,status", sent.Url);
        Assert.Equal("600000", sent.UploadLength);
        Assert.Equal("video/mp4", sent.UploadType);
        Assert.Equal("Bearer tok-1", sent.Authorization);
        Assert.Contains("\"privacyStatus\":\"private\"", sent.Body!);
        Assert.Contains("\"selfDeclaredMadeForKids\":false", sent.Body!);
        Assert.Contains("\"title\":\"Titolo di prova\"", sent.Body!);
    }

    [Fact]
    public async Task SendChunk_InviaContentRangeELeggeIlRangeRicevuto()
    {
        var handler = new ScriptedHandler();
        handler.Reply(308, "", ("Range", "bytes=0-262143"));

        UploadProgress progress = await Client(handler, new FakeTokens())
            .SendChunkAsync(new Uri(Session), new byte[262_144], 0, 600_000, CancellationToken.None);

        Assert.Equal(262_144L, progress.NextOffset);
        Assert.False(progress.Completed);
        SentRequest sent = Assert.Single(handler.Sent);
        Assert.Equal("PUT", sent.Method);
        Assert.Equal("bytes 0-262143/600000", sent.ContentRange);
        Assert.Equal(262_144, sent.ContentLength);
    }

    [Fact]
    public async Task Query_UsaBytesStarTotale_SenzaRangeRiparteDaZero()
    {
        var handler = new ScriptedHandler();
        handler.Reply(308);

        UploadProgress progress = await Client(handler, new FakeTokens())
            .QueryAsync(new Uri(Session), 600_000, CancellationToken.None);

        Assert.Equal(0L, progress.NextOffset);
        SentRequest sent = Assert.Single(handler.Sent);
        Assert.Equal("bytes */600000", sent.ContentRange);
        Assert.Equal(0, sent.ContentLength);
    }

    [Fact]
    public async Task Completamento_201_RestituisceIdVideo()
    {
        var handler = new ScriptedHandler();
        handler.Reply(201, "{\"id\":\"abc123XYZ\",\"kind\":\"youtube#video\"}");

        UploadProgress progress = await Client(handler, new FakeTokens())
            .SendChunkAsync(new Uri(Session), new byte[10], 599_990, 600_000, CancellationToken.None);

        Assert.True(progress.Completed);
        Assert.Equal("abc123XYZ", progress.VideoId);
    }

    [Fact]
    public async Task Unauthorized_RinnovaIlTokenUnaVoltaERiprova()
    {
        var handler = new ScriptedHandler();
        handler.Reply(401);
        handler.ReplyLocation(Session);
        var tokens = new FakeTokens();

        Uri uri = await Client(handler, tokens).StartSessionAsync(Metadata(), 600_000, CancellationToken.None);

        Assert.Equal(Session, uri.AbsoluteUri);
        Assert.Equal(1, tokens.Refreshes);
        Assert.Equal(2, handler.Sent.Count);
        Assert.Equal("Bearer tok-1", handler.Sent[0].Authorization);
        Assert.Equal("Bearer tok-2", handler.Sent[1].Authorization);
    }

    [Fact]
    public async Task Unauthorized_DueVolte_SolleveErroreSenzaAltriTentativi()
    {
        var handler = new ScriptedHandler();
        handler.Reply(401);
        handler.Reply(401);
        var tokens = new FakeTokens();

        YouTubeApiException ex = await Assert.ThrowsAsync<YouTubeApiException>(
            () => Client(handler, tokens).StartSessionAsync(Metadata(), 600_000, CancellationToken.None));

        Assert.True(ex.IsUnauthorized);
        Assert.Equal(1, tokens.Refreshes);
        Assert.Equal(2, handler.Sent.Count);
    }

    [Fact]
    public async Task Quota403_ConMotivoLeggibileESenzaCorpoNelMessaggio()
    {
        var handler = new ScriptedHandler();
        handler.Reply(403, "{\"error\":{\"message\":\"segreto-del-corpo\",\"errors\":[{\"reason\":\"quotaExceeded\"}]}}");

        YouTubeApiException ex = await Assert.ThrowsAsync<YouTubeApiException>(
            () => Client(handler, new FakeTokens()).StartSessionAsync(Metadata(), 600_000, CancellationToken.None));

        Assert.True(ex.IsQuotaExceeded);
        Assert.Equal("quotaExceeded", ex.Reason);
        Assert.DoesNotContain("segreto-del-corpo", ex.Message);
        Assert.DoesNotContain("tok-", ex.Message);
    }

    [Fact]
    public void Chunk_NonMultiploDi256KB_Rifiutato()
    {
        var handler = new ScriptedHandler();
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = new ResumableUploadClient(new HttpClient(handler), new FakeTokens(), 1000); });
    }
}
