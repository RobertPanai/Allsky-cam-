using System.Globalization;
using AllSky.Core.Tonight;

namespace AllSky.Core.Passes;

/// <summary>Punto cardinale a otto settori, per la direzione di un passaggio.</summary>
public enum CompassPoint
{
    /// <summary>Nord.</summary>
    N,

    /// <summary>Nord-est.</summary>
    NE,

    /// <summary>Est.</summary>
    E,

    /// <summary>Sud-est.</summary>
    SE,

    /// <summary>Sud.</summary>
    S,

    /// <summary>Sud-ovest.</summary>
    SW,

    /// <summary>Ovest.</summary>
    W,

    /// <summary>Nord-ovest.</summary>
    NW,
}

/// <summary>Una riga dell'elenco "Prossimi passaggi", già formattata.</summary>
/// <param name="Target">Nome del satellite.</param>
/// <param name="When">"tra 2 h 14 min" oppure "in corso".</param>
/// <param name="Times">Orari di inizio, culminazione e fine.</param>
/// <param name="Details">Altezza massima, direzione e condizioni di luce.</param>
/// <param name="InProgress">Vero se il passaggio è in corso.</param>
public sealed record PassRowData(string Target, string When, string Times, string Details, bool InProgress);

/// <summary>Contenuto della carta "Prossimi passaggi": un messaggio facoltativo e le righe.</summary>
/// <param name="Hint">Messaggio di stato (disattivata, in attesa, bloccata, errore, nessun passaggio), o null.</param>
/// <param name="Rows">Righe, al massimo quante richieste.</param>
public sealed record PassPanelData(string? Hint, IReadOnlyList<PassRowData> Rows);

/// <summary>
/// Trasforma la previsione in testi e righe per la UI. Logica pura: i testi vengono da <see cref="DesktopTexts"/>.
/// </summary>
public static class PassFormatter
{
    /// <summary>Numero massimo di righe nella carta della vista Stanotte.</summary>
    public const int DefaultMaxRows = 5;

    /// <summary>
    /// Punto cardinale dell'azimut (da nord verso est). Settori di 45 gradi centrati sui punti cardinali.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Se l'azimut non è finito.</exception>
    public static CompassPoint Compass(double azimuthDeg)
    {
        if (!double.IsFinite(azimuthDeg))
        {
            throw new ArgumentOutOfRangeException(nameof(azimuthDeg), azimuthDeg, "L'azimut deve essere un numero finito.");
        }

        double normalized = ((azimuthDeg % 360.0) + 360.0) % 360.0;
        int index = (int)Math.Floor((normalized + 22.5) / 45.0) % 8;
        return (CompassPoint)index;
    }

    /// <summary>Nome del punto cardinale nella lingua scelta.</summary>
    public static string CompassName(CompassPoint point, DesktopTexts texts) => point switch
    {
        CompassPoint.N => texts.PassCompassN,
        CompassPoint.NE => texts.PassCompassNE,
        CompassPoint.E => texts.PassCompassE,
        CompassPoint.SE => texts.PassCompassSE,
        CompassPoint.S => texts.PassCompassS,
        CompassPoint.SW => texts.PassCompassSW,
        CompassPoint.W => texts.PassCompassW,
        CompassPoint.NW => texts.PassCompassNW,
        _ => throw new ArgumentOutOfRangeException(nameof(point), point, "Punto cardinale sconosciuto."),
    };

    /// <summary>Direzione del passaggio: "da NO a SE".</summary>
    public static string Direction(PassForecastItem pass, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(pass);
        return string.Format(
            texts.Culture,
            texts.PassDirection,
            CompassName(Compass(pass.StartAzimuthDeg), texts),
            CompassName(Compass(pass.EndAzimuthDeg), texts));
    }

    /// <summary>Condizioni di luce: visibile a occhio nudo, illuminato con cielo non buio, oppure in ombra.</summary>
    public static string Light(PassForecastItem pass, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(pass);
        if (pass.VisibleToEye)
        {
            return texts.PassLightVisible;
        }

        return pass.IlluminatedFraction > 0.0 ? texts.PassLightLit : texts.PassLightShadow;
    }

    /// <summary>Orari di inizio, culminazione e fine nel fuso del sito: "inizio 21:03 · culmine 21:07 · fine 21:11".</summary>
    public static string Times(PassForecastItem pass, TimeZoneInfo tz, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(tz);
        return string.Format(
            texts.Culture,
            texts.PassTimes,
            TonightFormatter.FormatClock(pass.StartUtc, tz, texts.Culture),
            TonightFormatter.FormatClock(pass.CulminationUtc, tz, texts.Culture),
            TonightFormatter.FormatClock(pass.EndUtc, tz, texts.Culture));
    }

    /// <summary>Una riga dell'elenco per un passaggio, riferita all'istante <paramref name="now"/>.</summary>
    public static PassRowData Row(PassForecastItem pass, DateTimeOffset now, TimeZoneInfo tz, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(texts);

        bool inProgress = pass.StartUtc <= now && now < pass.EndUtc;
        string when = inProgress
            ? texts.PassInProgress
            : TonightFormatter.FormatCountdown(now, pass.StartUtc, texts);
        string maxElevation = string.Format(
            texts.Culture, texts.PassMaxElevation, pass.MaxElevationDeg.ToString("0", texts.Culture) + "°");
        string details = maxElevation + " · " + Direction(pass, texts) + " · " + Light(pass, texts);

        return new PassRowData(pass.Target, when, Times(pass, tz, texts), details, inProgress);
    }

    /// <summary>
    /// Contenuto della carta "Prossimi passaggi". Lo stato non attivo o non valido diventa un messaggio; le righe
    /// restano visibili anche con dati in cache.
    /// </summary>
    public static PassPanelData Panel(
        PassForecastSnapshot snapshot,
        DateTimeOffset now,
        TimeZoneInfo tz,
        DesktopTexts texts,
        int maxRows = DefaultMaxRows)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(tz);
        ArgumentNullException.ThrowIfNull(texts);
        if (maxRows < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRows), maxRows, "Il numero di righe non può essere negativo.");
        }

        string? hint = snapshot.Status switch
        {
            PassForecastStatus.Disabled => texts.PassStatusDisabled,
            PassForecastStatus.Waiting => snapshot.Message ?? texts.PassStatusWaiting,
            PassForecastStatus.Blocked => string.Format(texts.Culture, texts.PassStatusBlocked, snapshot.Message ?? string.Empty),
            PassForecastStatus.Failed => string.Format(texts.Culture, texts.PassStatusFailed, snapshot.Message ?? string.Empty),
            _ => null,
        };

        List<PassRowData> rows = snapshot.Passes
            .Take(maxRows)
            .Select(p => Row(p, now, tz, texts))
            .ToList();

        if (hint is null && rows.Count == 0)
        {
            hint = texts.PassNoneInWindow;
        }

        return new PassPanelData(hint, rows);
    }
}
