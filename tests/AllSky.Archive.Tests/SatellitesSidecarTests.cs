using System.Text.Json;
using AllSky.Archive;
using Xunit;

namespace AllSky.Archive.Tests;

/// <summary>Formato del campo <c>satellites</c> del sidecar: scrittura, lettura del formato nuovo e dei due formati precedenti.</summary>
public sealed class SatellitesSidecarTests
{
    private static FrameSidecar Read(string satellitesJson)
    {
        return JsonSerializer.Deserialize<FrameSidecar>("{\"satellites\":" + satellitesJson + "}", FrameSidecars.JsonOptions)!;
    }

    [Fact]
    public void RoundTrip_NewFormat_KeepsNamesNoradAndAngles()
    {
        var original = new FrameSidecar
        {
            Satellites =
            [
                new SatelliteInView("ISS (ZARYA)", 25544, 41.2, 98.7),
                new SatelliteInView("Bersaglio senza NORAD", null, 12.0, null),
            ],
        };

        string json = JsonSerializer.Serialize(original, FrameSidecars.JsonOptions);
        FrameSidecar back = JsonSerializer.Deserialize<FrameSidecar>(json, FrameSidecars.JsonOptions)!;

        Assert.Equal(original.Satellites, back.Satellites);
        Assert.Contains("\"noradId\": 25544", json);
        Assert.Contains("\"altDeg\": 41.2", json);
        Assert.Contains("\"azDeg\": null", json);
    }

    [Fact]
    public void NullField_IsWrittenAsNullAndReadBackAsNull()
    {
        var sidecar = new FrameSidecar { Satellites = null };

        string json = JsonSerializer.Serialize(sidecar, FrameSidecars.JsonOptions);
        FrameSidecar back = JsonSerializer.Deserialize<FrameSidecar>(json, FrameSidecars.JsonOptions)!;

        Assert.Contains("\"satellites\": null", json);
        Assert.Null(back.Satellites);
    }

    [Fact]
    public void EmptyList_IsDistinctFromNull()
    {
        // Lista vuota: previsione attiva e nessun satellite sopra l'orizzonte. Null: previsione spenta.
        FrameSidecar vuoto = Read("[]");
        FrameSidecar assente = Read("null");

        Assert.NotNull(vuoto.Satellites);
        Assert.Empty(vuoto.Satellites!);
        Assert.Null(assente.Satellites);
    }

    [Fact]
    public void LegacyArrayOfStrings_IsReadAsNamesOnly()
    {
        FrameSidecar back = Read("[\"ISS (ZARYA)\", \"NOAA 19\"]");

        Assert.Equal(
            [new SatelliteInView("ISS (ZARYA)", null, null, null), new SatelliteInView("NOAA 19", null, null, null)],
            back.Satellites);
    }

    [Fact]
    public void LegacyObjectsWithNomeOrName_AreRead()
    {
        FrameSidecar back = Read("[{\"nome\": \"ISS\", \"altDeg\": 41}, {\"name\": \"NOAA 19\", \"noradId\": 33591}]");

        Assert.Equal(new SatelliteInView("ISS", null, 41, null), back.Satellites![0]);
        Assert.Equal(new SatelliteInView("NOAA 19", 33591, null, null), back.Satellites[1]);
    }

    [Fact]
    public void UnexpectedShape_IsIgnoredWithoutFailingTheSidecar()
    {
        // Una stringa al posto dell'array, o valori di altro tipo dentro l'array: nessun satellite, sidecar comunque letto.
        FrameSidecar scalare = Read("\"ISS\"");
        FrameSidecar misto = Read("[42, true, \"NOAA 19\", {\"noradId\": \"non numero\"}]");

        Assert.Null(scalare.Satellites);
        Assert.Equal(
            [new SatelliteInView("NOAA 19", null, null, null), new SatelliteInView(string.Empty, null, null, null)],
            misto.Satellites);
    }
}
