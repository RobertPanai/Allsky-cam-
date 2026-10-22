using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using AllSky.Archive;
using AllSky.Core.Nights;
using AllSky.Engine;

namespace AllSky.Desktop.Views.Nights;

/// <summary>
/// Letture dell'archivio per la vista "Notti". Tutti i metodi vanno chiamati fuori dal thread dell'interfaccia.
/// Non lanciano eccezioni per file mancanti o illeggibili: restituiscono null e la vista mostra lo stato curato.
/// </summary>
/// <remarks>
/// Le immagini escono congelate (<see cref="System.Windows.Freezable.Freeze"/>) e ridotte in larghezza: keogram e
/// startrails non vanno a piena risoluzione nell'elenco, e la memoria resta limitata anche con notti lunghe.
/// </remarks>
internal static class NightsData
{
    /// <summary>Larghezza massima di decodifica del keogram, in pixel.</summary>
    internal const int KeogramDecodeWidth = 2048;

    /// <summary>Larghezza massima di decodifica degli startrails, in pixel.</summary>
    internal const int StartrailsDecodeWidth = 1600;

    /// <summary>Larghezza massima di decodifica di un frame, in pixel.</summary>
    internal const int FrameDecodeWidth = 1600;

    /// <summary>Esito della lettura dell'elenco. <paramref name="Error"/> è null se la lettura è riuscita.</summary>
    internal sealed record NightList(IReadOnlyList<NightSummary> Nights, string? Error);

    /// <summary>Dati di una notte selezionata.</summary>
    /// <param name="NightId">Notte a cui si riferiscono i dati.</param>
    /// <param name="Frames">Frame della notte, in ordine di salvataggio.</param>
    /// <param name="Keogram">Keogram congelato, o null se manca o non si legge.</param>
    /// <param name="Startrails">Startrails congelati, o null se manca o non si legge.</param>
    /// <param name="TimelapsePath">Percorso del file del timelapse se esiste, altrimenti null.</param>
    /// <param name="Error">Messaggio di errore della lettura dei frame, o null.</param>
    internal sealed record NightDetail(
        string NightId,
        IReadOnlyList<FrameDto> Frames,
        BitmapSource? Keogram,
        BitmapSource? Startrails,
        string? TimelapsePath,
        string? Error);

    /// <summary>Elenco delle notti, dalla più recente. In caso di errore restituisce il messaggio e nessuna notte.</summary>
    /// <param name="service">Servizio dell'Engine.</param>
    public static NightList ListNights(IAllSkyService service)
    {
        try
        {
            IReadOnlyList<NightDto> dtos = service.ListNights();
            return new NightList(dtos.Select(ToSummary).ToList(), null);
        }
        catch (Exception ex)
        {
            return new NightList(Array.Empty<NightSummary>(), ex.Message);
        }
    }

    /// <summary>Frame, keogram, startrails e percorso del timelapse di una notte.</summary>
    /// <param name="service">Servizio dell'Engine.</param>
    /// <param name="nightId">Identificativo YYYY-MM-DD.</param>
    public static NightDetail LoadDetail(IAllSkyService service, string nightId)
    {
        IReadOnlyList<FrameDto> frames = Array.Empty<FrameDto>();
        string? error = null;
        try
        {
            // Tutti i frame: il keogram e lo scorrimento devono coprire l'intera notte.
            frames = service.ListFrames(nightId, 0, int.MaxValue);
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        BitmapSource? keogram = TryDecode(service.GetProductPath(nightId, NightProduct.Keogram), KeogramDecodeWidth);
        BitmapSource? startrails = TryDecode(service.GetProductPath(nightId, NightProduct.Startrails), StartrailsDecodeWidth);

        string timelapse = service.GetProductPath(nightId, NightProduct.Timelapse);
        string? timelapsePath = File.Exists(timelapse) ? timelapse : null;

        return new NightDetail(nightId, frames, keogram, startrails, timelapsePath, error);
    }

    /// <summary>Immagine di un frame, congelata; null se il file manca o non si decodifica.</summary>
    /// <param name="service">Servizio dell'Engine.</param>
    /// <param name="nightId">Identificativo della notte.</param>
    /// <param name="frameId">Identificativo del frame.</param>
    public static BitmapSource? LoadFrame(IAllSkyService service, string nightId, string frameId)
    {
        try
        {
            string path = service.GetFramePath(nightId, frameId);
            return TryDecode(path, FrameDecodeWidth);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Decodifica un JPEG in un'immagine congelata, ridotta a <paramref name="decodePixelWidth"/> se è più larga.
    /// Restituisce null se il file non esiste o non si legge.
    /// </summary>
    /// <param name="path">Percorso del file.</param>
    /// <param name="decodePixelWidth">Larghezza massima di decodifica, in pixel.</param>
    public static BitmapSource? TryDecode(string path, int decodePixelWidth)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            int nativeWidth = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.None).Frames[0].PixelWidth;
            stream.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (nativeWidth > decodePixelWidth)
            {
                image.DecodePixelWidth = decodePixelWidth;
            }

            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static NightSummary ToSummary(NightDto dto) => new(
        dto.Id,
        dto.Frames,
        dto.FirstFrame,
        dto.LastFrame,
        dto.Keogram,
        dto.Startrails,
        NightTimelapseStates.FromWire(dto.Timelapse));
}
