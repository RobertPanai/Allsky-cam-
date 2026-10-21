using System.Globalization;
using Xunit;

namespace AllSky.Publish.Tests;

/// <summary>Test della classificazione degli esiti HTTP e del parsing di Retry-After.</summary>
public class ErrorClassifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 21, 30, 0, TimeSpan.Zero);

    private const string StoredBody = "{\"stored\":true,\"duplicate\":false,\"visible\":true}";

    private static SendResult Classify(int status, string? body = null, string? retryAfter = null)
        => ErrorClassifier.Classify(status, body, retryAfter, new PublishFakeClock(Now), UplinkTestSupport.Redactor());

    [Theory]
    [InlineData(400, "Permanent", "rejected")]
    [InlineData(401, "AuthError", "auth")]
    [InlineData(403, "AuthError", "auth")]
    [InlineData(404, "Permanent", "rejected")]
    [InlineData(408, "Transient", "server_error")]
    [InlineData(413, "Permanent", "too_large")]
    [InlineData(415, "Permanent", "rejected")]
    [InlineData(422, "Permanent", "rejected")]
    [InlineData(429, "RateLimited", "rate_limited")]
    [InlineData(500, "Transient", "server_error")]
    [InlineData(502, "Transient", "server_error")]
    [InlineData(503, "Transient", "server_error")]
    [InlineData(504, "Transient", "server_error")]
    public void Classify_StatusTable(int status, string outcome, string code)
    {
        SendResult result = Classify(status);

        Assert.Equal(Enum.Parse<SendOutcome>(outcome), result.Outcome);
        Assert.Equal(code, result.Code);
        Assert.Equal(status, result.StatusCode!.Value);
    }

    [Fact]
    public void Classify_2xxWithStoredBody_IsAcknowledged()
    {
        SendResult result = Classify(200, StoredBody);

        Assert.Equal(SendOutcome.Acknowledged, result.Outcome);
        Assert.Equal("stored", result.Code);
        Assert.Null(result.RetryAfter);
    }

    [Fact]
    public void Classify_2xxWithDuplicateBody_IsAcknowledged()
    {
        SendResult result = Classify(200, "{\"stored\":false,\"duplicate\":true,\"visible\":true}");

        Assert.Equal(SendOutcome.Acknowledged, result.Outcome);
        Assert.Equal("duplicate", result.Code);
    }

    [Fact]
    public void Classify_2xxWithoutConfirmation_IsTransientBadResponse()
    {
        SendResult result = Classify(200, "{\"stored\":false,\"duplicate\":false,\"visible\":false}");

        Assert.Equal(SendOutcome.Transient, result.Outcome);
        Assert.Equal("bad_response", result.Code);
    }

    [Fact]
    public void Classify_2xxUnreadableBody_IsTransientBadResponse()
    {
        SendResult result = Classify(200, "<html>proxy</html>");

        Assert.Equal(SendOutcome.Transient, result.Outcome);
        Assert.Equal("bad_response", result.Code);
    }

    [Fact]
    public void Classify_2xxWithoutBody_IsTransientBadResponse()
    {
        SendResult result = Classify(204, null);

        Assert.Equal(SendOutcome.Transient, result.Outcome);
        Assert.Equal("bad_response", result.Code);
    }

    [Fact]
    public void Classify_429_RetryAfterSeconds()
    {
        SendResult result = Classify(429, null, "30");

        Assert.Equal(SendOutcome.RateLimited, result.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(30), result.RetryAfter!.Value);
    }

    [Fact]
    public void Classify_429_RetryAfterHttpDate_IsRelativeToClock()
    {
        string date = Now.AddMinutes(2).UtcDateTime.ToString("R", CultureInfo.InvariantCulture);

        SendResult result = Classify(429, null, date);

        Assert.Equal(TimeSpan.FromMinutes(2), result.RetryAfter!.Value);
    }

    [Fact]
    public void Classify_429_RetryAfterDateInThePast_IsZero()
    {
        string date = Now.AddMinutes(-5).UtcDateTime.ToString("R", CultureInfo.InvariantCulture);

        SendResult result = Classify(429, null, date);

        Assert.Equal(TimeSpan.Zero, result.RetryAfter!.Value);
    }

    [Fact]
    public void Classify_429_RetryAfterDateBeyondLimit_IsCappedAt24Hours()
    {
        string date = Now.AddDays(3).UtcDateTime.ToString("R", CultureInfo.InvariantCulture);

        SendResult result = Classify(429, null, date);

        Assert.Equal(ErrorClassifier.MaxRetryAfter, result.RetryAfter!.Value);
    }

    [Fact]
    public void Classify_429_RetryAfterSecondsBeyondLimit_IsCappedAt24Hours()
    {
        SendResult result = Classify(429, null, "999999999");

        Assert.Equal(TimeSpan.FromHours(24), result.RetryAfter!.Value);
    }

    [Fact]
    public void Classify_429_NegativeRetryAfter_IsNull()
    {
        Assert.Null(Classify(429, null, "-5").RetryAfter);
    }

    [Fact]
    public void Classify_429_InvalidRetryAfter_IsNull()
    {
        Assert.Null(Classify(429, null, "presto").RetryAfter);
    }

    [Fact]
    public void Classify_429_MissingRetryAfter_IsNull()
    {
        Assert.Null(Classify(429).RetryAfter);
    }

    [Fact]
    public void Classify_401_ReasonTellsUserWhatToDo()
    {
        SendResult result = Classify(401);

        Assert.Equal(
            "Il server ha rifiutato il token: controlla token e ID dispositivo nelle impostazioni.",
            result.Reason);
    }

    [Fact]
    public void Classify_413_ReasonSaysFrameIsDropped()
    {
        Assert.Contains("viene scartato", Classify(413).Reason);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(413)]
    [InlineData(429)]
    [InlineData(500)]
    public void Classify_ReasonNeverContainsSecret(int status)
    {
        SendResult result = Classify(status, "contiene " + UplinkTestSupport.TokenValue, "10");

        Assert.DoesNotContain(UplinkTestSupport.TokenValue, result.Reason);
    }

    [Fact]
    public void ClassifyTimeout_IsTransientTimeout()
    {
        SendResult result = ErrorClassifier.ClassifyTimeout(UplinkTestSupport.Redactor());

        Assert.Equal(SendOutcome.Transient, result.Outcome);
        Assert.Equal("timeout", result.Code);
        Assert.Null(result.StatusCode);
    }

    [Fact]
    public void ClassifyNetworkError_IsTransientNetwork()
    {
        SendResult result = ErrorClassifier.ClassifyNetworkError(UplinkTestSupport.Redactor());

        Assert.Equal(SendOutcome.Transient, result.Outcome);
        Assert.Equal("network", result.Code);
        Assert.Null(result.StatusCode);
    }

    [Fact]
    public void ClassifyInvalidRequest_IsPermanent()
    {
        SendResult result = ErrorClassifier.ClassifyInvalidRequest(UplinkTestSupport.Redactor());

        Assert.Equal(SendOutcome.Permanent, result.Outcome);
        Assert.Equal("invalid_settings", result.Code);
    }
}
