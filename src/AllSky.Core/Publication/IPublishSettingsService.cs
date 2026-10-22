namespace AllSky.Core.Publication;

/// <summary>
/// Accesso della vista Pubblicazione alle impostazioni e allo stato dell'Uplink. Il motore (<c>IAllSkyService</c>) non
/// espone la pubblicazione, quindi la vista usa questa interfaccia.
/// </summary>
/// <remarks>
/// <para>
/// Chi la implementa: un adattatore nell'Host (AllSky.Host) che avvolge i servizi di pubblicazione già esistenti
/// (<c>PublishServices</c>: store su disco con DPAPI, <c>IPublisher</c>, validatore <c>SettingsValidator</c>).
/// L'adattatore replica la logica di <c>PublishRoutes</c>: unione con i segreti salvati, validazione, salvataggio, poi
/// <c>Replace</c> e <c>ApplySettings</c>. Il motore non cambia. Il tipo di <c>PublishSettingsBody</c> resta interno all'Host.
/// </para>
/// <para>
/// Il modulo di pubblicazione non è una dipendenza di AllSky.Core: per questo i tipi qui sono propri del desktop.
/// L'adattatore converte <c>PublishState</c>, <c>SettingsError</c> e <c>TestResult</c> nei tipi di questo namespace.
/// </para>
/// <para>
/// Regole: nessun metodo restituisce o accetta un segreto in chiaro, tranne <see cref="PublicationSettingsChange"/>
/// quando il nuovo token è digitato. <see cref="GetSettings"/> non riporta mai il token. Gli eventi arrivano dal thread
/// del publisher: la vista rimette l'aggiornamento sul thread dell'interfaccia.
/// </para>
/// </remarks>
public interface IPublishSettingsService
{
    /// <summary>Impostazioni correnti, senza segreti.</summary>
    PublicationSettingsSnapshot GetSettings();

    /// <summary>Stato corrente della pubblicazione.</summary>
    PublicationStatusSnapshot GetStatus();

    /// <summary>Emesso a ogni cambio di stato dell'Uplink. Può arrivare da un thread di sfondo.</summary>
    event Action<PublicationStatusSnapshot>? StatusChanged;

    /// <summary>
    /// Valida, salva e applica le impostazioni. Con errori non cambia nulla e non lancia: gli errori sono nel risultato.
    /// </summary>
    PublicationSaveResult SaveSettings(PublicationSettingsChange change);

    /// <summary>
    /// Prova la connessione con i valori del modulo, anche non salvati; un segreto non digitato usa quello salvato.
    /// Non invia immagini e non cambia lo stato. Non lancia: l'esito è nel risultato.
    /// </summary>
    Task<PublicationTestResult> TestConnectionAsync(PublicationSettingsChange change, CancellationToken cancellationToken);
}
