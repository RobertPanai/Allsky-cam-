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
    bool AllowLanOverride = false);
