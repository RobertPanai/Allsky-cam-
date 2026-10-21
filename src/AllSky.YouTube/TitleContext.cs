namespace AllSky.YouTube;

/// <summary>Dati della ripresa usati per riempire i segnaposto di titolo e descrizione.</summary>
/// <param name="NowLocal">Istante locale, con offset, di riferimento per {data} e {ora}.</param>
/// <param name="NightStartLocal">Inizio della notte: il giorno locale del tramonto, usato per {notte}.</param>
/// <param name="Location">Luogo di ripresa, usato per {luogo}.</param>
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
    int Transits)
{
    public TitleContext
    {
        ArgumentNullException.ThrowIfNull(Location);
        if (Frames < 0) throw new ArgumentOutOfRangeException(nameof(Frames), Frames, "Il numero di fotogrammi non può essere negativo.");
        if (VideoDuration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(VideoDuration), VideoDuration, "La durata non può essere negativa.");
        if (Meteors < 0) throw new ArgumentOutOfRangeException(nameof(Meteors), Meteors, "Il numero di meteore non può essere negativo.");
        if (Transits < 0) throw new ArgumentOutOfRangeException(nameof(Transits), Transits, "Il numero di transiti non può essere negativo.");
    }
}
