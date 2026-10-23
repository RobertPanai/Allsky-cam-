using System.Globalization;
using System.Text.Json;

namespace AllSky.Studio;

/// <summary>
/// Esito del parsing di una <see cref="StudioOperation"/>: un'operazione tipizzata oppure un errore in italiano.
/// </summary>
public sealed record OperationParseResult
{
    /// <summary>Operazione tipizzata. Null se il parsing è fallito.</summary>
    public MontageOperation? Operation { get; private init; }

    /// <summary>Messaggio in italiano per l'utente. Null se il parsing è riuscito.</summary>
    public string? Error { get; private init; }

    /// <summary>Vero se l'operazione è valida.</summary>
    public bool IsValid => Error is null && Operation is not null;

    internal static OperationParseResult Ok(MontageOperation operation) => new() { Operation = operation };

    internal static OperationParseResult Fail(string error) => new() { Error = error };
}

/// <summary>
/// Converte <see cref="StudioOperation"/> (contenitore neutro del progetto) in operazioni tipizzate.
/// Non lancia eccezioni per input dell'utente: ogni problema diventa un errore in italiano.
/// I parametri sconosciuti vengono ignorati; il contenitore resta quello del progetto e non cambia.
/// </summary>
public static class OperationParser
{
    /// <summary>Soglia di default sulla deviazione standard della luminanza (0..255).</summary>
    public const double DefaultBlankStdDevMax = 2.0;

    /// <summary>Fattore k di default per l'esposizione anomala (mediana ± k·MAD).</summary>
    public const double DefaultMadFactor = 4.0;

    /// <summary>Frame rate di default del montaggio.</summary>
    public const int DefaultFps = 25;

    /// <summary>Frame rate minimo.</summary>
    public const int MinFps = 1;

    /// <summary>Frame rate massimo.</summary>
    public const int MaxFps = 120;

    /// <summary>Fattore di velocità minimo: 100 ripetizioni per frame al massimo.</summary>
    public const double MinSpeedFactor = 0.01;

    /// <summary>Fattore di velocità massimo: 1 frame ogni 100.</summary>
    public const double MaxSpeedFactor = 100;

    /// <summary>Limite superiore della soglia di frame vuoti (la scala della luminanza è 0..255).</summary>
    public const double MaxBlankStdDev = 255;

    /// <summary>Durata minima di una scheda titolo, in secondi.</summary>
    public const double MinTitleSeconds = 0.5;

    /// <summary>Durata massima di una scheda titolo, in secondi.</summary>
    public const double MaxTitleSeconds = 30;

    /// <summary>Durata minima di una dissolvenza, in secondi.</summary>
    public const double MinFadeSeconds = 0.1;

    /// <summary>Durata massima di una dissolvenza, in secondi.</summary>
    public const double MaxFadeSeconds = 10;

    /// <summary>Limite superiore del fattore K della rimozione per esposizione (solo buon senso).</summary>
    private const double MaxMadFactor = 1000;

