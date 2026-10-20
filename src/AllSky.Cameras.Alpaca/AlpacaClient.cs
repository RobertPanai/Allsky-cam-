using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AllSky.Cameras.Alpaca;

/// <summary>
/// Client REST minimale per ASCOM Alpaca (solo BCL). Aggiunge ClientID e ClientTransactionID a ogni richiesta,
/// controlla ErrorNumber nella risposta JSON e restituisce il campo Value.
/// Percorsi: /api/v1/{devicetype}/{n}/{command} per i dispositivi, /management/... per il server.
/// Decisione: le GET inviano ClientID e ClientTransactionID nella query string, le PUT nel corpo form.
/// </summary>
public sealed class AlpacaClient
{
    private readonly HttpClient _http;
    private readonly string _root;
    private readonly uint _clientId;
    private long _transactionCounter;

    /// <summary>Crea il client per il server Alpaca raggiungibile a <paramref name="baseUri"/>.</summary>
    /// <param name="baseUri">URI base assoluto del server, es. http://192.168.1.10:11111/.</param>
    /// <param name="http">HttpClient da usare. Il suo <see cref="HttpClient.Timeout"/> limita ogni richiesta.</param>
    /// <exception cref="ArgumentException">L'URI non e' assoluto.</exception>
    public AlpacaClient(Uri baseUri, HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentNullException.ThrowIfNull(http);
        if (!baseUri.IsAbsoluteUri)
        {
            throw new ArgumentException("L'URI del server Alpaca deve essere assoluto, es. http://192.168.1.10:11111/.", nameof(baseUri));
        }

        BaseUri = baseUri;
        _root = baseUri.AbsoluteUri.TrimEnd('/');
        _http = http;

        // Decisione: ClientID casuale per istanza (il protocollo lo vuole uint, ma non ne impone il valore).
        _clientId = (uint)Random.Shared.Next(1, int.MaxValue);
    }

    /// <summary>URI base del server Alpaca a cui parla questo client.</summary>
    public Uri BaseUri { get; }

    /// <summary>
    /// Esegue una GET su un comando del dispositivo e restituisce il campo Value deserializzato come <typeparamref name="T"/>.
    /// Lancia <see cref="AlpacaException"/> se ErrorNumber e' diverso da zero.
    /// </summary>
    /// <typeparam name="T">Tipo atteso del campo Value (es. int, double, bool, string).</typeparam>
    /// <param name="deviceType">Tipo di dispositivo nel percorso, es. "camera".</param>
    /// <param name="deviceNumber">Numero del dispositivo nel server.</param>
    /// <param name="command">Nome del comando, es. "cameraxsize".</param>
    /// <param name="cancellationToken">Token di cancellazione.</param>
    public Task<T> GetAsync<T>(string deviceType, int deviceNumber, string command, CancellationToken cancellationToken)
    {
        return GetValueAsync<T>(DeviceUrl(deviceType, deviceNumber, command) + QueryString(), cancellationToken);
    }

    /// <summary>
    /// Esegue una PUT su un comando del dispositivo con parametri form-urlencoded.
    /// Lancia <see cref="AlpacaException"/> se ErrorNumber e' diverso da zero.
    /// </summary>
    /// <param name="deviceType">Tipo di dispositivo nel percorso, es. "camera".</param>
    /// <param name="deviceNumber">Numero del dispositivo nel server.</param>
    /// <param name="command">Nome del comando, es. "startexposure".</param>
    /// <param name="parameters">Parametri del comando, senza ClientID e ClientTransactionID (aggiunti qui).</param>
    /// <param name="cancellationToken">Token di cancellazione.</param>
    public async Task PutAsync(
        string deviceType,
        int deviceNumber,
        string command,
        IEnumerable<KeyValuePair<string, string>> parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var form = new List<KeyValuePair<string, string>>(parameters)
        {
            new KeyValuePair<string, string>("ClientID", _clientId.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("ClientTransactionID", NextTransactionId().ToString(CultureInfo.InvariantCulture)),
        };

        using var content = new FormUrlEncodedContent(form);
        using HttpResponseMessage response = await _http.PutAsync(DeviceUrl(deviceType, deviceNumber, command), content, cancellationToken);
        using JsonDocument document = await ReadJsonAsync(response, cancellationToken);
        ThrowIfError(document.RootElement);
    }

    /// <summary>
    /// Restituisce le versioni dell'API supportate dal server (GET /management/apiversions).
    /// </summary>
    /// <param name="cancellationToken">Token di cancellazione.</param>
    public Task<int[]> GetApiVersionsAsync(CancellationToken cancellationToken)
    {
        return GetValueAsync<int[]>(_root + "/management/apiversions" + QueryString(), cancellationToken);
    }

    /// <summary>
    /// Restituisce l'elenco dei dispositivi configurati sul server (GET /management/v1/configureddevices).
    /// </summary>
    /// <param name="cancellationToken">Token di cancellazione.</param>
    /// <exception cref="InvalidDataException">Un elemento dell'elenco non ha i campi attesi.</exception>
    public async Task<IReadOnlyList<AlpacaConfiguredDevice>> GetConfiguredDevicesAsync(CancellationToken cancellationToken)
    {
        JsonElement[] items = await GetValueAsync<JsonElement[]>(
            _root + "/management/v1/configureddevices" + QueryString(), cancellationToken);

        var devices = new List<AlpacaConfiguredDevice>(items.Length);
        foreach (JsonElement item in items)
        {
            devices.Add(new AlpacaConfiguredDevice(
                DeviceName: Field(item, "DeviceName").GetString() ?? string.Empty,
                DeviceType: Field(item, "DeviceType").GetString() ?? string.Empty,
                DeviceNumber: Field(item, "DeviceNumber").GetInt32(),
                UniqueId: Field(item, "UniqueID").GetString() ?? string.Empty));
        }

        return devices;
    }

    /// <summary>
    /// Invia la GET di imagearray con <c>Accept: application/imagebytes</c> e restituisce la risposta senza leggere il corpo.
    /// Il chiamante deve leggere e disporre la risposta.
    /// </summary>
    /// <param name="deviceNumber">Numero della camera nel server.</param>
    /// <param name="cancellationToken">Token di cancellazione.</param>
    internal async Task<HttpResponseMessage> SendImageArrayRequestAsync(int deviceNumber, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            DeviceUrl("camera", deviceNumber, "imagearray") + QueryString());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(ImageBytesReader.MediaType));

