using AllSky.Nina;
using Xunit;

namespace AllSky.Nina.Tests;

public sealed class YouTubeChaptersTests
{
    private static readonly DateTimeOffset Inizio = new(2026, 10, 22, 20, 0, 0, TimeSpan.Zero);

    private static NinaEvent Ws(string nome, double secondiDopoInizio, string? messaggio = null) =>
        NinaEvent.FromWebSocket(nome, Inizio.AddSeconds(secondiDopoInizio), 0, messaggio).Event!;

    private static NinaEvent Log(double secondiDopoInizio, string? categoria, string messaggio) =>
        new(Inizio.AddSeconds(secondiDopoInizio), NinaEvent.SourceLog, NinaEvent.LogLineName, NinaLogLevels.Info, messaggio, categoria);

    private static ChapterRules Regole(int? minimoNumero = null, int? minimoSecondi = null, bool primoA0 = false) =>
        new(minimoNumero, minimoSecondi, primoA0);

    [Fact]
    public void EventiNellaFinestra_DanoTempiAttesi()
    {
        NinaEvent[] eventi = [Ws("AUTOFOCUS-FINISHED", 75), Ws("MOUNT-BEFORE-FLIP", 3605)];

        ChapterResult r = ChapterBuilder.Build(eventi, Inizio, 1.0, Regole());

        Assert.Equal(new[] { 75, 3605 }, r.Chapters.Select(c => c.Seconds).ToArray());
        Assert.Equal("Messa a fuoco", r.Chapters[0].Title);
        Assert.Equal("Meridian flip", r.Chapters[1].Title);
        Assert.Empty(r.Skipped);
    }

    [Fact]
    public void Fps_TempoArrotondatoPerDifetto()
    {
        // 7 secondi reali a 2 fps: 3,5 video-secondi -> 3.
        ChapterResult r = ChapterBuilder.Build([Ws("AUTOFOCUS-FINISHED", 7)], Inizio, 2.0, Regole());

        Assert.Equal(3, r.Chapters[0].Seconds);
    }

    [Fact]
    public void EventoPrimaDellInizioOAlloDellaFine_Scartato()
    {
        NinaEvent[] eventi = [Ws("AUTOFOCUS-FINISHED", -5), Ws("AUTOFOCUS-FINISHED", 99), Ws("AUTOFOCUS-FINISHED", 100)];

        ChapterResult r = ChapterBuilder.Build(eventi, Inizio, 1.0, Regole(), Inizio.AddSeconds(100));

        Assert.Equal(new[] { 99 }, r.Chapters.Select(c => c.Seconds).ToArray());
        Assert.Equal(2, r.Skipped.Count);
        Assert.Contains(r.Skipped, s => s.Reason.Contains("prima dell'inizio", StringComparison.Ordinal));
        Assert.Contains(r.Skipped, s => s.Reason.Contains("dopo la fine", StringComparison.Ordinal));
    }

    [Fact]
    public void FineNonNota_AvvisaEMantieneEventiDopo()
    {
        ChapterResult r = ChapterBuilder.Build([Ws("AUTOFOCUS-FINISHED", 5000)], Inizio, 1.0, Regole());

        Assert.Single(r.Chapters);
        Assert.Contains(r.Warnings, w => w.Contains("fine della finestra", StringComparison.Ordinal));
    }

    [Fact]
    public void DueEventiNelloStessoSecondo_NeRestaUnoSolo_VinceIlPrimo()
    {
        NinaEvent[] eventi = [Ws("AUTOFOCUS-FINISHED", 10.2), Ws("GUIDER-DITHER", 10.7)];

        ChapterResult r = ChapterBuilder.Build(eventi, Inizio, 1.0, Regole());

        Assert.Single(r.Chapters);
        Assert.Equal(10, r.Chapters[0].Seconds);
        Assert.Equal("Messa a fuoco", r.Chapters[0].Title);
        Assert.Single(r.Skipped);
    }

    [Fact]
    public void Format_MSSeHHMMSS_OrdinatiESenzaDuplicati()
    {
        var risultato = new ChapterResult(
            [new Chapter(3725, "Meridian flip"), new Chapter(0, "Guida"), new Chapter(65, "Messa a fuoco"), new Chapter(65, "Dither")],
            [],
            []);

        string testo = ChapterBuilder.Format(risultato);

        Assert.Equal("0:00 Guida\n1:05 Messa a fuoco\n1:02:05 Meridian flip", testo);
    }

    [Fact]
    public void Format_OltreUnOra_SecondiEMinutiSempreDueCifre()
    {
        var risultato = new ChapterResult([new Chapter(3599, "Errore"), new Chapter(3600, "Guida")], [], []);

        Assert.Equal("59:59 Errore\n1:00:00 Guida", ChapterBuilder.Format(risultato));
    }

    [Fact]
    public void Format_SenzaCapitoli_StringaVuota()
    {
        Assert.Equal("", ChapterBuilder.Format(new ChapterResult([], [], [])));
    }

    [Fact]
    public void RegoleNull_AvvisoRegoleNonVerificate()
    {
        ChapterResult r = ChapterBuilder.Build([Ws("AUTOFOCUS-FINISHED", 10)], Inizio, 1.0, null);

        Assert.Contains(r.Warnings, w => w.Contains("regole YouTube non verificate", StringComparison.Ordinal));
        Assert.Single(r.Chapters);
    }

