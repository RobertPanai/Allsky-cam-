namespace AllSky.YouTube;

/// <summary>
/// Dati della ripresa usati per riempire i segnaposto di titolo e descrizione.
/// I valori vengono controllati in <see cref="TitleTemplate.Render"/> e <see cref="DescriptionTemplate.Render"/>.
/// </summary>
/// <param name="NowLocal">Istante locale, con offset, di riferimento per {data} e {ora}.</param>
/// <param name="NightStartLocal">Inizio della notte: il giorno locale del tramonto, usato per {notte}.</param>
/// <param name="Location">Luogo di ripresa, usato per {luogo}. Non nullo.</param>
/// <param name="Frames">Numero di fotogrammi, usato per {fotogrammi}. Non negativo.</param>
/// <param name="VideoDuration">Durata del video, usata per {durata} in formato mm:ss. Non negativa.</param>
/// <param name="Meteors">Numero di meteore, usato per {meteore}. Non negativo.</param>
/// <param name="Transits">Numero di transiti, usato per {transiti}. Non negativo.</param>
public sealed record TitleContext(
    DateTimeOffset NowLocal,
    DateTime NightStartLocal,
    string Location,
    int Frames,
    TimeSpan VideoDuration,
    int Meteors,
    int Transits);
