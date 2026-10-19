namespace AllSky.Imaging;

/// <summary>
/// Media mobile esponenziale dei parametri di stretch (neri per canale, bianco, midtone).
/// alpha e' il peso del NUOVO campione: s = alpha * nuovo + (1 - alpha) * precedente.
/// Il primo Update restituisce i parametri invariati.
/// La combinazione convessa mantiene valido il vincolo White - Black &gt;= 1 e il midtone in (0,1).
/// </summary>
public sealed class StretchParamsSmoother
{
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
        if (!_state.HasValue)
        {
            _state = parameters;
            return parameters;
        }

        StretchParams prev = _state.Value;
        var smoothed = new StretchParams(
            Mix(parameters.BlackR, prev.BlackR),
            Mix(parameters.BlackG, prev.BlackG),
            Mix(parameters.BlackB, prev.BlackB),
            Mix(parameters.White, prev.White),
            Mix(parameters.Midtone, prev.Midtone));
        _state = smoothed;
        return smoothed;
    }

    private float Mix(float fresh, float previous)
    {
        return (float)(_alpha * fresh + (1.0 - _alpha) * previous);
    }
}
