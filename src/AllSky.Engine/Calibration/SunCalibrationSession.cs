using AllSky.Core.Calibration;
using AllSky.Sky;
using AllSky.Sky.Calibration;

namespace AllSky.Engine;

/// <summary>
/// Stato della calibrazione dal Sole in memoria: punti raccolti, proposta in sospeso e frequenza di ricerca del Sole.
/// Nessuna I/O: il salvataggio in configurazione lo fa l'Engine. Thread-safe, con un solo lock breve.
/// </summary>
/// <remarks>
/// <para>
/// Il Sole si cerca solo con altezza tra 10 e 90 gradi (stessa regola del collector). Dopo un punto accettato si
/// aspetta la separazione minima del collector (5 minuti); dopo un tentativo senza Sole si riprova dopo un minuto.
/// Il tentativo costa una scansione del frame, quindi il controllo avviene prima della scansione.
/// </para>
/// <para>
/// Una proposta manuale resta quella in sospeso: i nuovi punti aggiornano l'elenco e i residui, ma non la sostituiscono
/// finche' l'utente non la accetta o non la rifiuta.
/// </para>
/// </remarks>
public sealed class SunCalibrationSession
{
    /// <summary>Attesa prima di un nuovo tentativo dopo un frame senza Sole rilevato.</summary>
    public static readonly TimeSpan RetryAfterMiss = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();
    private readonly SunCalibrationCollector _collector = new();
    private readonly List<CalibrationPoint> _points = new();
    private DateTimeOffset _nextDetection = DateTimeOffset.MinValue;
    private int _frameWidth;
    private int _frameHeight;
    private CalibrationProposal? _proposal;
    private string? _failure;

    /// <summary>Registra le dimensioni del frame in cui si raccolgono i punti.</summary>
    public void ObserveFrame(int width, int height)
    {
        lock (_gate)
        {
            _frameWidth = width;
            _frameHeight = height;
        }
    }

    /// <summary>Vero se conviene cercare il Sole in questo frame: altezza ammessa e nessuna attesa in corso.</summary>
    public bool ShouldDetect(DateTimeOffset utc, double sunAltDeg)
    {
        if (!(sunAltDeg >= SunCalibrationCollector.MinAltitudeDeg && sunAltDeg <= 90.0))
        {
            return false;
        }

        lock (_gate)
        {
            return utc >= _nextDetection;
        }
    }

    /// <summary>Segna un tentativo senza Sole rilevato: il prossimo sara' dopo <see cref="RetryAfterMiss"/>.</summary>
    public void RecordMiss(DateTimeOffset utc)
    {
        lock (_gate)
        {
            _nextDetection = utc + RetryAfterMiss;
        }
    }

    /// <summary>
    /// Aggiunge un avvistamento del Sole e, con abbastanza punti, rifa' il fit. Restituisce true se il collector
    /// ha accettato il punto (false per altezza fuori range o punto troppo vicino a un altro).
    /// </summary>
    public bool AddSighting(DateTimeOffset utc, double x, double y, double altDeg, double azDeg)
    {
        lock (_gate)
        {
            if (!_collector.Add(utc, x, y, altDeg, azDeg))
            {
                _nextDetection = utc + RetryAfterMiss;
                return false;
            }

            _points.Add(new CalibrationPoint(utc, x, y, altDeg, azDeg));
            _nextDetection = utc + SunCalibrationCollector.MinSeparation;

            bool manualPending = _proposal?.Source == OpticalCalibrationSource.Manual;
            if (!manualPending && _points.Count >= SunCalibrationCollector.MinPoints)
            {
                Refit();
            }

            return true;
        }
    }

    /// <summary>
    /// Propone un modello manuale al posto della proposta in sospeso. Il RMS e' calcolato su tutti i punti raccolti.
    /// Restituisce false se non c'e' ancora un frame (le coordinate non sarebbero definite).
    /// </summary>
    public bool SetManual(FisheyeModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        lock (_gate)
        {
            if (_frameWidth <= 0 || _frameHeight <= 0)
            {
                return false;
            }

            IReadOnlyList<CalibrationResidual> residuals = CalibrationFlow.Residuals(model, _points.ToArray());
            double rms = CalibrationFlow.RmsPx(residuals);
            _proposal = new CalibrationProposal(
                model,
                OpticalCalibrationSource.Manual,
                rms,
                rms / model.HorizonRadius,
                _points.Count,
                0,
                Acceptable: true,
                residuals);
            return true;
        }
    }

    /// <summary>Scarta la proposta in sospeso. La raccolta prosegue.</summary>
    public void Reject()
    {
        lock (_gate)
        {
            _proposal = null;
        }
    }

    /// <summary>Proposta in sospeso, null se non c'e'.</summary>
    public CalibrationProposal? Pending()
    {
        lock (_gate)
        {
            return _proposal;
        }
    }

    /// <summary>
    /// Toglie la proposta solo se e' ancora quella indicata (confronto per riferimento): una proposta nuova
    /// arrivata nel frattempo non viene persa.
    /// </summary>
    public void Consume(CalibrationProposal accepted)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_proposal, accepted))
            {
                _proposal = null;
            }
        }
    }

    /// <summary>Foto dello stato per l'interfaccia. <paramref name="accepted"/> e' il sotto-oggetto della configurazione.</summary>
    public CalibrationSnapshot Snapshot(OpticalCalibrationConfig? accepted)
    {
        lock (_gate)
        {
            OpticalCalibrationConfig? usable = accepted?.ToModel() is null ? null : accepted;
            CalibrationPhase phase = CalibrationFlow.PhaseOf(_proposal is not null, usable is not null);
            return new CalibrationSnapshot(
                phase,
                _points.ToArray(),
                SunCalibrationCollector.MinPoints,
                _proposal is null ? _failure : null,
                _proposal,
                usable,
                _frameWidth,
                _frameHeight);
        }
    }

    /// <summary>Dimensioni del frame di riferimento (0 se nessun frame ancora).</summary>
    public (int Width, int Height) FrameSize()
    {
        lock (_gate)
        {
            return (_frameWidth, _frameHeight);
        }
    }

    // Chiamato sotto _gate. Se il fit non riesce, la proposta automatica decade e il motivo resta per l'interfaccia.
    private void Refit()
    {
        if (!_collector.TryFit(out SkyCalibrationResult? result) || result is null)
        {
            _proposal = null;
            _failure = _collector.LastFailureReason;
            return;
        }

        _failure = null;
        IReadOnlyList<CalibrationResidual> residuals = CalibrationFlow.Residuals(result.Model, _points.ToArray());
        _proposal = new CalibrationProposal(
            result.Model,
            OpticalCalibrationSource.Sun,
            result.RmsPx,
            result.RmsFractionOfRadius,
            result.PointsUsed,
            result.OutliersRejected,
            Acceptable: result.Accepted,
            residuals);
    }
}
