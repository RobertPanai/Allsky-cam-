using AllSky.Satellites;
using SGPdotNET.Exception;
using SGPdotNET.Observation;

namespace AllSky.Satellites.Sgp4;

/// <summary>
/// Crea propagatori SGP4 con la libreria SGP.NET.
/// </summary>
/// <remarks>
/// Verificato sul sorgente di SGP.NET 1.6.0 (repository parzivail/SGP.NET, letto il 22/10/2026 via raw.githubusercontent.com):
/// <list type="bullet">
/// <item>Namespace: <c>SGPdotNET.*</c>, non <c>SGP.NET</c>. Costruttore <c>Satellite(name, tle1, tle2)</c>.</item>
/// <item>Il target del pacchetto include net10.0 (SGP.NET.csproj). Licenza MIT (file LICENSE, © 2019 Colby Newman).</item>
/// <item>Altitudine di <c>GeodeticCoordinate</c> in km; la posizione ECI di <c>EciCoordinate.Position</c> è in km.</item>
/// <item>Orbite profonde: SGP4.cs usa SDP4 quando il periodo è ≥ 225 min. La libreria le gestisce, quindi non vengono rifiutate.
/// L'accuratezza SDP4 NON è verificata da noi (nessun test con un TLE di orbita profonda).</item>
/// </list>
/// NON verificato: che il pacchetto NuGet "SGP.NET" 1.6.0 sia costruito da quel repository (il container non raggiunge NuGet);
/// la versione 1.6.0 e la data di pubblicazione sono quelle indicate nel brief del progetto.
/// </remarks>
public sealed class SgpNetPropagatorFactory : IOrbitPropagatorFactory
{
    /// <inheritdoc />
    public IOrbitPropagator Create(TleSet tle)
    {
        ArgumentNullException.ThrowIfNull(tle);

        try
        {
            var satellite = new Satellite(tle.Name, tle.Line1, tle.Line2);
            return new SgpNetPropagator(satellite);
        }
        catch (TleException ex)
        {
            // Il TLE è già stato validato dal parser del progetto; qui si intercetta solo ciò che SGP.NET rifiuta.
            throw new InvalidOperationException(
                $"TLE non leggibile da SGP.NET per il satellite NORAD {tle.NoradId}: {ex.Message}", ex);
        }
    }
}
