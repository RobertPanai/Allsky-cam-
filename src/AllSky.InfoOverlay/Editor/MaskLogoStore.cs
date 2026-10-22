using System.Text;

namespace AllSky.InfoOverlay.Editor;

/// <summary>Motivo per cui un logo non è stato importato.</summary>
public enum MaskLogoErrore
{
    Nessuno,
    FileMancante,
    EstensioneNonPng,
    TroppoGrande,
    NonLeggibile,
    FirmaNonPng,
    DimensioniTroppoGrandi,
    NomeNonValido,
    ScritturaFallita,
}

/// <summary>Esito di un'importazione: il nome del file copiato nella cartella loghi, oppure l'errore.</summary>
public sealed record MaskLogoImport(MaskLogoErrore Errore, string? NomeFile)
{
    public bool Ok => Errore == MaskLogoErrore.Nessuno;
}

/// <summary>
/// Gestione dei loghi PNG nella cartella loghi della cartella dati. Il layout conserva solo il nome del file: l'app non
/// legge mai un logo da un percorso esterno, quindi la copia è l'unico ingresso. Nessuna dipendenza da WPF.
/// </summary>
public static class MaskLogoStore
{
    /// <summary>Dimensione massima del file sorgente, in byte.</summary>
    public const long DimensioneMassimaByte = 2 * 1024 * 1024;

    /// <summary>Lato massimo in pixel, sia larghezza sia altezza.</summary>
    public const int LatoMassimoPx = 4000;

    private const int LunghezzaMassimaBase = 60;
    private const string Estensione = ".png";
    private const string NomeBasePredefinito = "logo";

