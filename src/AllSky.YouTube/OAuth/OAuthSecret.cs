namespace AllSky.YouTube;

/// <summary>
/// Valore segreto (client secret, token). <see cref="ToString"/> non lo rivela mai: il contenuto si ottiene solo con
/// <see cref="Reveal"/>, da usare per l'invio HTTP e per la cifratura su disco.
/// </summary>
public readonly struct OAuthSecret
{
    private readonly string? _value;

    public OAuthSecret(string? value)
    {
        _value = string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>True se il segreto non e' presente.</summary>
    public bool IsEmpty => _value is null;

    /// <summary>Contenuto in chiaro: solo per l'header o il corpo della richiesta e per la cifratura.</summary>
    public string Reveal() => _value ?? string.Empty;

    public override string ToString() => IsEmpty ? "(vuoto)" : "***";
}
