namespace AllSky.Host;

/// <summary>
/// Adattatore sottile: espone il protettore dei segreti della pubblicazione (DPAPI su Windows) come
/// <see cref="AllSky.YouTube.ISecretProtector"/>, cosi' le credenziali YouTube usano la stessa cifratura.
/// </summary>
public sealed class YouTubeSecretProtectorAdapter : AllSky.YouTube.ISecretProtector
{
    private readonly AllSky.Publish.ISecretProtector _inner;

    public YouTubeSecretProtectorAdapter(AllSky.Publish.ISecretProtector inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public byte[] Protect(byte[] plain) => _inner.Protect(plain);

    public byte[] Unprotect(byte[] protectedData) => _inner.Unprotect(protectedData);
}
