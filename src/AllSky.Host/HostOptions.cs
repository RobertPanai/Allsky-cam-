using AllSky.Publish;
using AllSky.Updates;

namespace AllSky.Host;

/// <summary>
/// Opzioni di avvio dell'Host HTTP locale.
/// </summary>
/// <param name="ConfigPath">File JSON della configurazione. Se manca, viene creato con i valori predefiniti.</param>
/// <param name="DataFolder">Cartella dati dell'utente (es. %LocalAppData%\AllSky): archivio in <c>nights</c>, log in <c>logs</c>. Viene creata se manca.</param>
/// <param name="WebRoot">Cartella dei file statici della UI (la cartella <c>web</c>).</param>
/// <param name="PortOverride">
/// Porta di partenza. Null usa <c>ui.port</c> della configurazione (predefinito 5810). Se occupata, l'Host prova le
/// successive fino a 10 posti dopo. Il valore 0 chiede una porta libera scelta dal sistema, senza tentativi successivi.
/// </param>
/// <param name="AllowLanOverride">
/// Se vero, ascolta su tutte le interfacce anche se <c>ui.allowLan</c> e' falso. Di default (falso) si ascolta solo su 127.0.0.1.
/// </param>
public sealed record HostOptions(
    string ConfigPath,
    string DataFolder,
    string WebRoot,
    int? PortOverride = null,
    bool AllowLanOverride = false)
{
    /// <summary>
    /// Protezione dei segreti della pubblicazione. Null usa DPAPI su Windows; altrove la pubblicazione non parte.
    /// Serve ai test per sostituire DPAPI con una protezione finta.
    /// </summary>
    public ISecretProtector? SecretProtector { get; init; }

    /// <summary>
    /// Gestore HTTP per la pubblicazione. Null crea un client di rete reale. Se indicato, non viene chiuso dall'Host
    /// (lo gestisce chi l'ha creato). Serve ai test.
    /// </summary>
    public HttpMessageHandler? PublishHttpHandler { get; init; }

    /// <summary>
    /// Sorgente degli aggiornamenti. Su Windows il Tray passa il backend Velopack; null (o un'installazione non aggiornabile)
    /// lascia gli aggiornamenti disabilitati. Il Tray la crea, non l'Host, perché Velopack è solo Windows.
    /// </summary>
    public IUpdateBackend? UpdateBackend { get; init; }
}