    private static readonly byte[] FirmaPng = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>
    /// Copia un PNG nella cartella loghi. Controlla estensione, dimensione, firma e dimensioni dell'immagine.
    /// Il nome di destinazione deriva dal nome sorgente, ripulito; se esiste già si aggiunge un suffisso numerico.
    /// </summary>
    /// <param name="sorgente">File scelto dall'utente. Viene solo letto.</param>
    /// <param name="cartellaLoghi">Cartella dei loghi dentro la cartella dati.</param>
    public static MaskLogoImport Importa(string sorgente, string cartellaLoghi)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sorgente);
        ArgumentException.ThrowIfNullOrWhiteSpace(cartellaLoghi);

        if (!File.Exists(sorgente))
        {
            return Errore(MaskLogoErrore.FileMancante);
        }

        if (!string.Equals(Path.GetExtension(sorgente), Estensione, StringComparison.OrdinalIgnoreCase))
        {
            return Errore(MaskLogoErrore.EstensioneNonPng);
        }

        if (new FileInfo(sorgente).Length > DimensioneMassimaByte)
        {
            return Errore(MaskLogoErrore.TroppoGrande);
        }

        byte[] dati;
        try
        {
            dati = File.ReadAllBytes(sorgente);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Errore(MaskLogoErrore.NonLeggibile);
        }

        MaskLogoErrore? problemaPng = ValutaPng(dati);
        if (problemaPng is not null)
        {
            return Errore(problemaPng.Value);
        }

        string nome = NomeDisponibile(cartellaLoghi, Path.GetFileNameWithoutExtension(sorgente));
        if (!OverlayWidgetFormats.FileLogoValido(nome))
        {
            return Errore(MaskLogoErrore.NomeNonValido);
        }

        try
        {
            Directory.CreateDirectory(cartellaLoghi);
            using var flusso = new FileStream(Path.Combine(cartellaLoghi, nome), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            flusso.Write(dati, 0, dati.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Errore(MaskLogoErrore.ScritturaFallita);
        }

        return new MaskLogoImport(MaskLogoErrore.Nessuno, nome);
    }

    /// <summary>Nomi dei PNG nella cartella loghi, in ordine alfabetico. Cartella assente: elenco vuoto.</summary>
    public static IReadOnlyList<string> Elenco(string cartellaLoghi)
    {
        if (!Directory.Exists(cartellaLoghi))
        {
            return Array.Empty<string>();
        }

        // Il filtro sull'estensione è in codice: il pattern del filesystem non è sempre sensibile alle maiuscole.
        return Directory.EnumerateFiles(cartellaLoghi, "*", SearchOption.TopDirectoryOnly)
            .Select(percorso => Path.GetFileName(percorso))
            .Where(nome => OverlayWidgetFormats.FileLogoValido(nome))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Elimina i PNG della cartella loghi che nessun widget usa più. Non tocca gli altri file. Un file che non si
    /// riesce a cancellare viene saltato.
    /// </summary>
    /// <returns>Nomi dei file eliminati.</returns>
    public static IReadOnlyList<string> PulisciNonUsati(string cartellaLoghi, IEnumerable<string> nomiUsati)
    {
        ArgumentNullException.ThrowIfNull(nomiUsati);
        if (!Directory.Exists(cartellaLoghi))
        {
            return Array.Empty<string>();
        }

        var usati = new HashSet<string>(nomiUsati, StringComparer.OrdinalIgnoreCase);
        var eliminati = new List<string>();
        foreach (string nome in Elenco(cartellaLoghi))
        {
            if (usati.Contains(nome))
            {
                continue;
            }

            try
            {
                File.Delete(Path.Combine(cartellaLoghi, nome));
                eliminati.Add(nome);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Il file resta: non è un errore di salvataggio.
            }
        }

        return eliminati;
    }

    /// <summary>Nome base ripulito, senza caratteri non ammessi, con suffisso se il file esiste già.</summary>
    internal static string NomeDisponibile(string cartella, string nomeSorgente)
    {
        string baseNome = NomeBase(nomeSorgente);
        string candidato = baseNome + Estensione;
        int n = 2;
        while (File.Exists(Path.Combine(cartella, candidato)))
        {
            candidato = $"{baseNome}-{n}{Estensione}";
            n++;
        }

        return candidato;
    }

    /// <summary>Solo lettere, cifre, trattino e underscore. Il resto diventa "_". Lunghezza massima 60.</summary>
    internal static string NomeBase(string nome)
    {
        var sb = new StringBuilder(nome.Length);
        foreach (char c in nome)
        {
            sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        }

        string pulito = sb.ToString().Trim('_');
        if (pulito.Length == 0)
        {
            return NomeBasePredefinito;
        }

        return pulito.Length > LunghezzaMassimaBase ? pulito[..LunghezzaMassimaBase] : pulito;
    }

    /// <summary>Controlla la firma PNG e la dimensione dichiarata nel primo blocco IHDR. Null se va bene.</summary>
    internal static MaskLogoErrore? ValutaPng(byte[] dati)
    {
        if (dati.Length < 24 || !dati.AsSpan(0, FirmaPng.Length).SequenceEqual(FirmaPng))
        {
            return MaskLogoErrore.FirmaNonPng;
        }

        // Il primo blocco deve essere IHDR (lunghezza 13 a offset 8, tipo a offset 12).
        if (dati[12] != (byte)'I' || dati[13] != (byte)'H' || dati[14] != (byte)'D' || dati[15] != (byte)'R')
        {
            return MaskLogoErrore.NonLeggibile;
        }

        uint larghezza = LeggiBigEndian(dati, 16);
        uint altezza = LeggiBigEndian(dati, 20);
        if (larghezza == 0 || altezza == 0)
        {
            return MaskLogoErrore.NonLeggibile;
        }

        if (larghezza > LatoMassimoPx || altezza > LatoMassimoPx)
        {
            return MaskLogoErrore.DimensioniTroppoGrandi;
        }

        return null;
    }

    private static uint LeggiBigEndian(byte[] dati, int offset) =>
        (uint)(dati[offset] << 24 | dati[offset + 1] << 16 | dati[offset + 2] << 8 | dati[offset + 3]);

    private static MaskLogoImport Errore(MaskLogoErrore errore) => new(errore, null);
}
