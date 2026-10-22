namespace AllSky.Core.Passes;

/// <summary>
/// Previsione dei passaggi di satelliti per la UI (P4.3). Implementata dall'Engine; le viste la leggono con
/// <c>service as IPassForecastService</c>, come per la calibrazione. Le letture non bloccano il ciclo di cattura.
/// </summary>
public interface IPassForecastService
{
    /// <summary>Previsione corrente, riferita all'istante di chiamata.</summary>
    PassForecastSnapshot GetForecast();

    /// <summary>Tracce previste nei pixel per i passaggi in corso o imminenti. Vuote se la calibrazione manca.</summary>
    PassTrackState GetTracks();

    /// <summary>Emesso quando un passaggio inizia o finisce, o quando la previsione viene ricalcolata.</summary>
    event Action<PassChange>? PassChanged;
}
