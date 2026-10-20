using System.Dynamic;
using AllSky.Core;

namespace AllSky.Cameras.Ascom.Tests;

/// <summary>
/// Driver ASCOM finto: proprieta' e metodi sono risolti a runtime tramite <see cref="DynamicObject"/>.
/// Un valore di tipo <see cref="Exception"/> lancia alla lettura; un <see cref="Func{TResult}"/> di tipo
/// <c>Func&lt;object?&gt;</c> viene invocato a ogni lettura. Le scritture e i metodi finiscono in <see cref="Calls"/>.
/// </summary>
public sealed class FakeAscomDriver : DynamicObject
{
    private readonly Dictionary<string, object?> _members = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<object?[], object?>> _methods = new(StringComparer.Ordinal);

    /// <summary>Registro delle chiamate: nomi dei metodi invocati e scritture delle proprieta'.</summary>
    public List<string> Calls { get; } = new();

    /// <summary>Legge il valore memorizzato di una proprieta' senza passare dal binder dinamico.</summary>
    public object? Get(string name) => _members.TryGetValue(name, out object? value) ? value : null;

    /// <summary>Imposta il valore di una proprieta' (anche un Exception o un Func&lt;object?&gt;).</summary>
    public void Set(string name, object? value) => _members[name] = value;

    /// <summary>Imposta il gestore di un metodo.</summary>
    public void OnMethod(string name, Func<object?[], object?> handler) => _methods[name] = handler;

    /// <inheritdoc />
    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        if (!_members.TryGetValue(binder.Name, out object? value))
        {
            result = null;
            return false;
        }

        if (value is Exception ex)
        {
            throw ex;
        }

        result = value is Func<object?> read ? read() : value;
        return true;
    }

    /// <inheritdoc />
    public override bool TrySetMember(SetMemberBinder binder, object? value)
    {
        Calls.Add($"set {binder.Name}={value}");
        _members[binder.Name] = value;
        return true;
    }

    /// <inheritdoc />
    public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? args, out object? result)
    {
        Calls.Add(binder.Name);
        if (!_methods.TryGetValue(binder.Name, out Func<object?[], object?>? handler))
        {
            result = null;
            return false;
        }

        result = handler(args ?? Array.Empty<object?>());
        return true;
    }
}

/// <summary>Fabbrica che restituisce sempre lo stesso driver finto e conta le creazioni.</summary>
public sealed class FakeDriverFactory : IAscomDriverFactory
{
    /// <summary>Crea la fabbrica.</summary>
    /// <param name="driver">Driver restituito a ogni creazione.</param>
    public FakeDriverFactory(FakeAscomDriver driver)
    {
        Driver = driver;
    }

    /// <summary>Driver restituito.</summary>
    public FakeAscomDriver Driver { get; }

    /// <summary>Numero di creazioni richieste.</summary>
    public int CreateCount { get; private set; }

    /// <summary>Se impostata, la creazione lancia questa eccezione.</summary>
    public Exception? FailWith { get; set; }

    /// <inheritdoc />
    public object CreateDriver(string progId)
    {
        CreateCount++;
        if (FailWith is not null)
        {
            throw FailWith;
        }

        return Driver;
    }
}

/// <summary>Registro finto: viste e sottochiavi preparate dal test, con eventuali errori per vista.</summary>
public sealed class FakeRegistryReader : IRegistryReader
{
    /// <summary>Sottochiavi restituite per ogni vista.</summary>
    public Dictionary<RegistryViewKind, List<RegistrySubKey>> Views { get; } = new();

    /// <summary>Eccezioni lanciate per una vista.</summary>
    public Dictionary<RegistryViewKind, Exception> Failures { get; } = new();

    /// <summary>Richieste ricevute, nell'ordine.</summary>
    public List<(RegistryViewKind View, string KeyPath)> Queries { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<RegistrySubKey> ListSubKeys(RegistryViewKind view, string keyPath)
    {
        Queries.Add((view, keyPath));
        if (Failures.TryGetValue(view, out Exception? failure))
        {
            throw failure;
        }

        return Views.TryGetValue(view, out List<RegistrySubKey>? entries) ? entries : [];
    }
}

/// <summary>Orologio di test: il tempo cambia solo con <see cref="Advance"/>.</summary>
public sealed class ManualClock : IClock
{
    /// <summary>Crea l'orologio all'istante indicato.</summary>
    public ManualClock(DateTimeOffset start)
    {
        UtcNow = start;
    }

    /// <inheritdoc />
    public DateTimeOffset UtcNow { get; private set; }

    /// <summary>Avanza il tempo.</summary>
    public void Advance(TimeSpan delta) => UtcNow += delta;
}
