using System.Net;
using AllSky.Weather;

namespace AllSky.Weather.Tests;

/// <summary>Orologio finto: il tempo avanza solo con <see cref="Avanza"/>.</summary>
internal sealed class FakeWeatherClock : IWeatherClock
{
    public DateTimeOffset UtcNow { get; private set; } = new(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);

    public void Avanza(TimeSpan durata)
    {
        UtcNow += durata;
    }
}

/// <summary>
/// Lettore finto di ObservingConditions. Le proprietà in <see cref="NonImplementate"/> lanciano
/// <see cref="NotImplementedException"/>; quelle senza valore lanciano <see cref="InvalidOperationException"/>.
/// </summary>
internal sealed class FakeObservingConditionsReader : IObservingConditionsReader
{
    public Dictionary<string, double> Valori { get; } = new();

    public HashSet<string> NonImplementate { get; } = new();

    private double Leggi(string nome)
    {
        if (NonImplementate.Contains(nome))
        {
            throw new NotImplementedException($"{nome}: proprietà non implementata dal driver finto");
        }

        if (!Valori.TryGetValue(nome, out double valore))
        {
            throw new InvalidOperationException($"{nome}: valore assente nel finto");
        }

        return valore;
    }

    public double CloudCover() => Leggi(nameof(CloudCover));
    public double DewPoint() => Leggi(nameof(DewPoint));
    public double Humidity() => Leggi(nameof(Humidity));
    public double Pressure() => Leggi(nameof(Pressure));
    public double RainRate() => Leggi(nameof(RainRate));
    public double SkyQuality() => Leggi(nameof(SkyQuality));
    public double SkyTemperature() => Leggi(nameof(SkyTemperature));
    public double Temperature() => Leggi(nameof(Temperature));
    public double WindDirection() => Leggi(nameof(WindDirection));
    public double WindGust() => Leggi(nameof(WindGust));
    public double WindSpeed() => Leggi(nameof(WindSpeed));
}

/// <summary>Fabbrica finta: restituisce il lettore indicato, oppure lancia <see cref="Errore"/>. Conta le chiamate.</summary>
internal sealed class FakeObservingConditionsFactory : IObservingConditionsFactory
{
    public int Chiamate { get; private set; }

    public IObservingConditionsReader? Lettore { get; set; }

    public Exception? Errore { get; set; }

    public IObservingConditionsReader Create(string progId)
    {
        Chiamate++;
        if (Errore is not null)
        {
            throw Errore;
        }

        return Lettore ?? throw new InvalidOperationException("nessun lettore configurato");
    }
}

/// <summary>Handler HTTP finto: registra le richieste e risponde come configurato. Nessuna rete.</summary>
internal sealed class FakeOpenMeteoHandler : HttpMessageHandler
{
    public List<Uri> Richieste { get; } = new();

    public List<string> UserAgent { get; } = new();

    /// <summary>Risposta per ogni richiesta. Di default: 200 con la risposta di esempio.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage> Risposta { get; set; } =
        _ => Ok(OpenMeteoJson.Esempio());

    /// <summary>Se valorizzata, ogni richiesta fallisce con questa eccezione.</summary>
    public Exception? Errore { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Richieste.Add(request.RequestUri!);
        UserAgent.Add(request.Headers.UserAgent.ToString());
        if (Errore is not null)
        {
            return Task.FromException<HttpResponseMessage>(Errore);
        }

        return Task.FromResult(Risposta(request));
    }

    public static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    public static HttpResponseMessage Stato(HttpStatusCode codice) => new(codice) { Content = new StringContent("errore") };
}

/// <summary>
/// Risposte JSON di esempio, costruite dalla struttura e dagli identificativi della documentazione Open-Meteo
/// (https://open-meteo.com/en/docs, letta il 21/10/2026). I valori sono di prova, non dati reali.
/// </summary>
internal static class OpenMeteoJson
{
    /// <summary>Risposta completa con tutte le variabili attese e le unità dichiarate.</summary>
    public static string Esempio(
        string unitaRaffica = "m/s",
        string unitaVento = "m/s") =>
        """
        {"latitude":52.52,"longitude":13.41,"generationtime_ms":0.05,"utc_offset_seconds":0,
         "timezone":"GMT","timezone_abbreviation":"GMT","elevation":38.0,
         "current_units":{"time":"iso8601","interval":"seconds","temperature_2m":"°C",
           "relative_humidity_2m":"%","cloud_cover":"%","wind_speed_10m":"UNITA_VENTO",
           "wind_gusts_10m":"UNITA_RAFFICA","wind_direction_10m":"°","surface_pressure":"hPa"},
         "current":{"time":"2026-10-21T08:00","interval":900,"temperature_2m":12.3,
           "relative_humidity_2m":81,"cloud_cover":45,"wind_speed_10m":3.2,"wind_gusts_10m":6.1,
           "wind_direction_10m":240,"surface_pressure":1012.4}}
        """
        .Replace("UNITA_VENTO", unitaVento, StringComparison.Ordinal)
        .Replace("UNITA_RAFFICA", unitaRaffica, StringComparison.Ordinal);
}
