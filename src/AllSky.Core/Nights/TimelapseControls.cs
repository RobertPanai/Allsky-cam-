using AllSky.Core;

namespace AllSky.Core.Nights;

/// <summary>Stato dei comandi del timelapse di una notte.</summary>
/// <param name="CanPlay">Vero se il file è pronto e si può riprodurre.</param>
/// <param name="CanRegenerate">Vero se si può accodare una nuova generazione.</param>
/// <param name="IsBusy">Vero mentre il timelapse è in coda o in lavorazione.</param>
/// <param name="StatusText">Testo dello stato, già nella lingua scelta.</param>
public sealed record TimelapseControlState(bool CanPlay, bool CanRegenerate, bool IsBusy, string StatusText);

/// <summary>Decide quali comandi del timelapse sono attivi e che testo mostrare. Logica pura.</summary>
public static class TimelapseControls
{
    /// <summary>Stato dei comandi per una notte.</summary>
    /// <param name="state">Stato del timelapse.</param>
    /// <param name="frames">Frame della notte; senza frame non si può rigenerare.</param>
    /// <param name="jobProgress">Avanzamento del lavoro da 0 a 1 se è in coda o in corso, altrimenti null.</param>
    /// <param name="texts">Testi della lingua scelta.</param>
    public static TimelapseControlState Resolve(NightTimelapseState state, int frames, double? jobProgress, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        bool busy = state == NightTimelapseState.Generating;
        string status = busy
            ? FormatProgress(jobProgress, texts)
            : NightRowFormatter.TimelapseLabel(state, texts);

        return new TimelapseControlState(
            CanPlay: state == NightTimelapseState.Ready,
            CanRegenerate: !busy && frames > 0,
            IsBusy: busy,
            StatusText: status);
    }

    /// <summary>
    /// Avanzamento in testo. Zero, valori non finiti o assenza di dato diventano "in coda o in avvio": il motore non
    /// distingue un lavoro in coda da uno appena partito. Il valore è limitato a 0–100%.
    /// </summary>
    /// <param name="progress">Avanzamento da 0 a 1, o null.</param>
    /// <param name="texts">Testi della lingua scelta.</param>
    public static string FormatProgress(double? progress, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (progress is not double value || !double.IsFinite(value) || value <= 0)
        {
            return texts.NightsTimelapseQueued;
        }

        int percent = (int)Math.Round(Math.Min(value, 1) * 100, MidpointRounding.AwayFromZero);
        return string.Format(texts.Culture, texts.NightsTimelapseProgress, percent);
    }
}
