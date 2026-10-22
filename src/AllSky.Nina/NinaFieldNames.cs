namespace AllSky.Nina;

/// <summary>
/// Nomi dei campi JSON letti dalle risposte di ninaAPI, in un unico file.
/// </summary>
/// <remarks>
/// [NV] da verificare su NINA reale (P11.13). Nessuno di questi nomi e' stato letto in una risposta reale:
/// il sorgente dei tipi (<c>TelescopeInfo</c>, <c>SequenceJson</c> ecc.) sta nel core di NINA, non nel plugin.
/// Il confronto sui nomi ignora maiuscole e minuscole. Un nome sbagliato produce null nel modello, mai un'eccezione.
/// </remarks>
internal static class NinaFieldNames
{
    // Contenitore di ogni risposta ninaAPI: verificato dal sorgente (Utility/CoreUtility.cs, classe HttpResponse).
    // {"Response": <dati>, "Error": "", "StatusCode": 200, "Success": true, "Type": "API"}
    public const string Envelope = "Response";
    public const string EnvelopeSuccess = "Success";
    public const string EnvelopeStatusCode = "StatusCode";

    // equipment/mount/info  [NV] da verificare su NINA reale (P11.13)
    public const string MountAtPark = "AtPark";
    public const string MountSlewing = "Slewing";
    public const string MountTrackingMode = "TrackingMode";
    public const string MountRightAscension = "RightAscension";
    public const string MountDeclination = "Declination";

    // sequence/json: target e coordinate  [NV] da verificare su NINA reale (P11.13)
    public const string TargetName = "TargetName";
    public const string TargetCoordinates = "InputCoordinates";
    public const string TargetCoordinatesValue = "Coordinates";
    public const string CoordinateRightAscension = "RA";
    public const string CoordinateDeclination = "Dec";

    // sequence/state  [NV] da verificare su NINA reale (P11.13)
    public const string SequenceStatus = "Status";

    /// <summary>
    /// Unita' dell'ascensione retta: vero se in ore (0..24), come si attende dal core di NINA.
    /// [NV] da verificare su NINA reale (P11.13). Se falso, si legge in gradi (0..360).
    /// </summary>
    public const bool RightAscensionInHours = true;
}
