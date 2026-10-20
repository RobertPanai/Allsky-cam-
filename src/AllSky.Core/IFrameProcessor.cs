namespace AllSky.Core;

/// <summary>Risultato dell'elaborazione di un frame grezzo: immagine pronta per archivio e interfaccia.</summary>
/// <param name="Width">Larghezza in pixel dell'immagine elaborata.</param>
/// <param name="Height">Altezza in pixel.</param>
/// <param name="Rgb8">Pixel sRGB 8 bit (3 byte per pixel, righe contigue) SENZA overlay: serve a keogram e startrails.</param>
/// <param name="Jpeg">JPEG a piena risoluzione, con overlay se abilitato.</param>
/// <param name="ThumbJpeg">Miniatura JPEG (larghezza circa 320 px).</param>
/// <param name="Phase">"day", "twilight" o "night".</param>
/// <param name="SunAltitudeDeg">Altezza del Sole in gradi al momento dello scatto.</param>
/// <param name="MedianAdu">Mediana ADU del frame grezzo (per la UI).</param>
public sealed record ProcessedFrame(
    int Width,
    int Height,
    byte[] Rgb8,
    byte[] Jpeg,
    byte[] ThumbJpeg,
    string Phase,
    double SunAltitudeDeg,
    int MedianAdu);

/// <summary>Trasforma un frame grezzo in immagine elaborata. Stateful (smoothing temporale): una istanza per sessione di cattura.</summary>
public interface IFrameProcessor
{
    /// <summary>Elabora il frame con la configurazione corrente.</summary>
    ProcessedFrame Process(RawFrame raw);

    /// <summary>Versione "prima" per il confronto: solo debayer e stretch automatico, senza bilanciamento del colore ne' gradiente. Restituisce JPEG.</summary>
    byte[] ProcessRawPreview(RawFrame raw);

    /// <summary>Cambia la configurazione senza perdere lo stato di smoothing quando possibile.</summary>
    void UpdateConfig(AppConfig config);
}
