namespace AllSky.Core.Tonight;

/// <summary>Controlli sugli argomenti geografici della vista Stanotte (stessi limiti di AllSky.Astro).</summary>
internal static class TonightGuard
{
    public static void CheckLatLon(double latDeg, double lonDeg)
    {
        if (double.IsNaN(latDeg) || latDeg < -90.0 || latDeg > 90.0)
        {
            throw new ArgumentOutOfRangeException(nameof(latDeg), latDeg, "La latitudine deve essere tra -90 e 90 gradi.");
        }
        if (double.IsNaN(lonDeg) || lonDeg < -180.0 || lonDeg > 180.0)
        {
            throw new ArgumentOutOfRangeException(nameof(lonDeg), lonDeg, "La longitudine deve essere tra -180 e 180 gradi (est positivo).");
        }
    }
}
