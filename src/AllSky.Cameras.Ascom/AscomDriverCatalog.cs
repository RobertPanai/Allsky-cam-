using System.Runtime.Versioning;
using Microsoft.Win32;

namespace AllSky.Cameras.Ascom;

/// <summary>Driver camera ASCOM registrato nel profilo di sistema.</summary>
/// <param name="ProgId">ProgID COM del driver (nome della sottochiave nel registro).</param>
/// <param name="Description">Descrizione del driver; se assente nel registro coincide con il ProgID.</param>
public sealed record AscomDriverInfo(string ProgId, string Description);

/// <summary>Vista del registro da leggere: nativa (64 bit) oppure Wow6432Node (driver a 32 bit).</summary>
public enum RegistryViewKind
{
    Native,
    Wow6432
}

/// <summary>Una sottochiave del registro con il valore predefinito (null se assente).</summary>
/// <param name="Name">Nome della sottochiave.</param>
/// <param name="DefaultValue">Valore predefinito della sottochiave, se presente.</param>
public sealed record RegistrySubKey(string Name, string? DefaultValue);

/// <summary>Accesso di sola lettura al registro, iniettabile per i test.</summary>
public interface IRegistryReader
{
    /// <summary>
    /// Elenca le sottochiavi dirette di HKEY_LOCAL_MACHINE\<paramref name="keyPath"/> nella vista indicata.
    /// Restituisce una lista vuota se la chiave non esiste.
    /// </summary>
    /// <param name="view">Vista del registro.</param>
    /// <param name="keyPath">Percorso della chiave, senza la radice HKLM.</param>
    IReadOnlyList<RegistrySubKey> ListSubKeys(RegistryViewKind view, string keyPath);
}

/// <summary>
/// Lettura reale del registro di Windows tramite <see cref="Registry"/>.
/// Usare solo su Windows: il chiamante deve controllare <see cref="OperatingSystem.IsWindows"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRegistryReader : IRegistryReader
{
    /// <inheritdoc />
    public IReadOnlyList<RegistrySubKey> ListSubKeys(RegistryViewKind view, string keyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);

        RegistryView registryView = view == RegistryViewKind.Native ? RegistryView.Registry64 : RegistryView.Registry32;
        using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView);
        using RegistryKey? key = baseKey.OpenSubKey(keyPath);
        if (key is null)
        {
            return [];
        }

        var result = new List<RegistrySubKey>();
        foreach (string name in key.GetSubKeyNames())
        {
            using RegistryKey? sub = key.OpenSubKey(name);
            string? defaultValue = sub?.GetValue(null) as string;
            result.Add(new RegistrySubKey(name, defaultValue));
        }

        return result;
    }
}

/// <summary>
/// Elenca i driver camera ASCOM registrati (chiave HKLM\SOFTWARE\ASCOM\Camera Drivers, una sottochiave per ProgID).
/// Legge sia la vista nativa sia Wow6432Node e unisce i risultati, deduplicando per ProgID (senza distinzione di maiuscole).
/// Se una vista non e' leggibile (IOException, UnauthorizedAccessException) viene saltata.
/// Su piattaforme diverse da Windows, senza lettore iniettato, restituisce una lista vuota senza eccezioni.
/// Il percorso della chiave e' un'ipotesi non verificata sul sito ASCOM (il sito e' bloccato da robots.txt).
/// </summary>
public sealed class AscomDriverCatalog
{
    /// <summary>Percorso della chiave dei driver camera, relativo a HKLM.</summary>
    public const string CameraDriversKeyPath = @"SOFTWARE\ASCOM\Camera Drivers";

    private readonly IRegistryReader? _reader;

    /// <summary>Crea il catalogo.</summary>
    /// <param name="reader">Lettore del registro; se null si usa quello di Windows (solo su Windows).</param>
    public AscomDriverCatalog(IRegistryReader? reader = null)
    {
        _reader = reader;
    }

    /// <summary>Restituisce i driver camera, ordinati per ProgID.</summary>
    public IReadOnlyList<AscomDriverInfo> Discover()
    {
        IRegistryReader? reader = _reader ?? CreateDefaultReader();
        if (reader is null)
        {
            return [];
        }

        var found = new Dictionary<string, AscomDriverInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (RegistryViewKind view in new[] { RegistryViewKind.Native, RegistryViewKind.Wow6432 })
        {
            IReadOnlyList<RegistrySubKey> entries;
            try
            {
                entries = reader.ListSubKeys(view, CameraDriversKeyPath);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (RegistrySubKey entry in entries)
            {
                string progId = entry.Name.Trim();
                if (progId.Length == 0)
                {
                    continue;
                }

                string description = string.IsNullOrWhiteSpace(entry.DefaultValue)
                    ? progId
                    : entry.DefaultValue.Trim();

                if (found.TryGetValue(progId, out AscomDriverInfo? existing))
                {
                    // La vista nativa ha la precedenza, ma se non ha una descrizione si usa quella dell'altra vista.
                    if (existing.Description == existing.ProgId && description != progId)
                    {
                        found[progId] = existing with { Description = description };
                    }

                    continue;
                }

                found[progId] = new AscomDriverInfo(progId, description);
            }
        }

        return found.Values
            .OrderBy(d => d.ProgId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IRegistryReader? CreateDefaultReader()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return CreateWindowsReader();
    }

    [SupportedOSPlatform("windows")]
    private static IRegistryReader CreateWindowsReader() => new WindowsRegistryReader();
}
