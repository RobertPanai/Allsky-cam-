using System.Security.Cryptography;
using System.Text;

namespace AllSky.Core;

/// <summary>
/// Calcolo puro della cartella dati del Tray e del nome del mutex di istanza unica.
/// Nessuna dipendenza da WinForms o da API Windows: il Tray la chiama, i test la eseguono anche su altre piattaforme.
/// Nessun metodo lancia eccezioni: un valore non valido viene ignorato e si passa al livello successivo.
/// </summary>
public static class DataFolderResolver
{
    /// <summary>Argomento da riga di comando. Accetta sia "--data-folder X" sia "--data-folder=X".</summary>
    public const string ArgumentName = "--data-folder";

    /// <summary>Variabile d'ambiente con la cartella dati, usata se l'argomento manca o è vuoto.</summary>
    public const string EnvironmentVariableName = "ALLSKY_DATA_FOLDER";

    /// <summary>Nome base del mutex di istanza unica (prefisso Local\: visibile solo nella sessione utente corrente).</summary>
    public const string BaseSingleInstanceMutexName = @"Local\AllSky.Tray.SingleInstance";

    /// <summary>
    /// Sceglie la cartella dati in quest'ordine: argomento <c>--data-folder</c>, variabile <c>ALLSKY_DATA_FOLDER</c>,
    /// <paramref name="defaultFolder"/>. Valori vuoti o non validi vengono ignorati. I percorsi relativi diventano assoluti.
    /// </summary>
    /// <param name="args">Argomenti della riga di comando (può essere null).</param>
    /// <param name="environmentValue">Valore di ALLSKY_DATA_FOLDER (può essere null).</param>
    /// <param name="defaultFolder">Cartella usata quando nessuna sorgente fornisce un valore valido.</param>
    /// <returns>La cartella dati scelta, sempre non vuota.</returns>
    public static string Resolve(string[]? args, string? environmentValue, string defaultFolder)
    {
        string? fromArgs = ReadArgument(args);
        if (fromArgs is not null)
        {
            return fromArgs;
        }

        string? fromEnvironment = TryMakeAbsolute(environmentValue);
        if (fromEnvironment is not null)
        {
            return fromEnvironment;
        }

        return defaultFolder;
    }

    /// <summary>
    /// Nome del mutex di istanza unica legato alla cartella dati. Per la cartella di default il nome è
    /// <see cref="BaseSingleInstanceMutexName"/> invariato; per le altre cartelle aggiunge un suffisso
    /// "." con i primi 8 caratteri esadecimali dello SHA-256 del percorso normalizzato in minuscolo.
    /// </summary>
    /// <param name="dataFolder">Cartella dati scelta da <see cref="Resolve"/>.</param>
    /// <param name="defaultFolder">Cartella dati di default.</param>
    public static string SingleInstanceMutexName(string dataFolder, string defaultFolder)
    {
        string normalized = NormalizeForIdentity(dataFolder);
        if (normalized == NormalizeForIdentity(defaultFolder))
        {
            return BaseSingleInstanceMutexName;
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        string shortHash = Convert.ToHexString(hash)[..8].ToLowerInvariant();
        return BaseSingleInstanceMutexName + "." + shortHash;
    }

    /// <summary>Primo valore valido tra gli argomenti <c>--data-folder X</c> o <c>--data-folder=X</c>, o null.</summary>
    private static string? ReadArgument(string[]? args)
    {
        if (args is null)
        {
            return null;
        }

        for (int i = 0; i < args.Length; i++)
        {
            string? arg = args[i];
            if (arg is null)
            {
                continue;
            }

            if (arg == ArgumentName)
            {
                // Forma con spazio: il valore è l'argomento successivo. Lo si consuma anche se è vuoto.
                if (i + 1 < args.Length)
                {
                    string? valueFromNext = TryMakeAbsolute(args[i + 1]);
                    i++;
                    if (valueFromNext is not null)
                    {
                        return valueFromNext;
                    }
                }

                continue;
            }

            string prefix = ArgumentName + "=";
            if (arg.StartsWith(prefix, StringComparison.Ordinal))
            {
                string? valueFromEquals = TryMakeAbsolute(arg[prefix.Length..]);
                if (valueFromEquals is not null)
                {
                    return valueFromEquals;
                }
            }
        }

        return null;
    }

    /// <summary>Restituisce il percorso assoluto, o null se il valore è vuoto o non è un percorso valido.</summary>
    private static string? TryMakeAbsolute(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(value);
        }
        catch (Exception)
        {
            // Percorso non valido per il sistema (caratteri vietati, troppo lungo): si ignora, senza eccezioni.
            return null;
        }
    }

    /// <summary>Percorso assoluto, senza separatore finale e in minuscolo: base del confronto e dell'hash.</summary>
    private static string NormalizeForIdentity(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).ToLowerInvariant();
        }
        catch (Exception)
        {
            // Percorso non normalizzabile: si usa la stringa così com'è, in minuscolo.
            return path.ToLowerInvariant();
        }
    }
}
