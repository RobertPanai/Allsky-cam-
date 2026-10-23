using AllSky.Core;
using AllSky.Engine;
using AllSky.Mcp;

namespace AllSky.Host;

/// <summary>
/// Avvio e arresto del server MCP locale secondo la sezione <c>mcp</c> della configurazione. Collegato all'Host dalla
/// notifica <see cref="AllSkyEngine.ConfigApplied"/>: ogni configurazione applicata passa da <see cref="Apply"/>.
/// Con <c>mcp.enabled</c> falso nessun listener viene aperto. Un errore di avvio (porta occupata) finisce nel log e non
/// ferma l'Host: il servizio prosegue senza server MCP.
/// </summary>
public sealed class McpServerHost : IDisposable
{
    private readonly IAllSkyService _service;
    private readonly IViewRenderer? _renderer;
    private readonly Action<string, Exception?> _log;
    private readonly object _gate = new();
    private McpServer? _server;   // _gate
    private bool _disposed;       // _gate

    /// <summary>Crea il gestore (senza avviare nulla).</summary>
    /// <param name="service">Servizio applicativo, in sola lettura per il server.</param>
    /// <param name="log">Log dell'Host. Riceve messaggi senza percorsi o segreti.</param>
    /// <param name="renderer">Render delle viste; null nell'Host attuale (il render WPF non e' ancora collegato).</param>
    public McpServerHost(IAllSkyService service, Action<string, Exception?> log, IViewRenderer? renderer = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _renderer = renderer;
    }

    /// <summary>Vero se il server MCP e' in ascolto.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _server?.IsRunning == true;
            }
        }
    }

    /// <summary>
    /// Allinea il server alla sezione <c>mcp</c>: spento e fermato se <c>enabled</c> e' falso; altrimenti avviato sulla
    /// porta indicata, riavviato se la porta cambia. Non lancia.
    /// </summary>
    public void Apply(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            AppConfig.McpConfig mcp = config.Mcp ?? new();
            if (!mcp.Enabled)
            {
                if (_server is not null)
                {
                    StopLocked();
                    _log("Server MCP fermato: mcp.enabled e' falso.", null);
                }

                return;
            }

            if (_server is not null && _server.Port == mcp.Port && _server.IsRunning)
            {
                return;
            }

            StopLocked();

            var server = new McpServer(_service, mcp.Port, _renderer, _log);
            try
            {
                server.Start();
                _server = server;
                _log($"Server MCP in ascolto su 127.0.0.1:{mcp.Port} (sola lettura).", null);
            }
            catch (Exception ex)
            {
                server.Dispose();
                // Solo il tipo dell'errore: il messaggio dell'eccezione puo' contenere dettagli di rete.
                _log($"Avvio del server MCP non riuscito ({ex.GetType().Name}): il servizio prosegue senza server MCP.", null);
            }
        }
    }

    /// <summary>Ferma il server, se e' in ascolto. Sicuro da chiamare piu' volte.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            StopLocked();
        }
    }

    /// <summary>Ferma il server. Dopo Dispose <see cref="Apply"/> non fa nulla.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopLocked();
        }
    }

    /// <summary>Chiamare solo con <c>_gate</c> preso.</summary>
    private void StopLocked()
    {
        McpServer? server = _server;
        _server = null;
        server?.Dispose();
    }
}
