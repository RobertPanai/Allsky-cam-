using System.Text.Json.Serialization;

namespace AllSky.InfoOverlay;

/// <summary>
/// Layout della maschera informativa sul timelapse. Modello puro: nessun disegno, nessuna dipendenza da Windows.
/// Si serializza con System.Text.Json (nomi camelCase, enum come stringhe).
/// </summary>
public sealed record OverlayLayout(int Versione, OverlayMask Maschera, OverlayStyle Stile, IReadOnlyList<OverlayWidget> Widget);

/// <summary>Versione corrente del formato di <see cref="OverlayLayout"/>.</summary>
public static class OverlayLayoutVersions
{
    public const int Corrente = 1;
}

/// <summary>Maschera ai bordi del frame. Le coordinate sono frazioni: larghezza per X, altezza per Y e raggio.</summary>
public sealed record OverlayMask(OverlayMaskMode Modo, OverlayCircleMask Cerchio, OverlayBandMask Bande);

/// <summary>Cerchio del cielo. Centro in frazioni di larghezza (X) e altezza (Y); raggio in frazione dell'altezza.</summary>
public sealed record OverlayCircleMask(double CentroX, double CentroY, double Raggio, OverlayMaskEdge Esterno, double Sfumatura);

/// <summary>Bande nere sopra e sotto, in frazione dell'altezza del frame.</summary>
public sealed record OverlayBandMask(double Superiore, double Inferiore);

/// <summary>Stile del testo. Entrambi in frazione dell'altezza del frame.</summary>
public sealed record OverlayStyle(double ScalaTesto, double Margine);

/// <summary>Un elemento della maschera. <see cref="Formato"/> dipende dal tipo: i campi non previsti sono null.</summary>
public sealed record OverlayWidget(OverlayWidgetKind Tipo, bool Abilitato, OverlayPosition Posizione, int Ordine, OverlayWidgetFormat? Formato);

/// <summary>
/// Opzioni di formato di un widget. Ogni campo è valido solo per alcuni tipi di widget
/// (vedi <see cref="OverlayWidgetKinds"/>); gli altri restano null.
/// </summary>
public sealed record OverlayWidgetFormat(
    string? Pattern = null,
    OverlayTimeZone? Fuso = null,
    int? Decimali = null,
    bool? MostraFase = null,
    int? MaxElementi = null,
    IReadOnlyList<OverlayMeteoField>? Campi = null,
    string? FileLogo = null,
    double? AltezzaMaxPercento = null,
    double? Opacita = null,
    string? Testo = null,
    bool? MostraGain = null);

public enum OverlayMaskMode
{
    [JsonStringEnumMemberName("nessuna")] Nessuna,
    [JsonStringEnumMemberName("cerchio")] Cerchio,
    [JsonStringEnumMemberName("bande")] Bande,
}

public enum OverlayMaskEdge
{
    [JsonStringEnumMemberName("opaco")] Opaco,
    [JsonStringEnumMemberName("sfumato")] Sfumato,
}

public enum OverlayTimeZone
{
    [JsonStringEnumMemberName("locale")] Locale,
    [JsonStringEnumMemberName("utc")] Utc,
}

/// <summary>Posizione di un widget. Le posizioni d'angolo sono TL, TR, BL, BR; le bande usano le fasce nere della maschera.</summary>
public enum OverlayPosition
{
    [JsonStringEnumMemberName("TL")] TL,
    [JsonStringEnumMemberName("TR")] TR,
    [JsonStringEnumMemberName("BL")] BL,
    [JsonStringEnumMemberName("BR")] BR,
    [JsonStringEnumMemberName("BandaAlta")] BandaAlta,
    [JsonStringEnumMemberName("BandaBassa")] BandaBassa,
}

public enum OverlayWidgetKind
{
    [JsonStringEnumMemberName("dataOra")] DataOra,
    [JsonStringEnumMemberName("sole")] Sole,
    [JsonStringEnumMemberName("luna")] Luna,
    [JsonStringEnumMemberName("satelliti")] Satelliti,
    [JsonStringEnumMemberName("meteo")] Meteo,
    [JsonStringEnumMemberName("logo")] Logo,
    [JsonStringEnumMemberName("testoLibero")] TestoLibero,
    [JsonStringEnumMemberName("esposizione")] Esposizione,
    [JsonStringEnumMemberName("bussola")] Bussola,
}

public enum OverlayMeteoField
{
    [JsonStringEnumMemberName("temperatura")] Temperatura,
    [JsonStringEnumMemberName("umidita")] Umidita,
    [JsonStringEnumMemberName("pressione")] Pressione,
    [JsonStringEnumMemberName("vento")] Vento,
    [JsonStringEnumMemberName("nuvole")] Nuvole,
    [JsonStringEnumMemberName("pioggia")] Pioggia,
    [JsonStringEnumMemberName("luminositaCielo")] LuminositaCielo,
}
