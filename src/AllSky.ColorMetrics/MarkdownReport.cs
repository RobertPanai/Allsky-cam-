using System.Globalization;

namespace AllSky.ColorMetrics;

/// <summary>Report in markdown: parametri, tabella dei numeri per notte, difetti, limiti e metadati dell'encoder.</summary>
public static class MarkdownReport
{
    /// <summary>Parametri delle misure, da stampare in testa al report.</summary>
    public const string ParametersText =
        "Finestra della mediana mobile: 15 frame (centrata, bordi simmetrici). "
        + "Zona valida: cerchio inscritto nel lato minore, ridotto al 90% del raggio (cerchio fisheye noto non disponibile: la config della notte non lo contiene). "
        + "Fondo cielo: mediana dei pixel della zona con luminanza <= 70° percentile. "
        + "Luminanza: Rec.709 su valori 0..1. "
        + "Stelle: massimi locali 3x3 con luminanza >= fondo + 5 sigma (sigma = rumore robusto dalle differenze orizzontali, 1.4826 MAD / sqrt 2, minimo 1 ADU), saturi esclusi, eccesso medio di verde >= 8 ADU. "
        + "Frame: JPEG dell'archivio (senza overlay), decodificati con ffmpeg in rgb24 (matrice di conversione non verificata [NV]); sidecar JSON per fase, esposizione, guadagno, Sole.";

    /// <summary>Limiti fissi della misura, sempre riportati.</summary>
    public static readonly string[] StandardLimits =
    [
        "Le misure sono sui JPEG dell'archivio, non sulla decodifica del timelapse.mp4: il video può cambiare i colori durante la codifica (vedi metadati).",
        "Le soglie di «Difetto» sono provvisorie e non calibrate su frame reali: servono a indicare dove guardare.",
        "La mediana mobile non misura bene un flicker periodico a 2 frame (il residuo vale circa l'escursione picco-picco, non l'ampiezza).",
        "I rapporti salto/tipico di una rampa lineare liscia valgono circa 5 (le due mediane di 5 frame distano 5 frame).",
        "Non c'è il guadagno effettivo del bilanciamento del bianco e dello stretch nei sidecar: le deriva misurata è quella del risultato, non dei guadagni.",
        "La luminanza è media sulla zona valida, incluse stelle e nuvole: non è un fondo cielo, il fondo è nelle colonne R/G e B/G.",
        "Il fondo delle stelle è quello globale della zona: un gradiente di colore del cielo sposta il rapporto R/G e B/G delle stelle.",
    ];

