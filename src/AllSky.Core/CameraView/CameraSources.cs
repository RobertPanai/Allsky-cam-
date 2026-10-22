namespace AllSky.Core.CameraView;

/// <summary>Filtro per sorgente dell'elenco delle camere nella vista "Camera".</summary>
public enum CameraSourceChoice
{
    /// <summary>Tutte le sorgenti.</summary>
    All,

    /// <summary>Simulatore integrato.</summary>
    Simulator,

    /// <summary>Driver ASCOM COM (solo Windows).</summary>
    Ascom,

    /// <summary>Camere ASCOM Alpaca trovate in rete.</summary>
    Alpaca,
}

/// <summary>
/// Collega il tipo di camera restituito dall'Engine (<c>CameraCandidate.Kind</c>, <c>StatusDto.Camera.Kind</c>)
/// al filtro della vista e alle sue etichette. Logica pura.
/// </summary>
public static class CameraSources
{
    /// <summary>Tipo del simulatore.</summary>
    public const string SimulatorKind = "sim";

    /// <summary>Tipo dei driver ASCOM COM.</summary>
    public const string AscomComKind = "ascom-com";

    /// <summary>Tipo delle camere ASCOM Alpaca.</summary>
    public const string AlpacaKind = "ascom-alpaca";

    /// <summary>Filtri nell'ordine in cui compaiono nella vista.</summary>
    public static IReadOnlyList<CameraSourceChoice> Choices { get; } =
    [
        CameraSourceChoice.All,
        CameraSourceChoice.Simulator,
        CameraSourceChoice.Ascom,
        CameraSourceChoice.Alpaca,
    ];

    /// <summary>Filtro corrispondente a un tipo di camera; null per un tipo sconosciuto.</summary>
    /// <param name="kind">Tipo dell'Engine.</param>
    public static CameraSourceChoice? ChoiceOf(string? kind) => kind switch
    {
        SimulatorKind => CameraSourceChoice.Simulator,
        AscomComKind => CameraSourceChoice.Ascom,
        AlpacaKind => CameraSourceChoice.Alpaca,
        _ => null,
    };

    /// <summary>Vero se una camera di tipo <paramref name="kind"/> appartiene al filtro <paramref name="choice"/>.</summary>
    /// <param name="choice">Filtro scelto.</param>
    /// <param name="kind">Tipo dell'Engine.</param>
    public static bool Matches(CameraSourceChoice choice, string? kind)
    {
        return choice == CameraSourceChoice.All || ChoiceOf(kind) == choice;
    }

    /// <summary>Etichetta di un filtro, nella lingua scelta.</summary>
    /// <param name="choice">Filtro.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    public static string ChoiceLabel(CameraSourceChoice choice, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        return choice switch
        {
            CameraSourceChoice.All => texts.CameraSourceAll,
            CameraSourceChoice.Simulator => texts.CameraSourceSimulator,
            CameraSourceChoice.Ascom => texts.CameraSourceAscom,
            CameraSourceChoice.Alpaca => texts.CameraSourceAlpaca,
            _ => texts.CameraSourceAll,
        };
    }

    /// <summary>Etichetta del tipo di una camera; per un tipo sconosciuto, il testo "sconosciuto".</summary>
    /// <param name="kind">Tipo dell'Engine.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    public static string KindLabel(string? kind, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        CameraSourceChoice? choice = ChoiceOf(kind);
        return choice is null ? texts.StateUnknown : ChoiceLabel(choice.Value, texts);
    }

    /// <summary>Suggerimento da mostrare quando il filtro non trova camere; null se non ce n'è uno specifico.</summary>
    /// <param name="choice">Filtro scelto.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    public static string? EmptyHint(CameraSourceChoice choice, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        return choice switch
        {
            CameraSourceChoice.Ascom => texts.CameraHintAscom,
            CameraSourceChoice.Alpaca => texts.CameraHintAlpaca,
            CameraSourceChoice.Simulator => texts.CameraHintSimulator,
            _ => null,
        };
    }
}
