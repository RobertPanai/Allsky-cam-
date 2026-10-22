using System.Globalization;
using AllSky.Astro;

namespace AllSky.Core.Tonight;

/// <summary>Una riga della carta "Stato della cattura", già formattata nella lingua scelta.</summary>
/// <param name="Label">Etichetta.</param>
/// <param name="Value">Valore.</param>
/// <param name="Problem">Vero se la riga segnala un errore, così la vista può aggiungere un'icona oltre al colore.</param>
public sealed record TonightStatusLine(string Label, string Value, bool Problem);

/// <summary>
/// Formattazione dei dati della vista "Stanotte". Logica pura: i testi vengono da <see cref="DesktopTexts"/>,
/// la cultura da <see cref="DesktopTexts.Culture"/>.
/// </summary>
public static class TonightFormatter
{
    /// <summary>Formato dell'orario locale: ore e minuti, senza secondi.</summary>
    private const string ClockFormat = "HH:mm";

    /// <summary>Formato dell'altezza: segno esplicito e un decimale.</summary>
    private const string AltitudeFormat = "+0.0;-0.0;0.0";

    /// <summary>Orario locale HH:mm del fuso indicato.</summary>
    public static string FormatClock(DateTimeOffset utc, TimeZoneInfo tz, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(tz);
        ArgumentNullException.ThrowIfNull(culture);

        return TimeZoneInfo.ConvertTime(utc, tz).ToString(ClockFormat, culture);
    }

    /// <summary>Altezza in gradi con segno e un decimale, per esempio "+12.3°" o "-0.8°".</summary>
    public static string FormatAltitude(double degrees, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return degrees.ToString(AltitudeFormat, culture) + "°";
    }

    /// <summary>
    /// Durata leggibile: "2 h 14 min", "14 min" oppure "meno di 1 min". Una durata non positiva è "adesso".
    /// </summary>
    public static string FormatDuration(TimeSpan span, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (span <= TimeSpan.Zero)
        {
            return texts.TonightNow;
        }

        int totalMinutes = (int)Math.Floor(span.TotalMinutes);
        if (totalMinutes < 1)
        {
            return texts.TonightCountdownLessThanMinute;
        }

        if (totalMinutes < 60)
        {
            return string.Format(texts.Culture, texts.TonightCountdownMinutes, totalMinutes);
        }

        return string.Format(texts.Culture, texts.TonightCountdownHoursMinutes, totalMinutes / 60, totalMinutes % 60);
    }

    /// <summary>Conto alla rovescia verso <paramref name="at"/>, con la preposizione: "tra 2 h 14 min".</summary>
    public static string FormatCountdown(DateTimeOffset now, DateTimeOffset at, DesktopTexts texts)
    {
        TimeSpan remaining = at - now;
        if (remaining <= TimeSpan.Zero)
        {
            return texts.TonightNow;
        }
        return string.Format(texts.Culture, texts.TonightCountdownIn, FormatDuration(remaining, texts));
    }

    /// <summary>Nome della fase del cielo nella lingua scelta.</summary>
    public static string PhaseName(SkyPhase phase, DesktopTexts texts) => phase switch
    {
        SkyPhase.Day => texts.TonightPhaseDay,
        SkyPhase.CivilTwilight => texts.TonightPhaseCivil,
        SkyPhase.NauticalTwilight => texts.TonightPhaseNautical,
        SkyPhase.AstronomicalTwilight => texts.TonightPhaseAstro,
        SkyPhase.Night => texts.TonightPhaseNight,
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Fase sconosciuta."),
    };

    /// <summary>Nome della fase lunare nella lingua scelta.</summary>
    public static string MoonName(MoonPhaseName name, DesktopTexts texts) => name switch
    {
        MoonPhaseName.New => texts.TonightMoonNew,
        MoonPhaseName.WaxingCrescent => texts.TonightMoonWaxingCrescent,
        MoonPhaseName.FirstQuarter => texts.TonightMoonFirstQuarter,
        MoonPhaseName.WaxingGibbous => texts.TonightMoonWaxingGibbous,
        MoonPhaseName.Full => texts.TonightMoonFull,
        MoonPhaseName.WaningGibbous => texts.TonightMoonWaningGibbous,
        MoonPhaseName.LastQuarter => texts.TonightMoonLastQuarter,
        MoonPhaseName.WaningCrescent => texts.TonightMoonWaningCrescent,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Fase lunare sconosciuta."),
    };

    /// <summary>Percentuale di disco illuminato, da 0 a 100, senza decimali: "47%".</summary>
    public static string FormatIllumination(double fraction, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        double percent = Math.Clamp(Math.Round(fraction * 100.0), 0.0, 100.0);
        return percent.ToString("0", culture) + "%";
    }

    /// <summary>
    /// Righe della carta "Stato della cattura" a partire dallo stato del servizio. Un frame mancante viene
    /// indicato con <see cref="DesktopTexts.NoFrameYet"/>, mai con uno zero.
    /// </summary>
    public static IReadOnlyList<TonightStatusLine> CaptureLines(
        TonightCaptureInput input, DateTimeOffset now, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(texts);

        var lines = new List<TonightStatusLine>
        {
            new(texts.StateLabel, texts.StateName(input.State), input.State == "error"),
            new(
                texts.CameraLabel,
                input.CameraConnected ? input.CameraName : texts.CameraNotConnected,
                !input.CameraConnected),
        };

        string lastFrame = texts.FormatLastFrame(input.LastFrameId, input.LastFrameUtc);
        if (input.LastFrameUtc is { } frameUtc)
        {
            lastFrame = string.Format(texts.Culture, texts.TonightAgo, FormatDuration(now - frameUtc, texts)) + " · " + lastFrame;
        }
        lines.Add(new TonightStatusLine(texts.LastFrameLabel, lastFrame, false));

        if (!string.IsNullOrWhiteSpace(input.Error))
        {
            lines.Add(new TonightStatusLine(texts.ErrorLabel, input.Error!, true));
        }

        return lines;
    }
}

/// <summary>Dati dello stato di cattura necessari alla carta. La vista li copia dallo <c>StatusDto</c>.</summary>
/// <param name="State">"idle", "capturing" oppure "error".</param>
/// <param name="CameraConnected">Vero se la camera è connessa.</param>
/// <param name="CameraName">Nome della camera; vuoto se non connessa.</param>
/// <param name="LastFrameId">Identificativo dell'ultimo frame, o null.</param>
/// <param name="LastFrameUtc">Istante dell'ultimo frame in UTC, o null.</param>
/// <param name="Error">Messaggio di errore del servizio, o null.</param>
public sealed record TonightCaptureInput(
    string State,
    bool CameraConnected,
    string CameraName,
    string? LastFrameId,
    DateTimeOffset? LastFrameUtc,
    string? Error);
