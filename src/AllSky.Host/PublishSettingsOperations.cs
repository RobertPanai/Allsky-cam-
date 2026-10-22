using AllSky.Publish;

namespace AllSky.Host;

/// <summary>
/// Logica di salvataggio e di prova delle impostazioni di pubblicazione, condivisa tra le rotte HTTP
/// (<see cref="PublishRoutes"/>) e l'adattatore della vista desktop (<see cref="HostPublishSettingsService"/>).
/// Non controlla chi chiama: il controllo del richiedente locale resta nelle rotte.
/// </summary>
internal static class PublishSettingsOperations
{
    /// <summary>Messaggio quando il modulo di pubblicazione non e' partito.</summary>
    internal const string UnavailableMessage =
        "La pubblicazione non è disponibile: il modulo non è partito. Controllare il file di log dell'applicazione.";

    // Salvataggi concorrenti (UI e rotte HTTP) non si mescolano: merge, salvataggio e applicazione avvengono insieme.
    private static readonly object SaveGate = new();

    /// <summary>Esito del salvataggio. Con <see cref="Ok"/> falso nulla e' stato cambiato.</summary>
    internal sealed record SaveOutcome(bool Ok, IReadOnlyList<SettingsError> Errors);

    /// <summary>Esito della prova di connessione, con il testo gia' sanificato.</summary>
    internal sealed record TestOutcome(bool Ok, string Code, string Message, int? StatusCode);

    /// <summary>Impostazioni salvate correnti; senza servizi, quelle predefinite.</summary>
    internal static PublishSettings Current(PublishServices? services) => services?.Current ?? new PublishSettings();

    /// <summary>
    /// Unisce, valida, salva e applica. Con errori di campo non salva nulla. Non lancia per errori di salvataggio:
    /// li riporta come errore sul campo "token", come le rotte.
    /// </summary>
    internal static SaveOutcome Save(PublishServices? services, PublishRoutes.PublishSettingsBody body)
    {
        if (services is null)
        {
            return new SaveOutcome(false, new[] { new SettingsError("publish", UnavailableMessage) });
        }

        lock (SaveGate)
        {
            PublishSettings merged = Merge(services.Current, body);
            IReadOnlyList<SettingsError> errors = SettingsValidator.Validate(merged);
            if (errors.Count > 0)
            {
                return new SaveOutcome(false, errors);
            }

            try
            {
                services.Store.Save(merged);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                services.Log.Error("Salvataggio delle impostazioni di pubblicazione non riuscito", ex);
                var error = new SettingsError("token", "Impossibile salvare le impostazioni: " + services.Redactor.Sanitize(ex.Message));
                return new SaveOutcome(false, new[] { error });
            }

            services.Replace(merged);
            services.Publisher.ApplySettings(merged);
            return new SaveOutcome(true, Array.Empty<SettingsError>());
        }
    }

    /// <summary>
    /// Prova di connessione con i valori indicati (anche non salvati). I segreti non digitati prendono quelli salvati.
    /// Non modifica le impostazioni ne' lo stato del publisher.
    /// </summary>
    internal static async Task<TestOutcome> TestAsync(PublishServices? services, PublishRoutes.PublishSettingsBody body, CancellationToken ct)
    {
        if (services is null)
        {
            return new TestOutcome(false, "unknown", UnavailableMessage, null);
        }

        PublishSettings merged = Merge(services.Current, body);
        IReadOnlyList<SettingsError> errors = SettingsValidator.Validate(merged);
        if (errors.Count > 0)
        {
            return new TestOutcome(false, "invalid_settings", errors[0].Message, null);
        }

        // I segreti digitati ma non salvati devono essere mascherati nei log e nelle risposte di questa prova.
        services.Secrets.SetTransient(new[] { Trimmed(body.Token), Trimmed(body.ExtraHeaderValue) });
        try
        {
            TestResult result = await services.Publisher.TestConnectionAsync(merged, ct);
            return new TestOutcome(result.Ok, result.Code, services.Redactor.Sanitize(result.Message), result.StatusCode);
        }
        finally
        {
            services.Secrets.ClearTransient();
        }
    }

    /// <summary>Testo di errore sanificato, o null se non c'e' errore.</summary>
    internal static string? SanitizeError(string? text, Redactor redactor) => text is null ? null : redactor.Sanitize(text);

    /// <summary>Unisce il corpo ricevuto alle impostazioni correnti. Un segreto vuoto o assente resta invariato.</summary>
    internal static PublishSettings Merge(PublishSettings current, PublishRoutes.PublishSettingsBody body)
    {
        Secret token = body.ClearToken == true
            ? default
            : Trimmed(body.Token) is { } newToken ? new Secret(newToken) : current.Token;
        Secret header = body.ClearExtraHeaderValue == true
            ? default
            : Trimmed(body.ExtraHeaderValue) is { } newHeader ? new Secret(newHeader) : current.ExtraHeaderValue;

        return new PublishSettings
        {
            Enabled = body.Enabled ?? current.Enabled,
            ServerUrl = body.ServerUrl ?? current.ServerUrl,
            DeviceId = body.DeviceId ?? current.DeviceId,
            Token = token,
            ProfileName = EmptyToNull(body.ProfileName ?? current.ProfileName),
            IntervalSeconds = body.IntervalSeconds ?? current.IntervalSeconds,
            LongEdgePx = body.LongEdgePx ?? current.LongEdgePx,
            JpegQuality = body.JpegQuality ?? current.JpegQuality,
            ExtraHeaderName = EmptyToNull(body.ExtraHeaderName ?? current.ExtraHeaderName),
            ExtraHeaderValue = header,
        };
    }

    /// <summary>Testo senza spazi ai bordi, oppure null se vuoto: un segreto digitato con spazi o a capo non e' valido.</summary>
    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? EmptyToNull(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
