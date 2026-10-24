namespace AllSky.Sky;

/// <summary>
/// Brillanza del cielo stimata (P9.3), in magnitudini per arcsecondo quadrato. Valore <b>stimato</b>: la UI lo etichetta come tale.
/// Il flusso di fondo, misurato dopo aver tolto le stelle, è convertito con il punto zero di P9.2 per unità di area.
/// </summary>
public static class SkyBrightness
{
    /// <summary>
    /// Magnitudine superficiale del fondo: μ = Z − 2,5·log10(F / (t·A)) + offset.
    /// </summary>
    /// <param name="backgroundAdu">Flusso di fondo per pixel, ADU, già depurato dalle stelle.</param>
    /// <param name="exposureSeconds">Durata dell'esposizione, secondi.</param>
    /// <param name="pixelAreaArcsec2">Area di un pixel allo zenit, arcsec².</param>
    /// <param name="zeroPoint">Punto zero per ADU/s/arcsec², da P9.2 (Z).</param>
    /// <param name="offsetMag">Offset di calibrazione facoltativo, magnitudini (confronto con un SQM reale).</param>
    /// <exception cref="ArgumentOutOfRangeException">Se un parametro non è finito o il flusso non è positivo.</exception>
    public static double MagPerArcsec2(
        double backgroundAdu,
        double exposureSeconds,
        double pixelAreaArcsec2,
        double zeroPoint,
        double offsetMag = 0.0)
    {
        if (!(backgroundAdu > 0.0) || !double.IsFinite(backgroundAdu))
        {
            throw new ArgumentOutOfRangeException(nameof(backgroundAdu), backgroundAdu, "Il fondo deve essere finito e maggiore di zero.");
        }

        if (!(exposureSeconds > 0.0) || !double.IsFinite(exposureSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(exposureSeconds), exposureSeconds, "L'esposizione deve essere finita e maggiore di zero.");
        }

        if (!(pixelAreaArcsec2 > 0.0) || !double.IsFinite(pixelAreaArcsec2))
        {
            throw new ArgumentOutOfRangeException(nameof(pixelAreaArcsec2), pixelAreaArcsec2, "L'area del pixel deve essere finita e maggiore di zero.");
        }

        if (!double.IsFinite(zeroPoint) || !double.IsFinite(offsetMag))
        {
            throw new ArgumentOutOfRangeException(nameof(zeroPoint), zeroPoint, "Il punto zero e l'offset devono essere finiti.");
        }

        double fluxPerArcsec2 = backgroundAdu / (exposureSeconds * pixelAreaArcsec2);
        return zeroPoint - 2.5 * Math.Log10(fluxPerArcsec2) + offsetMag;
    }
}
