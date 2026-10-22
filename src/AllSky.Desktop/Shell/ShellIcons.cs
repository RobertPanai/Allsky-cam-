using System.Windows.Media;
using AllSky.Core.Shell;

namespace AllSky.Desktop.Shell;

/// <summary>
/// Icone della shell come geometrie vettoriali (Path). Griglia di 24 unità, tratto di 1.5 unità, nessuna immagine raster.
/// </summary>
/// <remarks>
/// I dati sono nel mini-linguaggio di <see cref="Geometry.Parse(string)"/>, lo stesso di un attributo <c>Data</c> di Path in XAML.
/// Le geometrie sono congelate e condivise: il colore arriva dal Path che le usa, non dalla geometria.
/// </remarks>
internal static class ShellIcons
{
    /// <summary>Stella a quattro punte, per "Stanotte".</summary>
    internal static readonly Geometry Tonight = Build("M12,3 L13.5,10.5 L21,12 L13.5,13.5 L12,21 L10.5,13.5 L3,12 L10.5,10.5 Z");

    /// <summary>Cerchio fisheye con orizzonte, per "Live".</summary>
    internal static readonly Geometry Live = Build("M3,12 A9,9 0 1 0 21,12 A9,9 0 1 0 3,12 Z M4.5,15 H19.5");

    /// <summary>Corpo della camera con obiettivo.</summary>
    internal static readonly Geometry Camera = Build("M4,8 H7.5 L9.5,5.5 H14.5 L16.5,8 H20 V19 H4 Z M12,10 A3.5,3.5 0 1 0 12,17 A3.5,3.5 0 1 0 12,10 Z");

    /// <summary>Falce di luna, per "Notti" e per l'altezza della Luna.</summary>
    internal static readonly Geometry Moon = Build("M11.37,3.03 A9,9 0 1 0 20.55,14.81 A7.5,7.5 0 0 1 11.37,3.03 Z");

    /// <summary>Freccia verso l'alto su un vassoio, per "Pubblicazione".</summary>
    internal static readonly Geometry Upload = Build("M12,15 V4 M7.5,8.5 L12,4 L16.5,8.5 M4,14 V19.5 H20 V14");

    /// <summary>Cursori di regolazione, per "Impostazioni".</summary>
    internal static readonly Geometry Settings = Build("M4,7 H20 M4,17 H20 M8,4.5 V9.5 M16,14.5 V19.5");

    /// <summary>Foglio con righe, per "Log".</summary>
    internal static readonly Geometry Log = Build("M6,3.5 H18 V20.5 H6 Z M9,8.5 H15 M9,12 H15 M9,15.5 H13");

    /// <summary>Sole con raggi, per la fase "giorno" e l'altezza del Sole.</summary>
    internal static readonly Geometry Sun = Build(
        "M12,8 A4,4 0 1 0 12,16 A4,4 0 1 0 12,8 Z M12,2.5 V4.5 M12,19.5 V21.5 M2.5,12 H4.5 M19.5,12 H21.5 "
        + "M5.3,5.3 L6.7,6.7 M17.3,17.3 L18.7,18.7 M18.7,5.3 L17.3,6.7 M6.7,17.3 L5.3,18.7");

    /// <summary>Mezzo sole sull'orizzonte, per il crepuscolo.</summary>
    internal static readonly Geometry Twilight = Build("M7,15 A5,5 0 0 1 17,15 M3,18.5 H21");

    /// <summary>Linea di orizzonte, per una fase non nota.</summary>
    internal static readonly Geometry Horizon = Build("M3,18.5 H21");

    /// <summary>Immagine con paesaggio, per l'ultimo frame.</summary>
    internal static readonly Geometry Image = Build("M3.5,5.5 H20.5 V18.5 H3.5 Z M3.5,15.5 L8.5,10.5 L12.5,14.5 L15.5,11.5 L20.5,16.5");

    /// <summary>Cilindro, per lo spazio su disco.</summary>
    internal static readonly Geometry Satellite = Build("M9.5,14.5 L14.5,9.5 L16.5,11.5 L11.5,16.5 Z M4,11 L7,8 M6.5,13.5 L9,11 M15,17 L17,19 M14.5,19.5 L19,15 M4,5.5 L6,7.5");
    internal static readonly Geometry Disk = Build("M5,6 V18 A7,3 0 0 0 19,18 V6 M5,6 A7,3 0 0 1 19,6 A7,3 0 0 1 5,6 Z");

    /// <summary>Icona della vista corrispondente alla voce della barra laterale.</summary>
    /// <param name="view">Vista della voce.</param>
    internal static Geometry For(ShellView view) => view switch
    {
        ShellView.Tonight => Tonight,
        ShellView.Live => Live,
        ShellView.Camera => Camera,
        ShellView.Nights => Moon,
        ShellView.Publication => Upload,
        ShellView.Settings => Settings,
        ShellView.Log => Log,
        _ => Tonight,
    };

    /// <summary>Icona della fase del cielo nella barra di stato.</summary>
    /// <param name="phase">Fase, o <see cref="StatusPhase.Unknown"/>.</param>
    internal static Geometry ForPhase(StatusPhase phase) => phase switch
    {
        StatusPhase.Day => Sun,
        StatusPhase.Twilight => Twilight,
        StatusPhase.Night => Moon,
        _ => Horizon,
    };

    private static Geometry Build(string data)
    {
        Geometry geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}
