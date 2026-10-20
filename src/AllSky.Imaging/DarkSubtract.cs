namespace AllSky.Imaging;

/// <summary>Sottrazione del master dark da un frame grezzo.</summary>
public static class DarkSubtract
{
    /// <summary>
    /// Sottrae il master dark in-place. Per ogni pixel: v = max(0, frame - dark), poi v = min(65535, v + pedestal).
    /// Il pedestallo viene aggiunto dopo la sottrazione (e saturato a 65535), cosi' il frame risultante
    /// non scende sotto il pedestallo; un pixel uguale al dark diventa quindi pari a <paramref name="pedestal"/>.
    /// </summary>
    /// <param name="frame">Frame grezzo a 16 bit, modificato sul posto. Deve avere la stessa lunghezza di dark.Data.</param>
    /// <param name="dark">Master dark da sottrarre.</param>
    /// <param name="pedestal">Valore da riaggiungere dopo la sottrazione, tra 0 e 65535.</param>
    public static void Apply(ushort[] frame, MasterDark dark, int pedestal)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(dark);
        if (pedestal < 0 || pedestal > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(pedestal), "Il pedestallo deve essere tra 0 e 65535.");
        }

        ushort[] data = dark.Data;
        if (data is null || frame.Length != data.Length)
        {
            throw new ArgumentException("Il frame e il master dark hanno dimensioni diverse.", nameof(frame));
        }

        for (int i = 0; i < frame.Length; i++)
        {
            int v = frame[i] - data[i];
            if (v < 0)
            {
                v = 0;
            }

            v += pedestal;
            if (v > ushort.MaxValue)
            {
                v = ushort.MaxValue;
            }

            frame[i] = (ushort)v;
        }
    }
}
