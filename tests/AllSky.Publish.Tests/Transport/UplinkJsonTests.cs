using System.Globalization;
using System.Text.Json.Nodes;
using Xunit;

namespace AllSky.Publish.Tests;

/// <summary>Test del formato dei metadati (schema v1) e della lettura della risposta.</summary>
public class UplinkJsonTests
{
    /// <summary>Sentinella: al posto del profilo usa quello della fixture (null significa "profilo assente").</summary>
    private const string UseFixtureProfile = "\u0001fixture";

    private static UplinkMeta FixtureMeta(
        string? profileName = UseFixtureProfile,
        double? exposureMs = 175,
        double? gain = 0,
        PublishMode mode = PublishMode.Night,
        double? temperatureC = null)
        => new(
            1,
            UplinkTestSupport.FixtureString("deviceId"),
            profileName == UseFixtureProfile ? UplinkTestSupport.FixtureString("profileName") : profileName,
            UplinkTestSupport.FixtureString("captureId"),
            UplinkTestSupport.FixtureCapturedAt(),
            exposureMs,
            gain,
            mode,
            temperatureC);

    [Fact]
    public void SerializeMeta_MatchesFixtureStructurally()
    {
        string json = UplinkJson.SerializeMeta(FixtureMeta());

        JsonNode? actual = JsonNode.Parse(json);
        JsonNode? expected = UplinkTestSupport.LoadFixtureMeta();

        Assert.True(JsonNode.DeepEquals(expected, actual), "Meta diversa dalla fixture: " + json);
    }

    [Fact]
    public void SerializeMeta_FieldOrderIsFixed()
    {
        string json = UplinkJson.SerializeMeta(FixtureMeta());

        string[] names =
        {
            "schemaVersion", "deviceId", "profileName", "captureId", "capturedAt",
            "exposureMs", "gain", "mode", "temperatureC",
        };
        int previous = -1;
        foreach (string name in names)
        {
            int index = json.IndexOf("\"" + name + "\":", StringComparison.Ordinal);
            Assert.True(index > previous, "Campo fuori ordine o assente: " + name + " in " + json);
            previous = index;
        }
    }

    [Fact]
    public void SerializeMeta_CapturedAtIsUtcWithMilliseconds_FromOffsetTimestamp()
    {
        // 01:30:00.123 con offset +02:00 equivale alle 23:30:00.123 UTC del giorno prima.
        var offset = new DateTimeOffset(2026, 10, 22, 1, 30, 0, TimeSpan.FromHours(2)).AddMilliseconds(123);
        UplinkMeta meta = FixtureMeta() with { CapturedAt = offset };

        JsonNode? node = JsonNode.Parse(UplinkJson.SerializeMeta(meta));

        Assert.Equal("2026-10-21T23:30:00.123Z", node!["capturedAt"]!.GetValue<string>());
    }

    [Fact]
    public void SerializeMeta_CapturedAt_UsesInvariantDigitsAndMillisecondsZeroPadded()
    {
        var instant = new DateTimeOffset(2026, 1, 2, 3, 4, 5, 7, TimeSpan.Zero);

        Assert.Equal("2026-01-02T03:04:05.007Z", UplinkJson.FormatCapturedAt(instant));
    }

    [Fact]
    public void SerializeMeta_OmitsNullOptionalFields_KeepsTemperatureAsNull()
    {
        UplinkMeta meta = FixtureMeta(profileName: null, exposureMs: null, gain: null, temperatureC: null);

        JsonObject node = (JsonObject)JsonNode.Parse(UplinkJson.SerializeMeta(meta))!;

        Assert.False(node.ContainsKey("profileName"));
        Assert.False(node.ContainsKey("exposureMs"));
        Assert.False(node.ContainsKey("gain"));
        Assert.True(node.ContainsKey("temperatureC"));
        Assert.Null(node["temperatureC"]);
    }

