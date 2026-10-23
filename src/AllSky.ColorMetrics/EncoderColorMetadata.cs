using System.Text.Json;

namespace AllSky.ColorMetrics;

/// <summary>
/// Metadati colore dichiarati nel contenitore del video, letti dall'output di
/// <c>ffprobe -v error -select_streams v:0 -show_entries stream=codec_name,pix_fmt,color_range,color_space,color_transfer,color_primaries,width,height,r_frame_rate -of json</c>.
/// I campi assenti restano null. <see cref="Observations"/> elenca ciò che manca o è notevole.
/// </summary>
public sealed record EncoderColorInfo(
    bool HasVideoStream,
    string? CodecName,
    string? PixFmt,
    string? ColorRange,
    string? ColorSpace,
    string? ColorTransfer,
    string? ColorPrimaries,
    int? Width,
    int? Height,
    string? FrameRate,
    IReadOnlyList<string> Observations);

/// <summary>Parser dell'output JSON di ffprobe per i metadati colore.</summary>
public static class EncoderColorMetadata
{
    /// <summary>Legge il JSON di ffprobe. Lancia <see cref="JsonException"/> se il testo non è JSON valido.</summary>
    public static EncoderColorInfo Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using JsonDocument doc = JsonDocument.Parse(json);
        var observations = new List<string>();

        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("streams", out JsonElement streams)
            || streams.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Nell'output di ffprobe manca l'array 'streams'.");
        }

        if (streams.GetArrayLength() == 0)
        {
            observations.Add("nessuno stream video nel file");
            return new EncoderColorInfo(false, null, null, null, null, null, null, null, null, null, observations);
        }

        JsonElement s = streams[0];
        string? codec = Str(s, "codec_name");
        string? pixFmt = Str(s, "pix_fmt");
        string? range = Str(s, "color_range");
        string? space = Str(s, "color_space");
        string? transfer = Str(s, "color_transfer");
        string? primaries = Str(s, "color_primaries");
        int? width = Int(s, "width");
        int? height = Int(s, "height");
        string? fps = Str(s, "r_frame_rate");

        if (space is null)
        {
            observations.Add("color_space non dichiarato: il lettore deve indovinare la matrice");
        }
        else
        {
            observations.Add($"color_space dichiarato: {space}");
        }

        if (range is null)
        {
            observations.Add("color_range non dichiarato: il lettore assume il range di default");
        }
        else
        {
            observations.Add($"color_range dichiarato: {range}" + (range == "tv" ? " (range limitato)" : range == "pc" ? " (range pieno)" : string.Empty));
        }

        if (transfer is null)
        {
            observations.Add("color_transfer non dichiarato: il lettore assume la curva di default");
        }

        if (primaries is null)
        {
            observations.Add("color_primaries non dichiarato: il lettore assume i primari di default");
        }

        if (pixFmt is not null && pixFmt != "yuv420p")
        {
            observations.Add($"formato dei pixel {pixFmt}, diverso da yuv420p");
        }

        return new EncoderColorInfo(true, codec, pixFmt, range, space, transfer, primaries, width, height, fps, observations);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i) ? i : null;
}
