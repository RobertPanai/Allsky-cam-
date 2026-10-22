namespace AllSky.InfoOverlay;

/// <summary>Layout predefinito: cerchio cielo, DataOra in basso a sinistra, Sole e Luna in basso a destra.</summary>
public static class OverlayLayoutDefaults
{
    public static OverlayLayout Predefinito() => new(
        Versione: OverlayLayoutVersions.Corrente,
        Maschera: new OverlayMask(
            Modo: OverlayMaskMode.Cerchio,
            Cerchio: new OverlayCircleMask(CentroX: 0.5, CentroY: 0.5, Raggio: 0.5, Esterno: OverlayMaskEdge.Opaco, Sfumatura: 0.02),
            Bande: new OverlayBandMask(Superiore: 0.1, Inferiore: 0.1)),
        Stile: new OverlayStyle(ScalaTesto: 0.028, Margine: 0.02),
        Widget: new[]
        {
            Crea(OverlayWidgetKind.DataOra, true, OverlayPosition.BL, 0),
            Crea(OverlayWidgetKind.Sole, true, OverlayPosition.BR, 0),
            Crea(OverlayWidgetKind.Luna, true, OverlayPosition.BR, 1),
            Crea(OverlayWidgetKind.Satelliti, false, OverlayPosition.TL, 0),
            Crea(OverlayWidgetKind.Meteo, false, OverlayPosition.TR, 0),
            Crea(OverlayWidgetKind.Logo, false, OverlayPosition.TR, 1),
            Crea(OverlayWidgetKind.TestoLibero, false, OverlayPosition.TL, 1),
            Crea(OverlayWidgetKind.Esposizione, false, OverlayPosition.BL, 1),
            Crea(OverlayWidgetKind.Bussola, false, OverlayPosition.TR, 2),
        });

    /// <summary>Un widget con il formato predefinito del suo tipo.</summary>
    private static OverlayWidget Crea(OverlayWidgetKind tipo, bool abilitato, OverlayPosition posizione, int ordine) =>
        new(tipo, abilitato, posizione, ordine, OverlayWidgetFormats.Predefinito(tipo));
}
