namespace AllSky.Studio;

/// <summary>
/// Lettore EXIF minimo per i JPEG: legge DateTimeOriginal (0x9003, nel sotto-IFD Exif) o, in ripiego,
/// DateTime (0x0132, in IFD0). Legge solo i primi 64 KB e non lancia mai eccezioni: un file troncato
/// o corrotto non dà alcun valore.
/// <para>
/// Il valore è l'orario locale così come scritto: l'EXIF non ha fuso orario, che si applica dopo
/// con <see cref="LocalTime"/>.
/// </para>
/// </summary>
internal static class ExifDateReader
{
    /// <summary>Quanti byte iniziali del file vengono letti.</summary>
    internal const int MaxBytes = 64 * 1024;

    private const ushort TagDateTime = 0x0132;
    private const ushort TagExifIfdPointer = 0x8769;
    private const ushort TagDateTimeOriginal = 0x9003;
    private const ushort TypeAscii = 2;
    private const int EntrySize = 12;
    private const int MaxSegments = 256;
    private const int DateLength = 19;

    /// <summary>Legge la data dal file. Restituisce false per file mancanti, illeggibili, senza EXIF o corrotti.</summary>
    internal static bool TryReadFile(string path, out DateTime local)
    {
        local = default;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[MaxBytes];
            int filled = 0;
            while (filled < buffer.Length)
            {
                int read = stream.Read(buffer, filled, buffer.Length - filled);
                if (read == 0)
                {
                    break;
                }

                filled += read;
            }

            return TryReadBuffer(buffer, filled, out local);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Cerca il segmento APP1 Exif in un JPEG già caricato in memoria.</summary>
    /// <param name="buffer">Byte iniziali del file.</param>
    /// <param name="length">Quanti byte di <paramref name="buffer"/> sono validi.</param>
    /// <param name="local">Data trovata, senza fuso.</param>
    internal static bool TryReadBuffer(byte[] buffer, int length, out DateTime local)
    {
        local = default;
        if (length < 4 || buffer[0] != 0xFF || buffer[1] != 0xD8)
        {
            return false;
        }

        int pos = 2;
        for (int segment = 0; segment < MaxSegments && pos < length; segment++)
        {
            if (buffer[pos] != 0xFF)
            {
                return false;
            }

            while (pos < length && buffer[pos] == 0xFF)
            {
                pos++; // byte di riempimento
            }

            if (pos >= length)
            {
                return false;
            }

            byte marker = buffer[pos++];
            if (marker == 0xD9 || marker == 0xDA)
            {
                return false; // EOI o SOS: l'EXIF sta prima dei dati immagine
            }

            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                continue; // marker senza lunghezza
            }

            if (pos + 2 > length)
            {
                return false;
            }

            int segmentLength = (buffer[pos] << 8) | buffer[pos + 1];
            if (segmentLength < 2)
            {
                return false;
            }

            int dataStart = pos + 2;
            int dataEnd = pos + segmentLength;
            if (dataEnd > length)
            {
                return false; // segmento troncato
            }

            if (marker == 0xE1
                && segmentLength - 2 >= 6
                && buffer.AsSpan(dataStart, 6).SequenceEqual("Exif\0\0"u8))
            {
                return TryParseTiff(buffer, dataStart + 6, dataEnd, out local);
            }

            pos = dataEnd;
        }

        return false;
    }

    private static bool TryParseTiff(byte[] buffer, int start, int end, out DateTime local)
    {
        local = default;
        if (end - start < 8)
        {
            return false;
        }

        bool bigEndian;
        if (buffer[start] == 'I' && buffer[start + 1] == 'I')
        {
            bigEndian = false;
        }
        else if (buffer[start] == 'M' && buffer[start + 1] == 'M')
        {
            bigEndian = true;
        }
        else
        {
            return false;
        }

        if (ReadU16(buffer, start + 2, end, bigEndian) != 42)
        {
            return false;
        }

        long ifd0Offset = ReadU32(buffer, start + 4, end, bigEndian);
        if (ifd0Offset < 0)
        {
            return false;
        }

        long ifd0 = start + ifd0Offset;

        if (TryFindEntry(buffer, start, end, bigEndian, ifd0, TagExifIfdPointer, out int pointerEntry))
        {
            long exifIfdOffset = ReadU32(buffer, pointerEntry + 8, end, bigEndian);
            if (exifIfdOffset >= 0
                && TryFindEntry(buffer, start, end, bigEndian, start + exifIfdOffset, TagDateTimeOriginal, out int originalEntry)
                && TryReadAsciiDate(buffer, start, end, bigEndian, originalEntry, out local))
            {
                return true;
            }
        }

        return TryFindEntry(buffer, start, end, bigEndian, ifd0, TagDateTime, out int dateTimeEntry)
            && TryReadAsciiDate(buffer, start, end, bigEndian, dateTimeEntry, out local);
    }

