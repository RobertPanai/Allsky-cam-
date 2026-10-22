using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AllSky.Archive;
using AllSky.Core;
using AllSky.Imaging.Jpeg;
using AllSky.InfoOverlay;
using AllSky.Timelapse;

namespace AllSky.Desktop.Overlay;

/// <summary>
/// Maschera informativa del timelapse per il Desktop. Decodifica il JPEG con WIC, compone la scena con
/// <see cref="OverlayComposer"/>, la disegna con <see cref="WpfOverlayRenderer"/> sul thread STA e ricodifica il JPEG
/// con il codificatore del progetto. Il timelapse vede solo <see cref="IFrameOverlay"/>.
/// </summary>
/// <remarks>
/// Il thread STA e il renderer sono condivisi da tutti i job: <see cref="CreateFactory"/> crea un'istanza per job,
/// con il layout della configurazione di quel momento, così una rigenerazione usa il layout corrente.
/// </remarks>
public sealed class WpfFrameOverlay : IFrameOverlay
{
    /// <summary>Cartella dei loghi PNG, dentro la cartella dati.</summary>
    public const string LogoFolderName = "loghi";

    /// <summary>Qualità JPEG dei frame mascherati: la stessa dei prodotti dell'archivio.</summary>
    public const int JpegQuality = 90;

    private static readonly Lazy<OverlayStaWorker> Worker = new(() => new OverlayStaWorker());

    private readonly OverlayLayout _layout;
    private readonly Language _language;
    private readonly string _logoFolder;
    private readonly IJpegEncoder _encoder;

    /// <param name="layout">Layout già letto e validato.</param>
    /// <param name="language">Lingua delle etichette.</param>
    /// <param name="logoFolder">Cartella dei loghi PNG.</param>
    /// <param name="encoder">Codificatore JPEG per l'uscita.</param>
    public WpfFrameOverlay(OverlayLayout layout, Language language, string logoFolder, IJpegEncoder encoder)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _language = language;
        _logoFolder = logoFolder ?? throw new ArgumentNullException(nameof(logoFolder));
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
    }

    /// <summary>
    /// Fabbrica per <c>HostOptions.FrameOverlayFactory</c>: a ogni job legge il layout dalla configurazione ricevuta.
    /// Un layout non leggibile ricade sui predefiniti (il codec lo tollera).
    /// </summary>
    /// <param name="dataFolder">Cartella dati: i loghi stanno in <c>loghi</c> dentro di essa.</param>
    public static Func<AppConfig, IFrameOverlay?> CreateFactory(string dataFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolder);
        string logoFolder = Path.Combine(dataFolder, LogoFolderName);
        var encoder = new BaselineJpegEncoder();
        return config =>
        {
            OverlayLayoutReadResult lettura = OverlayLayoutCodec.Leggi(config.Overlay.Layout?.GetRawText());
            return new WpfFrameOverlay(lettura.Layout, config.Language, logoFolder, encoder);
        };
    }

    /// <inheritdoc />
    public Task<byte[]> ApplyAsync(byte[] jpeg, FrameSidecar? sidecar, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(jpeg);
        ct.ThrowIfCancellationRequested();
        if (sidecar is null)
        {
            throw new InvalidOperationException("sidecar del frame non disponibile.");
        }

        return Worker.Value.EseguiAsync(renderer => Compone(renderer, jpeg, sidecar));
    }

    /// <summary>Esegue sul thread STA: decodifica, maschera e ricodifica un frame.</summary>
    private byte[] Compone(WpfOverlayRenderer renderer, byte[] jpeg, FrameSidecar sidecar)
    {
        (byte[] bgra, int larghezza, int altezza) = DecodificaBgra(jpeg);
        OverlayScene scena = OverlayComposer.Compose(_layout, sidecar, new OverlayFrameSize(larghezza, altezza), _language);
        OverlayRenderResult risultato = renderer.Render(bgra, larghezza, altezza, scena, _logoFolder);
        return _encoder.Encode(BgraARgb(risultato.Bgra, larghezza, altezza), larghezza, altezza, JpegQuality);
    }

    /// <summary>JPEG in pixel BGRA a 8 bit, senza padding.</summary>
    private static (byte[] Bgra, int Larghezza, int Altezza) DecodificaBgra(byte[] jpeg)
    {
        using var flusso = new MemoryStream(jpeg, writable: false);
        BitmapDecoder decodificatore = BitmapDecoder.Create(flusso, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        BitmapSource sorgente = decodificatore.Frames[0];
        BitmapSource bgra = sorgente.Format == PixelFormats.Bgra32
            ? sorgente
            : new FormatConvertedBitmap(sorgente, PixelFormats.Bgra32, null, 0);

        int larghezza = bgra.PixelWidth;
        int altezza = bgra.PixelHeight;
        var pixel = new byte[larghezza * altezza * 4];
        bgra.CopyPixels(pixel, larghezza * 4, 0);
        return (pixel, larghezza, altezza);
    }

    /// <summary>Da BGRA (opaco) a RGB a 8 bit, come si aspetta <see cref="IJpegEncoder"/>.</summary>
    private static byte[] BgraARgb(byte[] bgra, int larghezza, int altezza)
    {
        var rgb = new byte[larghezza * altezza * 3];
        for (int s = 0, d = 0; s + 3 < bgra.Length; s += 4, d += 3)
        {
            rgb[d] = bgra[s + 2];
            rgb[d + 1] = bgra[s + 1];
            rgb[d + 2] = bgra[s];
        }

        return rgb;
    }
}
