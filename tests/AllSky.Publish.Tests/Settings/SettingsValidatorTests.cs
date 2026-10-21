using Xunit;

namespace AllSky.Publish.Tests.Settings;

public sealed class SettingsValidatorTests
{
    private static PublishSettings WithUrl(string url) => SampleSettings.Valid() with { ServerUrl = url };

    private static bool HasErrorOn(IReadOnlyList<SettingsError> errors, string path)
        => errors.Any(e => e.Path == path);

    [Fact]
    public void ValidSettings_HaveNoErrors()
    {
        var errors = SettingsValidator.Validate(SampleSettings.Valid());
        Assert.Empty(errors);
    }

    [Fact]
    public void DefaultDisabledSettings_HaveNoErrors()
    {
        Assert.Empty(SettingsValidator.Validate(new PublishSettings()));
    }

    [Theory]
    [InlineData("https://server.example.org")]
    [InlineData("https://server.example.org/base")]
    [InlineData("https://server.example.org:8443/a/b")]
    [InlineData("http://localhost")]
    [InlineData("http://localhost:8080")]
    [InlineData("http://127.0.0.1:5000")]
    [InlineData("http://[::1]:8080")]
    public void ServerUrl_AcceptedForms(string url)
    {
        var errors = SettingsValidator.Validate(WithUrl(url));
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("http://server.example.org")]
    [InlineData("http://localhost.example.org")]
    [InlineData("https://server.example.org/?token=abc")]
    [InlineData("https://server.example.org/?")]
    [InlineData("https://server.example.org/#frammento")]
    [InlineData("https://user:pass@server.example.org")]
    [InlineData("https://utente@server.example.org/base")]
    [InlineData("ftp://server.example.org")]
    [InlineData("file:///etc/hosts")]
    [InlineData("server.example.org")]
    [InlineData("https://server.example.org/ spazio")]
    public void ServerUrl_RejectedForms(string url)
    {
        var errors = SettingsValidator.Validate(WithUrl(url));
        Assert.True(HasErrorOn(errors, "serverUrl"), "Atteso errore su serverUrl per: " + url);
    }

    [Fact]
    public void ServerUrl_TooLong_IsRejected()
    {
        string url = "https://" + new string('a', 300) + ".example.org";
        var errors = SettingsValidator.Validate(WithUrl(url));
        Assert.True(HasErrorOn(errors, "serverUrl"));
    }

    [Fact]
    public void ServerUrl_QueryMessage_TellsWhatToDo()
    {
        var errors = SettingsValidator.Validate(WithUrl("https://server.example.org/?token=abc"));
        var error = errors.Single(e => e.Path == "serverUrl");
        Assert.Contains("parametri", error.Message);
        Assert.DoesNotContain("abc", error.Message);
    }

    [Fact]
    public void ServerUrl_RequiredWhenEnabled_NotWhenDisabled()
    {
        var enabled = SampleSettings.Valid() with { ServerUrl = "" };
        Assert.True(HasErrorOn(SettingsValidator.Validate(enabled), "serverUrl"));

        var disabled = new PublishSettings { Enabled = false, ServerUrl = "" };
        Assert.False(HasErrorOn(SettingsValidator.Validate(disabled), "serverUrl"));
    }

    [Theory]
    [InlineData("cam-01")]
    [InlineData("AB-01._x")]
    [InlineData("a")]
    public void DeviceId_Accepted(string id)
    {
        Assert.Empty(SettingsValidator.Validate(SampleSettings.Valid() with { DeviceId = id }));
    }

    [Theory]
    [InlineData("a b")]
    [InlineData("a/b")]
    [InlineData("caffeè")]
    [InlineData("riga\n")]
    public void DeviceId_Rejected(string id)
    {
        Assert.True(HasErrorOn(SettingsValidator.Validate(SampleSettings.Valid() with { DeviceId = id }), "deviceId"));
    }

    [Fact]
    public void DeviceId_Length64_OkAnd65_Rejected()
    {
        Assert.Empty(SettingsValidator.Validate(SampleSettings.Valid() with { DeviceId = new string('d', 64) }));
        Assert.True(HasErrorOn(SettingsValidator.Validate(SampleSettings.Valid() with { DeviceId = new string('d', 65) }), "deviceId"));
    }

    [Fact]
    public void DeviceId_RequiredWhenEnabled()
    {
        Assert.True(HasErrorOn(SettingsValidator.Validate(SampleSettings.Valid() with { DeviceId = "" }), "deviceId"));
        Assert.False(HasErrorOn(SettingsValidator.Validate(new PublishSettings { DeviceId = "" }), "deviceId"));
    }

    [Fact]
    public void Token_RequiredWhenEnabled()
    {
        var enabled = SampleSettings.Valid() with { Token = new Secret("") };
        Assert.True(HasErrorOn(SettingsValidator.Validate(enabled), "token"));

        var disabled = new PublishSettings { Enabled = false };
        Assert.False(HasErrorOn(SettingsValidator.Validate(disabled), "token"));
    }

