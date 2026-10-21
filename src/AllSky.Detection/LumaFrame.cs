namespace AllSky.Detection;

/// <summary>
/// Fotogramma di luminanza a 8 bit, riga per riga: il pixel (x, y) sta in <c>Pixels[y * Width + x]</c>.
/// Il chiamante fornisce gia' il frame ridotto (per esempio con il lato lungo a 960 pixel).
/// </summary>
public record LumaFrame(int Width, int Height, byte[] Pixels, DateTimeOffset TimestampUtc);
