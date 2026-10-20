namespace AllSky.Imaging;

/// <summary>
/// Parametri del cerchio fisheye e dell'eventuale profilo d'orizzonte per <see cref="SkyMask.Create"/>.
/// Proiezione equidistante: r = R * (90 - elev) / 90, con r distanza dal centro in pixel e R raggio in pixel.
/// Lo zenit sta al centro, l'orizzonte sul bordo del cerchio.
/// </summary>
/// <param name="CenterX">Centro orizzontale normalizzato sulla larghezza: 0 = bordo sinistro, 1 = bordo destro.</param>
/// <param name="CenterY">Centro verticale normalizzato sull'altezza: 0 = bordo superiore, 1 = bordo inferiore.</param>
/// <param name="Radius">
/// Raggio normalizzato: 1 = meta' del lato minore dell'immagine (min(W, H) / 2). Deve essere maggiore di zero.
/// </param>
/// <param name="Horizon">
/// Profilo dell'orizzonte come punti (azimutDeg, elevazioneDeg), con azimut 0 = Nord (alto dell'immagine).
/// Tra due punti l'elevazione e' interpolata linearmente, in modo circolare: 360 e 0 sono contigui.
/// Null = orizzonte piatto a 0 gradi. Scelta: l'ordine dei punti non conta, vengono ordinati per azimut.
/// </param>
/// <param name="EastLeft">
/// True (predefinito): Est a sinistra, convenzione delle mappe celesti viste dal basso.
/// False: Est a destra, per immagini gia' specchiate. Decisione da confermare sull'hardware reale.
/// </param>
public sealed record SkyMaskOptions(
    double CenterX,
    double CenterY,
    double Radius,
    IReadOnlyList<(double AzimuthDeg, double ElevationDeg)>? Horizon = null,
    bool EastLeft = true);
