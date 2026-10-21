using System.Net;
using Xunit;

namespace AllSky.Publish.Tests;

/// <summary>Test del client HTTP con handler finto: invio, timeout, annullamento e verifica connessione.</summary>
public class UplinkHttpClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 21, 30, 0, TimeSpan.Zero);

    private static UplinkHttpClient Client(HttpMessageHandler handler, TimeSpan? timeout = null)
        => new(new HttpClient(handler), new PublishFakeClock(Now), UplinkTestSupport.Redactor(), timeout);

    private static Task<SendResult> SendAsync(UplinkHttpClient client, PublishSettings? settings = null, CancellationToken ct = default)
        => client.SendAsync(UplinkTestSupport.Item(), UplinkTestSupport.Jpeg(), settings ?? UplinkTestSupport.Settings(), ct);

    [Fact]
    public async Task SendAsync_200WithStoredBody_IsAcknowledgedAndRequestIsWellFormed()
    {
        var handler = UplinkTestHandler.Reply(200, "{\"stored\":true,\"duplicate\":false,\"visible\":true}");
        UplinkHttpClient client = Client(handler);

        SendResult result = await SendAsync(client);

        Assert.Equal(SendOutcome.Acknowledged, result.Outcome);
        Assert.Equal(200, result.StatusCode!.Value);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(UplinkTestSupport.ServerUrl + "/api/uplink/allsky", handler.RequestUri!.ToString());
        Assert.Equal("Bearer " + UplinkTestSupport.TokenValue, handler.Header("Authorization"));
        Assert.StartsWith("multipart/form-data", handler.ContentType!);
    }

    [Fact]
    public async Task SendAsync_SentBodyContainsMetaAndImageParts()
    {
        var handler = UplinkTestHandler.Reply(200, "{\"stored\":true}");
        UplinkHttpClient client = Client(handler);

        await SendAsync(client);

        string boundary = UplinkTestSupport.BoundaryOf(handler.ContentType)!;
        List<MultipartPart> parts = UplinkTestSupport.ParseMultipart(handler.Body!, boundary);
        Assert.Equal(2, parts.Count);
        Assert.Equal("meta", parts[0].Name);
        Assert.Equal("image", parts[1].Name);
        Assert.True(UplinkTestSupport.Jpeg().AsSpan().SequenceEqual(parts[1].Content));
    }

    [Theory]
    [InlineData(400, "Permanent")]
    [InlineData(401, "AuthError")]
    [InlineData(403, "AuthError")]
    [InlineData(413, "Permanent")]
    [InlineData(500, "Transient")]
    public async Task SendAsync_StatusOutcomeThroughHttpClient(int status, string outcome)
    {
        UplinkHttpClient client = Client(UplinkTestHandler.Reply(status));

        SendResult result = await SendAsync(client);

        Assert.Equal(Enum.Parse<SendOutcome>(outcome), result.Outcome);
        Assert.Equal(status, result.StatusCode!.Value);
    }

    [Fact]
    public async Task SendAsync_429_ReadsRetryAfterHeader()
    {
        UplinkHttpClient client = Client(UplinkTestHandler.Reply(429, "{}", "15"));

        SendResult result = await SendAsync(client);

        Assert.Equal(SendOutcome.RateLimited, result.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(15), result.RetryAfter!.Value);
    }

    [Fact]
    public async Task SendAsync_HugeBodyOnError_IsReadPartlyAndClassified()
    {
        // Il corpo viene letto solo in parte (4 KB): l'esito dipende dal solo codice di stato.
        UplinkHttpClient client = Client(UplinkTestHandler.Reply(401, new string('x', 1_000_000)));

        SendResult result = await SendAsync(client);

        Assert.Equal(SendOutcome.AuthError, result.Outcome);
    }

    [Fact]
    public async Task SendAsync_NetworkExceptionWithTokenInMessage_IsSanitized()
    {
        var handler = UplinkTestHandler.Fail(new HttpRequestException(
            "Impossibile contattare https://uplink.test/api/uplink/allsky?token=abc con " + UplinkTestSupport.TokenValue));
        UplinkHttpClient client = Client(handler);

        SendResult result = await SendAsync(client);

        Assert.Equal(SendOutcome.Transient, result.Outcome);
        Assert.Equal("network", result.Code);
        Assert.Null(result.StatusCode);
        Assert.DoesNotContain(UplinkTestSupport.TokenValue, result.Reason);
        Assert.DoesNotContain("token=abc", result.Reason);
        Assert.DoesNotContain("abc", result.Reason);
    }

    [Fact]
    public async Task SendAsync_IOExceptionIsTransientNetwork()
    {
        UplinkHttpClient client = Client(UplinkTestHandler.Fail(new IOException("connessione reimpostata")));

        SendResult result = await SendAsync(client);

        Assert.Equal(SendOutcome.Transient, result.Outcome);
        Assert.Equal("network", result.Code);
    }

    [Fact]
    public async Task SendAsync_RequestHangs_TimeoutIsTransientTimeout()
    {
        UplinkHttpClient client = Client(UplinkTestHandler.Hang(), TimeSpan.FromMilliseconds(20));

        SendResult result = await SendAsync(client);

        Assert.Equal(SendOutcome.Transient, result.Outcome);
        Assert.Equal("timeout", result.Code);
        Assert.Null(result.StatusCode);
    }

    [Fact]
    public async Task SendAsync_CallerCancelled_ThrowsOperationCanceled()
    {
        using var callerCts = new CancellationTokenSource();
        var handler = new UplinkTestHandler(async (_, ct) =>
        {
            callerCts.Cancel();
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return UplinkTestHandler.Build(200, "", null);
        });
        UplinkHttpClient client = Client(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SendAsync(client, ct: callerCts.Token));
    }

    [Fact]
    public async Task SendAsync_InvalidServerUrl_IsPermanentWithoutSending()
    {
        var handler = UplinkTestHandler.Reply(200, "{\"stored\":true}");
        UplinkHttpClient client = Client(handler);
        PublishSettings settings = UplinkTestSupport.Settings() with { ServerUrl = "non valido" };

        SendResult result = await SendAsync(client, settings);

        Assert.Equal(SendOutcome.Permanent, result.Outcome);
        Assert.Equal("invalid_settings", result.Code);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task SendAsync_RequestTextAndReasonNeverShowToken()
    {
        var handler = UplinkTestHandler.Reply(401, "{}");
        UplinkHttpClient client = Client(handler);

        SendResult result = await SendAsync(client);

        Assert.DoesNotContain(UplinkTestSupport.TokenValue, handler.RequestText!);
        Assert.DoesNotContain(UplinkTestSupport.TokenValue, result.Reason);
        Assert.DoesNotContain(UplinkTestSupport.TokenValue, result.ToString()!);
    }

    [Fact]
    public async Task SendAsync_ExtraHeaderIsSentButNotInRequestText()
    {
        var handler = UplinkTestHandler.Reply(200, "{\"stored\":true}");
        UplinkHttpClient client = Client(handler);
        PublishSettings settings = UplinkTestSupport.Settings(extraHeaderName: "X-Uplink-Key");

        await SendAsync(client, settings);

        Assert.Equal(UplinkTestSupport.ExtraValue, handler.Header("X-Uplink-Key"));
        Assert.DoesNotContain(UplinkTestSupport.ExtraValue, handler.RequestText!);
        Assert.DoesNotContain(UplinkTestSupport.TokenValue, handler.RequestText!);
    }

    [Fact]
    public void Constructor_NonPositiveTimeout_Throws()
    {
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => Client(UplinkTestHandler.Reply(200), TimeSpan.Zero));
    }

    [Fact]
    public async Task TestConnection_200_IsOkWithAuthAndUrl()
    {
        var handler = UplinkTestHandler.Reply(200, "{}");
        UplinkHttpClient client = Client(handler);

        TestResult result = await client.TestConnectionAsync(UplinkTestSupport.Settings(), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("ok", result.Code);
        Assert.Equal("Connessione riuscita", result.Message);
        Assert.Equal(200, result.StatusCode!.Value);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.EndsWith("/api/uplink/health", handler.RequestUri!.ToString());
        Assert.Equal("Bearer " + UplinkTestSupport.TokenValue, handler.Header("Authorization"));
        Assert.DoesNotContain(UplinkTestSupport.TokenValue, result.Message);
    }

    [Fact]
    public async Task TestConnection_SendsExtraHeader()
    {
        var handler = UplinkTestHandler.Reply(200, "{}");
        UplinkHttpClient client = Client(handler);

        await client.TestConnectionAsync(UplinkTestSupport.Settings(extraHeaderName: "X-Uplink-Key"), CancellationToken.None);

        Assert.Equal(UplinkTestSupport.ExtraValue, handler.Header("X-Uplink-Key"));
    }

    [Theory]
    [InlineData(401, false, "auth", 401)]
    [InlineData(403, false, "auth", 403)]
    [InlineData(404, false, "no_health_endpoint", 404)]
    [InlineData(405, false, "no_health_endpoint", 405)]
    [InlineData(500, false, "server_error", 500)]
    public async Task TestConnection_StatusMapping(int status, bool ok, string code, int expectedStatus)
    {
        UplinkHttpClient client = Client(UplinkTestHandler.Reply(status));

        TestResult result = await client.TestConnectionAsync(UplinkTestSupport.Settings(), CancellationToken.None);

        Assert.Equal(ok, result.Ok);
        Assert.Equal(code, result.Code);
        Assert.Equal(expectedStatus, result.StatusCode!.Value);
    }

    [Fact]
    public async Task TestConnection_AuthMessage_IsTheExpectedText()
    {
        UplinkHttpClient client = Client(UplinkTestHandler.Reply(401));

        TestResult result = await client.TestConnectionAsync(UplinkTestSupport.Settings(), CancellationToken.None);

        Assert.Equal("Token o ID dispositivo rifiutati", result.Message);
    }

    [Fact]
    public async Task TestConnection_NetworkError_IsNetwork()
    {
        UplinkHttpClient client = Client(UplinkTestHandler.Fail(new HttpRequestException("rete giu' per " + UplinkTestSupport.TokenValue)));

        TestResult result = await client.TestConnectionAsync(UplinkTestSupport.Settings(), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("network", result.Code);
        Assert.Null(result.StatusCode);
        Assert.DoesNotContain(UplinkTestSupport.TokenValue, result.Message);
    }

    [Fact]
    public async Task TestConnection_Hang_IsTimeout()
    {
        UplinkHttpClient client = Client(UplinkTestHandler.Hang(), TimeSpan.FromMilliseconds(20));

        TestResult result = await client.TestConnectionAsync(UplinkTestSupport.Settings(), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("timeout", result.Code);
    }

    [Fact]
    public async Task TestConnection_CallerCancelled_ThrowsOperationCanceled()
    {
        using var callerCts = new CancellationTokenSource();
        callerCts.Cancel();
        UplinkHttpClient client = Client(UplinkTestHandler.Reply(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.TestConnectionAsync(UplinkTestSupport.Settings(), callerCts.Token));
    }
}
