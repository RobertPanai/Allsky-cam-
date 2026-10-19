namespace AllSky.Core;

/// <summary>Sorgente del tempo, iniettabile per i test.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
