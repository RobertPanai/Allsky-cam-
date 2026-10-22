using AllSky.Core.Settings;

namespace AllSky.Host;

/// <summary>
/// Controllo della password per le richieste dalla rete locale (HTTP Basic). Le richieste dal computer stesso
/// non passano di qui. Ricorda l'ultima intestazione verificata con successo: PBKDF2 è lento e il browser
/// ripete l'intestazione su ogni richiesta, quindi senza memoria ogni caricamento costerebbe centinaia di millisecondi.
/// </summary>
internal sealed class LanAccess
{
    private readonly object _gate = new();
    private string? _verifiedHeader;  // _gate
    private string? _verifiedHash;    // _gate

    /// <summary>Vero se l'intestazione Authorization contiene la password giusta per l'hash salvato.</summary>
    /// <param name="authorizationHeader">Valore dell'intestazione, o null.</param>
    /// <param name="storedHash">Hash da <c>ui.lanPasswordHash</c>.</param>
    public bool IsAuthorized(string? authorizationHeader, string storedHash)
    {
        if (string.IsNullOrEmpty(authorizationHeader) || string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        lock (_gate)
        {
            if (authorizationHeader == _verifiedHeader && storedHash == _verifiedHash)
            {
                return true;
            }
        }

        if (!LanPassword.TryReadBasicPassword(authorizationHeader, out string password)
            || !LanPassword.Verify(password, storedHash))
        {
            return false;
        }

        lock (_gate)
        {
            _verifiedHeader = authorizationHeader;
            _verifiedHash = storedHash;
        }

        return true;
    }
}
