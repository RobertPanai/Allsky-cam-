using System.Security.Cryptography;
using System.Text;

namespace AllSky.Core.Settings;

/// <summary>Esito del controllo di una password per la rete locale.</summary>
public enum LanPasswordProblem
{
    /// <summary>Password accettabile.</summary>
    None,

    /// <summary>Password vuota o solo spazi.</summary>
    Empty,

    /// <summary>Meno di <see cref="LanPassword.MinLength"/> caratteri.</summary>
    TooShort,
}

/// <summary>
/// Password per l'accesso dalla rete locale all'interfaccia web. Si conserva solo l'hash PBKDF2-SHA256 con sale casuale,
/// nel formato <c>pbkdf2-sha256$&lt;iterazioni&gt;$&lt;sale base64&gt;$&lt;hash base64&gt;</c>. Logica pura.
/// </summary>
public static class LanPassword
{
    /// <summary>Lunghezza minima, in caratteri.</summary>
    public const int MinLength = 8;

    /// <summary>Iterazioni PBKDF2 (raccomandazione OWASP per PBKDF2-HMAC-SHA256).</summary>
    public const int Iterations = 600_000;

    private const string Prefix = "pbkdf2-sha256";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>Controlla la password prima di calcolarne l'hash.</summary>
    /// <param name="password">Password digitata.</param>
    public static LanPasswordProblem Check(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return LanPasswordProblem.Empty;
        }

        return password.Length < MinLength ? LanPasswordProblem.TooShort : LanPasswordProblem.None;
    }

    /// <summary>Calcola l'hash da salvare in <c>ui.lanPasswordHash</c>. Lancia se la password non è accettabile.</summary>
    /// <param name="password">Password in chiaro.</param>
    /// <exception cref="ArgumentException">La password non supera <see cref="Check"/>.</exception>
    public static string Hash(string password)
    {
        if (Check(password) != LanPasswordProblem.None)
        {
            throw new ArgumentException("La password non è accettabile.", nameof(password));
        }

        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Derive(password, salt, Iterations);
        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Verifica una password contro l'hash salvato. Restituisce false se l'hash manca o non è nel formato atteso.
    /// Il confronto non dipende dalla posizione del primo carattere diverso.
    /// </summary>
    /// <param name="password">Password ricevuta.</param>
    /// <param name="storedHash">Hash da <c>ui.lanPasswordHash</c>; null se non impostata.</param>
    public static bool Verify(string? password, string? storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        string[] parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out int iterations) || iterations < 1)
        {
            return false;
        }

        try
        {
            byte[] salt = Convert.FromBase64String(parts[2]);
            byte[] expected = Convert.FromBase64String(parts[3]);
            if (salt.Length != SaltBytes || expected.Length != HashBytes)
            {
                return false;
            }

            byte[] actual = Derive(password, salt, iterations);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Legge la password da un'intestazione <c>Authorization: Basic ...</c> (utente qualsiasi, password dopo i due punti).
    /// </summary>
    /// <param name="authorizationHeader">Valore dell'intestazione, o null.</param>
    /// <param name="password">Password estratta; stringa vuota se non c'è.</param>
    /// <returns>True se l'intestazione è Basic ben formata.</returns>
    public static bool TryReadBasicPassword(string? authorizationHeader, out string password)
    {
        password = string.Empty;
        const string scheme = "Basic ";
        if (string.IsNullOrEmpty(authorizationHeader)
            || !authorizationHeader.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string encoded = authorizationHeader[scheme.Length..].Trim();
        try
        {
            string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            int colon = decoded.IndexOf(':');
            if (colon < 0)
            {
                return false;
            }

            password = decoded[(colon + 1)..];
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, HashBytes);
}
