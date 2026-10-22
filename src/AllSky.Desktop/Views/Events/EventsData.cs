using System;
using System.Windows.Media.Imaging;
using AllSky.Core.Events;
using AllSky.Desktop.Views.Nights;

namespace AllSky.Desktop.Views.Events;

/// <summary>
/// Letture per la vista Eventi. Tutti i metodi vanno chiamati fuori dal thread dell'interfaccia.
/// Le immagini escono congelate e ridotte in larghezza: la galleria non deve tenere in memoria i frame a piena risoluzione.
/// </summary>
internal static class EventsData
{
    /// <summary>Larghezza di decodifica di una miniatura, in pixel.</summary>
    internal const int ThumbnailDecodeWidth = 320;

    /// <summary>Larghezza di decodifica dell'immagine principale del dettaglio, in pixel.</summary>
    internal const int ImageDecodeWidth = 1280;

    /// <summary>
    /// Immagine decodificata e congelata, ridotta a <paramref name="decodeWidth"/>. Null se il percorso è vuoto o il file
    /// non si legge: la vista mostra il messaggio al suo posto.
    /// </summary>
    /// <param name="path">Percorso assoluto del JPEG, o null.</param>
    /// <param name="decodeWidth">Larghezza massima di decodifica.</param>
    internal static BitmapSource? LoadImage(string? path, int decodeWidth)
    {
        return string.IsNullOrEmpty(path) ? null : NightsData.TryDecode(path, decodeWidth);
    }

    /// <summary>Tutti gli eventi attivi. Il filtro per notte e tipo si applica in memoria con <see cref="EventListRules"/>.</summary>
    /// <param name="events">Archivio degli eventi.</param>
    internal static IReadOnlyList<EventListItem> ListAll(IEventBrowser events)
    {
        return events.List(new EventFilter());
    }
}