    [Theory]
    [InlineData((string?)null)]
    [InlineData("")]
    [InlineData("Cielo")]
    public void ProfileName_AbsentOrShort_Accepted(string? name)
    {
        Assert.Empty(SettingsValidator.Validate(SampleSettings.Valid() with { ProfileName = name }));
    }

    [Fact]
    public void ProfileName_Length80_OkAnd81_Rejected()
    {
        Assert.Empty(SettingsValidator.Validate(SampleSettings.Valid() with { ProfileName = new string('p', 80) }));
        Assert.True(HasErrorOn(SettingsValidator.Validate(SampleSettings.Valid() with { ProfileName = new string('p', 81) }), "profileName"));
    }

    [Fact]
    public void ProfileName_ControlChars_Rejected()
    {
        Assert.True(HasErrorOn(SettingsValidator.Validate(SampleSettings.Valid() with { ProfileName = "uno\r\ndue" }), "profileName"));
    }

    [Theory]
    [InlineData(14, false)]
    [InlineData(15, true)]
    [InlineData(86400, true)]
    [InlineData(86401, false)]
    public void IntervalSeconds_Bounds(int value, bool ok)
    {
        var errors = SettingsValidator.Validate(SampleSettings.Valid() with { IntervalSeconds = value });
        Assert.Equal(ok, !HasErrorOn(errors, "intervalSeconds"));
    }

    [Theory]
    [InlineData(199, false)]
    [InlineData(200, true)]
    [InlineData(2000, true)]
    [InlineData(2001, false)]
    public void LongEdgePx_Bounds(int value, bool ok)
    {
        var errors = SettingsValidator.Validate(SampleSettings.Valid() with { LongEdgePx = value });
        Assert.Equal(ok, !HasErrorOn(errors, "longEdgePx"));
    }

    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(95, true)]
    [InlineData(96, false)]
    public void JpegQuality_Bounds(int value, bool ok)
    {
        var errors = SettingsValidator.Validate(SampleSettings.Valid() with { JpegQuality = value });
        Assert.Equal(ok, !HasErrorOn(errors, "jpegQuality"));
    }

    [Theory]
    [InlineData("X-Api-Key", "abc", true)]
    [InlineData("X-Api-Key", "", false)]
    [InlineData("", "abc", false)]
    [InlineData("Authorization", "abc", false)]
    [InlineData("authorization", "abc", false)]
    [InlineData("Host", "abc", false)]
    [InlineData("CONTENT-TYPE", "abc", false)]
    [InlineData("content-length", "abc", false)]
    [InlineData("X_Bad", "abc", false)]
    [InlineData("X Bad", "abc", false)]
    public void ExtraHeader_Pairs(string name, string value, bool ok)
    {
        var settings = SampleSettings.Valid() with
        {
            ExtraHeaderName = name.Length == 0 ? null : name,
            ExtraHeaderValue = new Secret(value),
        };
        var errors = SettingsValidator.Validate(settings);
        Assert.Equal(ok, errors.Count == 0);
    }

    [Fact]
    public void ExtraHeader_NoNameNoValue_IsFine()
    {
        var settings = SampleSettings.Valid() with { ExtraHeaderName = null, ExtraHeaderValue = default };
        Assert.Empty(SettingsValidator.Validate(settings));
    }

    [Fact]
    public void ErrorMessages_NeverContainSecrets()
    {
        var settings = new PublishSettings
        {
            Enabled = true,
            ServerUrl = "https://user:pass@server.example.org/?token=PARAMETRO-SEGRETO",
            DeviceId = "",
            Token = new Secret("TOKEN-SEGRETO-XYZ"),
            ExtraHeaderName = "Authorization",
            ExtraHeaderValue = new Secret("HEADER-SEGRETO-XYZ"),
        };

        var errors = SettingsValidator.Validate(settings);

        Assert.True(errors.Count > 0);
        foreach (var e in errors)
        {
            Assert.DoesNotContain("SEGRETO", e.Message);
            Assert.DoesNotContain("TOKEN-", e.Message);
            Assert.DoesNotContain("server.example.org", e.Message);
        }
    }

    [Fact]
    public void ErrorPaths_AreCamelCaseFieldNames()
    {
        var settings = new PublishSettings
        {
            Enabled = true,
            ServerUrl = "http://server.example.org",
            DeviceId = "bad id",
            Token = new Secret(""),
            IntervalSeconds = 1,
            LongEdgePx = 1,
            JpegQuality = 1,
            ProfileName = new string('p', 81),
        };

        var paths = SettingsValidator.Validate(settings).Select(e => e.Path).ToList();

        Assert.Contains("serverUrl", paths);
        Assert.Contains("deviceId", paths);
        Assert.Contains("token", paths);
        Assert.Contains("profileName", paths);
        Assert.Contains("intervalSeconds", paths);
        Assert.Contains("longEdgePx", paths);
        Assert.Contains("jpegQuality", paths);
    }
}
