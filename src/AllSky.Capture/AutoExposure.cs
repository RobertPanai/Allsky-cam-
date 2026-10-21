using AllSky.Astro;
using System.Globalization;

namespace AllSky.Capture;

/// <summary>
/// Controller puro dell'esposizione automatica. Non legge orologi né camere: riceve la mediana ADU
/// già calcolata dal chiamante e restituisce la nuova impostazione.
/// <para>
/// Modello assunto per il segnale: misura = k * esposizione * 10^(gain/200). Il passo in logaritmo
/// è quindi <c>ln(ratio) * Smoothing</c>, con <c>ratio = target / misura</c> limitato al rapporto
/// massimo per passo (<see cref="AutoExposureOptions.StepLimit"/>, o <see cref="AutoExposureOptions.WideStepLimit"/>
/// se la misura è saturata o quasi zero).
/// </para>
/// <para>
/// Ordine delle leve: quando il segnale deve salire si alza prima l'esposizione fino al massimo, poi il gain;
/// quando deve scendere si riporta prima il gain al minimo, poi si riduce l'esposizione. Il gain quindi
/// cambia solo quando l'esposizione è al suo limite nella direzione richiesta.
/// Di giorno (<see cref="SkyPhase.Day"/>) il controller può solo scendere, mai salire.
/// </para>
/// <para>
/// Non è thread-safe: il chiamante deve sincronizzare l'accesso.
/// </para>
/// </summary>
public sealed class AutoExposure
{
    /// <summary>Logaritmo naturale del segnale per unità di gain: ln(10) / 200.</summary>
    private static readonly double LnSignalPerGainUnit = Math.Log(10.0) / 200.0;

    private readonly AutoExposureOptions _options;
    private readonly int _maxAdu;

    /// <summary>
    /// Crea il controller. L'impostazione iniziale viene limitata ai bordi di <paramref name="options"/>.
    /// </summary>
    /// <param name="options">Parametri del controller.</param>
    /// <param name="maxAdu">Valore massimo dei pixel della camera (saturazione), ad esempio 65535.</param>
    /// <param name="initial">Impostazione iniziale.</param>
    /// <exception cref="ArgumentNullException">Se <paramref name="options"/> è null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Se i parametri non sono validi.</exception>
    public AutoExposure(AutoExposureOptions options, int maxAdu, ExposureSetting initial)
    {
        ArgumentNullException.ThrowIfNull(options);
        Validate(options, maxAdu);

        _options = options;
        _maxAdu = maxAdu;
        Current = new ExposureSetting(
            Math.Clamp(initial.ExposureMs, options.MinExposureMs, options.MaxExposureMs),
            Math.Clamp(initial.Gain, options.MinGain, options.MaxGain));
    }

    /// <summary>Impostazione corrente (ultima restituita da <see cref="Update"/>, o quella iniziale).</summary>
    public ExposureSetting Current { get; private set; }

    /// <summary>Target della mediana ADU: <c>TargetFraction * MaxAdu</c>.</summary>
    public double TargetAdu => _options.TargetFraction * _maxAdu;

    /// <summary>
    /// Calcola la nuova impostazione a partire dalla mediana ADU dell'ultimo frame, che si suppone
    /// catturato con <see cref="Current"/>.
    /// </summary>
    /// <param name="measuredAdu">Mediana ADU misurata sul cielo, tra 0 e MaxAdu.</param>
    /// <param name="phase">Fase del cielo corrente.</param>
    /// <returns>L'impostazione aggiornata (uguale alla precedente se la misura è nella zona morta).</returns>
    /// <exception cref="ArgumentOutOfRangeException">Se <paramref name="measuredAdu"/> è fuori da [0, MaxAdu].</exception>
    public ExposureSetting Update(int measuredAdu, SkyPhase phase)
    {
        if (measuredAdu < 0 || measuredAdu > _maxAdu)
        {
            throw new ArgumentOutOfRangeException(nameof(measuredAdu), measuredAdu, "La mediana ADU deve essere tra 0 e MaxAdu.");
        }

        double target = TargetAdu;
        if (Math.Abs(measuredAdu - target) <= _options.DeadbandFraction * target)
        {
            return Current;
        }

        bool saturated = measuredAdu >= _options.SaturationFraction * _maxAdu;
        bool nearZero = measuredAdu <= _options.NearZeroFraction * _maxAdu;
        double limit = saturated || nearZero ? _options.WideStepLimit : _options.StepLimit;

        // Con misura 0 il rapporto diventerebbe infinito: si usa 1 ADU come minimo.
        double measured = Math.Max(measuredAdu, 1.0);
        double logLimit = Math.Log(limit);
        double logRatio = Math.Clamp(Math.Log(target / measured), -logLimit, logLimit);

        double step = _options.Smoothing * logRatio;
        if (phase == SkyPhase.Day && step > 0.0)
        {
            // Di giorno il controller scende soltanto.
            step = 0.0;
        }

        if (step == 0.0)
        {
            return Current;
        }

        Current = Apply(step);
        return Current;
    }