    [Fact]
    public void RegoleConNumeroMinimoNonRaggiunto_NessunCapitoloEAvviso()
    {
        NinaEvent[] eventi = [Ws("AUTOFOCUS-FINISHED", 10), Ws("GUIDER-DITHER", 20)];

        ChapterResult r = ChapterBuilder.Build(eventi, Inizio, 1.0, Regole(minimoNumero: 3), Inizio.AddSeconds(100));

        Assert.Empty(r.Chapters);
        Assert.Equal(2, r.Skipped.Count);
        Assert.Contains(r.Warnings, w => w.Contains("meno di 3 capitoli", StringComparison.Ordinal));
        Assert.DoesNotContain(r.Warnings, w => w.Contains("regole YouTube non verificate", StringComparison.Ordinal));
    }

    [Fact]
    public void RegoleDurataMinima_ScartaCapitoliBrevi()
    {
        // Fine a 200 s. Dal fondo: 150 dura 50 (<60, scartato); 60 dura 140 (tenuto); 30 dura 30 (scartato).
        NinaEvent[] eventi = [Ws("AUTOFOCUS-FINISHED", 30), Ws("MOUNT-BEFORE-FLIP", 60), Ws("GUIDER-DITHER", 150)];

        ChapterResult r = ChapterBuilder.Build(eventi, Inizio, 1.0, Regole(minimoSecondi: 60), Inizio.AddSeconds(200));

        Assert.Equal(new[] { 60 }, r.Chapters.Select(c => c.Seconds).ToArray());
        Assert.Equal(2, r.Skipped.Count);
    }

    [Fact]
    public void RegolaPrimoA0_PrimoNonA0_NessunCapitolo()
    {
        ChapterResult r = ChapterBuilder.Build([Ws("AUTOFOCUS-FINISHED", 10)], Inizio, 1.0, Regole(primoA0: true), Inizio.AddSeconds(100));

        Assert.Empty(r.Chapters);
        Assert.Contains(r.Warnings, w => w.Contains("0:00", StringComparison.Ordinal));
    }

    [Fact]
    public void NessunMessaggioGrezzo_NeiTitoliNeiMotivi()
    {
        NinaEvent[] eventi =
        [
            Ws("AUTOFOCUS-FINISHED", 10, "C:\\Users\\rob\\img token=SEGRETO"),
            Log(20, null, "riga di log con percorso /home/rob SEGRETO"),
            Log(30, "errore", "eccezione con SEGRETO e indirizzo 192.168.1.10"),
        ];

        ChapterResult r = ChapterBuilder.Build(eventi, Inizio, 1.0, Regole(), Inizio.AddSeconds(100));
        string testo = ChapterBuilder.Format(r);

        Assert.Equal(new[] { "Messa a fuoco", "Errore" }, r.Chapters.Select(c => c.Title).ToArray());
        Assert.Single(r.Skipped);
        Assert.False(testo.Contains("SEGRETO", StringComparison.Ordinal));
        Assert.False(testo.Contains("C:", StringComparison.Ordinal));
        Assert.False(testo.Contains("192.168", StringComparison.Ordinal));
        Assert.False(string.Join("|", r.Skipped.Select(s => s.Reason)).Contains("SEGRETO", StringComparison.Ordinal));
    }

    [Fact]
    public void CategorieConfermate_Allineate()
    {
        var attese = new (string Nome, string Titolo)[]
        {
            ("AUTOFOCUS-FINISHED", "Messa a fuoco"),
            ("MOUNT-AFTER-FLIP", "Meridian flip"),
            ("GUIDER-DITHER", "Dither"),
            ("GUIDER-START", "Guida"),
            ("SEQUENCE-ENTITY-FAILED", "Errore"),
        };

        foreach ((string nome, string titolo) in attese)
        {
            ChapterResult r = ChapterBuilder.Build([Ws(nome, 10)], Inizio, 1.0, Regole());
            Assert.Equal(titolo, r.Chapters[0].Title);
        }
    }

    [Fact]
    public void FpsNonValido_Lancia()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChapterBuilder.Build([], Inizio, 0, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChapterBuilder.Build([], Inizio, double.NaN, null));
    }

    [Fact]
    public void Merge_SegnapostoSostituitoDalBlocco()
    {
        var capitoli = new ChapterResult([new Chapter(65, "Messa a fuoco")], [], []);

        ChapterMergeResult m = ChapterBuilder.MergeIntoDescription("Notte.\n{capitoli}\nGrazie.", capitoli);

        Assert.Equal("Notte.\n1:05 Messa a fuoco\nGrazie.", m.Text);
        Assert.Empty(m.Warnings);
    }

    [Fact]
    public void Merge_SenzaCapitoli_SegnaposoVuotoEAvviso()
    {
        ChapterMergeResult m = ChapterBuilder.MergeIntoDescription("Notte.\n{capitoli}\nGrazie.", new ChapterResult([], [], []));

        Assert.Equal("Notte.\n\nGrazie.", m.Text);
        Assert.Contains(m.Warnings, w => w.Contains("nessun capitolo", StringComparison.Ordinal));
    }

    [Fact]
    public void Merge_SegnaposoAssente_DescrizioneInvariata()
    {
        const string modello = "Testo senza segnaposto dei capitoli.";
        var capitoli = new ChapterResult([new Chapter(65, "Messa a fuoco")], [], []);

        ChapterMergeResult m = ChapterBuilder.MergeIntoDescription(modello, capitoli);

        Assert.Equal(modello, m.Text);
        Assert.Contains(m.Warnings, w => w.Contains("{capitoli}", StringComparison.Ordinal));
    }

    [Fact]
    public void Merge_GraffeEscapate_NonSostituite()
    {
        var capitoli = new ChapterResult([new Chapter(65, "Messa a fuoco")], [], []);

        ChapterMergeResult m = ChapterBuilder.MergeIntoDescription("Letterale {{capitoli}}", capitoli);

        Assert.Equal("Letterale {{capitoli}}", m.Text);
    }
}
