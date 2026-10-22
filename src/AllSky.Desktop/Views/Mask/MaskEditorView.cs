using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AllSky.Archive;
using AllSky.Core;
using AllSky.Engine;
using AllSky.Imaging.Jpeg;
using AllSky.InfoOverlay;
using AllSky.InfoOverlay.Editor;
using AllSky.Desktop.Overlay;
using AllSky.Desktop.Views.Live;

namespace AllSky.Desktop.Views.Mask;

/// <summary>
/// Vista "Maschera": editor visivo della maschera informativa del timelapse. Anteprima sull'ultimo frame (o su un cielo
/// di esempio), lista dei widget con interruttore, posizione, ordine e formato, forma della maschera, testo, loghi e
/// interruttori di applicazione.
/// </summary>
/// <remarks>
/// <para>
/// Le regole (stato, validazione, spostamenti, loghi) stanno in <c>AllSky.InfoOverlay/Editor</c> e sono testate senza WPF.
/// Qui restano i controlli, la sincronizzazione con lo stato e le chiamate all'Engine: il salvataggio passa da
/// <see cref="IAllSkyService.UpdateConfig"/> e la pulizia dei loghi avviene solo dopo un salvataggio riuscito.
/// </para>
/// <para>
/// Le modifiche restano nello stato finché non si salva. Il timer dell'anteprima evita un render a ogni tasto premuto;
/// un risultato vecchio viene scartato con il numero di generazione.
/// </para>
/// </remarks>
public class MaskEditorView : UserControl
{
    private static readonly TimeSpan AttesaAnteprima = TimeSpan.FromMilliseconds(250);
    private const int LarghezzaSintetica = 1280;
    private const int AltezzaSintetica = 720;

    private readonly IAllSkyService _service;
    private readonly AppPaths _paths;
    private readonly DesktopTexts _texts;
    private readonly DispatcherTimer _timerAnteprima;
    private readonly Dictionary<string, string> _erroriCampi = new(StringComparer.Ordinal);

    private MaskEditorState _stato = new();
    private int _seleziona = -1;
    private bool _sospeso;
    private bool _modificato;
    private bool _occupato;
    private bool _attaccato;
    private int _generazione;
    private byte[]? _fotogrammaSintetico;
    private CancellationTokenSource _vita = new();
    private List<string> _nomiLoghi = new();

    // Anteprima e messaggi.
    private Image _anteprima = null!;
    private TextBlock _statoAnteprima = null!;
    private TextBlock _omessi = null!;
    private TextBlock _statoTesto = null!;
    private TextBlock _logoMancanti = null!;
    private TextBlock _dirty = null!;
    private TextBlock _problemi = null!;
    private TextBlock _avvisi = null!;
    private Button _salva = null!;
    private Button _ripristina = null!;

    // Widget.
    private ListBox _elenco = null!;
    private Button _su = null!;
    private Button _giu = null!;
    private Button _rimuovi = null!;
    private StackPanel _dettaglio = null!;
    private TextBlock _logoConteggio = null!;

    // Dettaglio del widget selezionato (riempito da RicostruisciDettaglio).
    private ComboBox? _posizioneBox;
    private TextBox? _patternBox;
    private ComboBox? _fusoBox;
    private ComboBox? _decimaliBox;
    private CheckBox? _fase;
    private ComboBox? _maxSatelliti;
    private CheckBox[]? _meteo;
    private ComboBox? _fileLogo;
    private TextBox? _altezzaLogo;
    private TextBox? _opacitaLogo;
    private TextBox? _testo;
    private CheckBox? _gain;

    // Maschera, testo, applicazione.
    private ComboBox _modalita = null!;
    private StackPanel _pannelloCerchio = null!;
    private StackPanel _pannelloBande = null!;
    private TextBox _centroX = null!;
    private TextBox _centroY = null!;
    private TextBox _raggio = null!;
    private ComboBox _bordo = null!;
    private TextBox _sfumatura = null!;
    private TextBox _bandaAlta = null!;
    private TextBox _bandaBassa = null!;
    private TextBox _scala = null!;
    private TextBox _margine = null!;
    private CheckBox _appTimelapse = null!;
    private CheckBox _appLive = null!;
    private CheckBox _appPubblicazione = null!;
    private TextBlock _notaLive = null!;
    private TextBlock _notaPubblicazione = null!;

    /// <summary>Crea la vista. Va creata sul thread dell'interfaccia.</summary>
    /// <param name="service">Servizio dell'Engine: configurazione, ultimo frame e stato.</param>
    /// <param name="paths">Percorsi dell'app: la cartella dati contiene la cartella dei loghi.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    public MaskEditorView(IAllSkyService service, AppPaths paths, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(texts);

        _service = service;
        _paths = paths;
        _texts = texts;
        _timerAnteprima = new DispatcherTimer { Interval = AttesaAnteprima };
        _timerAnteprima.Tick += OnTimerAnteprima;

        Content = BuildLayout();
        AutomationProperties.SetName(this, texts.MaskTitle);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private string CartellaLoghi => Path.Combine(_paths.DataFolder, WpfFrameOverlay.LogoFolderName);

    private CultureInfo Cultura => _texts.Culture;