    /// <summary>
    /// Applica un passo in logaritmo del segnale, distribuendolo tra esposizione e gain secondo l'ordine delle leve.
    /// </summary>
    private ExposureSetting Apply(double step)
    {
        double exposure = Current.ExposureMs;
        double gain = Current.Gain;

        if (step > 0.0)
        {
            // Prima l'esposizione fino al massimo, poi il gain.
            double roomExposure = Math.Max(0.0, Math.Log(_options.MaxExposureMs / exposure));
            double exposureStep = Math.Min(step, roomExposure);
            exposure *= Math.Exp(exposureStep);

            double gainStep = step - exposureStep;
            gain += gainStep / LnSignalPerGainUnit;
        }
        else
        {
            // Prima il gain fino al minimo, poi l'esposizione.
            double roomGain = Math.Max(0.0, (gain - _options.MinGain) * LnSignalPerGainUnit);
            double gainStep = Math.Max(step, -roomGain);
            gain += gainStep / LnSignalPerGainUnit;

            double exposureStep = step - gainStep;
            double roomExposure = Math.Max(0.0, Math.Log(exposure / _options.MinExposureMs));
            exposure *= Math.Exp(Math.Max(exposureStep, -roomExposure));
        }

        return new ExposureSetting(
            Snap(exposure, _options.MinExposureMs, _options.MaxExposureMs),
            Snap(gain, _options.MinGain, _options.MaxGain));
    }

    /// <summary>
    /// Limita <paramref name="value"/> a [min, max]. Un valore a meno di un errore di arrotondamento dal bordo
    /// viene portato esattamente sul bordo, così che "al minimo" sia un confronto esatto.
    /// </summary>
    private static double Snap(double value, double min, double max)
    {
        if (Math.Abs(value - min) <= 1e-9 * Math.Max(1.0, Math.Abs(min)))
        {
            return min;
        }
        if (Math.Abs(value - max) <= 1e-9 * Math.Max(1.0, Math.Abs(max)))
        {
            return max;
        }
        return Math.Clamp(value, min, max);
    }

    /// <summary>Formato invariante per i messaggi d'errore.</summary>
    private static string Fmt(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static void Validate(AutoExposureOptions options, int maxAdu)
    {
        if (maxAdu < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAdu), maxAdu, "MaxAdu deve essere almeno 2.");
        }
        if (!(options.MinExposureMs > 0.0) || !(options.MaxExposureMs >= options.MinExposureMs))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"Esposizione: serve 0 < min <= max. Ricevuti: min={Fmt(options.MinExposureMs)} max={Fmt(options.MaxExposureMs)}.");
        }
        if (!(options.MaxGain >= options.MinGain))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"Gain: serve min <= max. Ricevuti: min={Fmt(options.MinGain)} max={Fmt(options.MaxGain)}.");
        }
        if (!(options.TargetFraction > 0.0 && options.TargetFraction < 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "TargetFraction deve essere in (0, 1).");
        }
        if (!(options.Smoothing > 0.0 && options.Smoothing <= 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Smoothing deve essere in (0, 1].");
        }
        if (!(options.DeadbandFraction >= 0.0 && options.DeadbandFraction < 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "DeadbandFraction deve essere in [0, 1).");
        }
        if (!(options.StepLimit > 1.0) || !(options.WideStepLimit >= options.StepLimit))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Limiti di passo: serve 1 < StepLimit <= WideStepLimit.");
        }
        if (!(options.SaturationFraction > 0.0 && options.SaturationFraction <= 1.0)
            || !(options.NearZeroFraction >= 0.0 && options.NearZeroFraction < options.SaturationFraction))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Soglie di saturazione e di zero non valide.");
        }
    }
}
