using Xunit;
using AllSky.Nina;

namespace AllSky.Nina.Tests;

public sealed class ParkStateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 20, 0, 0, TimeSpan.Zero);

    /// <summary>Stato connesso con mount dato. Campi non usati dalla funzione: coordinate nulle.</summary>
    private static NinaState Stato(NinaMountState? mount, bool connesso = true) =>
        new(connesso, T0, mount, null, null, null);

    private static NinaMountState Mount(bool? atPark, bool? slewing = false, string? tracking = null) =>
        new(atPark, slewing, tracking, null, null);

    // --- Compute: un test per ramo ---

    [Fact]
    public void NonConnesso_E_Disconnected_ConQualsiasiMount()
    {
        Assert.Equal(ParkStatus.Disconnected, ParkState.Compute(Stato(Mount(false), connesso: false)));
        Assert.Equal(ParkStatus.Disconnected, ParkState.Compute(Stato(null, connesso: false)));
    }

    [Fact]
    public void NonConnesso_PrevaleSuAtParkTrue()
    {
        // Connected false vince anche se il mount dichiara park: l'ultimo dato non e' affidabile.
        NinaState s = Stato(Mount(atPark: true, slewing: false, tracking: "Off"), connesso: false);
        Assert.Equal(ParkStatus.Disconnected, ParkState.Compute(s));
    }

    [Fact]
    public void MountNull_E_Unknown()
    {
        Assert.Equal(ParkStatus.Unknown, ParkState.Compute(Stato(null)));
    }

    [Fact]
    public void AtParkNull_ConSlewingNullONonDeterminato_E_Unknown()
    {
        Assert.Equal(ParkStatus.Unknown, ParkState.Compute(Stato(Mount(atPark: null, slewing: null))));
        Assert.Equal(ParkStatus.Unknown, ParkState.Compute(Stato(Mount(atPark: null, slewing: false, tracking: "Sidereal"))));
    }

    [Fact]
    public void AtParkNull_MaSlewingTrue_E_Slewing_NonParcheggiato()
    {
        Assert.Equal(ParkStatus.Slewing, ParkState.Compute(Stato(Mount(atPark: null, slewing: true))));
    }

    [Fact]
    public void AtParkNull_NonDiventaMaiParked()
    {
        // Un valore assente non e' parcheggiato, qualunque sia il resto.
        Assert.NotEqual(ParkStatus.Parked, ParkState.Compute(Stato(Mount(atPark: null, slewing: null, tracking: "Off"))));
    }

    [Fact]
    public void AtParkTrue_E_Parked_AnchePerInseguimentoOSlew()
    {
        Assert.Equal(ParkStatus.Parked, ParkState.Compute(Stato(Mount(atPark: true))));
        Assert.Equal(ParkStatus.Parked, ParkState.Compute(Stato(Mount(atPark: true, slewing: true, tracking: "Sidereal"))));
    }

    [Fact]
    public void AtParkFalse_ConSlewingTrue_E_Slewing()
    {
        Assert.Equal(ParkStatus.Slewing, ParkState.Compute(Stato(Mount(atPark: false, slewing: true, tracking: "Sidereal"))));
    }

    [Fact]
    public void SlewingNull_NonBloccaTracking()
    {
        Assert.Equal(ParkStatus.Tracking, ParkState.Compute(Stato(Mount(atPark: false, slewing: null, tracking: "Sidereal"))));
    }

    [Fact]
    public void AtParkFalse_ConTrackingAttivo_E_Tracking()
    {
        Assert.Equal(ParkStatus.Tracking, ParkState.Compute(Stato(Mount(atPark: false, slewing: false, tracking: "Sidereal"))));
        Assert.Equal(ParkStatus.Tracking, ParkState.Compute(Stato(Mount(atPark: false, slewing: false, tracking: "  Lunar "))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Off")]
    [InlineData("off")]
    [InlineData("None")]
    [InlineData("none")]
    public void AtParkFalse_SenzaInseguimento_E_Stopped(string? tracking)
    {
        Assert.Equal(ParkStatus.Stopped, ParkState.Compute(Stato(Mount(atPark: false, slewing: false, tracking: tracking))));
    }

    [Fact]
    public void Offline_E_Disconnected()
    {
        NinaState s = NinaState.Offline(T0, "timeout (mount)");
        Assert.Equal(ParkStatus.Disconnected, ParkState.Compute(s));
    }

    [Fact]
    public void ArgomentoNull_Lancia()
    {
        Assert.Throws<ArgumentNullException>(() => ParkState.Compute(null!));
    }

    // --- ParkTransitionLog ---

    private static ParkTransition? Primo(ParkTransitionLog log, ParkStatus s) => log.Observe(s, T0);

    [Fact]
    public void PrimaOsservazione_FissaLoStato_SenzaTransizione()
    {
        var log = new ParkTransitionLog();
        Assert.Null(log.Current);
        Assert.Null(Primo(log, ParkStatus.Parked));
        Assert.Equal(ParkStatus.Parked, log.Current);
    }

    [Fact]
    public void UnknownVersoUnknown_NonEmetteNulla()
    {
        var log = new ParkTransitionLog();
        Assert.Null(Primo(log, ParkStatus.Unknown));
        Assert.Null(Primo(log, ParkStatus.Unknown));
        Assert.Null(Primo(log, ParkStatus.Unknown));
    }

    [Fact]
    public void UnknownVersoParked_E_Registrato()
    {
        var log = new ParkTransitionLog();
        Primo(log, ParkStatus.Unknown);
        ParkTransition? t = log.Observe(ParkStatus.Parked, T0.AddMinutes(1));
        Assert.NotNull(t);
        Assert.Equal(ParkStatus.Unknown, t!.From);
        Assert.Equal(ParkStatus.Parked, t.To);
    }

    [Fact]
    public void VersoDisconnected_E_Registrato()
    {
        var log = new ParkTransitionLog();
        Primo(log, ParkStatus.Tracking);
        ParkTransition? t = log.Observe(ParkStatus.Disconnected, T0.AddMinutes(2));
        Assert.NotNull(t);
        Assert.Equal(ParkStatus.Disconnected, t!.To);
    }

    [Fact]
    public void StatiUgualiConsecutivi_NonDuplicano()
    {
        var log = new ParkTransitionLog();
        var osservazioni = new[]
        {
            (ParkStatus.Stopped, T0),
            (ParkStatus.Stopped, T0.AddSeconds(10)),
            (ParkStatus.Stopped, T0.AddSeconds(20)),
            (ParkStatus.Tracking, T0.AddSeconds(30)),
            (ParkStatus.Tracking, T0.AddSeconds(40)),
        };
        IReadOnlyList<ParkTransition> eventi = ParkTransitionLog.FromSequence(osservazioni);

        Assert.Single(eventi);
        Assert.Equal(ParkStatus.Stopped, eventi[0].From);
        Assert.Equal(ParkStatus.Tracking, eventi[0].To);
        Assert.Equal(T0.AddSeconds(30), eventi[0].AtUtc);
    }

    [Fact]
    public void Sequenza_ProduceLeTransizioniAttese_SenzaDuplicati()
    {
        var osservazioni = new[]
        {
            (ParkStatus.Unknown,      T0),
            (ParkStatus.Unknown,      T0.AddMinutes(1)),
            (ParkStatus.Parked,       T0.AddMinutes(2)),
            (ParkStatus.Parked,       T0.AddMinutes(3)),
            (ParkStatus.Tracking,     T0.AddMinutes(4)),
            (ParkStatus.Slewing,      T0.AddMinutes(5)),
            (ParkStatus.Slewing,      T0.AddMinutes(6)),
            (ParkStatus.Tracking,     T0.AddMinutes(7)),
            (ParkStatus.Disconnected, T0.AddMinutes(8)),
            (ParkStatus.Disconnected, T0.AddMinutes(9)),
            (ParkStatus.Tracking,     T0.AddMinutes(10)),
        };

        IReadOnlyList<ParkTransition> eventi = ParkTransitionLog.FromSequence(osservazioni);

        var attese = new (ParkStatus From, ParkStatus To, int Minuti)[]
        {
            (ParkStatus.Unknown,      ParkStatus.Parked,       2),
            (ParkStatus.Parked,       ParkStatus.Tracking,     4),
            (ParkStatus.Tracking,     ParkStatus.Slewing,      5),
            (ParkStatus.Slewing,      ParkStatus.Tracking,     7),
            (ParkStatus.Tracking,     ParkStatus.Disconnected, 8),
            (ParkStatus.Disconnected, ParkStatus.Tracking,     10),
        };

        Assert.Equal(attese.Length, eventi.Count);
        for (int i = 0; i < attese.Length; i++)
        {
            Assert.Equal(attese[i].From, eventi[i].From);
            Assert.Equal(attese[i].To, eventi[i].To);
            Assert.Equal(T0.AddMinutes(attese[i].Minuti), eventi[i].AtUtc);
        }
    }

    [Fact]
    public void Istante_NormalizzatoInUtc()
    {
        var log = new ParkTransitionLog();
        var conOffset = new DateTimeOffset(2026, 10, 22, 22, 0, 0, TimeSpan.FromHours(2)); // = 20:00 UTC
        log.Observe(ParkStatus.Parked, conOffset.AddMinutes(-5));
        ParkTransition? t = log.Observe(ParkStatus.Stopped, conOffset);

        Assert.NotNull(t);
        Assert.Equal(TimeSpan.Zero, t!.AtUtc.Offset);
        Assert.Equal(T0, t.AtUtc);
    }

    [Fact]
    public void FromSequence_SequenzaVuota_NessunaTransizione()
    {
        Assert.Empty(ParkTransitionLog.FromSequence(Array.Empty<(ParkStatus, DateTimeOffset)>()));
    }
}
