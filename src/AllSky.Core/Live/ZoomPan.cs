namespace AllSky.Core.Live;

/// <summary>
/// Stato di zoom e spostamento della vista Live. <see cref="Zoom"/> è il fattore sull'immagine adattata alla vista;
/// <see cref="OffsetX"/> e <see cref="OffsetY"/> sono la posizione in pixel WPF dello spigolo in alto a sinistra
/// dell'immagine rispetto alla vista.
/// </summary>
public readonly record struct ZoomPanState(double Zoom, double OffsetX, double OffsetY);

/// <summary>
/// Matematica di zoom e trascinamento per la vista Live. Logica pura: nessuna dipendenza da WPF.
/// </summary>
/// <remarks>
/// <para>L'immagine a zoom 1 occupa il massimo spazio della vista mantenendo le proporzioni (vedi <see cref="FitContent"/>).
/// Con zoom maggiore di 1 può uscire dalla vista, ma il trascinamento non lascia mai scoperti i bordi: se l'immagine
/// è più piccola della vista, resta centrata.</para>
/// <para>Tutte le funzioni restituiscono uno stato già limitato (<see cref="Clamp"/>).</para>
/// </remarks>
public static class ZoomPan
{
    /// <summary>Zoom minimo: l'immagine intera adattata alla vista.</summary>
    public const double MinZoom = 1.0;

    /// <summary>Zoom massimo, come multiplo dell'immagine adattata.</summary>
    public const double MaxZoom = 8.0;

    /// <summary>Fattore di una tacca della rotella del mouse.</summary>
    public const double WheelStep = 1.2;

    /// <summary>Fattore di un clic sui pulsanti + e -.</summary>
    public const double ButtonStep = 1.25;

    /// <summary>Stato iniziale (zoom 1, centrato). Va comunque limitato con <see cref="Clamp"/> prima dell'uso.</summary>
    public static ZoomPanState Reset => new(MinZoom, 0, 0);

    /// <summary>
    /// Dimensione dell'immagine adattata alla vista, con le proporzioni originali. Restituisce (0, 0) se una dimensione
    /// non è positiva o non è finita, così la vista non riceve valori non validi.
    /// </summary>
    /// <param name="imageWidth">Larghezza dell'immagine in pixel.</param>
    /// <param name="imageHeight">Altezza dell'immagine in pixel.</param>
    /// <param name="viewWidth">Larghezza della vista.</param>
    /// <param name="viewHeight">Altezza della vista.</param>
    public static (double Width, double Height) FitContent(double imageWidth, double imageHeight, double viewWidth, double viewHeight)
    {
        if (!IsPositive(imageWidth) || !IsPositive(imageHeight) || !IsPositive(viewWidth) || !IsPositive(viewHeight))
        {
            return (0, 0);
        }

        double scale = Math.Min(viewWidth / imageWidth, viewHeight / imageHeight);
        return (imageWidth * scale, imageHeight * scale);
    }

    /// <summary>
    /// Fattore di zoom di una tacca della rotella. Un delta positivo (rotella in avanti) ingrandisce.
    /// </summary>
    /// <param name="wheelDelta">Delta della rotella, come in WPF (multipli di 120).</param>
    public static double WheelFactor(int wheelDelta) => wheelDelta switch
    {
        > 0 => WheelStep,
        < 0 => 1.0 / WheelStep,
        _ => 1.0,
    };