    /// <summary>Cerca una voce IFD per tag. Un IFD che esce dal segmento non produce risultati.</summary>
    private static bool TryFindEntry(byte[] buffer, int start, int end, bool bigEndian, long ifd, ushort tag, out int entry)
    {
        entry = 0;
        if (ifd < start || ifd + 2 > end)
        {
            return false;
        }

        long count = ReadU16(buffer, ifd, end, bigEndian);
        long first = ifd + 2;
        if (count < 0 || first + count * EntrySize > end)
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            long candidate = first + (long)i * EntrySize;
            if (ReadU16(buffer, candidate, end, bigEndian) == tag)
            {
                entry = (int)candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryReadAsciiDate(byte[] buffer, int start, int end, bool bigEndian, int entry, out DateTime local)
    {
        local = default;
        if (ReadU16(buffer, entry + 2, end, bigEndian) != TypeAscii)
        {
            return false;
        }

        long count = ReadU32(buffer, entry + 4, end, bigEndian);
        if (count < DateLength || count > 64)
        {
            return false;
        }

        long valueOffset = ReadU32(buffer, entry + 8, end, bigEndian);
        if (valueOffset < 0)
        {
            return false;
        }

        long valuePos = start + valueOffset;
        if (valuePos < start || valuePos + DateLength > end)
        {
            return false;
        }

        Span<char> text = stackalloc char[DateLength];
        for (int i = 0; i < DateLength; i++)
        {
            text[i] = (char)buffer[valuePos + i];
        }

        return TryParseExifDate(text, out local);
    }

    /// <summary>Formato EXIF rigoroso: "AAAA:MM:GG HH:MM:SS".</summary>
    private static bool TryParseExifDate(ReadOnlySpan<char> s, out DateTime local)
    {
        local = default;
        if (s.Length != DateLength || s[4] != ':' || s[7] != ':' || s[10] != ' ' || s[13] != ':' || s[16] != ':')
        {
            return false;
        }

        if (!TryDigits(s[0..4], out int year)
            || !TryDigits(s[5..7], out int month)
            || !TryDigits(s[8..10], out int day)
            || !TryDigits(s[11..13], out int hour)
            || !TryDigits(s[14..16], out int minute)
            || !TryDigits(s[17..19], out int second))
        {
            return false;
        }

        if (year < 1 || month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)
            || hour > 23 || minute > 59 || second > 59)
        {
            return false;
        }

        local = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
        return true;
    }

    private static bool TryDigits(ReadOnlySpan<char> s, out int value)
    {
        value = 0;
        foreach (char c in s)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }

            value = (value * 10) + (c - '0');
        }

        return s.Length > 0;
    }

    /// <summary>Legge un intero a 16 bit se sta tutto prima di <paramref name="end"/>; altrimenti -1.</summary>
    private static long ReadU16(byte[] buffer, long pos, long end, bool bigEndian)
    {
        if (pos < 0 || pos + 2 > end)
        {
            return -1;
        }

        int a = buffer[pos];
        int b = buffer[pos + 1];
        return bigEndian ? (a << 8) | b : (b << 8) | a;
    }

    /// <summary>Legge un intero a 32 bit se sta tutto prima di <paramref name="end"/>; altrimenti -1.</summary>
    private static long ReadU32(byte[] buffer, long pos, long end, bool bigEndian)
    {
        if (pos < 0 || pos + 4 > end)
        {
            return -1;
        }

        long b0 = buffer[pos];
        long b1 = buffer[pos + 1];
        long b2 = buffer[pos + 2];
        long b3 = buffer[pos + 3];
        return bigEndian
            ? (b0 << 24) | (b1 << 16) | (b2 << 8) | b3
            : (b3 << 24) | (b2 << 16) | (b1 << 8) | b0;
    }
}
