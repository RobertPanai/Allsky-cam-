using System.Globalization;
using AllSky.Cameras.Alpaca;
using AllSky.Cameras.Ascom;
using AllSky.Cameras.Sim;
using AllSky.Core;
using AllSky.Engine;

namespace AllSky.Host;

/// <summary>
/// Fornitore concreto delle camere: simulatore sempre disponibile, driver ASCOM COM (solo Windows) e
/// camere ASCOM Alpaca trovate sulla rete locale.
/// <para>
/// Formati degli identificativi: <c>sim-N</c>, <c>ascom-com:&lt;ProgID&gt;</c>, <c>alpaca:&lt;host&gt;:&lt;porta&gt;:&lt;deviceNumber&gt;</c>.
/// </para>
/// </summary>
internal sealed class CameraProvider : ICameraProvider
{
    private const string SimPrefix = "sim-";
    private const string AscomComPrefix = "ascom-com:";
    private const string AlpacaPrefix = "alpaca:";

    private readonly Func<AppConfig> _config;
    private readonly HostTuning _tuning;
    private readonly DailyFileLog _log;

    /// <summary>Crea il fornitore.</summary>
    /// <param name="config">Configurazione corrente: serve la posizione del sito per il simulatore.</param>
    /// <param name="tuning">Parametri di discovery.</param>
    /// <param name="log">Log per gli errori di ricerca (la ricerca non deve mai far fallire l'elenco).</param>
    public CameraProvider(Func<AppConfig> config, HostTuning tuning, DailyFileLog log)
    {
        _config = config;
        _tuning = tuning;
        _log = log;
    }

    /// <summary>
    /// Elenca il simulatore, i driver ASCOM COM e le camere Alpaca. Gli errori di una delle tre ricerche
    /// vengono registrati nel log e non impediscono di vedere le altre.
    /// </summary>
    public async Task<IReadOnlyList<CameraCandidate>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var candidates = new List<CameraCandidate>
        {
            new("sim-1", "Simulatore all-sky", "sim", "Simulatore integrato, senza hardware"),
        };

        candidates.AddRange(DiscoverAscomCom());

        if (_tuning.EnableAlpacaDiscovery)
        {
            candidates.AddRange(await DiscoverAlpacaAsync(cancellationToken));
        }

        return candidates;
    }

    /// <summary>Crea la sorgente per l'identificativo indicato, senza connetterla.</summary>
    /// <param name="id">Identificativo come restituito da <see cref="DiscoverAsync"/>.</param>
    /// <exception cref="ArgumentException">Identificativo vuoto, malformato o di un tipo sconosciuto.</exception>
    public ICameraSource Create(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("indicare la camera da collegare");
        }

        if (id.StartsWith(SimPrefix, StringComparison.Ordinal))
        {
            AppConfig config = _config();
            var options = new SimCameraOptions
            {
                Latitude = config.Site.Latitude,
                Longitude = config.Site.Longitude,
            };
            return new SimCamera(options, new SystemClock());
        }

        if (id.StartsWith(AscomComPrefix, StringComparison.Ordinal))
        {
            string progId = id[AscomComPrefix.Length..].Trim();
            if (progId.Length == 0)
            {
                throw new ArgumentException($"identificativo ASCOM non valido: '{id}'; scegliere di nuovo la camera dall'elenco");
            }

            return new AscomComCamera(progId);
        }

        if (id.StartsWith(AlpacaPrefix, StringComparison.Ordinal))
        {
            return CreateAlpaca(id);
        }

        throw new ArgumentException("il driver non è tra quelli trovati; cercare di nuovo le camere e sceglierne una dall'elenco");
    }

    /// <summary>Driver ASCOM COM registrati. Su sistemi diversi da Windows la lista e' vuota, senza errori.</summary>
    private IEnumerable<CameraCandidate> DiscoverAscomCom()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        try
        {
            return new AscomDriverCatalog().Discover()
                .Select(d => new CameraCandidate(
                    AscomComPrefix + d.ProgId,
                    string.IsNullOrWhiteSpace(d.Description) ? d.ProgId : d.Description,
                    "ascom-com",
                    d.ProgId))
                .ToList();
        }
        catch (Exception ex)
        {
            // Il registro puo' rifiutare l'accesso o essere incompleto: si registra e si continua.
            _log.Write("Ricerca dei driver ASCOM non riuscita", ex);
            return [];
        }
    }

    /// <summary>Camere Alpaca sulla rete locale, con la durata di ricerca configurata.</summary>
    private async Task<IReadOnlyList<CameraCandidate>> DiscoverAlpacaAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<AlpacaDeviceInfo> devices = await AlpacaDiscovery.DiscoverAsync(
                _tuning.AlpacaDiscoveryTimeout,
                cancellationToken: cancellationToken);

            return devices
                .Select(d => new CameraCandidate(
                    $"{AlpacaPrefix}{d.Host}:{d.Port}:{d.DeviceNumber}",
                    d.DeviceName,
                    "ascom-alpaca",
                    $"{d.Host}:{d.Port}"))
                .ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Write("Ricerca delle camere Alpaca non riuscita", ex);
            return [];
        }
    }

    /// <summary>Legge <c>alpaca:&lt;host&gt;:&lt;porta&gt;:&lt;deviceNumber&gt;</c>; l'host puo' contenere due punti (IPv6).</summary>
    private static AlpacaCamera CreateAlpaca(string id)
    {
        const string malformed = "identificativo Alpaca non valido; cercare di nuovo le camere dalla rete e scegliere quella giusta";
        string rest = id[AlpacaPrefix.Length..];

        int lastColon = rest.LastIndexOf(':');
        if (lastColon <= 0)
        {
            throw new ArgumentException(malformed);
        }

        string beforeDevice = rest[..lastColon];
        int portColon = beforeDevice.LastIndexOf(':');
        if (portColon <= 0)
        {
            throw new ArgumentException(malformed);
        }

        string host = beforeDevice[..portColon];
        if (!int.TryParse(beforeDevice[(portColon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int port)
            || port is < 1 or > 65535)
        {
            throw new ArgumentException(malformed);
        }

        if (!int.TryParse(rest[(lastColon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int deviceNumber))
        {
            throw new ArgumentException(malformed);
        }

        // Un IPv6 va scritto tra parentesi quadre nell'URI.
        string uriHost = host.Contains(':') && !host.StartsWith('[') ? "[" + host + "]" : host;
        var baseUri = new Uri($"http://{uriHost}:{port}/", UriKind.Absolute);
        return new AlpacaCamera(baseUri, deviceNumber);
    }
}
