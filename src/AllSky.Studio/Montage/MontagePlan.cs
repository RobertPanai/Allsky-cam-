using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AllSky.Studio;

/// <summary>Statistiche di un frame. Arrivano da una funzione iniettata: il piano non legge i file.</summary>
/// <param name="MeanLuma">Luminanza media, scala 0..255.</param>
/// <param name="StdDevLuma">Deviazione standard della luminanza, scala 0..255.</param>
public sealed record FrameStats(double MeanLuma, double StdDevLuma);

/// <summary>Motivo per cui un frame non entra nel montaggio.</summary>
public enum RemovalReason
{
    /// <summary>Fuori dall'intervallo di tempo.</summary>
    Interval,

    /// <summary>Rimosso a mano per nome file.</summary>
    ManualRemoval,

    /// <summary>Frame quasi uniforme.</summary>
    BlankFrame,

    /// <summary>Esposizione anomala (mediana ± k·MAD).</summary>
    ExposureOutlier,

    /// <summary>Saltato dalla velocità (fattore maggiore di uno).</summary>
    SpeedSkipped,
}

/// <summary>Tipo di segmento del piano, oltre ai frame.</summary>
public enum MontageSegmentKind
{
    /// <summary>Scheda titolo prima dei frame.</summary>
    TitleCardAtStart,

    /// <summary>Scheda titolo dopo i frame.</summary>
    TitleCardAtEnd,

    /// <summary>Dissolvenza in apertura.</summary>
    FadeIn,

    /// <summary>Dissolvenza in chiusura.</summary>
    FadeOut,

    /// <summary>Dissolvenza incrociata sul bordo di una scheda titolo.</summary>
    Crossfade,
}

/// <summary>
/// Segmento del piano. Le schede titolo aggiungono tempo; le dissolvenze e le incrociate
/// si sovrappongono al montaggio e non lo allungano.
/// </summary>
/// <param name="Kind">Tipo e posizione.</param>
/// <param name="DurationFrames">Durata in frame, round(secondi · fps), almeno 1.</param>
/// <param name="Text">Testo della scheda titolo; null per le dissolvenze.</param>
public sealed record MontageSegment(MontageSegmentKind Kind, int DurationFrames, string? Text);

/// <summary>Riepilogo numerico del piano.</summary>
/// <param name="InputFrames">Frame in ingresso.</param>
/// <param name="RemovedByReason">Frame rimossi per motivo (tutti i motivi, anche a zero).</param>
/// <param name="RepeatedFrames">Frame in più per le velocità inferiori a uno.</param>
/// <param name="OutputFrames">Frame del montaggio in uscita.</param>
/// <param name="TitleCardFrames">Frame delle schede titolo.</param>
/// <param name="DurationSeconds">Durata finale: (frame del montaggio + schede titolo) / fps.</param>
public sealed record MontageSummary(
    int InputFrames,
    IReadOnlyDictionary<RemovalReason, int> RemovedByReason,
    int RepeatedFrames,
    int OutputFrames,
    int TitleCardFrames,
    double DurationSeconds);

/// <summary>
/// Piano di montaggio deterministico: stessi frame, stesse operazioni e stessa funzione di statistiche
/// producono lo stesso piano e lo stesso <see cref="Fingerprint"/>.
/// </summary>
/// <param name="Frames">Frame in uscita, in ordine. Con fattori inferiori a uno un frame può comparire più volte.</param>
/// <param name="Fps">Frame rate del montaggio.</param>
/// <param name="Segments">Schede titolo e dissolvenze.</param>
/// <param name="PixelOps">Operazioni sui pixel da applicare a ogni frame, nell'ordine.</param>
/// <param name="Warnings">Avvisi in italiano, senza percorsi.</param>
/// <param name="Summary">Riepilogo.</param>
public sealed record MontagePlan(
    IReadOnlyList<StudioFrame> Frames,
    int Fps,
    IReadOnlyList<MontageSegment> Segments,
    IReadOnlyList<PixelOp> PixelOps,
    IReadOnlyList<string> Warnings,
    MontageSummary Summary)
{
    /// <summary>
    /// Impronta SHA-256 (esadecimale) del contenuto del piano. Usa nome file e istante dei frame,
    /// non il percorso della cartella: lo stesso progetto su un'altra cartella dà la stessa impronta.
    /// </summary>
    public string Fingerprint()
    {
        var text = new StringBuilder();
        text.Append(FormattableString.Invariant($"fps={Fps}\n"));
        text.Append(FormattableString.Invariant($"frames={Frames.Count}\n"));
        foreach (StudioFrame frame in Frames)
        {
            text.Append(FormattableString.Invariant($"{frame.FileName}|{frame.TimestampUtc.UtcTicks}\n"));
        }

        foreach (MontageSegment segment in Segments)
        {
            text.Append(FormattableString.Invariant($"segment={segment.Kind}|{segment.DurationFrames}|{segment.Text ?? string.Empty}\n"));
        }

        foreach (PixelOp op in PixelOps)
        {
            text.Append(FormattableString.Invariant($"pixel={op.Canonical}\n"));
        }

        foreach (string warning in Warnings)
        {
            text.Append(FormattableString.Invariant($"warning={warning}\n"));
        }

        text.Append(FormattableString.Invariant($"input={Summary.InputFrames}\n"));
        foreach (RemovalReason reason in Enum.GetValues<RemovalReason>())
        {
            int count = Summary.RemovedByReason.TryGetValue(reason, out int value) ? value : 0;
            text.Append(FormattableString.Invariant($"removed={reason}|{count}\n"));
        }

        text.Append(FormattableString.Invariant($"repeated={Summary.RepeatedFrames}\n"));
        text.Append(FormattableString.Invariant($"output={Summary.OutputFrames}\n"));
        text.Append(FormattableString.Invariant($"titleFrames={Summary.TitleCardFrames}\n"));
        text.Append(FormattableString.Invariant($"duration={Summary.DurationSeconds.ToString("R", CultureInfo.InvariantCulture)}\n"));

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
        return Convert.ToHexString(hash);
    }
}

/// <summary>Esito della pianificazione: un piano oppure gli errori delle operazioni.</summary>
/// <param name="Plan">Piano. Null se almeno un'operazione non è valida.</param>
/// <param name="Errors">Errori in italiano, uno per operazione non valida, con il numero dell'operazione.</param>
public sealed record MontagePlanOutcome(MontagePlan? Plan, IReadOnlyList<string> Errors)
{
    /// <summary>Vero se il piano è stato prodotto senza errori.</summary>
    public bool IsValid => Plan is not null && Errors.Count == 0;
}
