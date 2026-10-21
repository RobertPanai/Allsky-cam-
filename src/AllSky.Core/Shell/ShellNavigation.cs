namespace AllSky.Core.Shell;

/// <summary>
/// Ordine delle viste nella barra laterale e scorciatoie da tastiera. Logica pura, senza WPF.
/// </summary>
/// <remarks>
/// Ctrl+1..Ctrl+6 raggiungono le sei viste della <see cref="ShortcutOrder"/>. "Stanotte" non ha una scorciatoia
/// numerica: si raggiunge con il mouse, con le frecce o con Tab.
/// </remarks>
public static class ShellNavigation
{
    private static readonly ShellView[] Sidebar =
    [
        ShellView.Tonight,
        ShellView.Live,
        ShellView.Camera,
        ShellView.Nights,
        ShellView.Publication,
        ShellView.Settings,
        ShellView.Log,
    ];

    private static readonly ShellView[] Shortcuts =
    [
        ShellView.Live,
        ShellView.Camera,
        ShellView.Nights,
        ShellView.Publication,
        ShellView.Settings,
        ShellView.Log,
    ];

    /// <summary>Viste nell'ordine della barra laterale, dall'alto in basso.</summary>
    public static IReadOnlyList<ShellView> SidebarOrder => Sidebar;

    /// <summary>Viste con scorciatoia Ctrl+1..Ctrl+6, in quest'ordine.</summary>
    public static IReadOnlyList<ShellView> ShortcutOrder => Shortcuts;

    /// <summary>Vista mostrata all'avvio.</summary>
    public static ShellView Default => ShellView.Tonight;

    /// <summary>Traduce il numero di una scorciatoia (1..6) nella vista corrispondente.</summary>
    /// <param name="number">Numero premuto con Ctrl.</param>
    /// <param name="view">Vista corrispondente, o <see cref="Default"/> se il numero non ha una vista.</param>
    /// <returns>True se il numero ha una vista.</returns>
    public static bool TryGetShortcut(int number, out ShellView view)
    {
        if (number >= 1 && number <= Shortcuts.Length)
        {
            view = Shortcuts[number - 1];
            return true;
        }

        view = Default;
        return false;
    }

    /// <summary>Numero della scorciatoia della vista (1..6), oppure 0 se la vista non ne ha una.</summary>
    /// <param name="view">Vista da cercare.</param>
    public static int ShortcutNumber(ShellView view)
    {
        int index = Array.IndexOf(Shortcuts, view);
        return index < 0 ? 0 : index + 1;
    }

    /// <summary>
    /// Vista successiva (<paramref name="step"/> positivo) o precedente (negativo) nella barra laterale.
    /// Arrivati in fondo si riparte dall'inizio, e viceversa.
    /// </summary>
    /// <param name="current">Vista attuale.</param>
    /// <param name="step">Passo: +1 o -1.</param>
    public static ShellView Move(ShellView current, int step)
    {
        int index = Array.IndexOf(Sidebar, current);
        if (index < 0)
        {
            return Default;
        }

        int count = Sidebar.Length;
        int next = ((index + step) % count + count) % count;
        return Sidebar[next];
    }
}
