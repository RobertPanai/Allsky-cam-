namespace AllSky.Publish;

/// <summary>
/// Metadati della parte "meta" del multipart verso il server (schema v1). Serializzati da <see cref="UplinkJson.SerializeMeta"/>.
/// <see cref="CapturedAt"/> viene sempre scritto in UTC con millisecondi.
/// </summary>
/// <param name="SchemaVersion">Versione dello schema, oggi 1.</param>
/// <param name="DeviceId">ID dispositivo, 1-64 caratteri [A-Za-z0-9._-].</param>
/// <param name="ProfileName">Nome del profilo mostrato dal server; omesso se null.</param>
/// <param name="CaptureId">UUID della cattura.</param>
/// <param name="CapturedAt">Istante di cattura; convertito in UTC al momento della scrittura.</param>
/// <param name="ExposureMs">Esposizione in millisecondi; omessa se null o non finita.</param>
/// <param name="Gain">Guadagno; omesso se null o non finito.</param>
/// <param name="Mode">Modalita' del cielo, scritta in minuscolo (day, twilight, night).</param>
/// <param name="TemperatureC">Temperatura sensore in gradi C; scritta sempre, come null se assente o non finita.</param>
public sealed record UplinkMeta(
    int SchemaVersion,
    string DeviceId,
    string? ProfileName,
    string CaptureId,
    DateTimeOffset CapturedAt,
    double? ExposureMs,
    double? Gain,
    PublishMode Mode,
    double? TemperatureC);
