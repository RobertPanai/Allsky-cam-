namespace AllSky.Core.Settings;

/// <summary>
/// Valore della chiave di avvio automatico di Windows (HKCU\...\Run). La scrittura nel registro sta in AllSky.Desktop;
/// qui c'è solo ciò che va scritto, così si può testare senza Windows.
/// </summary>
public static class AutostartCommand
{
    /// <summary>Nome del valore usato da All Sky Brain.</summary>
    public const string ValueName = "AllSkyBrain";

    /// <summary>Nome usato dalle versioni col vecchio nome: va rimosso per non avere due avvii.</summary>
    public const string LegacyValueName = "AllSky";

    /// <summary>Riga di comando da registrare: il percorso dell'eseguibile tra virgolette.</summary>
    /// <param name="exePath">Percorso completo dell'eseguibile pubblicato.</param>
    /// <exception cref="ArgumentException">Percorso vuoto.</exception>
    public static string For(string exePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        return "\"" + exePath + "\"";
    }
}