    /// <summary>Applica un fattore di zoom mantenendo fermo il punto sotto il cursore (o al centro dei pulsanti).</summary>
    /// <param name="state">Stato attuale.</param>
    /// <param name="factor">Fattore moltiplicativo, maggiore di 0.</param>
    /// <param name="anchorX">Ascissa del punto di ancoraggio, nelle coordinate della vista.</param>
    /// <param name="anchorY">Ordinata del punto di ancoraggio, nelle coordinate della vista.</param>
    /// <param name="viewWidth">Larghezza della vista.</param>
    /// <param name="viewHeight">Altezza della vista.</param>
    /// <param name="contentWidth">Larghezza dell'immagine adattata a zoom 1 (vedi <see cref="FitContent"/>).</param>
    /// <param name="contentHeight">Altezza dell'immagine adattata a zoom 1.</param>
    public static ZoomPanState ZoomAt(
        ZoomPanState state,
        double factor,
        double anchorX,
        double anchorY,
        double viewWidth,
        double viewHeight,
        double contentWidth,
        double contentHeight)
    {
        if (!(factor > 0) || !double.IsFinite(factor))
        {
            return Clamp(state, viewWidth, viewHeight, contentWidth, contentHeight);
        }

        double zoom = Math.Clamp(state.Zoom * factor, MinZoom, MaxZoom);
        // Punto dell'immagine (in unità di zoom 1) che sta sotto l'ancora prima dello zoom.
        double imageX = (anchorX - state.OffsetX) / state.Zoom;
        double imageY = (anchorY - state.OffsetY) / state.Zoom;

        var next = new ZoomPanState(zoom, anchorX - imageX * zoom, anchorY - imageY * zoom);
        return Clamp(next, viewWidth, viewHeight, contentWidth, contentHeight);
    }

    /// <summary>Sposta la vista di (<paramref name="deltaX"/>, <paramref name="deltaY"/>) pixel, limitando i bordi.</summary>
    /// <param name="state">Stato attuale.</param>
    /// <param name="deltaX">Spostamento orizzontale in pixel della vista.</param>
    /// <param name="deltaY">Spostamento verticale in pixel della vista.</param>
    /// <param name="viewWidth">Larghezza della vista.</param>
    /// <param name="viewHeight">Altezza della vista.</param>
    /// <param name="contentWidth">Larghezza dell'immagine adattata a zoom 1.</param>
    /// <param name="contentHeight">Altezza dell'immagine adattata a zoom 1.</param>
    public static ZoomPanState PanBy(
        ZoomPanState state,
        double deltaX,
        double deltaY,
        double viewWidth,
        double viewHeight,
        double contentWidth,
        double contentHeight)
    {
        var moved = new ZoomPanState(state.Zoom, state.OffsetX + deltaX, state.OffsetY + deltaY);
        return Clamp(moved, viewWidth, viewHeight, contentWidth, contentHeight);
    }

    /// <summary>
    /// Limita lo zoom a 1–8 e gli spostamenti in modo che l'immagine copra la vista, oppure resti centrata se è più piccola.
    /// </summary>
    /// <param name="state">Stato da limitare.</param>
    /// <param name="viewWidth">Larghezza della vista.</param>
    /// <param name="viewHeight">Altezza della vista.</param>
    /// <param name="contentWidth">Larghezza dell'immagine adattata a zoom 1.</param>
    /// <param name="contentHeight">Altezza dell'immagine adattata a zoom 1.</param>
    public static ZoomPanState Clamp(
        ZoomPanState state,
        double viewWidth,
        double viewHeight,
        double contentWidth,
        double contentHeight)
    {
        double zoom = double.IsFinite(state.Zoom) ? Math.Clamp(state.Zoom, MinZoom, MaxZoom) : MinZoom;
        double offsetX = double.IsFinite(state.OffsetX) ? state.OffsetX : 0;
        double offsetY = double.IsFinite(state.OffsetY) ? state.OffsetY : 0;

        return new ZoomPanState(
            zoom,
            Axis(offsetX, contentWidth * zoom, viewWidth),
            Axis(offsetY, contentHeight * zoom, viewHeight));
    }

    private static double Axis(double offset, double size, double view)
    {
        if (!IsPositive(view) || size <= 0)
        {
            return 0;
        }

        // Immagine più piccola della vista: centrata. Altrimenti il bordo non può entrare nella vista.
        return size <= view
            ? (view - size) / 2
            : Math.Clamp(offset, view - size, 0);
    }

    private static bool IsPositive(double value) => double.IsFinite(value) && value > 0;
}
