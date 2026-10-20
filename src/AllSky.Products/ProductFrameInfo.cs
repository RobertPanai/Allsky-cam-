namespace AllSky.Products;

/// <summary>
/// Metadati di un frame usati dai prodotti: istante di cattura e fase del cielo
/// (per esempio "day", "twilight", "night", come in docs/API.md). La fase è una stringa libera:
/// il chiamante decide quali fasi escludere, per esempio passando <c>skip</c> a <see cref="StartrailsBuilder.AddFrame"/>.
/// </summary>
public sealed record ProductFrameInfo(DateTimeOffset Timestamp, string Phase);
