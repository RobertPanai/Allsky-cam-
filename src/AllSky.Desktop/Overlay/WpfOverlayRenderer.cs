using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AllSky.InfoOverlay;

namespace AllSky.Desktop.Overlay;

/// <summary>Esito di un render: pixel BGRA, tempo misurato e avvisi (per esempio loghi mancanti).</summary>
/// <remarks>I pixel sono premoltiplicati (Pbgra32). Con frame opachi, alfa 255, coincidono con BGRA.</remarks>
public sealed record OverlayRenderResult(byte[] Bgra, TimeSpan Elapsed, IReadOnlyList<string> Avvisi);

/// <summary>Statistiche di più render sullo stesso frame, per verificare il budget.</summary>
public sealed record OverlayTimingReport(TimeSpan Mediana, TimeSpan Minimo, TimeSpan Massimo, int Esecuzioni);

/// <summary>
/// Esegue una <see cref="OverlayScene"/> su un frame con WPF: <c>DrawingVisual</c> per il disegno e
/// <c>RenderTargetBitmap</c> per i pixel.
/// </summary>
/// <remarks>
/// <b>Thread STA obbligatorio.</b> Il renderer usa oggetti WPF che richiedono un thread con apartment STA.
/// Non è thread-safe: va usato da un solo thread alla volta. Per il lavoro in background usare
/// <see cref="OverlayStaWorker"/>, che possiede un thread STA dedicato.
/// Il font è quello indicato da <see cref="NomeFont"/> (di sistema, monospazio di default: le cifre restano allineate).
/// Le misure del testo nel posizionamento sono stime (vedi <see cref="OverlayComposer"/>): se una riga è più larga
/// dell'area, il renderer la tronca con puntini invece di farla uscire dal frame.
/// Budget: meno di <see cref="BudgetMs"/> millisecondi per frame a 1920×1080, da verificare sul PC Windows.
/// </remarks>
public sealed class WpfOverlayRenderer
{
    /// <summary>Budget di tempo per frame, in millisecondi, a 1920×1080.</summary>
    public const double BudgetMs = 30;

    private readonly Dictionary<OverlayColore, SolidColorBrush> _pennelli = new();
    private readonly Dictionary<string, BitmapSource> _loghi = new(StringComparer.Ordinal);

    /// <summary>Nome del font di sistema per le etichette.</summary>
    public string NomeFont { get; init; } = "Consolas";

    /// <summary>
    /// Compone la scena sul frame e restituisce i pixel per l'encoder.
    /// </summary>
    /// <param name="bgra">Frame di <paramref name="larghezza"/>×<paramref name="altezza"/> pixel in BGRA a 8 bit, senza padding.</param>
    /// <param name="scena">Scena prodotta da <see cref="OverlayComposer"/> per la stessa dimensione.</param>
    /// <param name="cartellaLoghi">Cartella dei loghi PNG (cartella dati). Se null, i loghi vengono saltati con un avviso.</param>
    public OverlayRenderResult Render(byte[] bgra, int larghezza, int altezza, OverlayScene scena, string? cartellaLoghi)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        ArgumentNullException.ThrowIfNull(scena);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(larghezza, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(altezza, 0);
        long attesi = (long)larghezza * altezza * 4;
        if (bgra.Length != attesi)
        {
            throw new ArgumentException($"Il frame deve avere {attesi} byte (BGRA {larghezza}x{altezza}), ne ha {bgra.Length}.", nameof(bgra));
        }

        var stopwatch = Stopwatch.StartNew();
        var avvisi = new List<string>();

        var sorgente = BitmapSource.Create(larghezza, altezza, 96, 96, PixelFormats.Bgra32, null, bgra, larghezza * 4);
        sorgente.Freeze();

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawImage(sorgente, new Rect(0, 0, larghezza, altezza));
            foreach (OverlayComando comando in scena.Comandi)
            {
                switch (comando)
                {
                    case OverlayRiempimento riempimento:
                        dc.DrawRectangle(Pennello(riempimento.Colore), null, ToRect(riempimento.Area));
                        break;
                    case OverlayMascheraCerchio maschera:
                        DisegnaMaschera(dc, maschera, larghezza, altezza);
                        break;
                    case OverlayTestoPosizionato testo:
                        DisegnaTesto(dc, testo);
                        break;
                    case OverlayImmagine immagine:
                        DisegnaLogo(dc, immagine, cartellaLoghi, avvisi);
                        break;
                }
            }
        }

