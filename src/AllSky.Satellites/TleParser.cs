using System.Globalization;

namespace AllSky.Satellites;

/// <summary>
/// Lettura e validazione del formato TLE (2 o 3 righe). Nessuna eccezione: l'esito è un booleano e un motivo.
/// </summary>
internal static class TleParser
{
    private const int LineLength = 69;

    /// <summary>
    /// Valida un blocco TLE. Controlla lunghezza, checksum modulo 10 di entrambe le righe e numero di catalogo
    /// (coerente con il bersaglio richiesto e tra le due righe).
    /// </summary>
    /// <param name="text">Testo grezzo della risposta.</param>
    /// <param name="target">Bersaglio richiesto.</param>
    /// <param name="fetchedUtc">Istante da assegnare al risultato.</param>
    /// <param name="set">Risultato valido, altrimenti null.</param>
    /// <param name="error">Motivo del rifiuto in italiano, altrimenti null.</param>
    public static bool TryParse(string? text, SatelliteTarget target, DateTimeOffset fetchedUtc, out TleSet? set, out string? error)
    {
        set = null;
        error = null;

        string[] lines = (text ?? string.Empty)
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Trim().Length > 0)
            .ToArray();

        string name;
        string line1;
        string line2;
        if (lines.Length == 3)
        {
            name = lines[0].Trim();
            if (name.StartsWith("0 ", StringComparison.Ordinal))
            {
                name = name[2..].Trim();
            }

            line1 = lines[1];
            line2 = lines[2];
        }
        else if (lines.Length == 2)
        {
            name = target.Name;
            line1 = lines[0];
            line2 = lines[1];
        }
        else
        {
            error = $"attese 2 o 3 righe, trovate {lines.Length}";
            return false;
        }

        if (!line1.StartsWith("1 ", StringComparison.Ordinal) || line1.Length != LineLength)
        {
            error = "riga 1 non valida";
            return false;
        }

        if (!line2.StartsWith("2 ", StringComparison.Ordinal) || line2.Length != LineLength)
        {
            error = "riga 2 non valida";
            return false;
        }

        if (!HasValidChecksum(line1))
        {
            error = "checksum della riga 1 errato";
            return false;
        }

        if (!HasValidChecksum(line2))
        {
            error = "checksum della riga 2 errato";
            return false;
        }

        int catalog1 = ParseCatalogNumber(line1);
        int catalog2 = ParseCatalogNumber(line2);
        if (catalog1 != target.NoradId || catalog2 != target.NoradId)
        {
            error = $"numero di catalogo {catalog1}/{catalog2} diverso da quello richiesto ({target.NoradId})";
            return false;
        }

        set = new TleSet(target.NoradId, name, line1, line2, fetchedUtc);
        return true;
    }

    /// <summary>Checksum modulo 10: cifre sommate, il segno meno vale 1, il resto 0 (prime 68 colonne).</summary>
    internal static int Checksum(string line)
    {
        int sum = 0;
        for (int i = 0; i < LineLength - 1; i++)
        {
            char c = line[i];
            if (char.IsAsciiDigit(c))
            {
                sum += c - '0';
            }
            else if (c == '-')
            {
                sum += 1;
            }
        }

        return sum % 10;
    }

    private static bool HasValidChecksum(string line) =>
        char.IsAsciiDigit(line[LineLength - 1]) && Checksum(line) == line[LineLength - 1] - '0';

    private static int ParseCatalogNumber(string line) =>
        int.TryParse(line.Substring(2, 5).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            ? value
            : -1;
}
