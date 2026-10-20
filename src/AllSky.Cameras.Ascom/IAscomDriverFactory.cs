namespace AllSky.Cameras.Ascom;

/// <summary>
/// Crea l'oggetto driver ASCOM. L'oggetto e' usato solo tramite <c>dynamic</c>, quindi puo' essere un
/// oggetto COM (binding tardivo) oppure un oggetto finto nei test.
/// Va sempre chiamato dal thread COM di <see cref="AscomComThread"/>.
/// </summary>
public interface IAscomDriverFactory
{
    /// <summary>Crea il driver identificato dal ProgID.</summary>
    /// <param name="progId">ProgID COM del driver.</param>
    object CreateDriver(string progId);
}

/// <summary>
/// Fabbrica di default: <see cref="Type.GetTypeFromProgID(string)"/> e <see cref="Activator.CreateInstance(Type)"/>.
/// Funziona solo su Windows; altrove lancia <see cref="NotSupportedException"/> con messaggio in italiano.
/// Scelta: nessuna dipendenza NuGet (niente ASCOM.DriverAccess), quindi i membri del driver si risolvono a runtime.
/// </summary>
public sealed class ComDriverFactory : IAscomDriverFactory
{
    /// <inheritdoc />
    public object CreateDriver(string progId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(progId);

        if (!OperatingSystem.IsWindows())
        {
            throw new NotSupportedException("i driver ASCOM COM sono disponibili solo su Windows.");
        }

        Type type = Type.GetTypeFromProgID(progId)
            ?? throw new InvalidOperationException($"driver ASCOM '{progId}' non registrato in COM.");

        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"impossibile istanziare il driver ASCOM '{progId}'.");
    }
}