    // ---- Ciclo di vita ----

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_attaccato)
        {
            return;
        }

        _attaccato = true;
        _service.FrameProduced += OnFrameProduced;
        if (!_modificato)
        {
            CaricaDaConfigurazione(_service.GetConfig());
        }

        PianificaAnteprima();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _attaccato = false;
        _service.FrameProduced -= OnFrameProduced;
        _timerAnteprima.Stop();
        _vita.Cancel();
        _vita = new CancellationTokenSource();
    }

    private void OnFrameProduced(AllSky.Engine.FrameEvent evento)
    {
        // Evento dal thread di cattura: si torna sul thread dell'interfaccia prima di toccare i controlli.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_attaccato)
            {
                PianificaAnteprima();
            }
        }));
    }

    // ---- Stato <-> controlli ----

    /// <summary>Riempie i controlli dallo stato. Non genera modifiche.</summary>
    private void CaricaModulo()
    {
        _sospeso = true;
        try
        {
            _modalita.SelectedIndex = _stato.Modo switch
            {
                OverlayMaskMode.Nessuna => 0,
                OverlayMaskMode.Bande => 2,
                _ => 1,
            };
            _centroX.Text = MaskInput.FormatPercento(_stato.Cerchio.CentroX, Cultura);
            _centroY.Text = MaskInput.FormatPercento(_stato.Cerchio.CentroY, Cultura);
            _raggio.Text = MaskInput.FormatPercento(_stato.Cerchio.Raggio, Cultura);
            _bordo.SelectedIndex = _stato.Cerchio.Esterno == OverlayMaskEdge.Sfumato ? 1 : 0;
            _sfumatura.Text = MaskInput.FormatPercento(_stato.Cerchio.Sfumatura, Cultura);
            _bandaAlta.Text = MaskInput.FormatPercento(_stato.Bande.Superiore, Cultura);
            _bandaBassa.Text = MaskInput.FormatPercento(_stato.Bande.Inferiore, Cultura);
            _scala.Text = MaskInput.FormatPercento(_stato.Stile.ScalaTesto, Cultura);
            _margine.Text = MaskInput.FormatPercento(_stato.Stile.Margine, Cultura);
            _appTimelapse.IsChecked = _stato.ApplyToTimelapse;
            _appLive.IsChecked = _stato.ApplyToLive;
            _appPubblicazione.IsChecked = _stato.ApplyToPublish;
        }
        finally
        {
            _sospeso = false;
        }

        AggiornaVisibilita();
        RicostruisciElenco();
        RicostruisciDettaglio();
        AggiornaStato();
    }

    private void CaricaDaConfigurazione(AppConfig config)
    {
        _stato = MaskEditorState.Da(config.Overlay);
        _seleziona = _stato.Widget.Count > 0 ? 0 : -1;
        _modificato = false;
        _erroriCampi.Clear();
        _avvisi.Text = _stato.AvvisiLettura.Count == 0
            ? string.Empty
            : string.Format(Cultura, _texts.MaskLoadWarnings, string.Join("; ", _stato.AvvisiLettura));
        CaricaModulo();
    }

    /// <summary>Legge i controlli della maschera, del testo e dell'applicazione nello stato. Un valore non valido resta com'era.</summary>
    private void LeggiModulo()
    {
        _erroriCampi.Clear();

        _stato.Modo = _modalita.SelectedIndex switch
        {
            0 => OverlayMaskMode.Nessuna,
            2 => OverlayMaskMode.Bande,
            _ => OverlayMaskMode.Cerchio,
        };

        OverlayCircleMask cerchio = _stato.Cerchio;
        cerchio = cerchio with
        {
            CentroX = Percento(_centroX, _texts.MaskCenterX, 0, 100) ?? cerchio.CentroX,
            CentroY = Percento(_centroY, _texts.MaskCenterY, 0, 100) ?? cerchio.CentroY,
            Raggio = Percento(_raggio, _texts.MaskRadius, 1, 100) ?? cerchio.Raggio,
            Esterno = _bordo.SelectedIndex == 1 ? OverlayMaskEdge.Sfumato : OverlayMaskEdge.Opaco,
            Sfumatura = Percento(_sfumatura, _texts.MaskFeather, 0, 20) ?? cerchio.Sfumatura,
        };
        _stato.Cerchio = cerchio;

        OverlayBandMask bande = _stato.Bande;
        _stato.Bande = bande with
        {
            Superiore = Percento(_bandaAlta, _texts.MaskBandTop, 0, 50) ?? bande.Superiore,
            Inferiore = Percento(_bandaBassa, _texts.MaskBandBottom, 0, 50) ?? bande.Inferiore,
        };

        OverlayStyle stile = _stato.Stile;
        _stato.Stile = stile with
        {
            ScalaTesto = Percento(_scala, _texts.MaskTextScale, 0.5, 10) ?? stile.ScalaTesto,
            Margine = Percento(_margine, _texts.MaskMargin, 0, 10) ?? stile.Margine,
        };

        _stato.ApplyToTimelapse = _appTimelapse.IsChecked == true;
        _stato.ApplyToLive = _appLive.IsChecked == true;
        _stato.ApplyToPublish = _appPubblicazione.IsChecked == true;
    }

    /// <summary>Legge i controlli del widget selezionato nel suo formato. Assente o nullo: nessuna modifica.</summary>
    private void LeggiDettaglio()
    {
        if (!HaSelezione())
        {
            return;
        }

        MaskWidgetDraft voce = _stato.Widget[_seleziona];
        OverlayWidgetFormat f = voce.Formato;

        if (_posizioneBox is not null && _posizioneBox.SelectedIndex >= 0)
        {
            voce.Posizione = MaskEditorCatalog.Posizioni[_posizioneBox.SelectedIndex];
        }

        switch (voce.Tipo)
        {
            case OverlayWidgetKind.DataOra:
                voce.Formato = f with
                {
                    Pattern = _patternBox?.Text ?? f.Pattern,
                    Fuso = _fusoBox is null ? f.Fuso : _fusoBox.SelectedIndex == 1 ? OverlayTimeZone.Utc : OverlayTimeZone.Locale,
                };
                break;
            case OverlayWidgetKind.Sole:
                voce.Formato = f with { Decimali = _decimaliBox is null ? f.Decimali : _decimaliBox.SelectedIndex };
                break;
            case OverlayWidgetKind.Luna:
                voce.Formato = f with
                {
                    Decimali = _decimaliBox is null ? f.Decimali : _decimaliBox.SelectedIndex,
                    MostraFase = _fase?.IsChecked ?? f.MostraFase,
                };
                break;
            case OverlayWidgetKind.Satelliti:
                voce.Formato = f with { MaxElementi = _maxSatelliti is null ? f.MaxElementi : _maxSatelliti.SelectedIndex + 1 };
                break;
            case OverlayWidgetKind.Meteo:
                if (_meteo is not null)
                {
                    OverlayMeteoField[] campi = Enum.GetValues<OverlayMeteoField>();
                    voce.Formato = f with
                    {
                        Campi = campi.Where((c, i) => _meteo[i].IsChecked == true).ToList(),
                    };
                }

                break;
            case OverlayWidgetKind.Logo:
                string? file = f.FileLogo;
                if (_fileLogo is not null && _fileLogo.SelectedIndex >= 0 && _fileLogo.SelectedIndex < _nomiLoghi.Count)
                {
                    file = _nomiLoghi[_fileLogo.SelectedIndex];
                }

                voce.Formato = f with
                {
                    FileLogo = file,
                    AltezzaMaxPercento = Percento(_altezzaLogo, _texts.MaskFieldAltezzaLogo, 1, 50) * 100 ?? f.AltezzaMaxPercento,
                    Opacita = Percento(_opacitaLogo, _texts.MaskFieldOpacita, 0, 100) ?? f.Opacita,
                };
                break;
            case OverlayWidgetKind.TestoLibero:
                voce.Formato = f with { Testo = _testo?.Text ?? f.Testo };
                break;
            case OverlayWidgetKind.Esposizione:
                voce.Formato = f with { MostraGain = _gain?.IsChecked ?? f.MostraGain };
                break;
        }
    }

    /// <summary>Legge un campo percentuale. Se non è valido, segnala l'errore e restituisce null.</summary>
    private double? Percento(TextBox? casella, string nomeCampo, double minimo, double massimo)
    {
        if (casella is null)
        {
            return null;
        }

        if (MaskInput.TryParsePercento(casella.Text, Cultura, minimo, massimo, out double frazione))
        {
            return frazione;
        }

        _erroriCampi[nomeCampo] = string.Format(Cultura, _texts.MaskInvalidField, nomeCampo);
        return null;
    }

    // ---- Eventi dei controlli ----

    private void OnModificato(bool ricostruisciElenco = false)
    {
        if (_sospeso)
        {
            return;
        }

        LeggiModulo();
        LeggiDettaglio();
        _modificato = true;
        if (ricostruisciElenco)
        {
            RicostruisciElenco();
        }

        AggiornaVisibilita();
        AggiornaStato();
        PianificaAnteprima();
    }

    private void OnSelezioneCambiata(object sender, SelectionChangedEventArgs e)
    {
        if (_sospeso)
        {
            return;
        }

        _seleziona = _elenco.SelectedIndex;
        RicostruisciDettaglio();
        AggiornaStato();
    }

    private void OnAbilitato(int indice, bool abilitato)
    {
        if (_sospeso || indice < 0 || indice >= _stato.Widget.Count)
        {
            return;
        }

        _stato.Widget[indice].Abilitato = abilitato;
        _modificato = true;
        AggiornaStato();
        PianificaAnteprima();
    }

    private void OnSu(object sender, RoutedEventArgs e) => SpostaSelezionato(su: true);

    private void OnGiu(object sender, RoutedEventArgs e) => SpostaSelezionato(su: false);

    private void SpostaSelezionato(bool su)
    {
        if (!HaSelezione())
        {
            return;
        }

        LeggiModulo();
        LeggiDettaglio();
        MaskWidgetDraft voce = _stato.Widget[_seleziona];
        bool spostato = su ? _stato.SpostaSu(_seleziona) : _stato.SpostaGiu(_seleziona);
        if (spostato)
        {
            _seleziona = IndiceDi(voce);
            _modificato = true;
            RicostruisciElenco();
            RicostruisciDettaglio();
            AggiornaStato();
            PianificaAnteprima();
        }
    }

    private void OnRimuovi(object sender, RoutedEventArgs e)
    {
        if (!HaSelezione())
        {
            return;
        }

        LeggiModulo();
        LeggiDettaglio();
        if (_stato.Rimuovi(_seleziona))
        {
            _seleziona = Math.Min(_seleziona, _stato.Widget.Count - 1);
            _modificato = true;
            RicostruisciElenco();
            RicostruisciDettaglio();
            AggiornaStato();
            PianificaAnteprima();
        }
    }

    private void OnAggiungiTesto(object sender, RoutedEventArgs e)
    {
        LeggiModulo();
        LeggiDettaglio();
        int indice = _stato.Aggiungi(OverlayWidgetKind.TestoLibero, OverlayPosition.TL);
        if (indice >= 0)
        {
            _seleziona = indice;
            _modificato = true;
            RicostruisciElenco();
            RicostruisciDettaglio();
            AggiornaStato();
            PianificaAnteprima();
        }
    }

    private async void OnCaricaLogo(object sender, RoutedEventArgs e)
    {
        var dialogo = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "PNG (*.png)|*.png",
            Title = _texts.MaskAddLogo,
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialogo.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        string sorgente = dialogo.FileName;
        string cartella = CartellaLoghi;
        MaskLogoImport esito = await Task.Run(() => MaskLogoStore.Importa(sorgente, cartella));
        if (!esito.Ok)
        {
            ImpostaStato(MessaggioLogo(esito.Errore), errore: true);
            return;
        }

        LeggiModulo();
        LeggiDettaglio();
        _seleziona = _stato.AggiungiLogo(esito.NomeFile!, OverlayPosition.TR);
        _modificato = true;
        RicostruisciElenco();
        RicostruisciDettaglio();
        AggiornaStato();
        PianificaAnteprima();
        ImpostaStato(string.Format(Cultura, _texts.MaskLogoImported, esito.NomeFile), errore: false);
    }

    private void OnRipristina(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(_texts.MaskResetConfirm, _texts.MaskReset, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _stato.RipristinaPredefiniti();
        _seleziona = _stato.Widget.Count > 0 ? 0 : -1;
        _erroriCampi.Clear();
        _modificato = true;
        CaricaModulo();
        PianificaAnteprima();
        ImpostaStato(_texts.MaskResetDone, errore: false);
    }

    private async void OnSalva(object sender, RoutedEventArgs e)
    {
        LeggiModulo();
        LeggiDettaglio();
        if (RaccogliProblemi().Count > 0)
        {
            ImpostaStato(_texts.MaskSaveInvalid, errore: true);
            AggiornaStato();
            return;
        }

        IniziaOccupato();
        try
        {
            AppConfig corrente = _service.GetConfig();
            AppConfig aggiornata = corrente with { Overlay = _stato.AggiornaConfig(corrente.Overlay) };
            IReadOnlyList<string> usati = _stato.LoghiUsati();
            string cartella = CartellaLoghi;

            ConfigUpdateResult risultato = await Task.Run(() => _service.UpdateConfig(aggiornata));
            if (!risultato.Ok)
            {
                string dettaglio = risultato.Errors.Count > 0 ? risultato.Errors[0].Message : string.Empty;
                ImpostaStato(string.Format(Cultura, _texts.MaskSaveRejected, dettaglio), errore: true);
                return;
            }

            // Solo dopo un salvataggio riuscito: i loghi non più referenziati vengono rimossi.
            IReadOnlyList<string> eliminati = await Task.Run(() => MaskLogoStore.PulisciNonUsati(cartella, usati));
            if (eliminati.Count > 0)
            {
                await WpfFrameOverlay.InvalidaCacheLoghiAsync();
            }

            CaricaDaConfigurazione(_service.GetConfig());
            string messaggio = _texts.MaskSaved;
            if (eliminati.Count > 0)
            {
                messaggio += " " + string.Format(Cultura, _texts.MaskLogoCleaned, eliminati.Count);
            }

            ImpostaStato(messaggio, errore: false);
            PianificaAnteprima();
        }
        catch (Exception)
        {
            ImpostaStato(_texts.MaskSaveError, errore: true);
        }
        finally
        {
            FineOccupato();
        }
    }

    private void IniziaOccupato()
    {
        _occupato = true;
        _salva.IsEnabled = false;
        _ripristina.IsEnabled = false;
        ImpostaStato(_texts.MaskSaving, errore: false);
    }

    private void FineOccupato()
    {
        _occupato = false;
        _ripristina.IsEnabled = true;
        AggiornaStato();
    }

    // ---- Anteprima ----

    private void PianificaAnteprima()
    {
        _timerAnteprima.Stop();
        _timerAnteprima.Start();
    }

    private async void OnTimerAnteprima(object? sender, EventArgs e)
    {
        _timerAnteprima.Stop();
        await AggiornaAnteprimaAsync();
    }

    private async Task AggiornaAnteprimaAsync()
    {
        if (!_attaccato)
        {
            return;
        }

        int generazione = ++_generazione;
        OverlayLayout layout = _stato.ToLayout();
        if (_stato.Valida().Count > 0)
        {
            _statoAnteprima.Text = _texts.MaskPreviewPaused;
            return;
        }

        _statoAnteprima.Text = _texts.MaskPreviewUpdating;
        CancellationToken token = _vita.Token;
        try
        {
            AppConfig config = _service.GetConfig();
            AnteprimaFotogramma fotogramma = await Task.Run(() => PreparaFotogramma(config), token);
            var renderer = new WpfFrameOverlay(layout, config.Language, CartellaLoghi, new BaselineJpegEncoder());
            byte[] overlay = await renderer.ApplyAsync(fotogramma.Jpeg, fotogramma.Sidecar, token);
            if (generazione != _generazione || !_attaccato)
            {
                return;
            }

            BitmapSource immagine = FrameDecoder.Decode(overlay);
            _anteprima.Source = immagine;
            IReadOnlyList<OverlayWidgetOmesso> omessi = OverlayComposer
                .Compose(layout, fotogramma.Sidecar, new OverlayFrameSize(immagine.PixelWidth, immagine.PixelHeight), config.Language)
                .Omessi;
            _omessi.Text = omessi.Count == 0
                ? string.Empty
                : string.Format(Cultura, _texts.MaskOmitted, string.Join(", ", omessi.Select(o => NomeTipo(o.Widget.Tipo))));
            _statoAnteprima.Text = fotogramma.Sintetico ? _texts.MaskPreviewSample : string.Empty;
        }
        catch (OperationCanceledException)
        {
            // Chiusura della vista: nessun messaggio.
        }
        catch (Exception ex)
        {
            if (generazione == _generazione)
            {
                _statoAnteprima.Text = string.Format(Cultura, _texts.MaskPreviewError, ex.Message);
            }
        }
    }

    /// <summary>Recupera l'ultimo frame e i suoi metadati. Senza frame, usa un cielo di esempio. Gira fuori dal thread UI.</summary>
    private AnteprimaFotogramma PreparaFotogramma(AppConfig config)
    {
        var stato = _service.GetStatus();
        byte[]? jpeg = _service.GetLatestJpeg(false);
        DateTimeOffset istante = stato.LastFrame?.Timestamp ?? DateTimeOffset.UtcNow;
        bool sintetico = jpeg is null;
        if (jpeg is null)
        {
            jpeg = FotogrammaSintetico();
            istante = DateTimeOffset.UtcNow;
        }

        var sito = new ObservingSite
        {
            LatitudeDeg = config.Site.Latitude,
            LongitudeDeg = config.Site.Longitude,
        };
        FrameSidecar sidecar = FrameSidecars.FromTimestamp(istante, sito, FusoOrario(config.Site.Timezone));
        return new AnteprimaFotogramma(jpeg, sidecar, sintetico);
    }

    /// <summary>Cielo di esempio: fondo quasi nero con stelle a seme fisso. Calcolato una volta sola.</summary>
    private byte[] FotogrammaSintetico()
    {
        byte[]? esistente = _fotogrammaSintetico;
        if (esistente is not null)
        {
            return esistente;
        }

        var rgb = new byte[LarghezzaSintetica * AltezzaSintetica * 3];
        for (int i = 0; i < rgb.Length; i += 3)
        {
            rgb[i] = 5;
            rgb[i + 1] = 7;
            rgb[i + 2] = 10;
        }

        var casuale = new Random(20261022);
        for (int s = 0; s < 900; s++)
        {
            int x = casuale.Next(LarghezzaSintetica);
            int y = casuale.Next(AltezzaSintetica);
            byte luminosita = (byte)casuale.Next(90, 255);
            int p = (y * LarghezzaSintetica + x) * 3;
            rgb[p] = luminosita;
            rgb[p + 1] = luminosita;
            rgb[p + 2] = (byte)Math.Min(255, luminosita + 20);
        }

        byte[] jpeg = new BaselineJpegEncoder().Encode(rgb, LarghezzaSintetica, AltezzaSintetica, 90);
        _fotogrammaSintetico = jpeg;
        return jpeg;
    }

    private static TimeZoneInfo FusoOrario(string? id)
    {
        try
        {
            return string.IsNullOrWhiteSpace(id) ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private sealed record AnteprimaFotogramma(byte[] Jpeg, FrameSidecar Sidecar, bool Sintetico);

    // ---- Presentazione ----

    private bool HaSelezione() => _seleziona >= 0 && _seleziona < _stato.Widget.Count;

    private int IndiceDi(MaskWidgetDraft voce)
    {
        for (int i = 0; i < _stato.Widget.Count; i++)
        {
            if (ReferenceEquals(_stato.Widget[i], voce))
            {
                return i;
            }
        }

        return -1;
    }

    private void RicostruisciElenco()
    {
        _sospeso = true;
        try
        {
            _elenco.Items.Clear();
            for (int i = 0; i < _stato.Widget.Count; i++)
            {
                MaskWidgetDraft voce = _stato.Widget[i];
                _elenco.Items.Add(CreaRiga(i, voce));
            }

            _elenco.SelectedIndex = HaSelezione() ? _seleziona : -1;
        }
        finally
        {
            _sospeso = false;
        }

        AggiornaPulsantiWidget();
    }

    private ListBoxItem CreaRiga(int indice, MaskWidgetDraft voce)
    {
        string nome = NomeTipo(voce.Tipo);
        var riga = new StackPanel { Orientation = Orientation.Horizontal };

        var interruttore = new CheckBox
        {
            IsChecked = voce.Abilitato,
            IsEnabled = MaskEditorCatalog.Disponibile(voce.Tipo),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        AutomationProperties.SetName(interruttore, string.Format(Cultura, _texts.MaskWidgetEnabled, nome));
        interruttore.Click += (_, _) => OnAbilitato(indice, interruttore.IsChecked == true);
        riga.Children.Add(interruttore);

        var testo = new TextBlock
        {
            Text = nome,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 150,
        };
        testo.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        testo.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");
        riga.Children.Add(testo);

        var posizione = new TextBlock
        {
            Text = NomePosizione(voce.Posizione),
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        posizione.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        posizione.SetResourceReference(TextBlock.FontSizeProperty, "Size.Caption");
        riga.Children.Add(posizione);

        var item = new ListBoxItem { Content = riga };
        AutomationProperties.SetName(item, nome);
        return item;
    }

    private void AggiornaPulsantiWidget()
    {
        bool c = HaSelezione();
        _su.IsEnabled = c;
        _giu.IsEnabled = c;
        _rimuovi.IsEnabled = c && MaskEditorCatalog.Multipla(_stato.Widget[_seleziona].Tipo);
    }

    /// <summary>Ricostruisce i controlli del formato per il tipo del widget selezionato.</summary>
    private void RicostruisciDettaglio()
    {
        _sospeso = true;
        try
        {
            _dettaglio.Children.Clear();
            _posizioneBox = null; _patternBox = null; _fusoBox = null; _decimaliBox = null; _fase = null;
            _maxSatelliti = null; _meteo = null; _fileLogo = null; _altezzaLogo = null; _opacitaLogo = null;
            _testo = null; _gain = null;

            if (!HaSelezione())
            {
                _dettaglio.Children.Add(Testo(_texts.MaskWidgetsHint, "Brush.TextSecondary", "Size.Body"));
                return;
            }

            MaskWidgetDraft voce = _stato.Widget[_seleziona];
            OverlayWidgetFormat f = voce.Formato;
            _dettaglio.Children.Add(Titolo(NomeTipo(voce.Tipo)));

            if (!MaskEditorCatalog.Disponibile(voce.Tipo))
            {
                _dettaglio.Children.Add(Testo(_texts.MaskWidgetUnavailable, "Brush.TextSecondary", "Size.Body"));
                return;
            }

            _posizioneBox = Combo(_texts.MaskPosition, MaskEditorCatalog.Posizioni.Select(NomePosizione).ToList());
            _posizioneBox.SelectedIndex = MaskEditorCatalog.Posizioni.ToList().IndexOf(voce.Posizione);
            _posizioneBox.SelectionChanged += (_, _) => { if (!_sospeso) OnModificato(ricostruisciElenco: true); };
            _dettaglio.Children.Add(Riga(_texts.MaskPosition, _posizioneBox));

            switch (voce.Tipo)
            {
                case OverlayWidgetKind.DataOra:
                    _patternBox = Campo(_texts.MaskFieldPattern, f.Pattern ?? string.Empty, 220);
                    _patternBox.TextChanged += (_, _) => OnModificato();
                    _dettaglio.Children.Add(Riga(_texts.MaskFieldPattern, _patternBox));
                    _dettaglio.Children.Add(Testo(_texts.MaskFieldPatternHint, "Brush.TextTertiary", "Size.Caption"));
                    _fusoBox = Combo(_texts.MaskFieldFuso, new[] { _texts.MaskFuseLocale, _texts.MaskFuseUtc });
                    _fusoBox.SelectedIndex = f.Fuso == OverlayTimeZone.Utc ? 1 : 0;
                    _fusoBox.SelectionChanged += (_, _) => { if (!_sospeso) OnModificato(); };
                    _dettaglio.Children.Add(Riga(_texts.MaskFieldFuso, _fusoBox));
                    break;

                case OverlayWidgetKind.Sole:
                    _decimaliBox = Combo(_texts.MaskFieldDecimali, new[] { "0", "1", "2" });
                    _decimaliBox.SelectedIndex = Math.Clamp(f.Decimali ?? 0, 0, 2);
                    _decimaliBox.SelectionChanged += (_, _) => { if (!_sospeso) OnModificato(); };
                    _dettaglio.Children.Add(Riga(_texts.MaskFieldDecimali, _decimaliBox));
                    break;

                case OverlayWidgetKind.Luna:
                    _decimaliBox = Combo(_texts.MaskFieldDecimali, new[] { "0", "1", "2" });
                    _decimaliBox.SelectedIndex = Math.Clamp(f.Decimali ?? 0, 0, 2);
                    _decimaliBox.SelectionChanged += (_, _) => { if (!_sospeso) OnModificato(); };
                    _dettaglio.Children.Add(Riga(_texts.MaskFieldDecimali, _decimaliBox));
                    _fase = Interruttore(_texts.MaskFieldMostraFase, f.MostraFase ?? true);
                    _fase.Click += (_, _) => OnModificato();
                    _dettaglio.Children.Add(_fase);
                    break;

                case OverlayWidgetKind.Satelliti:
                    _maxSatelliti = Combo(_texts.MaskFieldMaxElementi, Enumerable.Range(1, 10).Select(n => n.ToString(Cultura)).ToList());
                    _maxSatelliti.SelectedIndex = Math.Clamp((f.MaxElementi ?? 3) - 1, 0, 9);
                    _maxSatelliti.SelectionChanged += (_, _) => { if (!_sospeso) OnModificato(); };
                    _dettaglio.Children.Add(Riga(_texts.MaskFieldMaxElementi, _maxSatelliti));
                    break;

                case OverlayWidgetKind.Meteo:
                    _dettaglio.Children.Add(Etichetta(_texts.MaskFieldCampi));
                    var campiAttivi = f.Campi ?? Array.Empty<OverlayMeteoField>();
                    OverlayMeteoField[] tutti = Enum.GetValues<OverlayMeteoField>();
                    _meteo = new CheckBox[tutti.Length];
                    for (int i = 0; i < tutti.Length; i++)
                    {
                        _meteo[i] = Interruttore(NomeMeteo(tutti[i]), campiAttivi.Contains(tutti[i]));
                        _meteo[i].Click += (_, _) => OnModificato();
                        _dettaglio.Children.Add(_meteo[i]);
                    }

                    break;

                case OverlayWidgetKind.Logo:
                    _nomiLoghi = MaskLogoStore.Elenco(CartellaLoghi).ToList();
                    if (f.FileLogo is { } corrente && !_nomiLoghi.Contains(corrente, StringComparer.OrdinalIgnoreCase))
                    {
                        _nomiLoghi = _nomiLoghi.Append(corrente).ToList();
                    }

                    IReadOnlyList<string> vociLogo = _nomiLoghi.Count == 0 ? new[] { _texts.MaskNoLogoFiles } : _nomiLoghi;
                    _fileLogo = Combo(_texts.MaskFieldFileLogo, vociLogo);
                    _fileLogo.SelectedIndex = f.FileLogo is null ? -1 : _nomiLoghi.FindIndex(n => string.Equals(n, f.FileLogo, StringComparison.OrdinalIgnoreCase));
                    _fileLogo.SelectionChanged += (_, _) => { if (!_sospeso) OnModificato(); };
                    _dettaglio.Children.Add(Riga(_texts.MaskFieldFileLogo, _fileLogo));
                    _altezzaLogo = Campo(_texts.MaskFieldAltezzaLogo, MaskInput.FormatPercento((f.AltezzaMaxPercento ?? 8) / 100.0, Cultura), 90);
                    _altezzaLogo.TextChanged += (_, _) => OnModificato();
                    _dettaglio.Children.Add(Riga(_texts.MaskFieldAltezzaLogo, _altezzaLogo));
                    _opacitaLogo = Campo(_texts.MaskFieldOpacita, MaskInput.FormatPercento(f.Opacita ?? 0.9, Cultura), 90);
                    _opacitaLogo.TextChanged += (_, _) => OnModificato();
                    _dettaglio.Children.Add(Riga(_texts.MaskFieldOpacita, _opacitaLogo));
                    break;

                case OverlayWidgetKind.TestoLibero:
                    _testo = Campo(_texts.MaskFieldTesto, f.Testo ?? string.Empty, 220);
                    _testo.MaxLength = 120;
                    _testo.TextChanged += (_, _) => OnModificato();
                    _dettaglio.Children.Add(Riga(_texts.MaskFieldTesto, _testo));
                    break;

                case OverlayWidgetKind.Esposizione:
                    _gain = Interruttore(_texts.MaskFieldMostraGain, f.MostraGain ?? true);
                    _gain.Click += (_, _) => OnModificato();
                    _dettaglio.Children.Add(_gain);
                    break;
            }
        }
        finally
        {
            _sospeso = false;
        }
    }

    private void AggiornaVisibilita()
    {
        _pannelloCerchio.Visibility = _modalita.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        _pannelloBande.Visibility = _modalita.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        _notaLive.Visibility = _appLive.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        _notaPubblicazione.Visibility = _appPubblicazione.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Problemi che bloccano il salvataggio: campi non validi e errori di validazione del layout.</summary>
    private List<string> RaccogliProblemi()
    {
        var problemi = new List<string>(_erroriCampi.Values);
        problemi.AddRange(_stato.Valida());
        return problemi;
    }

    /// <summary>Aggiorna messaggi, stato sporco e abilitazione del salvataggio.</summary>
    private void AggiornaStato()
    {
        List<string> problemi = RaccogliProblemi();
        _problemi.Text = problemi.Count == 0 ? string.Empty : string.Join(Environment.NewLine, problemi);
        _problemi.Visibility = problemi.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _dirty.Text = _modificato ? _texts.MaskDirty : string.Empty;
        _salva.IsEnabled = !_occupato && problemi.Count == 0;
        IReadOnlyList<string> disponibili = MaskLogoStore.Elenco(CartellaLoghi);
        _logoConteggio.Text = string.Format(Cultura, _texts.MaskLogoCount, disponibili.Count);

        var mancanti = _stato.Widget
            .Where(v => v.Tipo == OverlayWidgetKind.Logo && !string.IsNullOrEmpty(v.Formato.FileLogo))
            .Select(v => v.Formato.FileLogo!)
            .Where(n => !disponibili.Contains(n, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _logoMancanti.Text = string.Join(Environment.NewLine, mancanti.Select(n => string.Format(Cultura, _texts.MaskLogoMissing, n)));
        AggiornaPulsantiWidget();
    }

    private void ImpostaStato(string testo, bool errore)
    {
        _statoTesto.Text = testo;
        _statoTesto.SetResourceReference(TextBlock.ForegroundProperty, errore ? "Brush.Error" : "Brush.TextSecondary");
    }

    private string MessaggioLogo(MaskLogoErrore errore) => errore switch
    {
        MaskLogoErrore.EstensioneNonPng => _texts.MaskLogoErrNotPng,
        MaskLogoErrore.FirmaNonPng => _texts.MaskLogoErrSignature,
        MaskLogoErrore.TroppoGrande => _texts.MaskLogoErrTooBig,
        MaskLogoErrore.DimensioniTroppoGrandi => _texts.MaskLogoErrDimensions,
        MaskLogoErrore.FileMancante => _texts.MaskLogoErrMissingFile,
        MaskLogoErrore.NomeNonValido => _texts.MaskLogoErrName,
        MaskLogoErrore.ScritturaFallita => _texts.MaskLogoErrWrite,
        _ => _texts.MaskLogoErrRead,
    };

    private string NomeTipo(OverlayWidgetKind tipo) => tipo switch
    {
        OverlayWidgetKind.DataOra => _texts.MaskKindDataOra,
        OverlayWidgetKind.Sole => _texts.MaskKindSole,
        OverlayWidgetKind.Luna => _texts.MaskKindLuna,
        OverlayWidgetKind.Satelliti => _texts.MaskKindSatelliti,
        OverlayWidgetKind.Meteo => _texts.MaskKindMeteo,
        OverlayWidgetKind.Logo => _texts.MaskKindLogo,
        OverlayWidgetKind.TestoLibero => _texts.MaskKindTestoLibero,
        OverlayWidgetKind.Esposizione => _texts.MaskKindEsposizione,
        _ => _texts.MaskKindBussola,
    };

    private string NomePosizione(OverlayPosition posizione) => posizione switch
    {
        OverlayPosition.TL => _texts.MaskPosTL,
        OverlayPosition.TR => _texts.MaskPosTR,
        OverlayPosition.BL => _texts.MaskPosBL,
        OverlayPosition.BR => _texts.MaskPosBR,
        OverlayPosition.BandaAlta => _texts.MaskPosBandaAlta,
        _ => _texts.MaskPosBandaBassa,
    };

    private string NomeMeteo(OverlayMeteoField campo) => campo switch
    {
        OverlayMeteoField.Temperatura => _texts.MaskMeteoTemperatura,
        OverlayMeteoField.Umidita => _texts.MaskMeteoUmidita,
        OverlayMeteoField.Pressione => _texts.MaskMeteoPressione,
        OverlayMeteoField.Vento => _texts.MaskMeteoVento,
        OverlayMeteoField.Nuvole => _texts.MaskMeteoNuvole,
        OverlayMeteoField.Pioggia => _texts.MaskMeteoPioggia,
        _ => _texts.MaskMeteoLuminositaCielo,
    };

    // ---- Costruzione del layout ----

    private UIElement BuildLayout()
    {
        var radice = new Grid { Margin = new Thickness(16) };
        radice.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        radice.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var intestazione = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        intestazione.Children.Add(Titolo(_texts.MaskTitle, grande: true));
        intestazione.Children.Add(Testo(_texts.MaskDescription, "Brush.TextSecondary", "Size.Body"));
        Grid.SetRow(intestazione, 0);
        radice.Children.Add(intestazione);

        var corpo = new Grid();
        corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
        Grid.SetRow(corpo, 1);
        radice.Children.Add(corpo);

        UIElement anteprima = CostruisciAnteprima();
        Grid.SetColumn(anteprima, 0);
        corpo.Children.Add(anteprima);
        UIElement impostazioni = CostruisciImpostazioni();
        Grid.SetColumn(impostazioni, 1);
        corpo.Children.Add(impostazioni);
        return radice;
    }

    private UIElement CostruisciAnteprima()
    {
        var pannello = new Grid();
        pannello.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        pannello.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _anteprima = new Image
        {
            Stretch = System.Windows.Media.Stretch.Uniform,
            MinHeight = 240,
        };
        AutomationProperties.SetName(_anteprima, _texts.MaskPreviewName);
        var contenitore = new Border { Child = _anteprima, Margin = new Thickness(0, 0, 0, 8) };
        contenitore.SetResourceReference(FrameworkElement.StyleProperty, "Panel");
        pannello.Children.Add(contenitore);
        Grid.SetRow(contenitore, 0);

        var sotto = new StackPanel();
        _statoAnteprima = Testo(string.Empty, "Brush.TextSecondary", "Size.Caption");
        _omessi = Testo(string.Empty, "Brush.Warning", "Size.Caption");
        sotto.Children.Add(_statoAnteprima);
        sotto.Children.Add(_omessi);
        pannello.Children.Add(sotto);
        Grid.SetRow(sotto, 1);
        return pannello;
    }

    private UIElement CostruisciImpostazioni()
    {
        var colonna = new Grid();
        colonna.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        colonna.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var scorrimento = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 8, 0),
        };
        var sezioni = new StackPanel();
        sezioni.Children.Add(Sezione(_texts.MaskWidgetsTitle, CostruisciWidget()));
        sezioni.Children.Add(Sezione(_texts.MaskSectionMask, CostruisciMaschera()));
        sezioni.Children.Add(Sezione(_texts.MaskSectionText, CostruisciTesto()));
        sezioni.Children.Add(Sezione(_texts.MaskSectionLogos, CostruisciLoghi()));
        sezioni.Children.Add(Sezione(_texts.MaskSectionApply, CostruisciApplicazione()));
        scorrimento.Content = sezioni;
        colonna.Children.Add(scorrimento);

        var piede = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var pulsanti = new StackPanel { Orientation = Orientation.Horizontal };
        _ripristina = Pulsante(_texts.MaskReset, primario: false);
        _ripristina.Click += OnRipristina;
        _salva = Pulsante(_texts.MaskSave, primario: true);
        _salva.Click += OnSalva;
        _salva.Margin = new Thickness(8, 4, 0, 0);
        pulsanti.Children.Add(_ripristina);
        pulsanti.Children.Add(_salva);
        piede.Children.Add(pulsanti);
        _dirty = Testo(string.Empty, "Brush.Warning", "Size.Caption");
        _problemi = Testo(string.Empty, "Brush.Error", "Size.Caption");
        _problemi.Visibility = Visibility.Collapsed;
        _statoTesto = Testo(string.Empty, "Brush.TextSecondary", "Size.Caption");
        _avvisi = Testo(string.Empty, "Brush.Warning", "Size.Caption");
        _logoMancanti = Testo(string.Empty, "Brush.Warning", "Size.Caption");
        piede.Children.Add(_dirty);
        piede.Children.Add(_problemi);
        piede.Children.Add(_logoMancanti);
        piede.Children.Add(_avvisi);
        piede.Children.Add(_statoTesto);
        Grid.SetRow(piede, 1);
        colonna.Children.Add(piede);
        return colonna;
    }

    private UIElement CostruisciWidget()
    {
        var pannello = new StackPanel();
        pannello.Children.Add(Testo(_texts.MaskWidgetsHint, "Brush.TextSecondary", "Size.Caption"));

        _elenco = new ListBox { Height = 200, Margin = new Thickness(0, 8, 0, 8) };
        _elenco.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "Brush.Surface1");
        _elenco.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "Brush.Text");
        _elenco.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "Brush.Border");
        AutomationProperties.SetName(_elenco, _texts.MaskWidgetsTitle);
        _elenco.SelectionChanged += OnSelezioneCambiata;
        pannello.Children.Add(_elenco);

        var azioni = new WrapPanel();
        _su = Pulsante(_texts.MaskMoveUp, primario: false);
        _su.Click += OnSu;
        _giu = Pulsante(_texts.MaskMoveDown, primario: false);
        _giu.Click += OnGiu;
        _rimuovi = Pulsante(_texts.MaskRemove, primario: false);
        _rimuovi.Click += OnRimuovi;
        var testo = Pulsante(_texts.MaskAddText, primario: false);
        testo.Click += OnAggiungiTesto;
        var logo = Pulsante(_texts.MaskAddLogo, primario: false);
        logo.Click += OnCaricaLogo;
        foreach (Button b in new[] { _su, _giu, _rimuovi, testo, logo })
        {
            b.Margin = new Thickness(0, 0, 8, 8);
            azioni.Children.Add(b);
        }

        pannello.Children.Add(azioni);
        _dettaglio = new StackPanel();
        pannello.Children.Add(_dettaglio);
        return pannello;
    }

    private UIElement CostruisciMaschera()
    {
        var pannello = new StackPanel();
        _modalita = Combo(_texts.MaskSectionMask, new[] { _texts.MaskModeNone, _texts.MaskModeCircle, _texts.MaskModeBands });
        _modalita.SelectionChanged += (_, _) => { if (!_sospeso) OnModificato(); };
        pannello.Children.Add(Riga(_texts.MaskSectionMask, _modalita));

        _pannelloCerchio = new StackPanel();
        _centroX = Campo(_texts.MaskCenterX, string.Empty, 90);
        _centroY = Campo(_texts.MaskCenterY, string.Empty, 90);
        _raggio = Campo(_texts.MaskRadius, string.Empty, 90);
        _bordo = Combo(_texts.MaskEdge, new[] { _texts.MaskEdgeOpaque, _texts.MaskEdgeFeather });
        _sfumatura = Campo(_texts.MaskFeather, string.Empty, 90);
        foreach (TextBox t in new[] { _centroX, _centroY, _raggio, _sfumatura })
        {
            t.TextChanged += (_, _) => OnModificato();
        }

        _bordo.SelectionChanged += (_, _) => { if (!_sospeso) OnModificato(); };
        _pannelloCerchio.Children.Add(Riga(_texts.MaskCenterX, _centroX));
        _pannelloCerchio.Children.Add(Riga(_texts.MaskCenterY, _centroY));
        _pannelloCerchio.Children.Add(Riga(_texts.MaskRadius, _raggio));
        _pannelloCerchio.Children.Add(Riga(_texts.MaskEdge, _bordo));
        _pannelloCerchio.Children.Add(Riga(_texts.MaskFeather, _sfumatura));
        pannello.Children.Add(_pannelloCerchio);

        _pannelloBande = new StackPanel();
        _bandaAlta = Campo(_texts.MaskBandTop, string.Empty, 90);
        _bandaBassa = Campo(_texts.MaskBandBottom, string.Empty, 90);
        _bandaAlta.TextChanged += (_, _) => OnModificato();
        _bandaBassa.TextChanged += (_, _) => OnModificato();
        _pannelloBande.Children.Add(Riga(_texts.MaskBandTop, _bandaAlta));
        _pannelloBande.Children.Add(Riga(_texts.MaskBandBottom, _bandaBassa));
        pannello.Children.Add(_pannelloBande);
        return pannello;
    }

    private UIElement CostruisciTesto()
    {
        var pannello = new StackPanel();
        _scala = Campo(_texts.MaskTextScale, string.Empty, 90);
        _margine = Campo(_texts.MaskMargin, string.Empty, 90);
        _scala.TextChanged += (_, _) => OnModificato();
        _margine.TextChanged += (_, _) => OnModificato();
        pannello.Children.Add(Riga(_texts.MaskTextScale, _scala));
        pannello.Children.Add(Riga(_texts.MaskMargin, _margine));
        return pannello;
    }

    private UIElement CostruisciLoghi()
    {
        var pannello = new StackPanel();
        pannello.Children.Add(Testo(_texts.MaskLogosHint, "Brush.TextSecondary", "Size.Caption"));
        _logoConteggio = Testo(string.Empty, "Brush.TextSecondary", "Size.Caption");
        pannello.Children.Add(_logoConteggio);
        return pannello;
    }

    private UIElement CostruisciApplicazione()
    {
        var pannello = new StackPanel();
        _appTimelapse = Interruttore(_texts.MaskApplyTimelapse, false);
        _appLive = Interruttore(_texts.MaskApplyLive, false);
        _appPubblicazione = Interruttore(_texts.MaskApplyPublish, false);
        _appTimelapse.Click += (_, _) => OnModificato();
        _appLive.Click += (_, _) => OnModificato();
        _appPubblicazione.Click += (_, _) => OnModificato();
        _notaLive = Testo(_texts.MaskApplyLiveNote, "Brush.Warning", "Size.Caption");
        _notaPubblicazione = Testo(_texts.MaskApplyPublishNote, "Brush.Warning", "Size.Caption");
        _notaLive.Visibility = Visibility.Collapsed;
        _notaPubblicazione.Visibility = Visibility.Collapsed;
        pannello.Children.Add(_appTimelapse);
        pannello.Children.Add(_appLive);
        pannello.Children.Add(_notaLive);
        pannello.Children.Add(_appPubblicazione);
        pannello.Children.Add(_notaPubblicazione);
        return pannello;
    }

    // ---- Utilità di costruzione ----

    private static Border Sezione(string titolo, UIElement contenuto)
    {
        var pila = new StackPanel();
        var intestazione = new TextBlock { Text = titolo, Margin = new Thickness(0, 0, 0, 8) };
        intestazione.SetResourceReference(FrameworkElement.StyleProperty, "PanelTitle");
        pila.Children.Add(intestazione);
        pila.Children.Add(contenuto);

        var pannello = new Border { Child = pila, Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 16) };
        pannello.SetResourceReference(FrameworkElement.StyleProperty, "Panel");
        AutomationProperties.SetName(pannello, titolo);
        return pannello;
    }

    private static TextBlock Titolo(string testo, bool grande = false)
    {
        var blocco = new TextBlock { Text = testo, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
        blocco.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        blocco.SetResourceReference(TextBlock.FontSizeProperty, grande ? "Size.Display" : "Size.Body");
        return blocco;
    }

    private static TextBlock Testo(string testo, string brushKey, string sizeKey)
    {
        var blocco = new TextBlock { Text = testo, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        blocco.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        blocco.SetResourceReference(TextBlock.FontSizeProperty, sizeKey);
        return blocco;
    }

    private static TextBlock Etichetta(string testo)
    {
        var blocco = new TextBlock { Text = testo, Margin = new Thickness(0, 8, 0, 0) };
        blocco.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        blocco.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");
        return blocco;
    }

    /// <summary>Riga "etichetta: controllo" su due colonne.</summary>
    private static Grid Riga(string etichetta, UIElement controllo)
    {
        var griglia = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        griglia.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        griglia.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var testo = Etichetta(etichetta);
        testo.Margin = new Thickness(0, 0, 8, 0);
        testo.VerticalAlignment = VerticalAlignment.Center;
        griglia.Children.Add(testo);
        Grid.SetColumn(controllo, 1);
        griglia.Children.Add(controllo);
        return griglia;
    }

    private static TextBox Campo(string nome, string valore, double larghezza)
    {
        var casella = new TextBox
        {
            Text = valore,
            Width = larghezza,
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(casella, nome);
        casella.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
        return casella;
    }

    private static ComboBox Combo(string nome, IReadOnlyList<string> voci)
    {
        var casella = new ComboBox { MinWidth = 160, Margin = new Thickness(0, 4, 0, 0) };
        foreach (string voce in voci)
        {
            casella.Items.Add(voce);
        }

        AutomationProperties.SetName(casella, nome);
        casella.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "Brush.Surface2");
        casella.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "Brush.Text");
        casella.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "Brush.Border");
        return casella;
    }

    private static CheckBox Interruttore(string testo, bool attivo)
    {
        var casella = new CheckBox { Content = testo, IsChecked = attivo, Margin = new Thickness(0, 6, 0, 0) };
        casella.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "Brush.Text");
        return casella;
    }

    private static Button Pulsante(string testo, bool primario)
    {
        var pulsante = new Button
        {
            Content = testo,
            MinHeight = 32,
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 4, 0, 0),
        };
        pulsante.SetResourceReference(FrameworkElement.StyleProperty, primario ? "Button.Primary" : "Button.Secondary");
        AutomationProperties.SetName(pulsante, testo);
        return pulsante;
    }
}
