namespace AllSky.Imaging.Overlay;

/// <summary>Angolo dell'immagine a cui ancorare un testo (con margine), usato da <see cref="OverlayRenderer.DrawTextAnchored"/>.</summary>
public enum OverlayAnchor
{
    /// <summary>Angolo in alto a sinistra.</summary>
    TopLeft,

    /// <summary>Angolo in alto a destra.</summary>
    TopRight,

    /// <summary>Angolo in basso a sinistra.</summary>
    BottomLeft,

    /// <summary>Angolo in basso a destra.</summary>
    BottomRight
}