        var bersaglio = new RenderTargetBitmap(larghezza, altezza, 96, 96, PixelFormats.Pbgra32);
        bersaglio.Render(visual);
        var uscita = new byte[attesi];
        bersaglio.CopyPixels(uscita, larghezza * 4, 0);
        stopwatch.Stop();

        return new OverlayRenderResult(uscita, stopwatch.Elapsed, avvisi);
    }

    /// <summary>
    /// Misura il tempo di render: un esecuzione di riscaldamento, poi <paramref name="esecuzioni"/> misure
    /// con <see cref="Stopwatch"/>. Restituisce mediana, minimo e massimo.
    /// </summary>
    public OverlayTimingReport Measure(byte[] bgra, int larghezza, int altezza, OverlayScene scena, string? cartellaLoghi, int esecuzioni = 10)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(esecuzioni, 1);
        Render(bgra, larghezza, altezza, scena, cartellaLoghi);

        var tempi = new List<TimeSpan>(esecuzioni);
        for (int i = 0; i < esecuzioni; i++)
        {
            tempi.Add(Render(bgra, larghezza, altezza, scena, cartellaLoghi).Elapsed);
        }

        tempi.Sort();
        return new OverlayTimingReport(tempi[tempi.Count / 2], tempi[0], tempi[^1], esecuzioni);
    }

    /// <summary>Vero se il tempo di un frame resta sotto il budget.</summary>
    public static bool EntroBudget(TimeSpan tempo) => tempo.TotalMilliseconds < BudgetMs;

    private SolidColorBrush Pennello(OverlayColore colore)
    {
        if (!_pennelli.TryGetValue(colore, out SolidColorBrush? pennello))
        {
            pennello = new SolidColorBrush(Color.FromArgb(colore.A, colore.R, colore.G, colore.B));
            pennello.Freeze();
            _pennelli[colore] = pennello;
        }

        return pennello;
    }

    private static Rect ToRect(OverlayRect area) => new(area.X, area.Y, area.Larghezza, area.Altezza);

    private static void DisegnaMaschera(DrawingContext dc, OverlayMascheraCerchio maschera, int larghezza, int altezza)
    {
        var tutto = new Rect(0, 0, larghezza, altezza);
        var centro = new Point(maschera.CentroX, maschera.CentroY);

        if (maschera.Esterno == OverlayMaskEdge.Opaco)
        {
            var cerchio = new EllipseGeometry(centro, maschera.Raggio, maschera.Raggio);
            var esterno = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(tutto), cerchio);
            esterno.Freeze();
            dc.DrawGeometry(Brushes.Black, null, esterno);
            return;
        }

        // Sfumato: trasparente dentro il raggio interno (raggio meno sfumatura), nero oltre il raggio esterno.
        // Il gradiente radiale è relativo al rettangolo del frame: RadiusX e RadiusY in frazioni, così il cerchio resta tondo.
        double raggioEsterno = maschera.Raggio + maschera.Sfumatura;
        double raggioInterno = Math.Max(0, maschera.Raggio - maschera.Sfumatura);
        double posizioneInterna = raggioEsterno > 0 ? raggioInterno / raggioEsterno : 0;
        var gradiente = new RadialGradientBrush
        {
            Center = new Point(maschera.CentroX / larghezza, maschera.CentroY / altezza),
            GradientOrigin = new Point(maschera.CentroX / larghezza, maschera.CentroY / altezza),
            RadiusX = raggioEsterno / larghezza,
            RadiusY = raggioEsterno / altezza,
        };
        gradiente.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
        gradiente.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), posizioneInterna));
        gradiente.GradientStops.Add(new GradientStop(Colors.Black, 1));
        gradiente.Freeze();
        dc.DrawRectangle(gradiente, null, tutto);
    }

    private void DisegnaTesto(DrawingContext dc, OverlayTestoPosizionato testo)
    {
        var tipografia = new Typeface(new FontFamily(NomeFont), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var formattato = new FormattedText(
            testo.Testo,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            tipografia,
            testo.AltezzaTestoPx,
            Pennello(testo.Colore),
            1.0);
        formattato.MaxLineCount = 1;
        formattato.MaxTextWidth = Math.Max(1, testo.Area.Larghezza);
        formattato.Trimming = TextTrimming.CharacterEllipsis;
        formattato.TextAlignment = testo.Allineamento switch
        {
            OverlayAllineamento.Sinistra => TextAlignment.Left,
            OverlayAllineamento.Centro => TextAlignment.Center,
            _ => TextAlignment.Right,
        };

        dc.DrawText(formattato, new Point(testo.Area.X, testo.Area.Y));
    }

    private void DisegnaLogo(DrawingContext dc, OverlayImmagine immagine, string? cartellaLoghi, List<string> avvisi)
    {
        if (string.IsNullOrEmpty(cartellaLoghi))
        {
            avvisi.Add($"cartella dei loghi non impostata: {immagine.NomeFile}");
            return;
        }

        // Solo un nome di file dentro la cartella dei loghi: niente percorsi.
        if (Path.GetFileName(immagine.NomeFile) != immagine.NomeFile)
        {
            avvisi.Add($"nome logo non valido: {immagine.NomeFile}");
            return;
        }

        string percorso = Path.Combine(cartellaLoghi, immagine.NomeFile);
        if (!File.Exists(percorso))
        {
            avvisi.Add($"logo mancante: {immagine.NomeFile}");
            return;
        }

        BitmapSource? logo = CaricaLogo(percorso);
        if (logo is null)
        {
            avvisi.Add($"logo non leggibile: {immagine.NomeFile}");
            return;
        }

        // Adattamento nell'area mantenendo le proporzioni, centrato.
        double scala = Math.Min(immagine.Area.Larghezza / logo.PixelWidth, immagine.Area.Altezza / logo.PixelHeight);
        double larghezza = logo.PixelWidth * scala;
        double altezza = logo.PixelHeight * scala;
        var destinazione = new Rect(
            immagine.Area.X + (immagine.Area.Larghezza - larghezza) / 2,
            immagine.Area.Y + (immagine.Area.Altezza - altezza) / 2,
            larghezza,
            altezza);

        dc.PushOpacity(Math.Clamp(immagine.Opacita, 0, 1));
        dc.DrawImage(logo, destinazione);
        dc.Pop();
    }

    /// <summary>Carica un PNG con <see cref="BitmapDecoder"/>. La cache è per percorso e data di modifica.</summary>
    private BitmapSource? CaricaLogo(string percorso)
    {
        string chiave = percorso + "|" + File.GetLastWriteTimeUtc(percorso).Ticks.ToString(CultureInfo.InvariantCulture);
        if (_loghi.TryGetValue(chiave, out BitmapSource? cached))
        {
            return cached;
        }

        try
        {
            using FileStream flusso = File.OpenRead(percorso);
            BitmapDecoder decodificatore = BitmapDecoder.Create(flusso, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            BitmapFrame frame = decodificatore.Frames[0];
            frame.Freeze();
            _loghi[chiave] = frame;
            return frame;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>
/// Thread STA dedicato al <see cref="WpfOverlayRenderer"/>. Il renderer nasce e lavora sempre su questo thread,
/// quindi il chiamante può usarlo da qualunque thread tramite <see cref="EseguiAsync{T}"/>.
/// Le richieste vengono eseguite una alla volta, nell'ordine di arrivo.
/// </summary>
public sealed class OverlayStaWorker : IDisposable
{
    private readonly BlockingCollection<Action> _coda = new();
    private readonly Thread _thread;

    // Accesso solo dal thread STA.
    private WpfOverlayRenderer? _renderer;

    public OverlayStaWorker()
    {
        _thread = new Thread(Ciclo) { IsBackground = true, Name = "AllSky.Overlay.STA" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Esegue <paramref name="lavoro"/> sul thread STA con il renderer condiviso.</summary>
    public Task<T> EseguiAsync<T>(Func<WpfOverlayRenderer, T> lavoro)
    {
        ArgumentNullException.ThrowIfNull(lavoro);
        var attesa = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _coda.Add(() =>
        {
            try
            {
                _renderer ??= new WpfOverlayRenderer();
                attesa.SetResult(lavoro(_renderer));
            }
            catch (Exception ex)
            {
                attesa.SetException(ex);
            }
        });
        return attesa.Task;
    }

    private void Ciclo()
    {
        foreach (Action azione in _coda.GetConsumingEnumerable())
        {
            azione();
        }
    }

    /// <summary>Chiude la coda e attende brevemente la fine del thread. Le richieste già in coda vengono completate.</summary>
    public void Dispose()
    {
        _coda.CompleteAdding();
        if (Thread.CurrentThread != _thread)
        {
            _thread.Join(TimeSpan.FromSeconds(5));
        }
    }
}
