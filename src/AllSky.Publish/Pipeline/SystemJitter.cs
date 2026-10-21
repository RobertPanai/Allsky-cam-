namespace AllSky.Publish;

/// <summary>Sorgente di jitter basata su <see cref="Random.Shared"/>: valori uniformi in [0, 1).</summary>
public sealed class SystemJitter : IJitter
{
    /// <summary>Restituisce un valore casuale in [0, 1).</summary>
    public double NextDouble() => Random.Shared.NextDouble();
}
