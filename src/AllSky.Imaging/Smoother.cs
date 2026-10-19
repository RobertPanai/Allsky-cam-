namespace AllSky.Imaging;

/// <summary>
/// Media mobile esponenziale dei guadagni.
/// alpha e' il peso del NUOVO campione: s = alpha * nuovo + (1 - alpha) * precedente.
/// L'Host usa alpha = 1 - smoothing. Il primo Update restituisce i guadagni invariati.
/// </summary>
public sealed class Smoother
{
    private readonly double _alpha;
    private (double R, double G, double B)? _state;

    /// <summary>Crea lo smoother. alpha deve essere in (0, 1].</summary>
    public Smoother(double alpha)
    {
        if (!(alpha > 0.0 && alpha <= 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(alpha), "alpha deve essere in (0, 1].");
        }

        _alpha = alpha;
    }

    /// <summary>Aggiorna con nuovi guadagni e restituisce quelli levigati.</summary>
    public (double R, double G, double B) Update((double R, double G, double B) gains)
    {
        if (!_state.HasValue)
        {
            _state = gains;
            return gains;
        }

        var prev = _state.Value;
        var smoothed = (
            R: _alpha * gains.R + (1.0 - _alpha) * prev.R,
            G: _alpha * gains.G + (1.0 - _alpha) * prev.G,
            B: _alpha * gains.B + (1.0 - _alpha) * prev.B);
        _state = smoothed;
        return smoothed;
    }
}
