using System;
using System.Windows;
using System.Windows.Media;
using AllSky.Core.Design;

namespace AllSky.Desktop.Theme;

/// <summary>
/// Traduce la <see cref="Palette"/> in risorse WPF e le aggiorna quando cambiano la modalità notte o la luminosità.
/// </summary>
/// <remarks>
/// Risorse scritte nel dizionario indicato (di norma <c>Application.Current.Resources</c>):
/// <list type="bullet">
/// <item><c>Brush.{ruolo}</c>: <see cref="SolidColorBrush"/>, uno per ruolo della palette.</item>
/// <item><c>Color.{ruolo}</c>: <see cref="Color"/>, per gli sfumati che usano un colore dinamico.</item>
/// <item><c>Font.Ui</c>, <c>Font.Mono</c>: <see cref="FontFamily"/> con fallback di sistema.</item>
/// <item><c>Size.*</c> e <c>Space.*</c>: scala tipografica e di spaziatura, come <see cref="double"/>.</item>
/// </list>
/// Il cambio di tema non sostituisce i pennelli già esistenti: ne modifica il colore. Così ogni controllo che
/// usa <c>DynamicResource</c> si aggiorna senza essere ricreato. Tutte le chiamate vanno fatte sul thread dell'interfaccia.
/// </remarks>
public sealed class ThemeManager
{
    /// <summary>Prefisso delle chiavi dei pennelli.</summary>
    public const string BrushPrefix = "Brush.";

    /// <summary>Prefisso delle chiavi dei colori.</summary>
    public const string ColorPrefix = "Color.";

    /// <summary>Chiave del font dell'interfaccia.</summary>
    public const string UiFontKey = "Font.Ui";

    /// <summary>Chiave del font monospaziato per i dati.</summary>
    public const string MonoFontKey = "Font.Mono";

    /// <summary>
    /// Font dell'interfaccia. Inter sarà incluso nell'app in P1.10; fino ad allora si usano i font di sistema.
    /// </summary>
    public const string UiFontFamily = "Inter, Segoe UI Variable Text, Segoe UI";

    /// <summary>
    /// Font monospaziato per i dati. JetBrains Mono sarà incluso in P1.10; fino ad allora si usano i font di sistema.
    /// </summary>
    public const string MonoFontFamily = "JetBrains Mono, Cascadia Mono, Consolas";

    /// <summary>Scala tipografica (§5.3): 12 / 14 / 16 / 20 / 28 / 40.</summary>
    private static readonly (string Key, double Value)[] Sizes =
    [
        ("Size.Caption", 12),
        ("Size.Body", 14),
        ("Size.Subtitle", 16),
        ("Size.Title", 20),
        ("Size.Display", 28),
        ("Size.Hero", 40),
    ];

    /// <summary>Scala di spaziatura (§5.3): 4 / 8 / 12 / 16 / 24 / 32 / 48.</summary>
    private static readonly (string Key, double Value)[] Spaces =
    [
        ("Space.1", 4),
        ("Space.2", 8),
        ("Space.3", 12),
        ("Space.4", 16),
        ("Space.5", 24),
        ("Space.6", 32),
        ("Space.7", 48),
    ];

    private readonly ResourceDictionary _resources;
    private bool _nightMode;
    private double _brightness = Palette.MaximumFactor;

    /// <summary>Crea il gestore e scrive subito tutte le risorse nel dizionario indicato.</summary>
    /// <param name="resources">Dizionario di destinazione, di norma <c>Application.Current.Resources</c>.</param>
    public ThemeManager(ResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        _resources = resources;
        WriteFixedResources();
        ApplyPalette();
    }

    /// <summary>Evento generato dopo ogni cambio di modalità o di luminosità effettivo.</summary>
    public event EventHandler? ThemeChanged;

    /// <summary>True se è attiva la modalità notte rossa.</summary>
    public bool NightMode => _nightMode;

    /// <summary>Luminosità relativa dell'interfaccia, da 0.2 a 1.0.</summary>
    public double Brightness => _brightness;

    /// <summary>Palette effettivamente applicata (modalità e luminosità già tenute in conto).</summary>
    public Palette CurrentPalette { get; private set; } = Palette.Dark;

    /// <summary>Attiva o disattiva la modalità notte rossa. Non fa nulla se il valore è già quello.</summary>
    /// <param name="enabled">True per la notte rossa, false per il tema scuro.</param>
    public void SetNightMode(bool enabled)
    {
        if (_nightMode == enabled)
        {
            return;
        }

        _nightMode = enabled;
        ApplyPalette();
        RaiseThemeChanged();
    }

    /// <summary>Inverte la modalità notte rossa (scorciatoia Ctrl+R).</summary>
    public void ToggleNightMode() => SetNightMode(!_nightMode);

    /// <summary>
    /// Imposta la luminosità dell'interfaccia. Il valore è limitato a 0.2–1.0; un valore non numerico equivale a 1.0.
    /// </summary>
    /// <param name="factor">Luminosità relativa (0.2 = 20%, 1.0 = 100%).</param>
    public void SetBrightness(double factor)
    {
        double clamped = double.IsNaN(factor)
            ? Palette.MaximumFactor
            : Math.Clamp(factor, Palette.MinimumFactor, Palette.MaximumFactor);
        if (clamped == _brightness)
        {
            return;
        }

        _brightness = clamped;
        ApplyPalette();
        RaiseThemeChanged();
    }

    /// <summary>Scrive i colori della palette corrente nelle risorse, aggiornando i pennelli esistenti.</summary>
    private void ApplyPalette()
    {
        Palette palette = (_nightMode ? Palette.NightRed : Palette.Dark).Scaled(_brightness);
        CurrentPalette = palette;

        foreach (var (role, hex) in palette.Entries())
        {
            Color color = ToColor(hex);
            _resources[ColorPrefix + role] = color;
            SetBrush(BrushPrefix + role, color);
        }
    }

    /// <summary>
    /// Aggiorna il pennello con la chiave indicata. Se esiste ed è modificabile, ne cambia il colore: i controlli che lo
    /// usano si ridisegnano. Altrimenti ne crea uno nuovo.
    /// </summary>
    private void SetBrush(string key, Color color)
    {
        if (_resources.Contains(key) && _resources[key] is SolidColorBrush existing && !existing.IsFrozen)
        {
            existing.Color = color;
            return;
        }

        _resources[key] = new SolidColorBrush(color);
    }

    /// <summary>Scrive le risorse che non cambiano con il tema: font, scala tipografica e di spaziatura.</summary>
    private void WriteFixedResources()
    {
        _resources[UiFontKey] = new FontFamily(UiFontFamily);
        _resources[MonoFontKey] = new FontFamily(MonoFontFamily);

        foreach (var (key, value) in Sizes)
        {
            _resources[key] = value;
        }

        foreach (var (key, value) in Spaces)
        {
            _resources[key] = value;
        }
    }

    private void RaiseThemeChanged() => ThemeChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Converte "#RRGGBB" in <see cref="Color"/>. La palette ha già validato i valori.</summary>
    private static Color ToColor(string hex)
    {
        var (r, g, b) = Palette.ParseHex(hex);
        return Color.FromRgb(r, g, b);
    }
}
