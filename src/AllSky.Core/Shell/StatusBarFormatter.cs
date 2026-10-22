using System.Globalization;
using AllSky.Core.Tonight;

namespace AllSky.Core.Shell;

/// <summary>
/// Trasforma i dati grezzi della barra di stato in voci già formattate nella lingua scelta.
/// Un dato mancante diventa "n/d" (o "n/a" in inglese) con una spiegazione nel tooltip.
/// </summary>
/// <remarks>
/// Logica pura: nessuna dipendenza da WPF né dallo stato del servizio. Il numero di gradi usa un solo decimale e il
/// separatore della lingua (la virgola in italiano), con il segno esplicito.
/// </remarks>
public static class StatusBarFormatter
{
    /// <summary>Formatta la barra di stato.</summary>
    /// <param name="snapshot">Dati grezzi dal servizio.</param>
    /// <param name="texts">Testi e cultura della lingua scelta.</param>
    /// <param name="now">Istante di riferimento per il conto alla rovescia del transito; null usa l'orologio di sistema.</param>
    public static StatusBarText Format(StatusBarSnapshot snapshot, DesktopTexts texts, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(texts);

        CultureInfo culture = texts.Culture;
        StatusPhase phase = ParsePhase(snapshot.Phase);
        DateTimeOffset reference = now ?? DateTimeOffset.UtcNow;

        return new StatusBarText(
            Camera: FormatCamera(snapshot, texts),
            Phase: phase == StatusPhase.Unknown
                ? Missing(texts.PhaseLabel, texts)
                : Known(texts.PhaseLabel, PhaseName(phase, texts)),
            Sun: FormatAltitude(texts.SunLabel, snapshot.SunAltitudeDeg, texts),
            Moon: FormatAltitude(texts.MoonLabel, snapshot.MoonAltitudeDeg, texts),
            LastFrame: FormatLastFrame(snapshot.LastFrameTimestamp, texts),
            Disk: FormatDisk(snapshot.DiskFreeGb, texts),
            Publications: FormatPublications(snapshot.Publication, texts),
            PhaseKind: phase,
            NextPass: FormatNextPass(snapshot, texts, reference));
    }

    /// <summary>
    /// Transito: "ISS · tra 2 h 14 min", oppure "ISS in corso". Senza dati, "n/d".
    /// </summary>
    private static StatusBarItem FormatNextPass(StatusBarSnapshot snapshot, DesktopTexts texts, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(snapshot.PassTarget) || snapshot.PassStartUtc is not { } start)
        {
            return Missing(texts.PassStatusLabel, texts);
        }

        if (snapshot.PassInProgress || start <= now)
        {
            return Known(texts.PassStatusLabel, string.Format(texts.Culture, texts.PassInProgressValue, snapshot.PassTarget));
        }

        string countdown = TonightFormatter.FormatCountdown(now, start, texts);
        return Known(texts.PassStatusLabel, string.Format(texts.Culture, texts.PassNextValue, snapshot.PassTarget, countdown));
    }

    /// <summary>Traduce la fase del servizio ("day", "twilight", "night") in <see cref="StatusPhase"/>.</summary>
    /// <param name="phase">Valore di <c>StatusDto.Phase</c>.</param>
    public static StatusPhase ParsePhase(string? phase) => phase switch
    {
        "day" => StatusPhase.Day,
        "twilight" => StatusPhase.Twilight,
        "night" => StatusPhase.Night,
        _ => StatusPhase.Unknown,
    };

    /// <summary>Nome della fase nella lingua scelta.</summary>
    /// <param name="phase">Fase da nominare.</param>
    /// <param name="texts">Testi della lingua scelta.</param>
    public static string PhaseName(StatusPhase phase, DesktopTexts texts) => phase switch
    {
        StatusPhase.Day => texts.PhaseDay,
        StatusPhase.Twilight => texts.PhaseTwilight,
        StatusPhase.Night => texts.PhaseNight,
        _ => texts.NotAvailable,
    };

    /// <summary>Altezza in gradi con un decimale e segno, per esempio "-12,3°". Null se il valore non è finito.</summary>
    /// <param name="degrees">Altezza in gradi.</param>
    /// <param name="culture">Cultura per il separatore decimale.</param>
    public static string? FormatDegrees(double? degrees, CultureInfo culture)
    {
        if (degrees is not { } value || !double.IsFinite(value))
        {
            return null;
        }

        return value.ToString("+0.0;-0.0;0.0", culture) + "°";
    }

    private static StatusBarItem FormatCamera(StatusBarSnapshot snapshot, DesktopTexts texts)
    {
        if (snapshot.CameraConnected is not { } connected)
        {
            return Missing(texts.CameraLabel, texts);
        }

        if (!connected)
        {
            return Known(texts.CameraLabel, texts.CameraNotConnected);
        }

        string value = string.IsNullOrWhiteSpace(snapshot.CameraName) ? texts.CameraConnected : snapshot.CameraName;
        return Known(texts.CameraLabel, value);
    }

    private static StatusBarItem FormatAltitude(string label, double? degrees, DesktopTexts texts)
    {
        string? text = FormatDegrees(degrees, texts.Culture);
        return text is null ? Missing(label, texts) : Known(label, text);
    }

    private static StatusBarItem FormatLastFrame(DateTimeOffset? timestamp, DesktopTexts texts)
    {
        if (timestamp is not { } value)
        {
            return Missing(texts.LastFrameLabel, texts);
        }

        // Nella barra basta l'ora locale; l'identificativo completo resta nel dettaglio della vista Live.
        return Known(texts.LastFrameLabel, value.ToLocalTime().ToString("HH:mm:ss", texts.Culture));
    }

    private static StatusBarItem FormatDisk(double? freeGb, DesktopTexts texts)
    {
        if (freeGb is not { } value || !double.IsFinite(value) || value < 0)
        {
            return Missing(texts.DiskLabel, texts);
        }

        string amount = value.ToString("0.0", texts.Culture);
        return Known(texts.DiskLabel, string.Format(texts.Culture, texts.DiskFree, amount));
    }

    private static StatusBarItem FormatPublications(PublicationStatus status, DesktopTexts texts)
    {
        string? text = status switch
        {
            PublicationStatus.Ok => texts.PublicationOk,
            PublicationStatus.Pending => texts.PublicationPending,
            PublicationStatus.Error => texts.PublicationError,
            _ => null,
        };

        return text is null ? Missing(texts.PublicationsLabel, texts) : Known(texts.PublicationsLabel, text);
    }

    private static StatusBarItem Known(string label, string value) => new(label, value, true, null);

    private static StatusBarItem Missing(string label, DesktopTexts texts) =>
        new(label, texts.NotAvailable, false, texts.NotAvailableTooltip);
}
