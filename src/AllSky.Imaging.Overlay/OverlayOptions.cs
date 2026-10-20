namespace AllSky.Imaging.Overlay;

/// <summary>Opzioni di composizione della sovraimpressione.</summary>
/// <param name="ShowTime">Se disegnare l'ora locale in basso a sinistra.</param>
/// <param name="ShowSensorTemp">Se disegnare la temperatura del sensore in basso a destra.</param>
/// <param name="Language">Lingua delle etichette: "it" o "en" (confronto senza distinzione di maiuscole).</param>
/// <param name="Scale">Scala intera del testo, da 1 a 4.</param>
public sealed record OverlayOptions(bool ShowTime, bool ShowSensorTemp, string Language, int Scale);
