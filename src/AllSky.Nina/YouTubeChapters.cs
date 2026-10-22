using System.Globalization;
using System.Text;

namespace AllSky.Nina;

/// <summary>
/// Regole di YouTube sui capitoli della descrizione (P11.10). Ogni campo e' facoltativo: <c>null</c> (o <c>false</c>)
/// significa che la regola non si applica.
/// [NV] Le regole NON sono verificate. Vanno lette sulla guida ufficiale di YouTube sui capitoli nella descrizione
/// e riportate qui dal chiamante; il codice non ne conosce i valori. Per questo <see cref="ChapterBuilder.Build"/>
/// avvisa "regole YouTube non verificate" quando <c>rules</c> e' null.
/// </summary>
/// <param name="MinimumCount">Numero minimo di capitoli: sotto questa soglia non se ne pubblica nessuno.</param>
/// <param name="MinimumSeconds">Durata minima di ciascun capitolo, in secondi di video.</param>
/// <param name="FirstAtZero">Se vero, il primo capitolo deve cadere a 0:00; altrimenti nessun capitolo.</param>
public sealed record ChapterRules(int? MinimumCount = null, int? MinimumSeconds = null, bool FirstAtZero = false);

/// <summary>Capitolo: istante nel video in secondi (gia' arrotondato per difetto) e titolo.</summary>
public sealed record Chapter(int Seconds, string Title);

/// <summary>Evento scartato, con il motivo. Non contiene messaggi di log ne' percorsi.</summary>
public sealed record SkippedChapter(string EventName, DateTimeOffset AtUtc, string Reason);

/// <summary>Esito della costruzione dei capitoli.</summary>
public sealed record ChapterResult(IReadOnlyList<Chapter> Chapters, IReadOnlyList<SkippedChapter> Skipped, IReadOnlyList<string> Warnings);

/// <summary>Modello di descrizione con il segnaposto <c>{capitoli}</c> sostituito, e gli avvisi relativi.</summary>
public sealed record ChapterMergeResult(string Text, IReadOnlyList<string> Warnings);

/// <summary>
/// Capitoli automatici per YouTube (P11.10), a partire dagli eventi NINA.
/// Il tempo di un capitolo e' (istante UTC dell'evento - inizio della finestra del timelapse) / fps, per difetto
/// al secondo. Questo presuppone che ogni fotogramma del timelapse corrisponda a un secondo reale di cattura:
/// con un intervallo diverso i tempi sono sbagliati. Da verificare con l'archivio reale (vedi NOTES.md).
/// Le funzioni non lanciano per dati di evento: solo per argomenti non validi (fps, finestra, regole negative).
/// </summary>
public static class ChapterBuilder
{
    /// <summary>Segnaposto del modello di descrizione (P7.1) che riceve il blocco dei capitoli.</summary>
    public const string Placeholder = "{capitoli}";

    /// <summary>Avviso emesso quando non sono passate regole YouTube.</summary>
    public const string RulesNotVerifiedWarning =
        "regole YouTube non verificate: nessun controllo su numero minimo, durata minima e primo capitolo a 0:00";

