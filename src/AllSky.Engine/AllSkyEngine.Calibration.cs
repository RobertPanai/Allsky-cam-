using AllSky.Astro;
using AllSky.Core;
using AllSky.Core.Calibration;
using AllSky.Sky;
using AllSky.Sky.Calibration;

namespace AllSky.Engine;

/// <summary>
/// Calibrazione ottica (P3.3): alimentazione del collector dal Sole e implementazione di <see cref="ICalibrationService"/>.
/// </summary>
/// <remarks>
/// Punto d'aggancio: <see cref="FeedCalibration"/> viene chiamato dal consumer dei frame (AllSkyEngine.Frames.cs) dopo
/// l'archiviazione, sul thread di cattura. Il lavoro e' limitato al tentativo di rilevamento, che avviene solo con il Sole
/// tra 10 e 90 gradi e al massimo ogni 5 minuti dopo un punto accettato; un errore non ferma mai la cattura.
/// I punti raccolti vivono in memoria: al riavvio si ricomincia la raccolta, il modello accettato resta in configurazione.
/// </remarks>
public sealed partial class AllSkyEngine : ICalibrationService
{
    private readonly SunCalibrationSession _calibration = new();

    /// <inheritdoc />
    public event Action? SnapshotChanged;

    /// <inheritdoc />
    public CalibrationSnapshot GetSnapshot()
    {
        OpticalCalibrationConfig? accepted;
        lock (_gate)
        {
            accepted = _config.Optics;
        }

        return _calibration.Snapshot(accepted);
    }

    /// <inheritdoc />
    public CalibrationActionResult AcceptProposal()
    {
        CalibrationProposal? proposal = _calibration.Pending();
        if (proposal is null)
        {
            return CalibrationActionResult.NoProposal;
        }

        if (!proposal.Acceptable)
        {
            return CalibrationActionResult.NotAcceptable;
        }

        (int width, int height) = _calibration.FrameSize();
        AppConfig current;
        lock (_gate)
        {
            current = _config;
        }

        OpticalCalibrationConfig optics = OpticalCalibrationConfig.FromModel(
            proposal.Model, width, height, proposal.Source, proposal.RmsPx, _clock.UtcNow);

        // Con errore di salvataggio la proposta resta: l'utente puo' riprovare.
        ConfigUpdateResult result = UpdateConfig(current with { Optics = optics });
        if (!result.Ok)
        {
            return CalibrationActionResult.SaveFailed;
        }

        _calibration.Consume(proposal);
        RaiseCalibration();
        return CalibrationActionResult.Ok;
    }

    /// <inheritdoc />
    public CalibrationActionResult RejectProposal()
    {
        if (_calibration.Pending() is null)
        {
            return CalibrationActionResult.NoProposal;
        }

        _calibration.Reject();
        RaiseCalibration();
        return CalibrationActionResult.Ok;
    }

    /// <inheritdoc />
    public CalibrationActionResult SetManual(FisheyeModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!_calibration.SetManual(model))
        {
            return CalibrationActionResult.NoFrame;
        }

        RaiseCalibration();
        return CalibrationActionResult.Ok;
    }

    /// <summary>
    /// Cerca il Sole nel frame e, se la posizione e' valida, lo aggiunge al collector. Non lancia mai eccezioni:
    /// un errore viene solo registrato nel log.
    /// </summary>
    private void FeedCalibration(RawFrame raw)
    {
        try
        {
            _calibration.ObserveFrame(raw.Width, raw.Height);

            AppConfig config;
            lock (_gate)
            {
                config = _config;
            }

            DateTimeOffset utc = raw.Timestamp.ToUniversalTime();
            HorizontalPosition sun = SolarPosition.Horizon(utc, config.Site.Latitude, config.Site.Longitude);
            if (!_calibration.ShouldDetect(utc, sun.AltitudeDeg))
            {
                return;
            }

            if (!SunDetector.TryFindSun(LumaOf(raw), raw.Width, raw.Height, out SunDetection detection))
            {
                _calibration.RecordMiss(utc);
                return;
            }

            if (_calibration.AddSighting(utc, detection.X, detection.Y, sun.AltitudeDeg, sun.AzimuthDeg))
            {
                RaiseCalibration();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log("Raccolta dei punti del Sole per la calibrazione non riuscita", ex);
        }
    }

    // Luminanza a 8 bit dal grezzo a 16 bit: il Sole satura tutti i canali, quindi basta il valore alto.
    private static byte[] LumaOf(RawFrame raw)
    {
        ushort[] pixels = raw.Pixels;
        var luma = new byte[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            luma[i] = (byte)(pixels[i] >> 8);
        }

        return luma;
    }

    private void RaiseCalibration()
    {
        Action? handler = SnapshotChanged;
        if (handler is null)
        {
            return;
        }

        foreach (Delegate subscriber in handler.GetInvocationList())
        {
            try
            {
                ((Action)subscriber)();
            }
            catch (Exception ex)
            {
                _log("Un ascoltatore della calibrazione ha generato un errore", ex);
            }
        }
    }
}
