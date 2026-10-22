namespace AllSky.InfoOverlay;

/// <summary>
/// Dimensione del frame in pixel. Usata dal composer e dal renderer per tradurre le frazioni del layout.
/// </summary>
public readonly record struct OverlayFrameSize(int Larghezza, int Altezza);

/// <summary>Colore RGBA a 8 bit. Tipo puro: il renderer lo converte nel colore del suo toolkit.</summary>
public readonly record struct OverlayColore(byte R, byte G, byte B, byte A = 255);

/// <summary>Allineamento orizzontale del testo dentro la propria area.</summary>
public enum OverlayAllineamento
{
    Sinistra,
    Centro,
    Destra,
}

/// <summary>
/// Un comando di disegno della maschera. La lista e' ordinata: il renderer disegna nell'ordine ricevuto,
/// quindi prima le parti di maschera e poi i widget.
/// </summary>
public abstract record OverlayComando;

/// <summary>Rettangolo pieno (bande nere, sfondi).</summary>
public sealed record OverlayRiempimento(OverlayRect Area, OverlayColore Colore) : OverlayComando;

/// <summary>
/// Maschera circolare del cielo, in pixel. Il renderer disegna in nero tutto cio' che sta fuori dal cerchio.
/// <see cref="Esterno"/> <c>Opaco</c>: bordo netto; <c>Sfumato</c>: sfumatura lineare di <see cref="Sfumatura"/> pixel
/// verso l'interno, a partire dal raggio.
/// </summary>
public sealed record OverlayMascheraCerchio(
    double CentroX,
    double CentroY,
    double Raggio,
    OverlayMaskEdge Esterno,
    double Sfumatura) : OverlayComando;

/// <summary>
/// Una riga di testo nella sua area. <see cref="AltezzaTestoPx"/> e' il corpo del font in pixel, coerente con
/// la misura usata per il posizionamento.
/// </summary>
public sealed record OverlayTestoPosizionato(
    OverlayRect Area,
    string Testo,
    double AltezzaTestoPx,
    OverlayAllineamento Allineamento,
    OverlayColore Colore) : OverlayComando;

/// <summary>
/// Logo PNG. <see cref="NomeFile"/> e' solo il nome del file, da cercare nella cartella loghi della cartella dati.
/// Il renderer lo adatta all'area mantenendo le proporzioni.
/// </summary>
public sealed record OverlayImmagine(OverlayRect Area, string NomeFile, double Opacita) : OverlayComando;

/// <summary>
/// Esito della composizione: comandi di disegno in ordine, widget omessi con il motivo e il corpo del font usato.
/// Non dipende da nessun toolkit grafico.
/// </summary>
public sealed record OverlayScene(
    OverlayFrameSize Frame,
    IReadOnlyList<OverlayComando> Comandi,
    IReadOnlyList<OverlayWidgetOmesso> Omessi,
    double AltezzaTestoPx);
