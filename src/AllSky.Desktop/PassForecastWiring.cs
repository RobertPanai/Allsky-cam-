using System;
using System.IO;
using System.Net.Http;
using AllSky.Satellites;
using AllSky.Satellites.Sgp4;

namespace AllSky.Desktop;

/// <summary>
/// Dipendenze reali della previsione dei passaggi (P4.3): sorgente CelesTrak e propagatore SGP.NET.
/// Stanno nel Desktop perché SGP.NET è un pacchetto NuGet e il Desktop è l'unico composition root Windows.
/// </summary>
internal static class PassForecastWiring
{
    /// <summary>Crea la sorgente TLE con cache nella cartella dati. Nessun reindirizzamento seguito in silenzio (usage policy CelesTrak).</summary>
    public static ITleFetcher CreateTleFetcher(string dataFolder)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        return new CelesTrakTleSource(
            http,
            new SystemCelesTrakClock(),
            Path.Combine(dataFolder, "satellites"));
    }
}
