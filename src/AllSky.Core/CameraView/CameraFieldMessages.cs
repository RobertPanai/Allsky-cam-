namespace AllSky.Core.CameraView;

/// <summary>Testo sotto un campo del form delle impostazioni di cattura, nella lingua scelta.</summary>
public static class CameraFieldMessages
{
    /// <summary>Testo del problema di un campo; null se il campo è valido.</summary>
    /// <param name="problem">Problema rilevato da <see cref="CameraSettingsForm"/>.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    public static string? For(CameraFieldProblem problem, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        return problem switch
        {
            CameraFieldProblem.None => null,
            CameraFieldProblem.Empty => texts.CameraProblemEmpty,
            CameraFieldProblem.NotNumber => texts.CameraProblemNotNumber,
            CameraFieldProblem.NotInteger => texts.CameraProblemNotInteger,
            CameraFieldProblem.Negative => texts.CameraProblemNegative,
            CameraFieldProblem.NotPositive => texts.CameraProblemNotPositive,
            _ => texts.CameraProblemNotNumber,
        };
    }
}
