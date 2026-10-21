using AllSky.Publish;

namespace AllSky.Host;

/// <summary>
/// Protezione dei segreti per i sistemi senza DPAPI: ogni operazione lancia <see cref="PlatformNotSupportedException"/>.
/// Lo store la traduce in un errore di salvataggio, quindi la pubblicazione resta spenta senza fermare l'Host.
/// </summary>
internal sealed class UnsupportedSecretProtector : ISecretProtector
{
    private const string Message =
        "La protezione dei segreti richiede Windows (DPAPI): la pubblicazione non e' disponibile su questo sistema.";

    /// <summary>Lancia sempre <see cref="PlatformNotSupportedException"/>.</summary>
    public byte[] Protect(byte[] plain) => throw new PlatformNotSupportedException(Message);

    /// <summary>Lancia sempre <see cref="PlatformNotSupportedException"/>.</summary>
    public byte[] Unprotect(byte[] protectedData) => throw new PlatformNotSupportedException(Message);
}
