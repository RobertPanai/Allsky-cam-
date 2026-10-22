using AllSky.Core;

namespace AllSky.Publish.Monitor;

/// <summary>
/// Costruisce il <see cref="MonitorSnapshot"/> dalle voci attive. Funzione pura: nessun I/O, nessun orologio
/// (l'ora arriva come parametro). Un dato entra nel payload solo se la sua voce è attiva.
/// </summary>
public static class MonitorSnapshotBuilder
{
    /// <summary>Lunghezza massima del nome del soggetto, in caratteri.</summary>
    public const int MaxTargetNameLength = 80;

    /// <summary>Numero massimo di punti della serie di altezza.</summary>
    public const int MaxChartPoints = 96;

    /// <summary>
    /// Fasi della notte ammesse: le tre categorie di <c>SkyPhases.Simplify</c> in AllSky.Astro.
    /// Duplicate qui perché AllSky.Publish non referenzia AllSky.Astro.
    /// </summary>
    public static IReadOnlyList<string> NightPhases { get; } = ["day", "twilight", "night"];

    /// <summary>
    /// Stati di park ammessi: i nomi di <c>ParkStatus</c> in AllSky.Nina, nella forma camelCase con cui il progetto
    /// serializza gli enum. Duplicati qui perché AllSky.Publish non referenzia AllSky.Nina.
    /// </summary>
    public static IReadOnlyList<string> ParkStates { get; } =
        ["parked", "tracking", "slewing", "stopped", "unknown", "disconnected"];

    /// <summary>
    /// Restituisce l'istantanea da pubblicare, oppure null se il monitoraggio è spento.
    /// </summary>
    public static MonitorSnapshot? Build(AppConfig.MonitorConfig config, MonitorInputs inputs, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(inputs);

        if (!config.Enabled)
        {
            return null;
        }

        AppConfig.MonitorConfig.MonitorVoices voices = config.Voices ?? new();

        return new MonitorSnapshot(
            SchemaVersion: MonitorSnapshot.CurrentSchemaVersion,
            UpdatedUtc: nowUtc.ToUniversalTime(),
            NightPhase: voices.NightPhase ? ClosedValue(inputs.NightPhase, NightPhases) : null,
            SunAltitudeDeg: voices.SunMoonAltitude ? Round1(inputs.SunAltitudeDeg) : null,
            MoonAltitudeDeg: voices.SunMoonAltitude ? Round1(inputs.MoonAltitudeDeg) : null,
            TargetName: voices.NinaTarget ? SanitizeTargetName(inputs.TargetName) : null,
            ParkState: voices.NinaParkState ? ClosedValue(inputs.ParkState, ParkStates) : null,
            MoonSeparationDeg: voices.MoonDistance ? Round1(inputs.MoonSeparationDeg) : null,
            TargetAltitudeChart: voices.TargetAltitudeChart ? BuildChart(inputs.TargetAltitudeChart) : null,
            Assets: BuildAssets(voices, inputs));
    }

    /// <summary>Solo il nome del file: separatori Windows e Unix, nessuna cartella. Null se il nome non è utilizzabile.</summary>
    public static string? FileNameOnly(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        int cut = path.LastIndexOfAny(['/', '\\']);
        string name = (cut >= 0 ? path[(cut + 1)..] : path).Trim();
        if (name.Length == 0 || name == "." || name == ".." || name.Any(char.IsControl))
        {
            return null;
        }

        return name;
    }

    private static IReadOnlyList<MonitorAsset> BuildAssets(AppConfig.MonitorConfig.MonitorVoices voices, MonitorInputs inputs)
    {
        var assets = new List<MonitorAsset>(4);
        AddAsset(assets, voices.AllSkyImage, MonitorAssetKind.AllSkyImage, inputs.AllSkyImagePath);
        AddAsset(assets, voices.LatestTimelapse, MonitorAssetKind.LatestTimelapse, inputs.LatestTimelapsePath);
        AddAsset(assets, voices.Keogram, MonitorAssetKind.Keogram, inputs.KeogramPath);
        AddAsset(assets, voices.StartTrails, MonitorAssetKind.StartTrails, inputs.StartTrailsPath);
        return assets;
    }

    private static void AddAsset(List<MonitorAsset> assets, bool enabled, MonitorAssetKind kind, string? path)
    {
        if (!enabled)
        {
            return;
        }

        string? fileName = FileNameOnly(path);
        if (fileName is not null)
        {
            assets.Add(new MonitorAsset(kind, fileName));
        }
    }

    /// <summary>Altezza o distanza in gradi, arrotondata a 0,1. Null se assente o non finita.</summary>
    private static double? Round1(double? value)
    {
        if (value is not { } v || !double.IsFinite(v))
        {
            return null;
        }

        return Math.Round(v, 1, MidpointRounding.AwayFromZero);
    }

    /// <summary>Valore ammesso da un elenco chiuso, confronto senza distinzione di maiuscole. Altrimenti null.</summary>
    private static string? ClosedValue(string? value, IReadOnlyList<string> allowed)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string candidate = value.Trim().ToLowerInvariant();
        return allowed.Contains(candidate) ? candidate : null;
    }

    /// <summary>Nome del soggetto: senza caratteri di controllo, senza spazi ai bordi, al massimo 80 caratteri.</summary>
    private static string? SanitizeTargetName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        string clean = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length == 0)
        {
            return null;
        }

        if (clean.Length <= MaxTargetNameLength)
        {
            return clean;
        }

        int length = MaxTargetNameLength;
        // Non spezzare una coppia di surrogati: il carattere di coda sarebbe illeggibile.
        if (char.IsHighSurrogate(clean[length - 1]))
        {
            length--;
        }

        return clean[..length];
    }

    /// <summary>
    /// Serie di altezza: punti con altezza finita, altezza a 0,1 gradi, al massimo 96 punti. Se la serie è più lunga
    /// si prende un punto ogni N, con N minimo che rispetta il limite e primo punto sempre incluso.
    /// </summary>
    private static IReadOnlyList<MonitorAltitudePoint>? BuildChart(IReadOnlyList<MonitorAltitudePoint>? source)
    {
        if (source is null)
        {
            return null;
        }

        var valid = new List<MonitorAltitudePoint>(source.Count);
        foreach (MonitorAltitudePoint point in source)
        {
            if (double.IsFinite(point.AltitudeDeg))
            {
                valid.Add(new MonitorAltitudePoint(point.TimeUtc.ToUniversalTime(), Math.Round(point.AltitudeDeg, 1, MidpointRounding.AwayFromZero)));
            }
        }

        if (valid.Count <= MaxChartPoints)
        {
            return valid;
        }

        int step = (valid.Count + MaxChartPoints - 1) / MaxChartPoints;
        var decimated = new List<MonitorAltitudePoint>(MaxChartPoints);
        for (int i = 0; i < valid.Count; i += step)
        {
            decimated.Add(valid[i]);
        }

        return decimated;
    }
}