    private static readonly string[] IsoUtcFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
        "yyyy-MM-dd'T'HH:mmK",
    ];

    private static readonly int[] AllowedRotations = [0, 90, 180, 270];

    /// <summary>Converte una singola operazione del progetto.</summary>
    /// <param name="operation">Operazione letta dal progetto. Può essere null se il JSON aveva una voce vuota.</param>
    public static OperationParseResult Parse(StudioOperation? operation)
    {
        if (operation is null)
        {
            return OperationParseResult.Fail("voce vuota.");
        }

        string type = operation.Type ?? string.Empty;
        if (type.Trim().Length == 0)
        {
            return OperationParseResult.Fail("tipo mancante.");
        }

        IReadOnlyDictionary<string, JsonElement> parameters = operation.Parameters
            ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var reader = new ParamReader(parameters);

        MontageOperation? parsed = type switch
        {
            StudioOperationTypes.Interval => ParseInterval(reader),
            StudioOperationTypes.RemoveFrames => ParseRemoveFrames(reader),
            StudioOperationTypes.AutoRemove => ParseAutoRemove(reader),
            StudioOperationTypes.Speed => ParseSpeed(reader),
            StudioOperationTypes.Crop => ParseCrop(reader),
            StudioOperationTypes.Rotate => ParseRotate(reader),
            StudioOperationTypes.Mirror => ParseMirror(reader),
            StudioOperationTypes.TitleCard => ParseTitleCard(reader),
            StudioOperationTypes.Fade => ParseFade(reader),
            StudioOperationTypes.InfoMask => new InfoMaskOperation(Copy(parameters)),
            StudioOperationTypes.ColorCorrection => new ColorCorrectionOperation(Copy(parameters)),
            _ => null,
        };

        if (parsed is null)
        {
            return OperationParseResult.Fail($"tipo di operazione non riconosciuto: «{Shorten(type)}».");
        }

        if (reader.Error is not null)
        {
            return OperationParseResult.Fail(reader.Error);
        }

        return OperationParseResult.Ok(parsed);
    }

    private static MontageOperation ParseInterval(ParamReader r)
    {
        DateTimeOffset? from = r.OptionalUtc("from");
        DateTimeOffset? to = r.OptionalUtc("to");
        if (from is not null && to is not null && from > to)
        {
            r.Fail("«from» deve essere precedente o uguale a «to».");
        }

        return new IntervalOperation(from, to);
    }

    private static MontageOperation ParseRemoveFrames(ParamReader r)
    {
        return new RemoveFramesOperation(r.FileNames("fileNames"));
    }

    private static MontageOperation ParseAutoRemove(ParamReader r)
    {
        bool blank = r.Flag("blankFrames", false);
        bool exposure = r.Flag("exposureOutliers", false);
        bool clouds = r.Flag("clouds", false);
        double blankMax = r.Real("blankStdDevMax", DefaultBlankStdDevMax, 0, MaxBlankStdDev);
        double madFactor = r.Real("madFactor", DefaultMadFactor, 0, MaxMadFactor, minExclusive: true);

        if (!blank && !exposure && !clouds)
        {
            r.Fail("«autoRemove» richiede almeno una regola attiva: blankFrames, exposureOutliers o clouds.");
        }

        return new AutoRemoveOperation(blank, exposure, clouds, blankMax, madFactor);
    }

    private static MontageOperation ParseSpeed(ParamReader r)
    {
        double factor = r.Real("factor", null, MinSpeedFactor, MaxSpeedFactor);
        int fps = r.Integer("fps", DefaultFps, MinFps, MaxFps);
        return new SpeedOperation(factor, fps);
    }

    private static MontageOperation ParseCrop(ParamReader r)
    {
        int x = r.Integer("x", null, 0, int.MaxValue);
        int y = r.Integer("y", null, 0, int.MaxValue);
        int width = r.Integer("width", null, 1, int.MaxValue);
        int height = r.Integer("height", null, 1, int.MaxValue);
        return new CropOperation(x, y, width, height);
    }

    private static MontageOperation ParseRotate(ParamReader r)
    {
        int degrees = r.Integer("degrees", null, 0, 270);
        if (Array.IndexOf(AllowedRotations, degrees) < 0)
        {
            r.Fail("«degrees» deve essere 0, 90, 180 o 270.");
        }

        return new RotateOperation(degrees);
    }

    private static MontageOperation ParseMirror(ParamReader r)
    {
        bool horizontal = r.Flag("horizontal", false);
        bool vertical = r.Flag("vertical", false);
        if (!horizontal && !vertical)
        {
            r.Fail("«mirror» richiede almeno un asse: horizontal o vertical.");
        }

        return new MirrorOperation(horizontal, vertical);
    }

    private static MontageOperation ParseTitleCard(ParamReader r)
    {
        string text = r.Text("text");
        double seconds = r.Real("durationSeconds", null, MinTitleSeconds, MaxTitleSeconds);
        string position = r.Choice("position", "start", "end");
        return new TitleCardOperation(text, seconds, position == "end" ? TitleCardPosition.End : TitleCardPosition.Start);
    }

    private static MontageOperation ParseFade(ParamReader r)
    {
        string kind = r.Choice("kind", "in", "out", "crossfade");
        double seconds = r.Real("durationSeconds", null, MinFadeSeconds, MaxFadeSeconds);
        FadeKind parsed = kind switch
        {
            "out" => FadeKind.Out,
            "crossfade" => FadeKind.Crossfade,
            _ => FadeKind.In,
        };
        return new FadeOperation(parsed, seconds);
    }

    private static Dictionary<string, JsonElement> Copy(IReadOnlyDictionary<string, JsonElement> parameters)
    {
        return parameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static string Shorten(string text)
    {
        return text.Length <= 40 ? text : text[..40] + "…";
    }

    /// <summary>
    /// Lettore dei parametri. Registra il primo errore; i valori restituiti dopo un errore non vanno usati.
    /// </summary>
    internal sealed class ParamReader
    {
        private readonly IReadOnlyDictionary<string, JsonElement> _values;

        public ParamReader(IReadOnlyDictionary<string, JsonElement> values)
        {
            _values = values;
        }

        /// <summary>Primo errore incontrato, in italiano. Null se tutto è valido.</summary>
        public string? Error { get; private set; }

        /// <summary>Registra un errore, se non ce n'è già uno.</summary>
        public void Fail(string message) => Error ??= message;

        /// <summary>Testo obbligatorio, senza spazi ai bordi e non vuoto.</summary>
        public string Text(string key)
        {
            if (!TryGet(key, out JsonElement value))
            {
                Fail($"manca il parametro «{key}».");
                return string.Empty;
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                Fail($"«{key}» deve essere un testo.");
                return string.Empty;
            }

            string text = (value.GetString() ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                Fail($"«{key}» non può essere vuoto.");
            }

            return text;
        }

        /// <summary>Data ISO 8601 in UTC facoltativa. Senza suffisso Z o +00:00 è un errore.</summary>
        public DateTimeOffset? OptionalUtc(string key)
        {
            if (!Has(key))
            {
                return null;
            }

            string text = Text(key);
            if (Error is not null)
            {
                return null;
            }

            bool explicitUtc = text.EndsWith('Z') || text.EndsWith('z') || text.EndsWith("+00:00", StringComparison.Ordinal);
            if (!explicitUtc
                || !DateTimeOffset.TryParseExact(text, IsoUtcFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset value)
                || value.Offset != TimeSpan.Zero)
            {
                Fail($"«{key}» deve essere una data ISO 8601 in UTC, per esempio 2026-10-22T20:00:00Z.");
                return null;
            }

            return value;
        }

        /// <summary>Elenco di nomi di file: solo nomi, senza cartelle, senza duplicati, non vuoto.</summary>
        public List<string> FileNames(string key)
        {
            var names = new List<string>();
            if (!TryGet(key, out JsonElement list))
            {
                Fail($"manca il parametro «{key}».");
                return names;
            }

            if (list.ValueKind != JsonValueKind.Array)
            {
                Fail($"«{key}» deve essere un elenco di nomi di file.");
                return names;
            }

            foreach (JsonElement item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    Fail($"«{key}» deve contenere solo testi.");
                    return names;
                }

                string name = (item.GetString() ?? string.Empty).Trim();
                if (name.Length == 0)
                {
                    Fail($"«{key}» contiene un nome vuoto.");
                    return names;
                }

                if (name.IndexOfAny(['/', '\\']) >= 0)
                {
                    Fail($"«{key}» deve contenere solo nomi di file, senza cartelle.");
                    return names;
                }

                if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    names.Add(name);
                }
            }

            if (names.Count == 0)
            {
                Fail($"«{key}» è vuoto.");
            }

            return names;
        }

        /// <summary>Valore vero/falso, con default se assente.</summary>
        public bool Flag(string key, bool defaultValue)
        {
            if (!TryGet(key, out JsonElement value))
            {
                return defaultValue;
            }

            if (value.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (value.ValueKind == JsonValueKind.False)
            {
                return false;
            }

            Fail($"«{key}» deve essere vero o falso.");
            return defaultValue;
        }

        /// <summary>Numero reale finito nell'intervallo. Senza default, l'assenza è un errore.</summary>
        public double Real(string key, double? defaultValue, double min, double max, bool minExclusive = false)
        {
            if (!TryGet(key, out JsonElement value))
            {
                if (defaultValue is double fallback)
                {
                    return fallback;
                }

                Fail($"manca il parametro «{key}».");
                return 0;
            }

            if (value.ValueKind != JsonValueKind.Number
                || !value.TryGetDouble(out double number)
                || !double.IsFinite(number))
            {
                Fail($"«{key}» deve essere un numero.");
                return 0;
            }

            bool tooLow = minExclusive ? number <= min : number < min;
            if (tooLow || number > max)
            {
                Fail($"«{key}» è fuori dall'intervallo consentito ({(minExclusive ? ">" : string.Empty)}{Show(min)} – {Show(max)}).");
                return 0;
            }

            return number;
        }

        /// <summary>Intero nell'intervallo. Senza default, l'assenza è un errore.</summary>
        public int Integer(string key, int? defaultValue, int min, int max)
        {
            if (!TryGet(key, out JsonElement value))
            {
                if (defaultValue is int fallback)
                {
                    return fallback;
                }

                Fail($"manca il parametro «{key}».");
                return 0;
            }

            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number))
            {
                Fail($"«{key}» deve essere un numero intero.");
                return 0;
            }

            if (number < min || number > max)
            {
                Fail($"«{key}» è fuori dall'intervallo consentito ({Show(min)} – {Show(max)}).");
                return 0;
            }

            return number;
        }

        /// <summary>Testo obbligatorio tra quelli ammessi.</summary>
        public string Choice(string key, params string[] allowed)
        {
            string text = Text(key);
            if (Error is not null)
            {
                return allowed[0];
            }

            if (Array.IndexOf(allowed, text) < 0)
            {
                Fail($"«{key}» deve essere uno di: {string.Join(", ", allowed)}.");
                return allowed[0];
            }

            return text;
        }

        private bool Has(string key) => TryGet(key, out _);

        private bool TryGet(string key, out JsonElement value)
        {
            if (_values.TryGetValue(key, out value) && value.ValueKind != JsonValueKind.Null)
            {
                return true;
            }

            value = default;
            return false;
        }

        private static string Show(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