    /// <summary>Scrive il report. <paramref name="notes"/> sono le note aggiunte dal chiamante (per esempio «notte simulata»).</summary>
    public static string Render(IReadOnlyList<NightMetricsResult> nights, IReadOnlyList<string> notes, DateTimeOffset generatedUtc)
    {
        ArgumentNullException.ThrowIfNull(nights);
        ArgumentNullException.ThrowIfNull(notes);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Diagnosi misurata del colore del timelapse (P14.1)");
        sb.AppendLine();
        sb.AppendLine($"Generato il {generatedUtc:yyyy-MM-dd HH:mm} UTC da `AllSky.ColorMetrics.Cli`. Nessuna correzione è applicata: la correzione è P14.2.");
        sb.AppendLine();

        sb.AppendLine("## Cosa è stato misurato");
        sb.AppendLine();
        sb.AppendLine(ParametersText);
        sb.AppendLine();

        sb.AppendLine("## Notti analizzate");
        sb.AppendLine();
        sb.AppendLine("| Notte | Frame analizzati | Frame scartati | Primo frame (UTC) | Ultimo frame (UTC) |");
        sb.AppendLine("|---|---:|---:|---|---|");
        foreach (NightMetricsResult n in nights)
        {
            sb.AppendLine($"| {n.NightId} | {n.FramesAnalyzed} | {n.FramesSkipped} | {Ts(n.FirstUtc)} | {Ts(n.LastUtc)} |");
        }

        sb.AppendLine();
        foreach (NightMetricsResult n in nights)
        {
            sb.AppendLine($"## Tabella dei numeri: {n.NightId}");
            sb.AppendLine();
            sb.AppendLine("| Metrica | Valore | Unità | Nota |");
            sb.AppendLine("|---|---:|---|---|");
            sb.AppendLine($"| Flicker: deviazione dei residui | {F(n.Flicker.StdPercent)} | % | finestra {n.Flicker.Window}, {n.Flicker.Frames} frame |");
            sb.AppendLine($"| Flicker: P95 di abs(residuo) | {F(n.Flicker.P95AbsPercent)} | % | |");
            sb.AppendLine($"| Fondo R/G: escursione | {F(n.Drift.RG.Excursion, 4)} | rapporto | max-min |");
            sb.AppendLine($"| Fondo R/G: pendenza | {F(n.Drift.RG.SlopePerHour, 4)} | 1/h | regressione sul tempo reale |");
            sb.AppendLine($"| Fondo R/G: residuo | {F(n.Drift.RG.ResidualStdPercent)} | % | dopo mediana mobile |");
            sb.AppendLine($"| Fondo B/G: escursione | {F(n.Drift.BG.Excursion, 4)} | rapporto | max-min |");
            sb.AppendLine($"| Fondo B/G: pendenza | {F(n.Drift.BG.SlopePerHour, 4)} | 1/h | regressione sul tempo reale |");
            sb.AppendLine($"| Fondo B/G: residuo | {F(n.Drift.BG.ResidualStdPercent)} | % | dopo mediana mobile |");
            sb.AppendLine($"| Salto tipico luminanza | {F(n.Phase.TypicalLumaStep, 5)} | 0..1 | mediana del valore assoluto delle differenze fra frame consecutivi della stessa fase |");
            sb.AppendLine($"| Salto tipico R/G | {F(n.Phase.TypicalRgStep, 5)} | rapporto | idem |");
            sb.AppendLine($"| Salto tipico B/G | {F(n.Phase.TypicalBgStep, 5)} | rapporto | idem |");
            foreach (PhaseBoundary b in n.Phase.Boundaries)
            {
                sb.AppendLine($"| Confine {b.From}→{b.To} ({b.Time:HH:mm} UTC) | {F(b.LumaRatio)} / {F(b.RgRatio)} / {F(b.BgRatio)} | rapporto | salto/tipico: luminanza / R/G / B/G |");
            }

            sb.AppendLine($"| Cambi di esposizione o guadagno | {n.Exposure.Changes} | cambi | di cui {n.Exposure.ChangesAtPhaseChange} con cambio di fase |");
            sb.AppendLine($"| Banda ai cambi: salto medio | {F(n.Exposure.MeanAbsRelJumpAtChanges * 100, 3)} | % | |");
            sb.AppendLine($"| Banda fuori dai cambi: salto medio | {F(n.Exposure.MeanAbsRelJumpWithoutChanges * 100, 3)} | % | |");
            sb.AppendLine($"| Banda: rapporto | {F(n.Exposure.Ratio)} | rapporto | ~1 = nessuna banda |");
            sb.AppendLine($"| Stelle: mediana R/G | {F(n.Stars.MedianRg, 4)} | rapporto | IQR {F(n.Stars.MedianRgQ25, 4)}–{F(n.Stars.MedianRgQ75, 4)} |");
            sb.AppendLine($"| Stelle: mediana B/G | {F(n.Stars.MedianBg, 4)} | rapporto | IQR {F(n.Stars.MedianBgQ25, 4)}–{F(n.Stars.MedianBgQ75, 4)} |");
            sb.AppendLine($"| Stelle: frame usati | {n.Stars.FramesUsed} | frame | su {n.Stars.FramesWithStars} con stelle; media {F(n.Stars.MeanStarsPerFrame, 1)} stelle per frame |");
            sb.AppendLine();
        }

        sb.AppendLine("## Difetti (con i numeri)");
        sb.AppendLine();
        sb.AppendLine("Soglie provvisorie: flicker 1 %, residuo di deriva 0.5 %, salto/tipico 10, banda 2. Ogni riga dice se il difetto emerge.");
        sb.AppendLine();
        foreach (NightMetricsResult n in nights)
        {
            sb.AppendLine($"### {n.NightId}");
            sb.AppendLine();
            foreach (string f in n.Findings)
            {
                sb.AppendLine($"- {f}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("## Metadati colore dell'encoder (ffprobe)");
        sb.AppendLine();
        foreach (NightMetricsResult n in nights)
        {
            sb.AppendLine($"### {n.NightId}");
            sb.AppendLine();
            if (n.Encoder is null)
            {
                sb.AppendLine($"Non verificabile: {n.EncoderNote}.");
            }
            else
            {
                EncoderColorInfo e = n.Encoder;
                sb.AppendLine("| Campo | Valore |");
                sb.AppendLine("|---|---|");
                sb.AppendLine($"| codec | {D(e.CodecName)} |");
                sb.AppendLine($"| pix_fmt | {D(e.PixFmt)} |");
                sb.AppendLine($"| color_range | {D(e.ColorRange)} |");
                sb.AppendLine($"| color_space (matrice) | {D(e.ColorSpace)} |");
                sb.AppendLine($"| color_transfer | {D(e.ColorTransfer)} |");
                sb.AppendLine($"| color_primaries | {D(e.ColorPrimaries)} |");
                sb.AppendLine($"| dimensioni | {(e.Width is null ? "n.d." : $"{e.Width}x{e.Height}")} |");
                sb.AppendLine($"| r_frame_rate | {D(e.FrameRate)} |");
                sb.AppendLine();
                foreach (string o in e.Observations)
                {
                    sb.AppendLine($"- {o}");
                }

                sb.AppendLine();
            }
        }

        sb.AppendLine("## Limiti");
        sb.AppendLine();
        foreach (string note in notes)
        {
            sb.AppendLine($"- {note}");
        }

        foreach (string l in StandardLimits)
        {
            sb.AppendLine($"- {l}");
        }

        sb.AppendLine("- [NV] Colore reale: la notte è simulata dalla camera Sim, quindi i numeri non dicono nulla sul colore del cielo di Robert. Da ripetere sui frame veri.");
        sb.AppendLine("- [NV] Decodifica del timelapse.mp4 e confronto con la sorgente: non fatti in questo passo.");
        return sb.ToString();
    }

    private static string Ts(DateTimeOffset? t) => t is { } v ? v.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "n.d.";

    private static string D(string? s) => s ?? "non dichiarato";

    private static string F(double? v, int digits = 2) =>
        v is { } x && double.IsFinite(x) ? x.ToString($"F{digits}", CultureInfo.InvariantCulture) : "n.d.";
}