    [Fact]
    public void SerializeMeta_WritesTemperatureWhenPresent()
    {
        JsonObject node = (JsonObject)JsonNode.Parse(UplinkJson.SerializeMeta(FixtureMeta(temperatureC: 12.5)))!;

        Assert.Equal(12.5, node["temperatureC"]!.GetValue<double>(), 6);
    }

    [Fact]
    public void SerializeMeta_NonFiniteNumbers_AreNeverWrittenAsNumbers()
    {
        UplinkMeta meta = FixtureMeta(exposureMs: double.NaN, gain: double.PositiveInfinity, temperatureC: double.NegativeInfinity);

        string json = UplinkJson.SerializeMeta(meta);
        JsonObject node = (JsonObject)JsonNode.Parse(json)!;

        Assert.False(node.ContainsKey("exposureMs"));
        Assert.False(node.ContainsKey("gain"));
        Assert.Null(node["temperatureC"]);
        Assert.DoesNotContain("NaN", json);
        Assert.DoesNotContain("Infinity", json);
    }

    [Theory]
    [InlineData("Day", "day")]
    [InlineData("Twilight", "twilight")]
    [InlineData("Night", "night")]
    public void SerializeMeta_ModeIsLowercase(string mode, string expected)
    {
        var parsed = Enum.Parse<PublishMode>(mode);
        JsonObject node = (JsonObject)JsonNode.Parse(UplinkJson.SerializeMeta(FixtureMeta(mode: parsed)))!;

        Assert.Equal(expected, node["mode"]!.GetValue<string>());
    }

    [Fact]
    public void SerializeMeta_NumbersIgnoreCurrentCulture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            // Cultura con virgola decimale: il JSON deve restare con il punto.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("it-IT");

            string json = UplinkJson.SerializeMeta(FixtureMeta(exposureMs: 175.5, temperatureC: -3.25));

            Assert.Contains("\"exposureMs\":175.5", json);
            Assert.Contains("\"temperatureC\":-3.25", json);
            Assert.DoesNotContain("175,5", json);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void SerializeMeta_ProfileNameWithAccentsStaysReadable()
    {
        string json = UplinkJson.SerializeMeta(FixtureMeta(profileName: "Cielo è sereno"));

        JsonObject node = (JsonObject)JsonNode.Parse(json)!;
        Assert.Equal("Cielo è sereno", node["profileName"]!.GetValue<string>());
    }

    [Fact]
    public void TryParseResponse_ValidBody_ReadsFlags()
    {
        bool ok = UplinkJson.TryParseResponse("{\"stored\":true,\"duplicate\":false,\"visible\":true}",
            out bool stored, out bool duplicate, out bool visible);

        Assert.True(ok);
        Assert.True(stored);
        Assert.False(duplicate);
        Assert.True(visible);
    }

    [Fact]
    public void TryParseResponse_MissingFields_AreFalseButBodyIsValid()
    {
        bool ok = UplinkJson.TryParseResponse("{}", out bool stored, out bool duplicate, out bool visible);

        Assert.True(ok);
        Assert.False(stored);
        Assert.False(duplicate);
        Assert.False(visible);
    }

    [Fact]
    public void TryParseResponse_NonBooleanField_IsFalse()
    {
        bool ok = UplinkJson.TryParseResponse("{\"stored\":\"yes\",\"duplicate\":1}", out bool stored, out bool duplicate, out _);

        Assert.True(ok);
        Assert.False(stored);
        Assert.False(duplicate);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"stored\":true")]
    [InlineData("   ")]
    public void TryParseResponse_InvalidBody_ReturnsFalseAndFalseFlags(string body)
    {
        bool ok = UplinkJson.TryParseResponse(body, out bool stored, out bool duplicate, out bool visible);

        Assert.False(ok);
        Assert.False(stored);
        Assert.False(duplicate);
        Assert.False(visible);
    }

    [Fact]
    public void TryParseResponse_NullBody_ReturnsFalse()
    {
        bool ok = UplinkJson.TryParseResponse(null, out bool stored, out bool duplicate, out bool visible);

        Assert.False(ok);
        Assert.False(stored);
        Assert.False(duplicate);
        Assert.False(visible);
    }
}
