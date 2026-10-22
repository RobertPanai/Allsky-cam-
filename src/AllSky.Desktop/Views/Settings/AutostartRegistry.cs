using System;
using AllSky.Core.Settings;
using Microsoft.Win32;

namespace AllSky.Desktop.Views.Settings;

/// <summary>
/// Avvio con Windows: voce nella chiave HKCU\...\Run dell'utente corrente. Stesso comportamento del Tray: la voce
/// creata con il vecchio nome viene rimossa, così non resta un doppio avvio. Il valore scritto viene da <see cref="AutostartCommand"/>.
/// </summary>
internal static class AutostartRegistry
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Vero se la voce di avvio esiste. Se il registro non è leggibile restituisce falso.</summary>
    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(AutostartCommand.ValueName) is string;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Scrive o rimuove la voce di avvio.</summary>
    /// <param name="enable">True per avviare All Sky Brain con Windows.</param>
    /// <param name="exePath">Percorso dell'eseguibile da registrare.</param>
    /// <exception cref="InvalidOperationException">La chiave di avvio non è accessibile.</exception>
    public static void Set(bool enable, string exePath)
    {
        using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("La chiave di avvio di Windows non è accessibile.");

        key.DeleteValue(AutostartCommand.LegacyValueName, throwOnMissingValue: false);

        if (enable)
        {
            key.SetValue(AutostartCommand.ValueName, AutostartCommand.For(exePath), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(AutostartCommand.ValueName, throwOnMissingValue: false);
        }
    }
}
