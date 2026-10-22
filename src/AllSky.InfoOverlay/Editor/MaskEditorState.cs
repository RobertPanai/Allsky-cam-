using System.Text.Json;
using AllSky.Core;

namespace AllSky.InfoOverlay.Editor;

/// <summary>
/// Una voce della lista widget dell'editor. La sua posizione nella lista è l'ordine: <see cref="MaskEditorState.ToLayout"/>
/// lo scrive nel campo <c>ordine</c>. Le proprietà sono modificabili dall'interfaccia; le regole stanno in <see cref="MaskEditorState"/>.
/// </summary>
public sealed class MaskWidgetDraft
{
    public MaskWidgetDraft(OverlayWidgetKind tipo, bool abilitato, OverlayPosition posizione, OverlayWidgetFormat formato)
    {
        Tipo = tipo;
        Abilitato = abilitato;
        Posizione = posizione;
        Formato = formato;
    }

    /// <summary>Tipo del widget. Non cambia: per un altro tipo si rimuove la voce e se ne aggiunge una nuova.</summary>
    public OverlayWidgetKind Tipo { get; }

    public bool Abilitato { get; set; }

    public OverlayPosition Posizione { get; set; }

    /// <summary>Formato. Può contenere campi non previsti dal tipo: <see cref="MaskEditorState.ToLayout"/> li scarta.</summary>
    public OverlayWidgetFormat Formato { get; set; }
}

/// <summary>
/// Stato dell'editor della maschera, senza nessuna dipendenza da WPF. Converte da e verso <see cref="OverlayLayout"/>
/// e dalla configurazione (<see cref="AppConfig.OverlayConfig"/>), applica gli spostamenti su/giù e le aggiunte,
/// e produce il layout che il codec serializza. La validazione passa da <see cref="OverlayLayoutValidator"/>.
/// </summary>
/// <remarks>
/// Gli spostamenti su/giù scambiano la voce con la più vicina della stessa <see cref="OverlayPosition"/>: così l'ordine
/// si vede solo dentro ogni posizione, come nel posizionamento. Le fasce e le frazioni restano nel modello; la conversione
/// in percentuale sta nella vista.
/// </remarks>
public sealed class MaskEditorState
{
    private readonly List<MaskWidgetDraft> _widget = new();

    /// <summary>Crea lo stato con il layout predefinito e nessun avviso.</summary>
    public MaskEditorState()
    {
        CaricaLayout(OverlayLayoutDefaults.Predefinito());
    }

    public OverlayMaskMode Modo { get; set; }

    public OverlayCircleMask Cerchio { get; set; } = null!;

    public OverlayBandMask Bande { get; set; } = null!;

    public OverlayStyle Stile { get; set; } = null!;

    /// <summary>Lista dei widget nell'ordine di <c>ordine</c>. Sola lettura: le modifiche passano dai metodi della classe.</summary>
    public IReadOnlyList<MaskWidgetDraft> Widget => _widget;

    /// <summary>Avvisi della lettura del layout salvato (valori corretti, sezioni assenti, migrazioni).</summary>
    public IReadOnlyList<string> AvvisiLettura { get; private set; } = Array.Empty<string>();

    /// <summary>Applica la maschera ai frame del timelapse (<see cref="AppConfig.OverlayConfig.ApplyToTimelapse"/>).</summary>
    public bool ApplyToTimelapse { get; set; }

    /// <summary>Flusso dal vivo: non ancora collegato, il valore non cambia nulla.</summary>
    public bool ApplyToLive { get; set; }

    /// <summary>Immagini pubblicate (Uplink): non ancora collegato.</summary>
    public bool ApplyToPublish { get; set; }

    /// <summary>
    /// Stato dalla configurazione salvata. Senza layout salvato usa il predefinito, senza avvisi; con un layout
    /// non leggibile usa il predefinito e lo segnala negli avvisi.
    /// </summary>
    public static MaskEditorState Da(AppConfig.OverlayConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var stato = new MaskEditorState();
        if (config.Layout is { } json)
        {
            OverlayLayoutReadResult lettura = OverlayLayoutCodec.Leggi(json.GetRawText());
            stato.CaricaLayout(lettura.Layout);
            stato.AvvisiLettura = lettura.Avvisi;
        }

        stato.ApplyToTimelapse = config.ApplyToTimelapse;
        stato.ApplyToLive = config.ApplyToLive;
        stato.ApplyToPublish = config.ApplyToPublish;
        return stato;
    }

    /// <summary>
    /// Layout da serializzare. Ogni formato contiene solo i campi previsti dal tipo (quelli mancanti prendono il predefinito).
    /// <c>ordine</c> è il rango della voce dentro la sua posizione (0, 1, 2…), che è l'unico ordine che conta per il posizionamento.
    /// Il risultato può ancora non passare la validazione: vedi <see cref="Valida"/>.
    /// </summary>
    public OverlayLayout ToLayout()
    {
        var widget = new List<OverlayWidget>(_widget.Count);
        var ranghi = new Dictionary<OverlayPosition, int>();
        foreach (MaskWidgetDraft voce in _widget)
        {
            int rango = ranghi.GetValueOrDefault(voce.Posizione);
            ranghi[voce.Posizione] = rango + 1;
            widget.Add(new OverlayWidget(voce.Tipo, voce.Abilitato, voce.Posizione, rango, NormalizzaFormato(voce.Tipo, voce.Formato)));
        }

        return new OverlayLayout(
            OverlayLayoutVersions.Corrente,
            new OverlayMask(Modo, Cerchio, Bande),
            Stile,
            widget);
    }

