using System.Text;
using System.Text.Json;

namespace AllSky.Publish;

/// <summary>Esito dell'applicazione del file di avvio bootstrap.json.</summary>
public enum BootstrapResult
{
    /// <summary>Il file non esiste: nessuna modifica.</summary>
    NotFound,
    /// <summary>Il file era valido: impostazioni salvate e file cancellato (best effort).</summary>
    Applied,
    /// <summary>Il file non e' leggibile o non valido: ne' salvato ne' cancellato.</summary>
    Invalid,
}

/// <summary>
/// Applica il file di avvio <c>bootstrap.json</c> (campi camelCase: enabled, serverUrl, deviceId, token,
/// profileName, intervalSeconds, longEdgePx, jpegQuality, extraHeaderName, extraHeaderValue).
/// I campi assenti (o null) mantengono il valore corrente dello store; i campi sconosciuti vengono ignorati.
/// Il log riceve solo messaggi senza valori: mai token, valori degli header o indirizzi.
/// Scelta: un errore di Save (I/O o protezione non disponibile) non viene inghiottito: il file di avvio resta
/// intatto perche' la cancellazione avviene solo dopo un Save riuscito.
/// </summary>
public static class BootstrapLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Applica il file di avvio, se presente.</summary>
    /// <param name="bootstrapPath">Percorso di bootstrap.json.</param>
    /// <param name="store">Store in cui salvare le impostazioni fuse.</param>
    /// <param name="log">Callback di log (riceve solo testo sanificato, senza valori).</param>
    public static BootstrapResult Apply(string bootstrapPath, PublishSettingsStore store, Action<string> log)
    {
        ArgumentException.ThrowIfNullOrEmpty(bootstrapPath);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(log);

        if (!File.Exists(bootstrapPath))
            return BootstrapResult.NotFound;

        string text;
        try
        {
            text = File.ReadAllText(bootstrapPath, Encoding.UTF8);
        }
        catch (Exception)
        {
            Emit(log, "File di avvio non leggibile: impostazioni non applicate, file lasciato al suo posto.");
            return BootstrapResult.Invalid;
        }

        BootstrapFileDto? file;
        try
        {
            file = JsonSerializer.Deserialize<BootstrapFileDto>(text, JsonOptions);
        }
        catch (Exception)
        {
            file = null;
        }

        if (file is null)
        {
            Emit(log, "File di avvio non valido: il contenuto non e' un JSON leggibile. Il file e' stato lasciato al suo posto.");
            return BootstrapResult.Invalid;
        }

        PublishSettings merged = Merge(store.Load(), file);
        IReadOnlyList<SettingsError> errors = SettingsValidator.Validate(merged);
        if (errors.Count > 0)
        {
            foreach (string path in errors.Select(e => e.Path).Distinct())
                Emit(log, "File di avvio non valido, campo da correggere: " + path);
            Emit(log, "Impostazioni di avvio non applicate. Il file e' stato lasciato al suo posto.");
            return BootstrapResult.Invalid;
        }

        store.Save(merged);
        Emit(log, "Impostazioni di avvio applicate");

        OverwriteAndDelete(bootstrapPath, log);
        return BootstrapResult.Applied;
    }

    private static PublishSettings Merge(PublishSettings current, BootstrapFileDto f) => current with
    {
        Enabled = f.Enabled ?? current.Enabled,
        ServerUrl = f.ServerUrl ?? current.ServerUrl,
        DeviceId = f.DeviceId ?? current.DeviceId,
        Token = f.Token is null ? current.Token : new Secret(f.Token),
        ProfileName = f.ProfileName ?? current.ProfileName,
        IntervalSeconds = f.IntervalSeconds ?? current.IntervalSeconds,
        LongEdgePx = f.LongEdgePx ?? current.LongEdgePx,
        JpegQuality = f.JpegQuality ?? current.JpegQuality,
        ExtraHeaderName = f.ExtraHeaderName ?? current.ExtraHeaderName,
        ExtraHeaderValue = f.ExtraHeaderValue is null ? current.ExtraHeaderValue : new Secret(f.ExtraHeaderValue),
    };

    /// <summary>Sovrascrive il file con spazi (best effort) e poi lo cancella. Un errore di cancellazione non cambia l'esito.</summary>
    private static void OverwriteAndDelete(string path, Action<string> log)
    {
        try
        {
            long length = new FileInfo(path).Length;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            var blank = new byte[4096];
            Array.Fill(blank, (byte)' ');
            long remaining = length;
            while (remaining > 0)
            {
                int n = (int)Math.Min(blank.Length, remaining);
                fs.Write(blank, 0, n);
                remaining -= n;
            }
            fs.Flush(true);
        }
        catch (Exception)
        {
            // Best effort: si prosegue con la cancellazione.
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            Emit(log, "Cancellazione del file di avvio non riuscita.");
        }
    }

    private static void Emit(Action<string> log, string message)
    {
        try
        {
            log(message);
        }
        catch (Exception)
        {
            // Un logger guasto non deve impedire l'applicazione delle impostazioni.
        }
    }
}

/// <summary>Forma del file bootstrap.json. Ogni campo nullable: null = assente = valore corrente dello store.</summary>
internal sealed class BootstrapFileDto
{
    public bool? Enabled { get; set; }
    public string? ServerUrl { get; set; }
    public string? DeviceId { get; set; }
    public string? Token { get; set; }
    public string? ProfileName { get; set; }
    public int? IntervalSeconds { get; set; }
    public int? LongEdgePx { get; set; }
    public int? JpegQuality { get; set; }
    public string? ExtraHeaderName { get; set; }
    public string? ExtraHeaderValue { get; set; }
}
