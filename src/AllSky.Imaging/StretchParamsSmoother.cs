namespace AllSky.Imaging;

/// <summary>
/// Media mobile esponenziale dei parametri di stretch (neri per canale, bianco, midtone).
/// alpha e' il peso del NUOVO campione: s = alpha * nuovo + (1 - alpha) * precedente.
/// Il primo Update restituisce i parametri invariati.
/// La combinazione convessa mantiene valido il vincolo White - Black &gt;= 1 e il midtone in (0,1).
/// </summary>
public sealed class StretchParamsSmoother
{
    private const float FullScale = 65535f;
    private const float MaxBlack = 65534f;

    private readonly double _alpha;
    private StretchParams? _state;

    /// <summary>Crea lo smoother. alpha deve essere in (0, 1].</summary>
    public StretchParamsSmoother(double alpha)
    {
        if (!(alpha > 0.0 && alpha <= 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(alpha), "alpha deve essere in (0, 1].");
        }

        _alpha = alpha;
    }

    /// <summary>Aggiorna con i parametri nuovi e restituisce quelli levigati.</summary>
    public StretchParams Update(StretchParams parameters)
    {
        return Blend(parameters, _alpha);
    }

    /// <summary>
    /// Come <see cref="Update(StretchParams)"/>, con un alpha proprio per questa chiamata. alpha deve essere in (0, 1].
    /// Non cambia l'alpha dello smoother.
    /// </summary>
    public StretchParams Update(StretchParams parameters, double alpha)
    {
        if (!(alpha > 0.0 && alpha <= 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(alpha), "alpha deve essere in (0, 1].");
        }

        return Blend(parameters, alpha);
    }

    /// <summary>
    /// Riscala lo stato per il rapporto fra il segnale nuovo e quello precedente (per esempio esposizione · guadagno).
    /// Moltiplica neri e bianco; il midtone non cambia. Il bianco si alza se serve a mantenere White - Black &gt;= 1
    /// su ogni canale, e tutti i valori restano in 0..65535. Senza stato non fa nulla.
    /// </summary>
    /// <param name="ratio">Rapporto nuovo / precedente. Finito e maggiore di zero.</param>
    public void Rescale(double ratio)
    {
        if (!(ratio > 0.0 && double.IsFinite(ratio)))
        {
            throw new ArgumentOutOfRangeException(nameof(ratio), "Il rapporto deve essere finito e maggiore di zero.");
        }

        if (!_state.HasValue)
        {
            return;
        }

        StretchParams p = _state.Value;
        float blackR = ScaleBlack(p.BlackR, ratio);
        float blackG = ScaleBlack(p.BlackG, ratio);
        float blackB = ScaleBlack(p.BlackB, ratio);
        float maxBlack = Math.Max(blackR, Math.Max(blackG, blackB));
        float white = (float)Math.Clamp(p.White * ratio, 1.0, FullScale);
        white = Math.Min(FullScale, Math.Max(white, maxBlack + 1f));
        _state = new StretchParams(blackR, blackG, blackB, white, p.Midtone);
    }

    private static float ScaleBlack(float black, double ratio)
    {
        return (float)Math.Clamp(black * ratio, 0.0, MaxBlack);
    }

    private StretchParams Blend(StretchParams parameters, double alpha)
    {
        if (!_state.HasValue)
        {
            _state = parameters;
            return parameters;
        }

        StretchParams prev = _state.Value;
        var smoothed = new StretchParams(
            Mix(parameters.BlackR, prev.BlackR, alpha),
            Mix(parameters.BlackG, prev.BlackG, alpha),
            Mix(parameters.BlackB, prev.BlackB, alpha),
            Mix(parameters.White, prev.White, alpha),
            Mix(parameters.Midtone, prev.Midtone, alpha));
        _state = smoothed;
        return smoothed;
    }

    private static float Mix(float fresh, float previous, double alpha)
    {
        return (float)(alpha * fresh + (1.0 - alpha) * previous);
    }
}