        return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    /// <summary>
    /// Legge il corpo JSON di una risposta. Se il server risponde con errore HTTP e il corpo non contiene
    /// ErrorNumber, lancia <see cref="HttpRequestException"/>.
    /// </summary>
    /// <param name="response">Risposta da leggere.</param>
    /// <param name="cancellationToken">Token di cancellazione.</param>
    /// <exception cref="HttpRequestException">Corpo non JSON, oppure errore HTTP senza dettagli Alpaca.</exception>
    /// <exception cref="InvalidDataException">Il JSON non e' un oggetto.</exception>
    internal static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        int status = (int)response.StatusCode;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException($"Risposta non JSON dal server Alpaca (HTTP {status}).", ex);
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw new InvalidDataException("La risposta Alpaca non e' un oggetto JSON.");
        }

        if (!response.IsSuccessStatusCode && !document.RootElement.TryGetProperty("ErrorNumber", out _))
        {
            document.Dispose();
            throw new HttpRequestException($"Il server Alpaca ha risposto con HTTP {status}.");
        }

        return document;
    }

    /// <summary>
    /// Lancia <see cref="AlpacaException"/> se la risposta ha ErrorNumber diverso da zero.
    /// </summary>
    /// <param name="root">Radice del JSON di risposta.</param>
    internal static void ThrowIfError(JsonElement root)
    {
        int errorNumber = 0;
        if (root.TryGetProperty("ErrorNumber", out JsonElement number) && number.ValueKind == JsonValueKind.Number)
        {
            errorNumber = number.GetInt32();
        }

        if (errorNumber == 0)
        {
            return;
        }

        string message = root.TryGetProperty("ErrorMessage", out JsonElement text) && text.ValueKind == JsonValueKind.String
            ? text.GetString() ?? string.Empty
            : string.Empty;
        throw new AlpacaException(errorNumber, message);
    }

    private async Task<T> GetValueAsync<T>(string url, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _http.GetAsync(url, cancellationToken);
        using JsonDocument document = await ReadJsonAsync(response, cancellationToken);
        ThrowIfError(document.RootElement);

        if (!document.RootElement.TryGetProperty("Value", out JsonElement value))
        {
            throw new InvalidDataException("La risposta Alpaca non contiene il campo Value.");
        }

        return value.Deserialize<T>() ?? throw new InvalidDataException("Il campo Value della risposta Alpaca e' vuoto.");
    }

    private string DeviceUrl(string deviceType, int deviceNumber, string command)
    {
        return string.Concat(
            _root,
            "/api/v1/",
            deviceType.ToLowerInvariant(),
            "/",
            deviceNumber.ToString(CultureInfo.InvariantCulture),
            "/",
            command.ToLowerInvariant());
    }

    private string QueryString()
    {
        return string.Concat(
            "?ClientID=",
            _clientId.ToString(CultureInfo.InvariantCulture),
            "&ClientTransactionID=",
            NextTransactionId().ToString(CultureInfo.InvariantCulture));
    }

    private uint NextTransactionId()
    {
        // ClientTransactionID parte da 1 e cresce a ogni richiesta; il wrap a 2^32 non e' un caso pratico.
        return unchecked((uint)Interlocked.Increment(ref _transactionCounter));
    }

    private static JsonElement Field(JsonElement item, string name)
    {
        return item.TryGetProperty(name, out JsonElement value)
            ? value
            : throw new InvalidDataException($"Campo '{name}' mancante nella risposta configureddevices.");
    }
}
