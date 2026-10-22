using AllSky.Astro;
using AllSky.Nina;
using Xunit;

namespace AllSky.Nina.Tests;

public sealed class MoonSeparationTests
{
    // Sito di prova (valori arbitrari, non quelli di Robert).
    private const double Lat = 45.0;
    private const double Lon = 9.0;

    // Tolleranza = errore atteso della posizione lunare dichiarato nel modulo (1 grado, limite superiore di 0.5-1).
    // Non va stretta: la separazione e' un angolo tra direzioni, quindi ne eredita l'errore.
    private const double Tol = MoonSeparationCalculator.PositionErrorDeg;

    private static readonly DateTimeOffset SearchStart = new(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Primo istante (ogni ora, per 14 giorni) con la Luna sopra la quota minima indicata.</summary>
    private static DateTimeOffset FindMoonAbove(double minAltDeg)
    {
        for (int h = 0; h < 14 * 24; h++)
        {
            DateTimeOffset t = SearchStart.AddHours(h);
            if (MoonPosition.Altitude(t, Lat, Lon) > minAltDeg) return t;
        }
        throw new InvalidOperationException("Nessun istante con la Luna sopra la quota richiesta.");
    }

    /// <summary>Primo istante (ogni ora, per 14 giorni) con la Luna sotto la quota indicata.</summary>
    private static DateTimeOffset FindMoonBelow(double maxAltDeg)
    {
        for (int h = 0; h < 14 * 24; h++)
        {
            DateTimeOffset t = SearchStart.AddHours(h);
            if (MoonPosition.Altitude(t, Lat, Lon) < maxAltDeg) return t;
        }
        throw new InvalidOperationException("Nessun istante con la Luna sotto la quota richiesta.");
    }

    /// <summary>RA/Dec di un soggetto posto in una direzione alt/az data (stesso istante e sito).</summary>
    private static (double Ra, double Dec) TargetAt(double altDeg, double azDeg, DateTimeOffset t) =>
        HorizontalToEquatorial.Convert(altDeg, azDeg, t, Lat, Lon);

    [Fact]
    public void Inversa_RiportaAlloStessoAltAz()
    {
        // Controllo di coerenza: inversa seguita dalla conversione in avanti deve ridare la stessa direzione.
        DateTimeOffset t = FindMoonAbove(10.0);
        (double ra, double dec) = TargetAt(35.0, 120.0, t);
        HorizontalPosition back = EquatorialToHorizontal.Convert(ra, dec, t, Lat, Lon);

        Assert.Equal(35.0, back.AltitudeDeg, 6);
        Assert.Equal(120.0, back.AzimuthDeg, 6);
    }

    [Fact]
    public void SoggettoNellaStessaPosizioneDellaLuna_SeparazioneVicinaAZero()
    {
        DateTimeOffset t = FindMoonAbove(10.0);
        HorizontalPosition moon = MoonPosition.Horizon(t, Lat, Lon);
        (double ra, double dec) = TargetAt(moon.AltitudeDeg, moon.AzimuthDeg, t);

        MoonSeparation r = MoonSeparationCalculator.Compute(ra, dec, CoordinateEpoch.J2000, t, Lat, Lon);

        Assert.Equal(MoonSeparationStatus.Ok, r.Status);
        Assert.NotNull(r.SeparationDeg);
        Assert.True(Math.Abs(r.SeparationDeg!.Value) <= Tol,
            $"Separazione {r.SeparationDeg.Value} oltre la tolleranza di {Tol} gradi.");
    }

    [Fact]
    public void SoggettiOpposti_SeparazioneVicinaA180()
    {
        DateTimeOffset t = FindMoonAbove(10.0);
        HorizontalPosition moon = MoonPosition.Horizon(t, Lat, Lon);

        // Antipodo della Luna: altezza opposta, azimut di 180 gradi.
        double antiAz = AzimuthOpposite(moon.AzimuthDeg);
        (double ra, double dec) = TargetAt(-moon.AltitudeDeg, antiAz, t);

        MoonSeparation r = MoonSeparationCalculator.Compute(ra, dec, CoordinateEpoch.J2000, t, Lat, Lon);

        Assert.NotNull(r.SeparationDeg);
        Assert.True(Math.Abs(r.SeparationDeg!.Value - 180.0) <= Tol,
            $"Separazione {r.SeparationDeg.Value} lontana da 180 oltre la tolleranza di {Tol} gradi.");
    }

    [Fact]
    public void SoggettoAPochiGradi_SeparazioneVicinaAlValoreNoto()
    {
        // Soggetto spostato di 3 gradi lungo lo stesso cerchio verticale: la distanza angolare e' esattamente 3.
        DateTimeOffset t = FindMoonAbove(20.0);
        HorizontalPosition moon = MoonPosition.Horizon(t, Lat, Lon);
        double altTarget = moon.AltitudeDeg + 3.0 <= 87.0 ? moon.AltitudeDeg + 3.0 : moon.AltitudeDeg - 3.0;
        (double ra, double dec) = TargetAt(altTarget, moon.AzimuthDeg, t);

        MoonSeparation r = MoonSeparationCalculator.Compute(ra, dec, CoordinateEpoch.J2000, t, Lat, Lon);

        Assert.NotNull(r.SeparationDeg);
        Assert.True(Math.Abs(r.SeparationDeg!.Value - 3.0) <= Tol,
            $"Separazione {r.SeparationDeg.Value} lontana da 3 gradi oltre la tolleranza di {Tol} gradi.");
    }

    [Fact]
    public void SoggettoSenzaCoordinate_NoTarget_MaLunaCalcolata()
    {
        DateTimeOffset t = FindMoonAbove(10.0);

        MoonSeparation r = MoonSeparationCalculator.Compute(null, 10.0, CoordinateEpoch.J2000, t, Lat, Lon);

        Assert.Equal(MoonSeparationStatus.NoTarget, r.Status);
        Assert.Null(r.SeparationDeg);
        Assert.Equal(MoonPosition.Altitude(t, Lat, Lon), r.MoonAltitudeDeg, 9);
        Assert.NotNull(r.MoonIlluminationFraction);
    }

    [Fact]
    public void DeclinazioneNonFinita_NoTarget()
    {
        DateTimeOffset t = FindMoonAbove(10.0);

        MoonSeparation r = MoonSeparationCalculator.Compute(120.0, double.NaN, CoordinateEpoch.J2000, t, Lat, Lon);

        Assert.Equal(MoonSeparationStatus.NoTarget, r.Status);
        Assert.Null(r.SeparationDeg);
    }

    [Fact]
    public void EpocaNonJ2000_Unsupported()
    {
        DateTimeOffset t = FindMoonAbove(10.0);

        MoonSeparation r = MoonSeparationCalculator.Compute(120.0, 10.0, CoordinateEpoch.OfDate, t, Lat, Lon);

        Assert.Equal(MoonSeparationStatus.Unsupported, r.Status);
        Assert.Null(r.SeparationDeg);
    }

    [Fact]
    public void LunaSottoOrizzonte_StatoSegnalatoMaSeparazioneCalcolata()
    {
        DateTimeOffset t = FindMoonBelow(-5.0);
        HorizontalPosition moon = MoonPosition.Horizon(t, Lat, Lon);
        (double ra, double dec) = TargetAt(moon.AltitudeDeg, moon.AzimuthDeg, t);

        MoonSeparation r = MoonSeparationCalculator.Compute(ra, dec, CoordinateEpoch.J2000, t, Lat, Lon);

        Assert.Equal(MoonSeparationStatus.MoonBelowHorizon, r.Status);
        Assert.NotNull(r.SeparationDeg);
        Assert.True(Math.Abs(r.SeparationDeg!.Value) <= Tol,
            $"Separazione {r.SeparationDeg.Value} oltre la tolleranza di {Tol} gradi.");
        Assert.True(r.MoonAltitudeDeg < 0.0);
    }

    [Fact]
    public void SogliaNull_NessunAvviso()
    {
        DateTimeOffset t = FindMoonAbove(10.0);
        HorizontalPosition moon = MoonPosition.Horizon(t, Lat, Lon);
        (double ra, double dec) = TargetAt(moon.AltitudeDeg, moon.AzimuthDeg, t);
        MoonSeparation r = MoonSeparationCalculator.Compute(ra, dec, CoordinateEpoch.J2000, t, Lat, Lon);

        Assert.False(MoonSeparationCalculator.IsBelowMinimum(r, null));
    }

    [Fact]
    public void SogliaAlta_AvvisoConSeparazioneBassa()
    {
        DateTimeOffset t = FindMoonAbove(10.0);
        HorizontalPosition moon = MoonPosition.Horizon(t, Lat, Lon);
        (double ra, double dec) = TargetAt(moon.AltitudeDeg, moon.AzimuthDeg, t);
        MoonSeparation r = MoonSeparationCalculator.Compute(ra, dec, CoordinateEpoch.J2000, t, Lat, Lon);

        // Soglia di 30 gradi contro una separazione prossima a zero (entro la tolleranza): avviso.
        Assert.True(MoonSeparationCalculator.IsBelowMinimum(r, 30.0));
    }

    [Fact]
    public void SogliaInferiorAllaSeparazione_NessunAvviso()
    {
        DateTimeOffset t = FindMoonAbove(20.0);
        HorizontalPosition moon = MoonPosition.Horizon(t, Lat, Lon);
        double altTarget = moon.AltitudeDeg + 3.0 <= 87.0 ? moon.AltitudeDeg + 3.0 : moon.AltitudeDeg - 3.0;
        (double ra, double dec) = TargetAt(altTarget, moon.AzimuthDeg, t);
        MoonSeparation r = MoonSeparationCalculator.Compute(ra, dec, CoordinateEpoch.J2000, t, Lat, Lon);

        // Separazione attesa 3 gradi: con soglia 2 non c'e' avviso.
        Assert.False(MoonSeparationCalculator.IsBelowMinimum(r, 2.0));
    }

    [Fact]
    public void SenzaSeparazione_NessunAvvisoAnchePerSogliaAlta()
    {
        DateTimeOffset t = FindMoonAbove(10.0);
        MoonSeparation r = MoonSeparationCalculator.Compute(null, null, CoordinateEpoch.J2000, t, Lat, Lon);

        Assert.False(MoonSeparationCalculator.IsBelowMinimum(r, 90.0));
    }

    [Fact]
    public void SogliaFuoriIntervallo_Eccezione()
    {
        DateTimeOffset t = FindMoonAbove(10.0);
        MoonSeparation r = MoonSeparationCalculator.Compute(null, null, CoordinateEpoch.J2000, t, Lat, Lon);

        Assert.Throws<ArgumentOutOfRangeException>(() => MoonSeparationCalculator.IsBelowMinimum(r, -1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MoonSeparationCalculator.IsBelowMinimum(r, 181.0));
    }

    private static double AzimuthOpposite(double azDeg)
    {
        double a = (azDeg + 180.0) % 360.0;
        return a < 0.0 ? a + 360.0 : a;
    }
}
