using AllSky.Nina;
using Xunit;

namespace AllSky.Nina.Tests;

public sealed class NinaEventLogTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 20, 0, 0, TimeSpan.Zero);

    private readonly string _tmp;

    public NinaEventLogTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "allsky-nina-events-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tmp, recursive: true);
        }
        catch (IOException)
        {
            // Pulizia best effort.
        }
    }

    private NinaEventLog NewLog() => new(Path.Combine(_tmp, "nina-events.jsonl"));

    private static NinaEvent Ev(DateTimeOffset at, string name = "AUTOFOCUS-FINISHED", string msg = "fine") =>
        FromWs(name, at, 0, msg).Event!;

    private static NinaEventResult FromWs(string name, DateTimeOffset at, double offset, string? msg = null) =>
        NinaEvent.FromWebSocket(name, at, offset, msg);

    // ---- Scrittura e lettura ----

    [Fact]
    public void AppendERilettura_ConservaCampiEOrdine()
    {
        NinaEventLog log = NewLog();
        NinaEvent a = FromWs("GUIDER-DITHER", T0, 0).Event!;
        NinaEvent b = FromWs("AUTOFOCUS-FINISHED", T0.AddMinutes(1), 0, "Messa a fuoco ok").Event!;

        Assert.True(log.TryAppend(a));
        Assert.True(log.TryAppend(b));

        NinaEventsRead letto = log.ReadAll();
        Assert.Equal(0, letto.MalformedLines);
        Assert.Equal(2, letto.Events.Count);
        Assert.Equal(a, letto.Events[0]);
        Assert.Equal(b, letto.Events[1]);
        Assert.Equal("messa a fuoco", letto.Events[1].Category);
        Assert.Null(letto.Events[0].Level);
    }

    [Fact]
    public void FileMancante_DaListaVuota()
    {
        NinaEventsRead letto = NewLog().ReadAll();
        Assert.Empty(letto.Events);
        Assert.Equal(0, letto.MalformedLines);
    }

    [Fact]
    public void TryAppend_NullONonScrivibile_NonLanciaEMentreRestituisceFalse()
    {
        var log = new NinaEventLog(Path.Combine(_tmp, "nina-events.jsonl"));
        Assert.False(log.TryAppend(null));

        // Il percorso punta a una cartella: la scrittura fallisce, senza eccezione.
        string dirAsFile = Path.Combine(_tmp, "cartella");
        Directory.CreateDirectory(dirAsFile);
        var rotto = new NinaEventLog(dirAsFile);
        Assert.False(rotto.TryAppend(Ev(T0)));
    }

    [Fact]
    public void DueEventiStessoIstante_SonoEntrambiMantenuti()
    {
        NinaEventLog log = NewLog();
        Assert.True(log.TryAppend(Ev(T0, "GUIDER-START", "uno")));
        Assert.True(log.TryAppend(Ev(T0, "GUIDER-STOP", "due")));

        NinaEventsRead letto = log.ReadAll();
        Assert.Equal(2, letto.Events.Count);
        Assert.Equal("uno", letto.Events[0].Message);
        Assert.Equal("due", letto.Events[1].Message);
    }

    [Fact]
    public void RigaCorrotta_ContataNonLanciaEccezione()
    {
        NinaEventLog log = NewLog();
        Assert.True(log.TryAppend(Ev(T0)));
        File.AppendAllText(log.FilePath, "{ non e' json\n");
        Assert.True(log.TryAppend(Ev(T0.AddSeconds(5))));

        NinaEventsRead letto = log.ReadAll();
        Assert.Equal(2, letto.Events.Count);
        Assert.Equal(1, letto.MalformedLines);
    }

    [Fact]
    public void PercorsoDellaNotte_RispettaLaStruttura_ENotteNonValidaRifiutata()
    {
        string p = NinaEventLog.PathForNight(_tmp, "2026-10-22");
        Assert.Equal(Path.Combine(Path.GetFullPath(_tmp), "nights", "2026-10-22", "nina-events.jsonl"), p);
        Assert.Throws<ArgumentException>(() => NinaEventLog.PathForNight(_tmp, "../2026-10-22"));
        Assert.Throws<ArgumentException>(() => NinaEventLog.PathForNight(_tmp, "2026-02-30"));
    }

    // ---- Collegamento per timestamp ----

    [Fact]
    public void FinestraPlusMinusN_EstremiInclusi()
    {
        DateTimeOffset frame = T0.AddMinutes(10);
        var eventi = new List<NinaEvent>
        {
            Ev(frame.AddSeconds(-61), msg: "fuori"),
            Ev(frame.AddSeconds(-60), msg: "bordo sinistro"),
            Ev(frame, msg: "centro"),
            Ev(frame.AddSeconds(60), msg: "bordo destro"),
            Ev(frame.AddSeconds(61), msg: "fuori destra"),
        };

        IReadOnlyList<NinaEvent> vicini = NinaEventLinking.EventsAroundFrame(eventi, frame, 60);

        Assert.Equal(new[] { "bordo sinistro", "centro", "bordo destro" }, vicini.Select(e => e.Message));
    }

    [Fact]
    public void FinestraDiVerso_RispettaIlLimite()
    {
        var eventi = new List<NinaEvent> { Ev(T0) };
        Assert.Single(NinaEventLinking.EventsAroundFrame(eventi, T0.AddSeconds(900), 900));
        Assert.Throws<ArgumentOutOfRangeException>(() => NinaEventLinking.EventsAroundFrame(eventi, T0, 901));
        Assert.Throws<ArgumentOutOfRangeException>(() => NinaEventLinking.EventsAroundFrame(eventi, T0, -1));
    }

    [Fact]
    public void FrameSenzaEventi_DaListaVuota()
    {
        var eventi = new List<NinaEvent> { Ev(T0.AddHours(3)) };
        Assert.Empty(NinaEventLinking.EventsAroundFrame(eventi, T0, 60));
        Assert.Empty(NinaEventLinking.EventsAroundFrame(new List<NinaEvent>(), T0));
    }

    [Fact]
    public void EventoSenzaFrameVicino_NessunCollegamento()
    {
        var frame = new List<DateTimeOffset> { T0 };

        Assert.Null(NinaEventLinking.NearestFrameIndex(frame, T0.AddMinutes(5), 60));
        Assert.Null(NinaEventLinking.NearestFrameIndex(new List<DateTimeOffset>(), T0, 60));
    }

    [Fact]
    public void EventoAlBordoDellaFinestra_SiCollega()
    {
        var frame = new List<DateTimeOffset> { T0 };

        NinaEventLinking.NearestFrame? link = NinaEventLinking.NearestFrameIndex(frame, T0.AddSeconds(60), 60);
        Assert.NotNull(link);
        Assert.Equal(0, link!.Index);
        Assert.Equal(60, link.DistanceSeconds, 6);
    }

    [Fact]
    public void FrameMoreVicino_SceltoConRicercaBinaria()
    {
        var frame = new List<DateTimeOffset>
        {
            T0,
            T0.AddSeconds(90),
            T0.AddMinutes(5),
        };

        NinaEventLinking.NearestFrame? link = NinaEventLinking.NearestFrameIndex(frame, T0.AddMinutes(1), 120);

        Assert.NotNull(link);
        Assert.Equal(1, link!.Index);
        Assert.Equal(30, link.DistanceSeconds, 6);
    }

    [Fact]
    public void Parita_VinceIlFramePrecedente()
    {
        var frame = new List<DateTimeOffset> { T0, T0.AddMinutes(2) };

        NinaEventLinking.NearestFrame? link = NinaEventLinking.NearestFrameIndex(frame, T0.AddMinutes(1), 60);

        Assert.NotNull(link);
        Assert.Equal(0, link!.Index);
    }

    [Fact]
    public void EventoPrimaDelPrimoFrame_SiCollegaAlPrimo()
    {
        var frame = new List<DateTimeOffset> { T0, T0.AddSeconds(30) };

        NinaEventLinking.NearestFrame? link = NinaEventLinking.NearestFrameIndex(frame, T0.AddSeconds(-20), 60);

        Assert.NotNull(link);
        Assert.Equal(0, link!.Index);
    }

    // ---- Orologio ----

    [Fact]
    public void OffsetOrologio_ApplicatoAlTimestampWebSocket()
    {
        // NINA e' 10 s indietro: 20:00:00 di NINA = 20:00:10 reali.
        NinaEventResult r = FromWs("AUTOFOCUS-STARTING", T0, 10);

        Assert.True(r.IsRecognized);
        Assert.Equal(T0.AddSeconds(10), r.Event!.AtUtc);
        Assert.Equal(TimeSpan.Zero, r.Event.AtUtc.Offset);
    }

    [Fact]
    public void OffsetNegativo_EFusoDiversoConvertitoInUtc()
    {
        // Orario di NINA con fuso +02:00 (22:00:05) e correzione -12.5 s.
        var nina = new DateTimeOffset(2026, 10, 22, 22, 0, 5, TimeSpan.FromHours(2));
        NinaEvent ev = FromWs("GUIDER-START", nina, -12.5).Event!;

        Assert.Equal(new DateTimeOffset(2026, 10, 22, 20, 0, 0, TimeSpan.Zero).AddMilliseconds(-7500), ev.AtUtc);
    }

    [Fact]
    public void OffsetFuoriIntervallo_RifiutatoSenzaEccezione()
    {
        NinaEventResult r = FromWs("GUIDER-START", T0, 1000);
        Assert.False(r.IsRecognized);
        Assert.Equal("correzione dell'orologio fuori intervallo", r.Reason);
    }

    [Fact]
    public void ApplyClockOffset_Diretto()
    {
        Assert.Equal(T0.AddSeconds(-600), NinaEventLinking.ApplyClockOffset(T0, -600));
        Assert.Equal(T0, NinaEventLinking.ApplyClockOffset(T0, 0));
    }

    // ---- Privacy ----

    [Fact]
    public void MessaggioConPercorsoUtenteEIp_Ripulito()
    {
        string grezzo = @"Salvato in C:\Users\robert\Pictures\m31.jpg dal PC 192.168.1.20 password=abc123";

        string pulito = NinaMessageSanitizer.Clean(grezzo);

        Assert.DoesNotContain("robert", pulito);
        Assert.DoesNotContain("Users", pulito);
        Assert.DoesNotContain("192.168.1.20", pulito);
        Assert.DoesNotContain("abc123", pulito);
        Assert.Contains(NinaMessageSanitizer.UserPathMask, pulito);
        Assert.Contains(NinaMessageSanitizer.IpAddressMask, pulito);
        Assert.Contains("password=***", pulito);
    }

    [Fact]
    public void PercorsiUnixEVariabiliAmbiente_Ripuliti()
    {
        Assert.DoesNotContain("alice", NinaMessageSanitizer.Clean("File /home/alice/log.txt letto"));
        Assert.DoesNotContain("%localappdata%", NinaMessageSanitizer.Clean(@"Cartella %localappdata%\NINA\logs"));
        Assert.DoesNotContain("bob", NinaMessageSanitizer.Clean(@"Cartella %LOCALAPPDATA%\bob\NINA"));
    }

    [Fact]
    public void IpV6ConDoppiaDueP_Ripulito_OrarioNo()
    {
        string pulito = NinaMessageSanitizer.Clean("Connessione da fe80::1a2b:3c4d e alle 20:31:05");
        Assert.DoesNotContain("fe80::1a2b:3c4d", pulito);
        Assert.Contains("20:31:05", pulito);
    }

    [Fact]
    public void NumeriDiVersione_NonSonoIndirizzi()
    {
        Assert.Equal("ninaAPI 3.2.0.9001", NinaMessageSanitizer.Clean("ninaAPI 3.2.0.9001"));
    }

    [Fact]
    public void MessaggioLungo_TroncatoEUnaRiga()
    {
        string lungo = new string('a', 2000) + "\n" + "b";
        string pulito = NinaMessageSanitizer.Clean(lungo);

        Assert.True(pulito.Length <= NinaMessageSanitizer.MaxLength);
        Assert.DoesNotContain('\n', pulito);
    }

    // ---- Fabbriche WebSocket ----

    [Theory]
    [InlineData("AUTOFOCUS-FINISHED", "messa a fuoco")]
    [InlineData("MOUNT-BEFORE-FLIP", "meridian flip")]
    [InlineData("GUIDER-DITHER", "dither")]
    [InlineData("GUIDER-START", "guida")]
    [InlineData("SEQUENCE-ENTITY-FAILED", "errore")]
    [InlineData("MOUNT-PARKED", null)]
    [InlineData("STACK-UPDATED", null)]
    public void Categoria_SoloDaNomiConfermati(string nome, string? categoria)
    {
        NinaEvent ev = FromWs(nome, T0, 0).Event!;
        Assert.Equal(NinaEvent.SourceWebSocket, ev.Source);
        Assert.Equal(categoria, ev.Category);
        Assert.Null(ev.Level);
        Assert.Equal(nome, ev.Message);
    }

    [Theory]
    [InlineData("SLEW-STARTED")]
    [InlineData("FILTER-CHANGED")]
    [InlineData("")]
    [InlineData(null)]
    public void NomeNonConfermato_Rifiutato(string? nome)
    {
        NinaEventResult r = NinaEvent.FromWebSocket(nome, T0, 0);
        Assert.False(r.IsRecognized);
        Assert.NotNull(r.Reason);
    }

    // ---- Parser del log [NV] ----

    [Fact]
    public void RigaLog_FormatoIpotizzato_Riconosciuta()
    {
        INinaLogParser parser = new UnverifiedNinaLogParser();

        NinaEventResult r = parser.Parse("2026-10-22T22:01:05.123+02:00 [WRN] Guida: ritardo elevato", 0);

        Assert.True(r.IsRecognized);
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 20, 1, 5, 123, TimeSpan.Zero), r.Event!.AtUtc);
        Assert.Equal(NinaEvent.SourceLog, r.Event.Source);
        Assert.Equal(NinaLogLevels.Warning, r.Event.Level);
        Assert.Equal("Guida: ritardo elevato", r.Event.Message);
        Assert.Null(r.Event.Category);
    }

    [Theory]
    [InlineData("2026-10-22T20:00:00Z [ERR] x", NinaLogLevels.Error)]
    [InlineData("2026-10-22T20:00:00Z [INFO] x", NinaLogLevels.Info)]
    [InlineData("2026-10-22T20:00:00Z [dbg] x", NinaLogLevels.Debug)]
    [InlineData("2026-10-22T20:00:00Z [TRACE] x", NinaLogLevels.Trace)]
    public void RigaLog_LivelliNormalizzati(string riga, string livello)
    {
        NinaEventResult r = new UnverifiedNinaLogParser().Parse(riga, 0);
        Assert.True(r.IsRecognized);
        Assert.Equal(livello, r.Event!.Level);
    }

    [Fact]
    public void RigaLog_OffsetApplicatoEMessaggioPulito()
    {
        NinaEventResult r = new UnverifiedNinaLogParser().Parse(
            @"2026-10-22T22:00:00+02:00 [INF] Log in C:\Users\robert\NINA da 10.0.0.5", 5);

        Assert.True(r.IsRecognized);
        Assert.Equal(T0.AddSeconds(5), r.Event!.AtUtc);
        Assert.DoesNotContain("robert", r.Event.Message);
        Assert.DoesNotContain("10.0.0.5", r.Event.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("riga qualunque senza formato")]
    [InlineData("2026-10-22 20:00:00 [INF] senza offset")]
    [InlineData("2026-10-22T20:00:00Z [XYZ] livello ignoto")]
    [InlineData("2026-02-30T20:00:00Z [INF] data impossibile")]
    public void RigaNonRiconosciuta_SegnalataSenzaEccezione(string? riga)
    {
        NinaEventResult r = new UnverifiedNinaLogParser().Parse(riga, 0);

        Assert.False(r.IsRecognized);
        Assert.Null(r.Event);
        Assert.Equal("formato non riconosciuto", r.Reason);
    }
}
