using System.Net;
using AllSky.Satellites;

namespace AllSky.Satellites.Tests;

/// <summary>Costruzione di TLE di prova con checksum calcolato a parte (non riusa il codice di produzione).</summary>
internal static class TleFixtures
{
    /// <summary>Riga TLE completa: 68 colonne di corpo più il checksum calcolato.</summary>
    public static string Linea(string corpo)
    {
        if (corpo.Length > 68)
        {
            throw new InvalidOperationException("Corpo della riga TLE più lungo di 68 colonne.");
        }

        string base68 = corpo.PadRight(68);
        return base68 + ChecksumDiProva(base68);
    }

    /// <summary>Checksum scritto apposta per i test: cifre sommate, meno uguale 1, resto 0.</summary>
    public static int ChecksumDiProva(string base68)
    {
        int somma = 0;
        foreach (char c in base68)
        {
            if (c >= '0' && c <= '9')
            {
                somma += c - '0';
            }
            else if (c == '-')
            {
                somma += 1;
            }
        }

        return somma % 10;
    }

    public static string Linea1(int norad) => Linea($"1 {norad:D5}U 98067A   26294.50000000  .00016717  00000-0  10270-3 0  999");

    public static string Linea2(int norad) => Linea($"2 {norad:D5}  51.6400 200.0000 0006703 100.0000 260.0000 15.5000000000001");

    /// <summary>Risposta CelesTrak a tre righe (nome più TLE).</summary>
    public static string Tre(int norad, string nome = "ISS (ZARYA)") => $"{nome}\n{Linea1(norad)}\n{Linea2(norad)}\n";
}

/// <summary>Handler HTTP finto: registra ogni richiesta e risponde come configurato. Nessuna rete.</summary>
internal sealed class FakeCelesTrakHandler : HttpMessageHandler
{
    /// <summary>Tutti gli URL richiesti, nell'ordine.</summary>
    public List<Uri> Richieste { get; } = new();

    /// <summary>User-Agent ricevuti, uno per richiesta.</summary>
    public List<string> UserAgent { get; } = new();

    /// <summary>Risposta per ogni richiesta. Di default: 200 con un TLE valido per la ISS.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage> Risposta { get; set; } =
        _ => Ok(TleFixtures.Tre(25544));

    /// <summary>Se valorizzata, la richiesta fallisce con questa eccezione.</summary>
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

/// <summary>Orologio finto: il tempo avanza solo con <see cref="Avanza"/>.</summary>
internal sealed class FakeCelesTrakClock : ICelesTrakClock
{
    public DateTimeOffset UtcNow { get; private set; } = new(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);

    public void Avanza(TimeSpan durata)
    {
        UtcNow += durata;
    }
}