    /// <summary>Problemi del layout corrente, nel testo della validazione. Vuoto se si può salvare.</summary>
    public IReadOnlyList<string> Valida() => OverlayLayoutValidator.Valida(ToLayout());

    /// <summary>Nomi dei file dei loghi referenziati da almeno una voce logo, anche disabilitata: servono alla pulizia.</summary>
    public IReadOnlyList<string> LoghiUsati() =>
        _widget
            .Where(v => v.Tipo == OverlayWidgetKind.Logo && !string.IsNullOrEmpty(v.Formato.FileLogo))
            .Select(v => v.Formato.FileLogo!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Configurazione aggiornata: copia i tre interruttori di applicazione e sostituisce il layout con il JSON del codec.
    /// Chiamare solo quando <see cref="Valida"/> è vuoto.
    /// </summary>
    public AppConfig.OverlayConfig AggiornaConfig(AppConfig.OverlayConfig corrente)
    {
        ArgumentNullException.ThrowIfNull(corrente);
        string json = OverlayLayoutCodec.Serializza(ToLayout());
        using JsonDocument documento = JsonDocument.Parse(json);
        return corrente with
        {
            ApplyToTimelapse = ApplyToTimelapse,
            ApplyToLive = ApplyToLive,
            ApplyToPublish = ApplyToPublish,
            Layout = documento.RootElement.Clone(),
        };
    }

    /// <summary>
    /// Ripristina il layout predefinito: DataOra in basso a sinistra, Sole e Luna in basso a destra, maschera a cerchio
    /// attiva, gli altri widget spenti. Gli interruttori di applicazione restano come sono.
    /// </summary>
    public void RipristinaPredefiniti() => CaricaLayout(OverlayLayoutDefaults.Predefinito());

    /// <summary>
    /// Aggiunge un widget in fondo alla lista. Un tipo non ripetibile già presente non viene aggiunto: restituisce -1.
    /// </summary>
    /// <returns>Indice della nuova voce, o -1.</returns>
    public int Aggiungi(OverlayWidgetKind tipo, OverlayPosition posizione)
    {
        if (!MaskEditorCatalog.Multipla(tipo) && _widget.Any(v => v.Tipo == tipo))
        {
            return -1;
        }

        _widget.Add(new MaskWidgetDraft(tipo, true, posizione, OverlayWidgetFormats.Predefinito(tipo)));
        return _widget.Count - 1;
    }

    /// <summary>Aggiunge un logo che usa il file indicato, già copiato nella cartella loghi.</summary>
    /// <returns>Indice della nuova voce.</returns>
    public int AggiungiLogo(string nomeFile, OverlayPosition posizione)
    {
        if (!OverlayWidgetFormats.FileLogoValido(nomeFile))
        {
            throw new ArgumentException("Nome di logo non valido.", nameof(nomeFile));
        }

        OverlayWidgetFormat predefinito = OverlayWidgetFormats.Predefinito(OverlayWidgetKind.Logo);
        _widget.Add(new MaskWidgetDraft(
            OverlayWidgetKind.Logo,
            true,
            posizione,
            predefinito with { FileLogo = nomeFile }));
        return _widget.Count - 1;
    }

    /// <summary>Rimuove una voce, ma solo se il tipo è ripetibile (logo, testo libero). Gli altri restano nella lista.</summary>
    public bool Rimuovi(int indice)
    {
        if (indice < 0 || indice >= _widget.Count || !MaskEditorCatalog.Multipla(_widget[indice].Tipo))
        {
            return false;
        }

        _widget.RemoveAt(indice);
        return true;
    }

    /// <summary>Sposta la voce verso l'alto, scambiandola con la precedente della stessa posizione. False se è già la prima.</summary>
    public bool SpostaSu(int indice)
    {
        int vicino = IndiceVicino(indice, -1);
        return Scambia(indice, vicino);
    }

    /// <summary>Sposta la voce verso il basso, scambiandola con la successiva della stessa posizione. False se è già l'ultima.</summary>
    public bool SpostaGiu(int indice)
    {
        int vicino = IndiceVicino(indice, +1);
        return Scambia(indice, vicino);
    }

    private void CaricaLayout(OverlayLayout layout)
    {
        Modo = layout.Maschera.Modo;
        Cerchio = layout.Maschera.Cerchio;
        Bande = layout.Maschera.Bande;
        Stile = layout.Stile;

        _widget.Clear();
        foreach (OverlayWidget w in layout.Widget.Select((w, i) => (w, i)).OrderBy(x => x.w.Ordine).ThenBy(x => x.i).Select(x => x.w))
        {
            _widget.Add(new MaskWidgetDraft(w.Tipo, w.Abilitato, w.Posizione, w.Formato ?? OverlayWidgetFormats.Predefinito(w.Tipo)));
        }

        // Un tipo non presente nel file compare comunque nella lista, spento, con i valori predefiniti.
        foreach (OverlayWidget predefinito in OverlayLayoutDefaults.Predefinito().Widget)
        {
            if (!MaskEditorCatalog.Multipla(predefinito.Tipo) && _widget.All(v => v.Tipo != predefinito.Tipo))
            {
                _widget.Add(new MaskWidgetDraft(predefinito.Tipo, false, predefinito.Posizione, predefinito.Formato ?? OverlayWidgetFormats.Predefinito(predefinito.Tipo)));
            }
        }
    }

    private int IndiceVicino(int indice, int verso)
    {
        if (indice < 0 || indice >= _widget.Count)
        {
            return -1;
        }

        OverlayPosition posizione = _widget[indice].Posizione;
        for (int i = indice + verso; i >= 0 && i < _widget.Count; i += verso)
        {
            if (_widget[i].Posizione == posizione)
            {
                return i;
            }
        }

        return -1;
    }

    private bool Scambia(int indice, int altro)
    {
        if (indice < 0 || indice >= _widget.Count || altro < 0 || altro >= _widget.Count)
        {
            return false;
        }

        (_widget[indice], _widget[altro]) = (_widget[altro], _widget[indice]);
        return true;
    }

    /// <summary>
    /// Formato con i soli campi previsti dal tipo. Un campo previsto ma assente prende il predefinito del tipo.
    /// </summary>
    internal static OverlayWidgetFormat NormalizzaFormato(OverlayWidgetKind tipo, OverlayWidgetFormat f)
    {
        OverlayWidgetFormat p = OverlayWidgetFormats.Predefinito(tipo);
        OverlayFormatField previsti = OverlayWidgetKinds.CampiPrevisti(tipo);

        return new OverlayWidgetFormat(
            Pattern: Previsto(previsti, OverlayFormatField.Pattern) ? f.Pattern ?? p.Pattern : null,
            Fuso: Previsto(previsti, OverlayFormatField.Fuso) ? f.Fuso ?? p.Fuso : null,
            Decimali: Previsto(previsti, OverlayFormatField.Decimali) ? f.Decimali ?? p.Decimali : null,
            MostraFase: Previsto(previsti, OverlayFormatField.MostraFase) ? f.MostraFase ?? p.MostraFase : null,
            MaxElementi: Previsto(previsti, OverlayFormatField.MaxElementi) ? f.MaxElementi ?? p.MaxElementi : null,
            Campi: Previsto(previsti, OverlayFormatField.Campi) ? f.Campi ?? p.Campi : null,
            FileLogo: Previsto(previsti, OverlayFormatField.FileLogo) ? f.FileLogo ?? p.FileLogo : null,
            AltezzaMaxPercento: Previsto(previsti, OverlayFormatField.AltezzaMaxPercento) ? f.AltezzaMaxPercento ?? p.AltezzaMaxPercento : null,
            Opacita: Previsto(previsti, OverlayFormatField.Opacita) ? f.Opacita ?? p.Opacita : null,
            Testo: Previsto(previsti, OverlayFormatField.Testo) ? f.Testo ?? p.Testo : null,
            MostraGain: Previsto(previsti, OverlayFormatField.MostraGain) ? f.MostraGain ?? p.MostraGain : null);
    }

    private static bool Previsto(OverlayFormatField previsti, OverlayFormatField campo) => previsti.HasFlag(campo);
}

/// <summary>Informazioni sui tipi di widget per l'editor: cosa si può aggiungere, cosa è ancora spento.</summary>
public static class MaskEditorCatalog
{
    /// <summary>Tutti i tipi, nell'ordine dell'enum. La vista li mostra nello stesso ordine.</summary>
    public static IReadOnlyList<OverlayWidgetKind> Tipi { get; } = Enum.GetValues<OverlayWidgetKind>();

    /// <summary>Tutte le posizioni, nell'ordine con cui compaiono nella tendina.</summary>
    public static IReadOnlyList<OverlayPosition> Posizioni { get; } = Enum.GetValues<OverlayPosition>();

    /// <summary>Logo ed etichette libere possono comparire più volte.</summary>
    public static bool Multipla(OverlayWidgetKind tipo) => OverlayWidgetKinds.AmmetteIstanzeMultiple(tipo);

    /// <summary>
    /// Vero se il tipo si può attivare. La bussola dipende dalla geometria del cielo (P3) e non è ancora disponibile:
    /// resta visibile nella lista ma con l'interruttore bloccato.
    /// </summary>
    public static bool Disponibile(OverlayWidgetKind tipo) => tipo != OverlayWidgetKind.Bussola;

    /// <summary>Campi di formato previsti per il tipo.</summary>
    public static OverlayFormatField CampiPrevisti(OverlayWidgetKind tipo) => OverlayWidgetKinds.CampiPrevisti(tipo);
}
