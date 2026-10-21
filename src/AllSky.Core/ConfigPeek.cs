using System.Text.Json;

namespace AllSky.Core;

/// <summary>
/// Lettura minima di config.json per l'avvio delle app Windows: solo lingua e porta, senza passare dal modello completo.
/// Sta in AllSky.Core (logica pura) perché la usano sia il Tray sia l'app desktop.
/// </summary>
public static class ConfigPeek
{
    /// <summary>Porta predefinita, usata se config.json manca o non la specifica.</summary>
    public const int DefaultPort = 5810;

    /// <summary>
    /// Legge lingua e porta da config.json. Se il file manca o non è leggibile restituisce i default (italiano, porta 5810).
    /// </summary>
    /// <param name="configPath">Percorso di config.json.</param>
    /// <returns>True se la lingua è "en"; la porta letta da ui.port.</returns>
    public static (bool English, int Port) Read(string configPath)
    {
        bool english = false;
        int port = DefaultPort;

        try
        {
            if (!File.Exists(configPath))
            {
                return (false, DefaultPort);
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (false, DefaultPort);
            }

            if (root.TryGetProperty("language", out JsonElement languageElement)
                && languageElement.ValueKind == JsonValueKind.String)
            {
                english = string.Equals(languageElement.GetString(), "en", StringComparison.OrdinalIgnoreCase);
            }

            if (root.TryGetProperty("ui", out JsonElement uiElement)
                && uiElement.ValueKind == JsonValueKind.Object
                && uiElement.TryGetProperty("port", out JsonElement portElement)
                && portElement.ValueKind == JsonValueKind.Number
                && portElement.TryGetInt32(out int configuredPort))
            {
                port = configuredPort;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // File illeggibile: restano i default. Il file JSON non valido lo rinomina l'host in ".bad".
        }

        return (english, port);
    }
}