    // Categorie confermate (NinaWebSocketEvents.CategoryOf in NinaEvent.cs) con il titolo mostrato.
    // Tenere allineato con CategoryOf: il test "CategorieConfermate_Allineate" lo verifica.
    private static readonly IReadOnlyDictionary<string, string> TitoliPerCategoria = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["messa a fuoco"] = "Messa a fuoco",
        ["meridian flip"] = "Meridian flip",
        ["dither"] = "Dither",
        ["guida"] = "Guida",
        ["errore"] = "Errore",
    };

    /// <summary>
    /// Costruisce i capitoli dalla finestra del timelapse.
    /// Ordine: titolo (categoria confermata, altrimenti nome WebSocket confermato; i log senza categoria si scartano);
    /// finestra (prima dell'inizio o alla fine e oltre: scartati); stesso secondo (vince il primo per istante);
    /// durata minima; primo a 0:00; numero minimo.
    /// Il fine finestra e' facoltativo: senza, gli eventi dopo la fine non si scartano e l'ultimo capitolo non ha durata verificabile.
    /// </summary>
    /// <param name="eventi">Eventi NINA (qualsiasi ordine).</param>
    /// <param name="timelapseStartUtc">Inizio della finestra del timelapse, UTC.</param>
    /// <param name="timelapseFps">Fotogrammi al secondo del timelapse; finito e maggiore di zero.</param>
    /// <param name="rules">Regole YouTube; null = nessuna regola (con avviso).</param>
    /// <param name="timelapseEndUtc">Fine della finestra del timelapse, UTC, facoltativa.</param>
    /// <exception cref="ArgumentOutOfRangeException">Fps non valido, fine non successiva all'inizio, regole con valori negativi.</exception>
    public static ChapterResult Build(
        IReadOnlyList<NinaEvent> eventi,
        DateTimeOffset timelapseStartUtc,
        double timelapseFps,
        ChapterRules? rules,
        DateTimeOffset? timelapseEndUtc = null)
    {
        ArgumentNullException.ThrowIfNull(eventi);
        if (!double.IsFinite(timelapseFps) || timelapseFps <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timelapseFps), timelapseFps, "Il fps del timelapse deve essere finito e maggiore di zero.");
        }

        if (rules?.MinimumCount is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rules), rules.MinimumCount, "Il numero minimo di capitoli non puo' essere negativo.");
        }

        if (rules?.MinimumSeconds is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rules), rules.MinimumSeconds, "La durata minima dei capitoli non puo' essere negativa.");
        }

        DateTimeOffset inizio = timelapseStartUtc.ToUniversalTime();
        DateTimeOffset? fine = timelapseEndUtc?.ToUniversalTime();
        if (fine is not null && fine.Value <= inizio)
        {
            throw new ArgumentOutOfRangeException(nameof(timelapseEndUtc), timelapseEndUtc, "La fine del timelapse deve essere successiva all'inizio.");
        }

        var avvisi = new List<string>();
        var scartati = new List<SkippedChapter>();
        if (rules is null)
        {
            avvisi.Add(RulesNotVerifiedWarning);
        }

        if (fine is null)
        {
            avvisi.Add("fine della finestra del timelapse non nota: gli eventi dopo la fine non sono scartati");
        }

        // Durata del video in secondi (finestra / fps), solo se la fine e' nota.
        double? durataVideo = fine is null ? null : (fine.Value - inizio).TotalSeconds / timelapseFps;

        var candidati = new List<Candidate>();
        var secondiUsati = new HashSet<int>();
        foreach (NinaEvent ev in eventi.OrderBy(e => e.AtUtc))
        {
            string? titolo = TitoloDi(ev);
            if (titolo is null)
            {
                scartati.Add(new SkippedChapter(ev.Name, ev.AtUtc, "nessuna categoria confermata e nome non riconosciuto"));
                continue;
            }

            DateTimeOffset at = ev.AtUtc.ToUniversalTime();
            if (at < inizio)
            {
                scartati.Add(new SkippedChapter(ev.Name, at, "prima dell'inizio della finestra del timelapse"));
                continue;
            }

            if (fine is not null && at >= fine.Value)
            {
                scartati.Add(new SkippedChapter(ev.Name, at, "dopo la fine della finestra del timelapse"));
                continue;
            }

            int secondi = (int)Math.Floor((at - inizio).TotalSeconds / timelapseFps);
            if (!secondiUsati.Add(secondi))
            {
                scartati.Add(new SkippedChapter(ev.Name, at, "stesso secondo di un capitolo precedente"));
                continue;
            }

            candidati.Add(new Candidate(secondi, titolo, ev.Name, at));
        }

        List<Candidate> tenuti = candidati;

        // Durata minima: si scorre dal fondo. Un capitolo dura fino al successivo tenuto (o fino alla fine del video).
        if (rules?.MinimumSeconds is { } minimoSecondi)
        {
            if (fine is null)
            {
                avvisi.Add("durata dell'ultimo capitolo non verificabile: fine della finestra non nota");
            }

            var invertiti = new List<Candidate>();
            double? confine = durataVideo;
            for (int i = candidati.Count - 1; i >= 0; i--)
            {
                Candidate c = candidati[i];
                if (confine is { } limite && limite - c.Seconds < minimoSecondi)
                {
                    scartati.Add(new SkippedChapter(c.EventName, c.AtUtc, $"capitolo piu' corto di {minimoSecondi} s"));
                    continue;
                }

                invertiti.Add(c);
                confine = c.Seconds;
            }

            invertiti.Reverse();
            tenuti = invertiti;
        }

        if (rules?.FirstAtZero == true && tenuti.Count > 0 && tenuti[0].Seconds != 0)
        {
            foreach (Candidate c in tenuti)
            {
                scartati.Add(new SkippedChapter(c.EventName, c.AtUtc, "il primo capitolo non cade a 0:00"));
            }

            tenuti = new List<Candidate>();
            avvisi.Add("il primo capitolo non cade a 0:00: nessun capitolo");
        }

        if (rules?.MinimumCount is { } minimoNumero && tenuti.Count < minimoNumero)
        {
            foreach (Candidate c in tenuti)
            {
                scartati.Add(new SkippedChapter(c.EventName, c.AtUtc, $"meno di {minimoNumero} capitoli richiesti"));
            }

            tenuti = new List<Candidate>();
            avvisi.Add($"meno di {minimoNumero} capitoli: nessun capitolo");
        }

        return new ChapterResult(
            tenuti.Select(c => new Chapter(c.Seconds, c.Title)).ToList(),
            scartati,
            avvisi);
    }

    /// <summary>
    /// Blocco di testo per la descrizione: una riga "m:ss Titolo" (o "h:mm:ss" da un'ora in su), in ordine di tempo.
    /// Due capitoli nello stesso secondo: resta il primo. Stringa vuota se non ci sono capitoli.
    /// </summary>
    public static string Format(ChapterResult risultato)
    {
        ArgumentNullException.ThrowIfNull(risultato);
        var usati = new HashSet<int>();
        var sb = new StringBuilder();
        foreach (Chapter c in risultato.Chapters.OrderBy(c => c.Seconds))
        {
            if (c.Seconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(risultato), c.Seconds, "Il tempo di un capitolo non puo' essere negativo.");
            }

            if (!usati.Add(c.Seconds))
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append('\n');
            }

            sb.Append(FormatTime(c.Seconds)).Append(' ').Append(PulisciRiga(c.Title));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Sostituisce ogni <c>{capitoli}</c> del modello di descrizione con il blocco di <see cref="Format"/>.
    /// Se non ci sono capitoli il segnaposto diventa vuoto e si aggiunge un avviso. Se il modello non contiene il segnaposto,
    /// il testo resta invariato (il blocco non viene aggiunto) e si avvisa solo quando ci sarebbero stati capitoli.
    /// Le graffe "{{" e "}}" del modello restano tali, cosi' il parser di P7.1 le legge come letterali.
    /// </summary>
    public static ChapterMergeResult MergeIntoDescription(string modello, ChapterResult capitoli)
    {
        ArgumentNullException.ThrowIfNull(modello);
        ArgumentNullException.ThrowIfNull(capitoli);

        string blocco = Format(capitoli);
        var avvisi = new List<string>(capitoli.Warnings);
        var sb = new StringBuilder(modello.Length + blocco.Length);
        bool trovato = false;

        int i = 0;
        while (i < modello.Length)
        {
            if (IsAt(modello, i, "{{") || IsAt(modello, i, "}}"))
            {
                sb.Append(modello, i, 2);
                i += 2;
            }
            else if (IsAt(modello, i, Placeholder))
            {
                trovato = true;
                sb.Append(Escape(blocco));
                i += Placeholder.Length;
            }
            else
            {
                sb.Append(modello[i]);
                i++;
            }
        }

        if (!trovato && blocco.Length > 0)
        {
            avvisi.Add("modello senza {capitoli}: capitoli non inseriti nella descrizione");
        }

        if (trovato && blocco.Length == 0)
        {
            avvisi.Add("nessun capitolo: segnaposto {capitoli} lasciato vuoto");
        }

        return new ChapterMergeResult(sb.ToString(), avvisi);
    }

    /// <summary>
    /// Titolo del capitolo: categoria confermata se presente; altrimenti il nome dell'evento WebSocket, se confermato.
    /// Null se nessuno dei due: non si usa mai il testo del messaggio.
    /// </summary>
    private static string? TitoloDi(NinaEvent ev)
    {
        if (!string.IsNullOrEmpty(ev.Category) && TitoliPerCategoria.TryGetValue(ev.Category, out string? titolo))
        {
            return titolo;
        }

        if (ev.Source == NinaEvent.SourceWebSocket && NinaWebSocketEvents.Known.Contains(ev.Name))
        {
            return ev.Name;
        }

        return null;
    }

    /// <summary>m:ss sotto l'ora, h:mm:ss da un'ora in su.</summary>
    private static string FormatTime(int secondi)
    {
        int ore = secondi / 3600;
        int minuti = secondi % 3600 / 60;
        int sec = secondi % 60;
        return ore > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", ore, minuti, sec)
            : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", minuti, sec);
    }

    private static string PulisciRiga(string testo) =>
        new string(testo.Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray()).Trim();

    private static bool IsAt(string testo, int posizione, string frammento) =>
        posizione + frammento.Length <= testo.Length
        && string.CompareOrdinal(testo, posizione, frammento, 0, frammento.Length) == 0;

    private static string Escape(string testo) =>
        testo.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal);

    private sealed record Candidate(int Seconds, string Title, string EventName, DateTimeOffset AtUtc);
}
