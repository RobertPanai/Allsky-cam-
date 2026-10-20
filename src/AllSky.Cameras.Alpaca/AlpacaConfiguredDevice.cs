namespace AllSky.Cameras.Alpaca;

/// <summary>Voce dell'elenco /management/v1/configureddevices di un server Alpaca.</summary>
/// <param name="DeviceName">Nome leggibile del dispositivo.</param>
/// <param name="DeviceType">Tipo del dispositivo, es. "Camera" o "Telescope".</param>
/// <param name="DeviceNumber">Numero del dispositivo nel server.</param>
/// <param name="UniqueId">Identificativo unico (campo UniqueID).</param>
public sealed record AlpacaConfiguredDevice(string DeviceName, string DeviceType, int DeviceNumber, string UniqueId);
