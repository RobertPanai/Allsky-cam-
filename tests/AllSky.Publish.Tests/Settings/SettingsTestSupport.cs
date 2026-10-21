using System.Security.Cryptography;
using System.Text;
using AllSky.Core;

namespace AllSky.Publish.Tests.Settings;

/// <summary>Protettore finto: XOR reversibile con prefisso. Non e' sicurezza, serve solo a verificare il flusso.</summary>
public sealed class FakeProtector : ISecretProtector
{
    private static readonly byte[] Prefix = Encoding.ASCII.GetBytes("FAKE1:");

    public byte[] Protect(byte[] plain)
    {
        var output = new byte[Prefix.Length + plain.Length];
        Array.Copy(Prefix, 0, output, 0, Prefix.Length);
        for (int i = 0; i < plain.Length; i++)
            output[Prefix.Length + i] = (byte)(plain[i] ^ 0x5A);
        return output;
    }

    public byte[] Unprotect(byte[] protectedData)
    {
        if (protectedData.Length < Prefix.Length || !protectedData.AsSpan(0, Prefix.Length).SequenceEqual(Prefix))
            throw new CryptographicException("Dati non riconosciuti.");
        var plain = new byte[protectedData.Length - Prefix.Length];
        for (int i = 0; i < plain.Length; i++)
            plain[i] = (byte)(protectedData[Prefix.Length + i] ^ 0x5A);
        return plain;
    }
}

/// <summary>Protettore che cifra ma non riesce mai a decifrare (simula un segreto di un altro utente).</summary>
public sealed class FailingUnprotectProtector : ISecretProtector
{
    private readonly ISecretProtector _inner = new FakeProtector();

    public byte[] Protect(byte[] plain) => _inner.Protect(plain);

    public byte[] Unprotect(byte[] protectedData) => throw new CryptographicException("Decifratura non riuscita (simulata).");
}

/// <summary>Protettore non disponibile su questo sistema.</summary>
public sealed class UnsupportedProtector : ISecretProtector
{
    public byte[] Protect(byte[] plain) => throw new PlatformNotSupportedException("Simulato.");

    public byte[] Unprotect(byte[] protectedData) => throw new PlatformNotSupportedException("Simulato.");
}

/// <summary>Orologio finto con ora impostabile.</summary>
public sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset now) => UtcNow = now;

    public DateTimeOffset UtcNow { get; set; }
}

/// <summary>Cartella temporanea per un test, cancellata alla dispose.</summary>
public sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "allsky-publish-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Pulizia best effort.
        }
    }
}

/// <summary>Impostazioni valide di esempio (abilitate), per i test.</summary>
public static class SampleSettings
{
    public const string Token = "TOKEN-SEGRETO-di-prova-42";
    public const string HeaderValue = "HEADER-SEGRETO-di-prova-7";

    public static PublishSettings Valid() => new()
    {
        Enabled = true,
        ServerUrl = "https://server.example.org/base",
        DeviceId = "cam-01.nord_2",
        Token = new Secret(Token),
        ProfileName = "Cielo di prova",
        IntervalSeconds = 120,
        LongEdgePx = 1600,
        JpegQuality = 80,
        ExtraHeaderName = "X-Test-Key",
        ExtraHeaderValue = new Secret(HeaderValue),
    };
}
