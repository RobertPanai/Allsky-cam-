using AllSky.Core.Passes;
using AllSky.Detection;
using AllSky.Engine.Forecasting;
using AllSky.Sky.Calibration;
using AllSky.Transits;

namespace AllSky.Engine.Detecting;

/// <summary>
/// Tracce previste per il classificatore (P5.5). Il set di satelliti viene letto dalla previsione e l'adattatore si rifà
/// ogni volta che arriva un nuovo insieme di TLE: i TLE possono arrivare dopo l'avvio del rilevamento.
/// Usato solo dal worker di rilevamento.
/// </summary>
internal sealed class ForecastTrackProvider : IPredictedTrackProvider
{
    private readonly PassForecaster _forecaster;
    private readonly PassForecastSettings _settings;
    private readonly ObserverSite _site;
    private readonly SkyCalibrationResult _calibration;
    private readonly PredictedTrackOptions _options;
    private PredictedTrackProviderAdapter? _adapter;
    private int _tleVersion = -1;

    public ForecastTrackProvider(
        PassForecaster forecaster,
        PassForecastSettings settings,
        ObserverSite site,
        SkyCalibrationResult calibration,
        PredictedTrackOptions options)
    {
        _forecaster = forecaster;
        _settings = settings;
        _site = site;
        _calibration = calibration;
        _options = options;
    }

    public IReadOnlyList<PredictedTrack> GetTracks(DateTimeOffset utc)
    {
        int version = _forecaster.TleVersion;
        if (_adapter is null || version != _tleVersion)
        {
            IReadOnlyList<TrackedSatellite> satellites = _settings.Enabled
                ? _forecaster.TrackedSatellites(_settings)
                : Array.Empty<TrackedSatellite>();
            _adapter = new PredictedTrackProviderAdapter(satellites, _site, _calibration, _options);
            _tleVersion = version;
        }

        return _adapter.GetTracks(utc);
    }
}
