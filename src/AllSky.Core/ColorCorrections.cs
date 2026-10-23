namespace AllSky.Core;

/// <summary>
/// Correzioni temporali del colore del timelapse (P14.2). Sono spente di default: con la sezione assente
/// (configurazioni precedenti) il comportamento resta quello di sempre.
/// <list type="bullet">
/// <item><see cref="TimeConstantSmoothing"/>: alpha dello smoothing dei guadagni WB e dello stretch dipendente dal tempo
/// reale fra i frame, con costante di tempo che passa da <see cref="TauDaySeconds"/> (Sole alto) a
/// <see cref="TauNightSeconds"/> (Sole sotto -12 gradi), interpolata come il blend del crepuscolo.
/// Con questa correzione accesa <c>Color.Smoothing</c> non ha effetto.</item>
/// <item><see cref="ExposureCompensation"/>: quando cambia il prodotto esposizione · guadagno lineare, lo stato dello
/// stretch (neri e bianco) si riscala per il rapporto prima della media mobile.</item>
/// </list>
/// <see cref="Preset"/> diverso da "personalizzato" sovrascrive i campi di sopra (vedi <see cref="ColorCorrectionPresets"/>).
/// </summary>
public sealed record ColorCorrections
{
    /// <summary>Valore di <see cref="Preset"/> che usa i campi così come sono scritti.</summary>
    public const string CustomPreset = "personalizzato";

    /// <summary>Attiva lo smoothing con alpha = 1 - exp(-dt / tau), dt = tempo reale fra frame consecutivi.</summary>
    public bool TimeConstantSmoothing { get; init; } = false;

    /// <summary>Costante di tempo di notte, in secondi. Intervallo (0, 3600].</summary>
    public double TauNightSeconds { get; init; } = 180.0;

    /// <summary>Costante di tempo di giorno, in secondi. Intervallo (0, 3600].</summary>
    public double TauDaySeconds { get; init; } = 20.0;

    /// <summary>Compensa i cambi di esposizione e di guadagno riscalando lo stato dello stretch.</summary>
    public bool ExposureCompensation { get; init; } = false;

    /// <summary>
    /// Decibel per unità di guadagno della camera. ASSUNZIONE [NV]: unità di gain ZWO = 0,1 dB. Da verificare sulla
    /// documentazione del driver. Intervallo [0, 1].
    /// </summary>
    public double GainDbPerUnit { get; init; } = 0.1;

    /// <summary>"naturale", "neutro", "profondo" o "personalizzato".</summary>
    public string Preset { get; init; } = CustomPreset;
}

/// <summary>Valori effettivi delle correzioni, dopo la risoluzione del preset.</summary>
/// <param name="TimeConstantSmoothing">Smoothing con alpha dipendente dal tempo.</param>
/// <param name="TauNightSeconds">Costante di tempo di notte, in secondi.</param>
/// <param name="TauDaySeconds">Costante di tempo di giorno, in secondi.</param>
/// <param name="ExposureCompensation">Compensazione dei cambi di esposizione e guadagno.</param>
/// <param name="GainDbPerUnit">Decibel per unità di guadagno.</param>
/// <param name="Saturation">Saturazione dello stretch da usare.</param>
public sealed record ResolvedColorCorrections(
    bool TimeConstantSmoothing,
    double TauNightSeconds,
    double TauDaySeconds,
    bool ExposureCompensation,
    double GainDbPerUnit,
    double Saturation)
{
    /// <summary>True se almeno una correzione è accesa.</summary>
    public bool Enabled => TimeConstantSmoothing || ExposureCompensation;
}

/// <summary>
/// Risoluzione dei preset di <see cref="ColorCorrections"/>. Funzione pura: non modifica la configurazione salvata.
/// Scelte di design [S], non misure:
/// <list type="bullet">
/// <item>«naturale»: entrambe le correzioni accese, costanti di tempo di default (180 s notte, 20 s giorno), saturazione
/// della configurazione.</item>
/// <item>«neutro»: entrambe accese, costanti più lunghe (300 s notte, 40 s giorno), saturazione 1,0.</item>
/// <item>«profondo»: entrambe accese, costanti di default, saturazione 1,25.</item>
/// <item>Il preset non cambia <see cref="ColorCorrections.GainDbPerUnit"/>: è un dato della camera, non una scelta estetica.</item>
/// <item>Un valore sconosciuto di <see cref="ColorCorrections.Preset"/> si tratta come «personalizzato» (la validazione lo segnala).</item>
/// </list>
/// </summary>
public static class ColorCorrectionPresets
{
    /// <summary>Nomi dei preset riconosciuti, in minuscolo.</summary>
    public static IReadOnlyList<string> KnownNames { get; } = ["naturale", "neutro", "profondo", ColorCorrections.CustomPreset];

    /// <summary>Valori di default della costante di tempo di notte, in secondi.</summary>
    public const double DefaultTauNightSeconds = 180.0;

    /// <summary>Valori di default della costante di tempo di giorno, in secondi.</summary>
    public const double DefaultTauDaySeconds = 20.0;

    /// <summary>
    /// Restituisce i valori effettivi. Con <paramref name="corrections"/> null (sezione assente) tutto è spento e la
    /// saturazione è quella della configurazione.
    /// </summary>
    /// <param name="corrections">Sezione <c>color.corrections</c>, oppure null.</param>
    /// <param name="configuredSaturation">Saturazione di <c>color</c>.</param>
    public static ResolvedColorCorrections Resolve(ColorCorrections? corrections, double configuredSaturation)
    {
        if (corrections is null)
        {
            return new ResolvedColorCorrections(
                TimeConstantSmoothing: false,
                TauNightSeconds: DefaultTauNightSeconds,
                TauDaySeconds: DefaultTauDaySeconds,
                ExposureCompensation: false,
                GainDbPerUnit: new ColorCorrections().GainDbPerUnit,
                Saturation: configuredSaturation);
        }

        return corrections.Preset switch
        {
            "naturale" => new ResolvedColorCorrections(true, DefaultTauNightSeconds, DefaultTauDaySeconds, true, corrections.GainDbPerUnit, configuredSaturation),
            "neutro" => new ResolvedColorCorrections(true, 300.0, 40.0, true, corrections.GainDbPerUnit, 1.0),
            "profondo" => new ResolvedColorCorrections(true, DefaultTauNightSeconds, DefaultTauDaySeconds, true, corrections.GainDbPerUnit, 1.25),
            _ => new ResolvedColorCorrections(
                corrections.TimeConstantSmoothing,
                corrections.TauNightSeconds,
                corrections.TauDaySeconds,
                corrections.ExposureCompensation,
                corrections.GainDbPerUnit,
                configuredSaturation),
        };
    }
}
