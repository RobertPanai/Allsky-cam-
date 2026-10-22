using Xunit;
namespace AllSky.Studio.Tests;

public sealed class ExifDateReaderTests : IDisposable
{
    private readonly TempFolder _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private static bool Read(byte[] bytes, out DateTime local) =>
        ExifDateReader.TryReadBuffer(bytes, bytes.Length, out local);

    [Fact]
    public void LittleEndian_LeggeDateTimeOriginal()
    {
        byte[] jpeg = JpegBuilder.WithExif("2026:10:23 22:15:30", null);

        Assert.True(Read(jpeg, out DateTime local));
        Assert.Equal(new DateTime(2026, 10, 23, 22, 15, 30), local);
    }

    [Fact]
    public void BigEndian_LeggeDateTimeOriginal()
    {
        byte[] jpeg = JpegBuilder.WithExif("2026:10:23 22:15:30", null, bigEndian: true);

        Assert.True(Read(jpeg, out DateTime local));
        Assert.Equal(new DateTime(2026, 10, 23, 22, 15, 30), local);
    }

    [Fact]
    public void SenzaDateTimeOriginal_RipiegaSuDateTime()
    {
        byte[] jpeg = JpegBuilder.WithExif(null, "2026:01:02 03:04:05");

        Assert.True(Read(jpeg, out DateTime local));
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5), local);
    }

    [Fact]
    public void DateTimeOriginalHaPrecedenzaSuDateTime()
    {
        byte[] jpeg = JpegBuilder.WithExif("2026:10:23 22:15:30", "2020:01:01 00:00:00", bigEndian: true);

        Assert.True(Read(jpeg, out DateTime local));
        Assert.Equal(new DateTime(2026, 10, 23, 22, 15, 30), local);
    }

    [Fact]
    public void FileTroncato_NessunValore()
    {
        byte[] jpeg = JpegBuilder.WithExif("2026:10:23 22:15:30", null, cutAt: 60);

        Assert.False(Read(jpeg, out _));
    }

    [Fact]
    public void Png_NessunValoreSenzaEccezioni()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

        Assert.False(Read(png, out _));
    }

    [Fact]
    public void DataNonValida_NessunValore()
    {
        byte[] jpeg = JpegBuilder.WithExif("0000:00:00 00:00:00", null);

        Assert.False(Read(jpeg, out _));
    }

    [Fact]
    public void DataConFormatoErrato_NessunValore()
    {
        byte[] jpeg = JpegBuilder.WithExif("2026-10-23 22:15:30", null);

        Assert.False(Read(jpeg, out _));
    }

    [Fact]
    public void JpegSenzaExif_NessunValore()
    {
        Assert.False(Read(JpegBuilder.Plain(), out _));
    }

    [Fact]
    public void DatiCasuali_MaiEccezioni()
    {
        var random = new Random(20261023);
        byte[] valid = JpegBuilder.WithExif("2026:10:23 22:15:30", "2026:10:23 22:15:30");
        for (int round = 0; round < 3000; round++)
        {
            byte[] buffer;
            if (round % 2 == 0)
            {
                buffer = new byte[random.Next(0, 300)];
                random.NextBytes(buffer);
            }
            else
            {
                buffer = (byte[])valid.Clone();
                for (int flips = 0; flips < 4; flips++)
                {
                    buffer[random.Next(buffer.Length)] = (byte)random.Next(256);
                }
            }

            int length = random.Next(0, buffer.Length + 1);
            ExifDateReader.TryReadBuffer(buffer, length, out _);
        }
    }

    [Fact]
    public void LetturaDaFile_TroncaEMancanti_SenzaEccezioni()
    {
        string jpegPath = _tmp.FilePath("ok.jpg");
        File.WriteAllBytes(jpegPath, JpegBuilder.WithExif("2026:10:23 22:15:30", null));
        string truncPath = _tmp.FilePath("tronco.jpg");
        File.WriteAllBytes(truncPath, JpegBuilder.WithExif("2026:10:23 22:15:30", null, cutAt: 40));

        Assert.True(ExifDateReader.TryReadFile(jpegPath, out DateTime local));
        Assert.Equal(new DateTime(2026, 10, 23, 22, 15, 30), local);
        Assert.False(ExifDateReader.TryReadFile(truncPath, out _));
        Assert.False(ExifDateReader.TryReadFile(_tmp.FilePath("assente.jpg"), out _));
        Assert.False(ExifDateReader.TryReadFile(_tmp.Root, out _));
    }
}

