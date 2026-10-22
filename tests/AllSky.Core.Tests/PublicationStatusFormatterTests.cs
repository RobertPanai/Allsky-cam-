using System.Globalization;
using AllSky.Core.Publication;
using Xunit;

namespace AllSky.Core.Tests;

public sealed class PublicationStatusFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static PublicationStatusSnapshot Status(
        PublicationState state = PublicationState.Idle,
        DateTimeOffset? lastSuccess = null,
        string? errorCode = null,
        string? rawError = null,
        DateTimeOffset? errorAt = null,
        DateTimeOffset? nextAttempt = null,
        int queue = 0,
        int sent = 0,
        int discarded = 0) => new(
            state, lastSuccess, errorCode, rawError, errorAt, queue, sent, discarded, nextAttempt);

    [Fact]
    public void StateText_EveryState_HasItsOwnText()
    {
        DesktopTexts t = DesktopTexts.Italian;

        Assert.Equal(t.PublicationStateDisabled, PublicationStatusFormatter.StateText(PublicationState.Disabled, t));
        Assert.Equal(t.PublicationStateIdle, PublicationStatusFormatter.StateText(PublicationState.Idle, t));
        Assert.Equal(t.PublicationStateSending, PublicationStatusFormatter.StateText(PublicationState.Sending, t));
        Assert.Equal(t.PublicationStateBackoff, PublicationStatusFormatter.StateText(PublicationState.Backoff, t));
        Assert.Equal(t.PublicationStateSuspended, PublicationStatusFormatter.StateText(PublicationState.Suspended, t));
        Assert.Equal(t.PublicationStateUnknown, PublicationStatusFormatter.StateText(PublicationState.Unknown, t));
    }

    [Fact]
    public void StateText_English_UsesEnglishTexts()
    {
        Assert.Equal(DesktopTexts.English.PublicationStateSuspended,
            PublicationStatusFormatter.StateText(PublicationState.Suspended, DesktopTexts.English));
    }

    [Fact]
    public void Counters_ShowQueueSentAndDiscarded()
    {
        string text = PublicationStatusFormatter.Counters(Status(queue: 2, sent: 10, discarded: 1), DesktopTexts.Italian);

        Assert.Equal("In coda: 2 · Inviate: 10 · Scartate: 1", text);
    }

    [Fact]
    public void LastSuccess_Never_SaysNoSuccessYet()
    {
        Assert.Equal(DesktopTexts.Italian.PublicationNoSuccessYet,
            PublicationStatusFormatter.LastSuccess(null, Now, DesktopTexts.Italian));
    }

    [Fact]
    public void LastSuccess_Today_ShowsOnlyTheTime()
    {
        DateTimeOffset when = Now.AddMinutes(-5);
        string expectedTime = when.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

        string text = PublicationStatusFormatter.LastSuccess(when, Now, DesktopTexts.English);

        Assert.Equal(string.Format(CultureInfo.InvariantCulture, DesktopTexts.English.PublicationLastSuccess, expectedTime), text);
    }

    [Fact]
    public void TimeText_OtherDay_ShowsDayAndMonth()
    {
        DateTimeOffset when = Now.AddDays(-2);

        string text = PublicationStatusFormatter.TimeText(when, Now);

        Assert.Equal(when.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture), text);
    }

    [Fact]
    public void NextAttempt_NotScheduled_IsNull()
    {
        Assert.Null(PublicationStatusFormatter.NextAttempt(null, Now, DesktopTexts.Italian));
    }

    [Fact]
    public void NextAttempt_Scheduled_ShowsTheTime()
    {
        string? text = PublicationStatusFormatter.NextAttempt(Now.AddMinutes(1), Now, DesktopTexts.Italian);

        Assert.NotNull(text);
        Assert.StartsWith("Prossimo tentativo: ", text);
    }

    [Fact]
    public void LastError_WithoutCode_IsNull()
    {
        Assert.Null(PublicationStatusFormatter.LastError(Status(), Now, DesktopTexts.Italian));
    }

    [Fact]
    public void LastError_ShowsTheReasonFromTheCode_NeverTheRawText()
    {
        const string raw = "Errore con Bearer abc123-segreto e https://server.example/?token=abc123";
        PublicationStatusSnapshot status = Status(errorCode: "auth", rawError: raw, errorAt: Now);

        string? text = PublicationStatusFormatter.LastError(status, Now, DesktopTexts.English);

        Assert.NotNull(text);
        Assert.Contains(DesktopTexts.English.PublicationReasonAuth, text);
        Assert.DoesNotContain("abc123", text);
        Assert.DoesNotContain("Bearer", text);
    }

    [Theory]
    [InlineData("auth", nameof(DesktopTexts.PublicationReasonAuth))]
    [InlineData("rejected", nameof(DesktopTexts.PublicationReasonRejected))]
    [InlineData("too_large", nameof(DesktopTexts.PublicationReasonTooLarge))]
    [InlineData("bad_response", nameof(DesktopTexts.PublicationReasonBadResponse))]
    [InlineData("rate_limited", nameof(DesktopTexts.PublicationReasonRateLimited))]
    [InlineData("network", nameof(DesktopTexts.PublicationReasonNetwork))]
    [InlineData("timeout", nameof(DesktopTexts.PublicationReasonTimeout))]
    [InlineData("invalid_settings", nameof(DesktopTexts.PublicationReasonInvalidSettings))]
    [InlineData("no_health_endpoint", nameof(DesktopTexts.PublicationReasonNoHealth))]
    [InlineData("codice-nuovo", nameof(DesktopTexts.PublicationReasonOther))]
    [InlineData(null, nameof(DesktopTexts.PublicationReasonOther))]
    public void Reason_EveryKnownCode_HasItsOwnText(string? code, string propertyName)
    {
        var property = typeof(DesktopTexts).GetProperty(propertyName)!;
        string expected = (string)property.GetValue(DesktopTexts.Italian)!;

        Assert.Equal(expected, PublicationStatusFormatter.Reason(code, null, DesktopTexts.Italian));
    }

    [Fact]
    public void Reason_ServerErrorWithStatusCode_ShowsTheCode()
    {
        string text = PublicationStatusFormatter.Reason("server_error", 503, DesktopTexts.English);

        Assert.Equal(string.Format(CultureInfo.InvariantCulture, DesktopTexts.English.PublicationReasonServerCode, 503), text);
    }

    [Fact]
    public void Reason_ServerErrorWithoutStatusCode_UsesTheGenericText()
    {
        Assert.Equal(DesktopTexts.English.PublicationReasonServer, PublicationStatusFormatter.Reason("server_error", null, DesktopTexts.English));
    }

    [Fact]
    public void TestText_Ok_ShowsSuccess()
    {
        var result = new PublicationTestResult(true, "ok", "Connessione riuscita", 200);

        Assert.Equal(DesktopTexts.Italian.PublicationTestOk, PublicationStatusFormatter.TestText(result, DesktopTexts.Italian));
    }

    [Fact]
    public void TestText_Failure_ShowsTheReasonAndNeverTheServerMessage()
    {
        var result = new PublicationTestResult(false, "auth", "Token o ID dispositivo rifiutati", 401);

        string text = PublicationStatusFormatter.TestText(result, DesktopTexts.Italian);

        Assert.Equal(string.Format(CultureInfo.InvariantCulture, DesktopTexts.Italian.PublicationTestFailed, DesktopTexts.Italian.PublicationReasonAuth), text);
    }

    [Fact]
    public void SecretStatus_SaysOnlySetOrNotSet()
    {
        Assert.Equal("Stato: impostato", PublicationStatusFormatter.SecretStatus(true, DesktopTexts.Italian));
        Assert.Equal("Stato: non impostato", PublicationStatusFormatter.SecretStatus(false, DesktopTexts.Italian));
        Assert.Equal("Status: set", PublicationStatusFormatter.SecretStatus(true, DesktopTexts.English));
    }
}
