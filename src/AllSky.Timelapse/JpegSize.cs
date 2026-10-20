namespace AllSky.Timelapse;

/// <summary>Lettura delle dimensioni di un JPEG dal solo header (marker SOFn), senza decodificarlo.</summary>
public static class JpegSize
{
    /// <summary>
    /// Legge larghezza e altezza dal primo marker SOF. Restituisce false se il file non è un JPEG valido.
    /// </summary>
    public static bool TryRead(string path, out int width, out int height)
    {
        width = 0;
        height = 0;

        using FileStream stream = File.OpenRead(path);
        if (stream.ReadByte() != 0xFF || stream.ReadByte() != 0xD8)
        {
            return false;
        }

        while (true)
        {
            int b = stream.ReadByte();
            while (b != -1 && b != 0xFF)
            {
                b = stream.ReadByte();
            }

            // Riempimento: più 0xFF di seguito prima del marker.
            do
            {
                b = stream.ReadByte();
            }
            while (b == 0xFF);

            if (b == -1)
            {
                return false;
            }

            // Marker senza lunghezza: SOI, TEM, RSTn.
            if (b == 0x01 || b == 0xD8 || (b >= 0xD0 && b <= 0xD7))
            {
                continue;
            }

            // EOI o SOS prima di un SOF: il file non ha dimensioni leggibili.
            if (b == 0xD9 || b == 0xDA)
            {
                return false;
            }

            int lengthHigh = stream.ReadByte();
            int lengthLow = stream.ReadByte();
            if (lengthHigh == -1 || lengthLow == -1)
            {
                return false;
            }

            int segmentLength = (lengthHigh << 8) | lengthLow;

            if (IsStartOfFrame(b))
            {
                // Precisione (1 byte), altezza (2), larghezza (2).
                int precision = stream.ReadByte();
                int h1 = stream.ReadByte();
                int h2 = stream.ReadByte();
                int w1 = stream.ReadByte();
                int w2 = stream.ReadByte();
                if (precision == -1 || h1 == -1 || h2 == -1 || w1 == -1 || w2 == -1)
                {
                    return false;
                }

                height = (h1 << 8) | h2;
                width = (w1 << 8) | w2;
                return width > 0 && height > 0;
            }

            if (segmentLength < 2)
            {
                return false;
            }

            stream.Seek(segmentLength - 2, SeekOrigin.Current);
        }
    }

    /// <summary>SOF0..SOF15 esclusi DHT (C4), JPG (C8) e DAC (CC).</summary>
    private static bool IsStartOfFrame(int marker)
    {
        return marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
    }
}
