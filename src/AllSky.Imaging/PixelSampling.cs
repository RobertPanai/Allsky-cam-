namespace AllSky.Imaging;

/// <summary>Campionamento a passo fisso per statistiche (percentili, mediane) su immagini grandi.</summary>
internal static class PixelSampling
{
    /// <summary>Numero massimo di elementi usati per una statistica.</summary>
    public const int MaxSampleSize = 200000;

    /// <summary>
    /// Passo di campionamento: 1 se count &lt;= MaxSampleSize, altrimenti il quoziente arrotondato per eccesso
    /// count / MaxSampleSize. Cosi' il campione non supera mai MaxSampleSize elementi.
    /// </summary>
    public static int Step(long count)
    {
        if (count <= MaxSampleSize)
        {
            return 1;
        }

        return (int)((count + MaxSampleSize - 1) / MaxSampleSize);
    }
}
