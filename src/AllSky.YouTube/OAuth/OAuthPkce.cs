using System.Security.Cryptography;
using System.Text;

namespace AllSky.YouTube;

/// <summary>
/// PKCE (RFC 7636, metodo S256, come chiede la guida Google "OAuth 2.0 for Mobile &amp; Desktop Apps") e state
/// anti-CSRF. Tutti i valori casuali vengono da <see cref="RandomNumberGenerator"/>.
/// </summary>
public static class OAuthPkce
{
    /// <summary>Valore del parametro code_challenge_method.</summary>
    public const string ChallengeMethod = "S256";

    /// <summary>32 byte casuali = 43 caratteri base64url: il minimo consentito dalla RFC 7636 (43..128).</summary>
    private const int RandomBytes = 32;

    /// <summary>Nuovo code_verifier.</summary>
    public static string NewVerifier() => Base64Url(RandomNumberGenerator.GetBytes(RandomBytes));

    /// <summary>Nuovo state per la verifica del redirect.</summary>
    public static string NewState() => Base64Url(RandomNumberGenerator.GetBytes(RandomBytes));

    /// <summary>code_challenge = BASE64URL(SHA256(ASCII(code_verifier))).</summary>
    /// <exception cref="ArgumentException">Il verifier non rispetta la RFC 7636.</exception>
    public static string ComputeChallenge(string verifier)
    {
        if (!IsValidVerifier(verifier))
            throw new ArgumentException("Il code_verifier non rispetta la RFC 7636 (43-128 caratteri non riservati).", nameof(verifier));

        byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64Url(hash);
    }

    /// <summary>True se il verifier ha 43-128 caratteri del set non riservato della RFC 7636.</summary>
    public static bool IsValidVerifier(string? verifier)
    {
        if (verifier is null || verifier.Length < 43 || verifier.Length > 128)
            return false;

        foreach (char c in verifier)
        {
            bool unreserved = c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '.' or '_' or '~';
            if (!unreserved)
                return false;
        }

        return true;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
