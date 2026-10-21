namespace AllSky.Satellites;

/// <summary>
/// Illuminazione del satellite dal Sole, con modello di ombra cilindrica della Terra.
/// </summary>
/// <remarks>
/// Modello: la Terra proietta un cilindro d'ombra di raggio pari al raggio equatoriale (6378.137 km, WGS84)
/// lungo la direzione opposta al Sole. Il satellite è in ombra se si trova dal lato notte
/// (componente lungo la direzione Terra-Sole negativa) e la sua distanza dall'asse Terra-Sole è minore del raggio.
/// È un modello più semplice di quello conico: non tiene conto della penombra né dell'atmosfera.
/// </remarks>
public static class EarthShadow
{
    /// <summary>Raggio equatoriale terrestre WGS84, in km.</summary>
    public const double EarthRadiusKm = 6378.137;

    /// <summary>
    /// Indica se il satellite è illuminato dal Sole.
    /// </summary>
    /// <param name="satEci">Posizione del satellite in ECI, km.</param>
    /// <param name="sunEci">Posizione del Sole in ECI, km (solo la direzione conta).</param>
    /// <returns>True se illuminato, false se in ombra terrestre.</returns>
    public static bool IsSunlit(EciKm satEci, EciKm sunEci)
    {
        double sunNorm = sunEci.Norm;
        if (sunNorm <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(sunEci), "La posizione del Sole non può essere il vettore nullo.");
        }

        // Componente del satellite lungo la direzione Terra-Sole (positiva dal lato Sole).
        double along = satEci.Dot(sunEci) / sunNorm;
        if (along >= 0.0)
        {
            return true;
        }

        // Distanza dal satellite all'asse Terra-Sole, al quadrato: |sat|^2 - along^2.
        double perpSquared = satEci.Dot(satEci) - along * along;
        return perpSquared >= EarthRadiusKm * EarthRadiusKm;
    }
}
