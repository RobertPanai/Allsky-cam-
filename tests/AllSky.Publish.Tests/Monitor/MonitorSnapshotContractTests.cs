using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AllSky.Core;
using AllSky.Publish.Monitor;
using Xunit;

namespace AllSky.Publish.Tests.Monitor;

/// <summary>Contratto del payload del monitoraggio: campi ammessi fissi e nessun dato sensibile nel JSON.</summary>
public sealed class MonitorSnapshotContractTests
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    /// <summary>Elenco CHIUSO. Aggiungere un campo al payload richiede di aggiornare questo test di proposito.</summary>
    private static readonly string[] AllowedSnapshotProperties =
    [
        "SchemaVersion", "UpdatedUtc", "NightPhase", "SunAltitudeDeg", "MoonAltitudeDeg",
        "TargetName", "ParkState", "MoonSeparationDeg", "TargetAltitudeChart", "Assets",
    ];

    private static readonly string[] AllowedAltitudePointProperties = ["TimeUtc", "AltitudeDeg"];

    private static readonly string[] AllowedAssetProperties = ["Kind", "FileName"];

    /// <summary>Parole che non devono comparire in nessuna chiave JSON (posizione, percorsi, segreti, log).</summary>
    private static readonly string[] ForbiddenKeyFragments =
    [
        "latitude", "longitude", "lat", "lon", "path", "folder", "ip", "host", "token", "secret", "password", "log",
    ];

    private static string[] PropertyNames(Type type) =>
        type.GetProperties(PublicInstance).Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    [Fact]
    public void MonitorSnapshot_PublicPropertiesAreExactlyTheAllowedList()
    {
        Assert.Equal(
            AllowedSnapshotProperties.OrderBy(n => n, StringComparer.Ordinal),
            PropertyNames(typeof(MonitorSnapshot)));
    }

    [Fact]
    public void MonitorAltitudePoint_PublicPropertiesAreExactlyTheAllowedList()
    {
        Assert.Equal(
            AllowedAltitudePointProperties.OrderBy(n => n, StringComparer.Ordinal),
            PropertyNames(typeof(MonitorAltitudePoint)));
    }

    [Fact]
    public void MonitorAsset_PublicPropertiesAreExactlyTheAllowedList()
    {
        Assert.Equal(
            AllowedAssetProperties.OrderBy(n => n, StringComparer.Ordinal),
            PropertyNames(typeof(MonitorAsset)));
    }

    [Fact]
    public void MonitorAssetKind_HasOnlyTheAllowedValues()
    {
        Assert.Equal(
            new[] { "AllSkyImage", "LatestTimelapse", "Keogram", "StartTrails" },
            Enum.GetNames<MonitorAssetKind>());
    }

    [Fact]
    public void FullSnapshot_JsonHasNoForbiddenKey()
    {
        MonitorSnapshot snapshot = FullSnapshot();

        string json = JsonSerializer.Serialize(snapshot, JsonOptions());
        JsonNode? root = JsonNode.Parse(json);

        var keys = new List<string>();
        CollectKeys(root, keys);

        Assert.NotEmpty(keys);
        foreach (string key in keys)
        {
            string lower = key.ToLowerInvariant();
            foreach (string fragment in ForbiddenKeyFragments)
            {
                Assert.False(lower.Contains(fragment, StringComparison.Ordinal), $"Chiave vietata \"{key}\" (contiene \"{fragment}\").");
            }
        }
    }

    [Fact]
    public void FullSnapshot_JsonValuesHoldNoDirectoryOrCoordinates()
    {
        string json = JsonSerializer.Serialize(FullSnapshot(), JsonOptions());

        Assert.DoesNotContain(@"D:\", json);
        Assert.DoesNotContain("/home/", json);
        Assert.DoesNotContain("45.07", json);
        Assert.DoesNotContain("7.69", json);
    }

    [Fact]
    public void SerializedSnapshot_UsesCamelCaseNamesAndStringEnum()
    {
        string json = JsonSerializer.Serialize(FullSnapshot(), JsonOptions());

        Assert.Contains("\"schemaVersion\":1", json);
        Assert.Contains("\"kind\":\"allSkyImage\"", json);
        Assert.Contains("\"fileName\":\"latest.jpg\"", json);
    }

    private static MonitorSnapshot FullSnapshot()
    {
        var now = new DateTimeOffset(2026, 10, 23, 22, 0, 0, TimeSpan.Zero);
        var config = new AppConfig.MonitorConfig
        {
            Enabled = true,
            Voices = new AppConfig.MonitorConfig.MonitorVoices
            {
                AllSkyImage = true,
                NightPhase = true,
                SunMoonAltitude = true,
                NinaTarget = true,
                NinaParkState = true,
                TargetAltitudeChart = true,
                MoonDistance = true,
                LatestTimelapse = true,
                Keogram = true,
                StartTrails = true,
            },
        };
        var inputs = new MonitorInputs
        {
            NightPhase = "night",
            SunAltitudeDeg = -40,
            MoonAltitudeDeg = 10,
            TargetName = "M 31",
            ParkState = "tracking",
            MoonSeparationDeg = 70,
            TargetAltitudeChart = [new MonitorAltitudePoint(now, 30)],
            AllSkyImagePath = @"C:\data\latest.jpg",
            LatestTimelapsePath = "/data/t.mp4",
            KeogramPath = "/data/k.png",
            StartTrailsPath = "/data/s.jpg",
        };

        return MonitorSnapshotBuilder.Build(config, inputs, now)!;
    }

    private static JsonSerializerOptions JsonOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    private static void CollectKeys(JsonNode? node, List<string> keys)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (KeyValuePair<string, JsonNode?> pair in obj)
                {
                    keys.Add(pair.Key);
                    CollectKeys(pair.Value, keys);
                }

                break;
            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    CollectKeys(item, keys);
                }

                break;
        }
    }
}
