using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AllSky.Publish;

/// <summary>
/// Genera il captureId di uno scatto: UUID deterministico name-based versione 5 (RFC 4122, SHA-1).
/// Lo stesso dispositivo e lo stesso istante UTC danno sempre lo stesso id, anche dopo un riavvio o su un'altra macchina,
/// quindi il server puo' riconoscere i duplicati. Il nome hashato e' "deviceId|capturedAtUtc" con l'orario in UTC
/// nel formato yyyy-MM-ddTHH:mm:ss.fffZ.
/// </summary>
public static class CaptureIdFactory
{
    /// <summary>
    /// Namespace fisso del protocollo per i captureId (UUID v5). Non cambiarlo: cambierebbe tutti gli id generati.
    /// Scelta: un GUID arbitrario fissato nel codice, perche' il namespace deve essere stabile fra riavvii e macchine.
    /// </summary>
    public static readonly Guid Namespace = new("5b3e7c9a-1d4f-4a8e-9c2b-7f0a6d3e8b15");

    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    /// <summary>
    /// Calcola il captureId dello scatto.
    /// </summary>
    /// <param name="deviceId">Identificativo del dispositivo, non vuoto e senza il carattere '|' (che separa i campi del nome).</param>
    /// <param name="capturedAtUtc">Istante di cattura. Un offset diverso dall'UTC viene convertito in UTC; i microsecondi sono troncati ai millisecondi.</param>
    /// <returns>UUID minuscolo con trattini, 36 caratteri, versione 5 e variante RFC 4122.</returns>
    /// <exception cref="ArgumentException">Se deviceId e' vuoto o contiene '|'.</exception>
    public static string Create(string deviceId, DateTimeOffset capturedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        if (deviceId.Length == 0 || deviceId.Contains('|'))
        {
            throw new ArgumentException("L'identificativo del dispositivo non puo' essere vuoto né contenere '|'.", nameof(deviceId));
        }

        string timestamp = capturedAtUtc.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);
        return Version5(Namespace, deviceId + "|" + timestamp);
    }

    /// <summary>
    /// Algoritmo UUID v5: SHA-1 di (namespace in ordine di rete + nome UTF-8), primi 16 byte, versione 5 e variante RFC 4122.
    /// </summary>
    private static string Version5(Guid namespaceId, string name)
    {
        // Guid.ToByteArray(true) restituisce i campi in ordine big-endian, cioe' l'ordine di rete richiesto dalla RFC.
        byte[] namespaceBytes = namespaceId.ToByteArray(bigEndian: true);
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);

        byte[] input = new byte[namespaceBytes.Length + nameBytes.Length];
        namespaceBytes.CopyTo(input, 0);
        nameBytes.CopyTo(input, namespaceBytes.Length);

        byte[] hash = SHA1.HashData(input);
        byte[] uuid = new byte[16];
        Array.Copy(hash, uuid, 16);

        uuid[6] = (byte)((uuid[6] & 0x0F) | 0x50); // versione 5
        uuid[8] = (byte)((uuid[8] & 0x3F) | 0x80); // variante RFC 4122 (10xx)

        // Guid(byte[], bigEndian: true) rilegge i campi nell'ordine di rete, quindi ToString produce la forma canonica.
        return new Guid(uuid, bigEndian: true).ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant();
    }
}