public sealed class LocalTimeTests
{
    private static TimeZoneInfo Rome()
    {
        Assert.True(LocalTime.TryResolveZone("Europe/Rome", out TimeZoneInfo? zone));
        return zone!;
    }

    [Fact]
    public void EstateOraLegale_ConvertitaConOffsetDue()
    {
        DateTimeOffset utc = LocalTime.ToUtc(new DateTime(2026, 7, 1, 12, 0, 0), Rome(), out LocalTimeNote note);

        Assert.Equal(LocalTimeNote.None, note);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero), utc);
    }

    [Fact]
    public void InvernoOraSolare_ConvertitaConOffsetUno()
    {
        DateTimeOffset utc = LocalTime.ToUtc(new DateTime(2026, 1, 15, 12, 0, 0), Rome(), out LocalTimeNote note);

        Assert.Equal(LocalTimeNote.None, note);
        Assert.Equal(new DateTimeOffset(2026, 1, 15, 11, 0, 0, TimeSpan.Zero), utc);
    }

    [Fact]
    public void FineOraLegale_2026_10_25_0230_Ambiguo_SceltaLaPrimaOccorrenza()
    {
        // Il 25/10/2026 alle 03:00 CEST l'orologio torna alle 02:00 CET: 02:00-02:59 compaiono due volte.
        // Scelta documentata: l'occorrenza più antica in UTC (ora legale, offset +2), cioè 00:30 UTC.
        DateTimeOffset utc = LocalTime.ToUtc(new DateTime(2026, 10, 25, 2, 30, 0), Rome(), out LocalTimeNote note);

        Assert.Equal(LocalTimeNote.Ambiguous, note);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), utc);
    }

    [Fact]
    public void FineOraLegale_2026_10_25_0130_NonAmbiguo()
    {
        // Nota: 01:30 del 25/10/2026 compare una sola volta (è ancora CEST, +2), quindi non è ambiguo.
        // L'ambiguità inizia alle 02:00.
        DateTimeOffset utc = LocalTime.ToUtc(new DateTime(2026, 10, 25, 1, 30, 0), Rome(), out LocalTimeNote note);

        Assert.Equal(LocalTimeNote.None, note);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 23, 30, 0, TimeSpan.Zero), utc);
    }

    [Fact]
    public void InizioOraLegale_2026_03_29_0230_Inesistente_UsaOffsetPrecedente()
    {
        // Il 29/03/2026 alle 02:00 CET l'orologio salta alle 03:00 CEST: 02:00-02:59 non esistono.
        // Scelta documentata: offset in vigore prima del salto (+1), quindi 01:30 UTC. Il frame non si scarta.
        DateTimeOffset utc = LocalTime.ToUtc(new DateTime(2026, 3, 29, 2, 30, 0), Rome(), out LocalTimeNote note);

        Assert.Equal(LocalTimeNote.Nonexistent, note);
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), utc);
    }

    [Fact]
    public void InizioOraLegale_DopoIlSalto_Normale()
    {
        DateTimeOffset utc = LocalTime.ToUtc(new DateTime(2026, 3, 29, 3, 30, 0), Rome(), out LocalTimeNote note);

        Assert.Equal(LocalTimeNote.None, note);
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), utc);
    }

    [Fact]
    public void SenzaFuso_TrattaComeUtc()
    {
        DateTimeOffset utc = LocalTime.ToUtc(new DateTime(2026, 3, 29, 2, 30, 0), null, out LocalTimeNote note);

        Assert.Equal(LocalTimeNote.None, note);
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 2, 30, 0, TimeSpan.Zero), utc);
    }

    [Fact]
    public void IdentificatoriNonValidi_NonRisolti()
    {
        Assert.False(LocalTime.TryResolveZone("Luogo/Inventato", out _));
        Assert.False(LocalTime.TryResolveZone("", out _));
        Assert.False(LocalTime.TryResolveZone(null, out _));
    }

    [Fact]
    public void NomeWindows_RisoltoComeIana()
    {
        // .NET risolve anche gli ID Windows su Linux (ICU). Se mancasse, il progetto usa l'IANA.
        Assert.True(LocalTime.TryResolveZone("W. Europe Standard Time", out TimeZoneInfo? zone));
        Assert.Equal(TimeSpan.FromHours(2), zone!.GetUtcOffset(new DateTime(2026, 7, 1, 12, 0, 0)));
    }
}
