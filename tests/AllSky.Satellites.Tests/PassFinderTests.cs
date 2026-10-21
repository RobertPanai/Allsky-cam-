using AllSky.Satellites;
using Xunit;

namespace AllSky.Satellites.Tests;

/// <summary>Test di <see cref="PassFinder"/> con il propagatore finto <see cref="CircularOrbitPropagator"/>.</summary>
public sealed class PassFinderTests
{
    private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);

    // Torino, osservatorio di riferimento del progetto.
    private const double TorinoLat = 45.07;
    private const double TorinoLon = 7.69;
    private const double TorinoAltM = 240.0;

    [Fact]
    public void Passaggi_periodici_con_il_periodo_orbitale()
    {
        // Terra non rotante e osservatore fisso: elevazione(t) è periodica con il periodo orbitale, quindi
        // anche i passaggi. RAAN 0 e osservatore a longitudine 90 gradi: il satellite passa quasi allo zenit a ogni orbita.
        var orbit = new CircularOrbitPropagator(T0, inclinationDeg: 51.6, raanDeg: 0.0, argLatitudeAtEpochDeg: 0.0, rotatingEarth: false);
        double period = CircularOrbitPropagator.PeriodSeconds;

        IReadOnlyList<SatellitePass> passes = PassFinder.Find(orbit, 51.6, 90.0, 0.0, T0, TimeSpan.FromSeconds(3.5 * period));

        Assert.True(passes.Count >= 3, $"Attesi almeno 3 passaggi in 3.5 periodi, trovati {passes.Count}.");
        for (int i = 0; i < passes.Count; i++)
        {
            Assert.True(passes[i].MaxElevationDeg > 80.0, $"Passaggio {i}: elevazione massima {passes[i].MaxElevationDeg:F2} troppo bassa per un passaggio sullo zenit.");
        }

        for (int i = 1; i < passes.Count; i++)
        {
            double gap = (passes[i].Culmination.Utc - passes[i - 1].Culmination.Utc).TotalSeconds;
            Assert.True(Math.Abs(gap - period) < 1.0, $"Distanza tra culminazioni {gap:F3} s, attesa il periodo {period:F3} s.");
        }
    }

    [Fact]
    public void Ogni_passaggio_ha_una_sola_culminazione_e_passaggi_disgiunti()
    {
        var orbit = new CircularOrbitPropagator(T0);
        IReadOnlyList<SatellitePass> passes = PassFinder.Find(orbit, TorinoLat, TorinoLon, TorinoAltM, T0, TimeSpan.FromHours(24));

        Assert.NotEmpty(passes);
        for (int i = 0; i < passes.Count; i++)
        {
            SatellitePass p = passes[i];
            Assert.True(p.Start.Utc <= p.Culmination.Utc && p.Culmination.Utc <= p.End.Utc, $"Passaggio {i}: culminazione fuori dall'intervallo.");
            Assert.True(p.MaxElevationDeg >= 10.0 - 1e-9, $"Passaggio {i}: elevazione massima sotto la soglia.");

            if (i > 0)
            {
                Assert.True(passes[i - 1].End.Utc < p.Start.Utc, $"Passaggi {i - 1} e {i} si sovrappongono.");
            }

            // Sulla griglia di 1 s l'elevazione deve salire fino al massimo e poi solo scendere.
            double prevEl = double.NegativeInfinity;
            bool descending = false;
            for (DateTimeOffset t = p.Start.Utc; t <= p.End.Utc; t = t.AddSeconds(1))
            {
                double el = orbit.Observe(t, TorinoLat, TorinoLon, TorinoAltM).ElevationDeg;
                if (el < prevEl - 1e-9)
                {
                    descending = true;
                }
                else if (descending && el > prevEl + 1e-9)
                {
                    Assert.Fail($"Passaggio {i}: l'elevazione risale dopo il massimo, il passaggio ha più culminazioni.");
                }

                prevEl = el;
            }

            Assert.True(p.MaxElevationDeg >= prevEl - 1e-6, $"Passaggio {i}: il massimo dichiarato non copre l'ultimo campione.");
        }
    }

    [Fact]
    public void Finestra_che_attraversa_l_ora_non_duplica_ne_perde_passaggi()
    {
        var orbit = new CircularOrbitPropagator(T0);
        IReadOnlyList<SatellitePass> all = PassFinder.Find(orbit, TorinoLat, TorinoLon, TorinoAltM, T0, TimeSpan.FromHours(24));
        Assert.NotEmpty(all);

        SatellitePass first = all[0];
        DateTimeOffset culm = first.Culmination.Utc;

        // Finestra da mezz'ora prima dell'ora piena che contiene la culminazione: attraversa il cambio d'ora.
        var hour = new DateTimeOffset(culm.Year, culm.Month, culm.Day, culm.Hour, 0, 0, TimeSpan.Zero);
        DateTimeOffset from = hour.AddMinutes(-30);
        IReadOnlyList<SatellitePass> part = PassFinder.Find(orbit, TorinoLat, TorinoLon, TorinoAltM, from, TimeSpan.FromHours(2));

        var matches = part.Where(p => Math.Abs((p.Culmination.Utc - culm).TotalSeconds) < 2.0).ToList();
        Assert.True(matches.Count == 1, $"Il passaggio deve comparire una volta, trovato {matches.Count} volte.");
        Assert.True(Math.Abs(matches[0].MaxElevationDeg - first.MaxElevationDeg) < 1e-3,
            "L'elevazione massima non coincide con la finestra completa.");
    }

    [Fact]
    public void Satellite_mai_sopra_l_orizzonte_da_lista_vuota()
    {
        // Dal Polo Sud l'orbita a 51.6 gradi resta sempre sotto l'orizzonte: la componente verticale del satellite
        // è sempre maggiore di -R_terra, quindi il satellite non si vede mai.
        var orbit = new CircularOrbitPropagator(T0);
        IReadOnlyList<SatellitePass> passes = PassFinder.Find(orbit, -90.0, 0.0, 0.0, T0, TimeSpan.FromHours(24));

        Assert.Empty(passes);
    }

    [Fact]
    public void Con_Sole_sempre_troppo_alto_nessun_passaggio_e_visibile_a_occhio()
    {
        var orbit = new CircularOrbitPropagator(T0);
        var options = new PassOptions { SunMaxAltDeg = -90.0 };
        IReadOnlyList<SatellitePass> passes = PassFinder.Find(orbit, TorinoLat, TorinoLon, TorinoAltM, T0, TimeSpan.FromDays(7), options);

        Assert.NotEmpty(passes);
        Assert.True(passes.All(p => !p.IsVisibleToEye), "Con soglia del Sole a -90 gradi nessun passaggio può essere visibile.");
    }

    [Fact]
    public void Con_soglia_del_Sole_a_90_il_flag_visibile_coincide_con_l_illuminazione()
    {
        var orbit = new CircularOrbitPropagator(T0);
        var options = new PassOptions { SunMaxAltDeg = 90.0 };
        IReadOnlyList<SatellitePass> passes = PassFinder.Find(orbit, TorinoLat, TorinoLon, TorinoAltM, T0, TimeSpan.FromDays(7), options);

        Assert.NotEmpty(passes);
        foreach (SatellitePass p in passes)
        {
            Assert.True(p.IsVisibleToEye == (p.IlluminatedFraction > 0.0),
                "Con il Sole sempre sotto soglia, visibile deve valere quanto 'almeno un campione illuminato'.");
        }
    }

    [Fact]
    public void Passaggi_con_Sole_basso_e_satellite_illuminato_sono_visibili_e_quelli_diurni_no()
    {
        var orbit = new CircularOrbitPropagator(T0);
        IReadOnlyList<SatellitePass> passes = PassFinder.Find(orbit, TorinoLat, TorinoLon, TorinoAltM, T0, TimeSpan.FromDays(7));

        // Caso positivo: almeno un passaggio con satellite illuminato e Sole sotto -6 gradi.
        Assert.True(passes.Any(p => p.IsVisibleToEye), "Atteso almeno un passaggio visibile a occhio nei 7 giorni.");

        // Caso negativo: passaggi completamente illuminati di giorno (Sole sopra -6 gradi) non sono visibili.
        Assert.True(passes.Any(p => p.IlluminatedFraction > 0.99 && !p.IsVisibleToEye),
            "Atteso almeno un passaggio diurno completamente illuminato e non visibile.");

        // Caso in ombra: satellite dietro la Terra, nessun campione illuminato.
        Assert.True(passes.Any(p => p.IlluminatedFraction == 0.0),
            "Atteso almeno un passaggio in ombra terrestre nei 7 giorni.");
        Assert.True(passes.Where(p => p.IlluminatedFraction == 0.0).All(p => !p.IsVisibleToEye),
            "Un passaggio in ombra non può essere visibile.");
    }

    [Fact]
    public void Opzioni_non_valide_vengono_rifiutate()
    {
        var orbit = new CircularOrbitPropagator(T0);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            PassFinder.Find(orbit, TorinoLat, TorinoLon, TorinoAltM, T0, TimeSpan.Zero);
        });

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            PassFinder.Find(orbit, TorinoLat, TorinoLon, TorinoAltM, T0, TimeSpan.FromHours(1),
                new PassOptions { Step = TimeSpan.FromMilliseconds(100) });
        });

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            PassFinder.Find(orbit, 91.0, TorinoLon, TorinoAltM, T0, TimeSpan.FromHours(1));
        });
    }
}
