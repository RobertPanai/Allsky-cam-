namespace AllSky.Studio;

/// <summary>
/// Costruisce il piano di montaggio a partire dai frame di una sorgente e dalle operazioni del progetto.
/// Le operazioni si applicano nell'ordine del progetto. Le statistiche dei frame arrivano da una funzione
/// iniettata: il pianificatore non legge né decodifica immagini, e non lancia eccezioni per input dell'utente.
/// </summary>
public static class MontagePlanner
{
    /// <summary>
    /// Pianifica il montaggio.
    /// </summary>
    /// <param name="frames">Frame in ingresso, già ordinati per istante (come li dà la scansione).</param>
    /// <param name="operations">Operazioni del progetto, nell'ordine dato.</param>
    /// <param name="statsOf">Statistiche di un frame. Viene chiamata solo se serve, una volta per frame.</param>
    /// <returns>Piano, oppure gli errori delle operazioni non valide.</returns>
    public static MontagePlanOutcome Plan(
        IReadOnlyList<StudioFrame> frames,
        IReadOnlyList<StudioOperation> operations,
        Func<StudioFrame, FrameStats> statsOf)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(statsOf);

        var errors = new List<string>();
        var parsed = new List<MontageOperation>();
        for (int i = 0; i < operations.Count; i++)
        {
            OperationParseResult result = OperationParser.Parse(operations[i]);
            if (result.IsValid)
            {
                parsed.Add(result.Operation!);
            }
            else
            {
                errors.Add($"operazione {i + 1}: {result.Error}");
            }
        }

        if (errors.Count > 0)
        {
            return new MontagePlanOutcome(null, errors);
        }

        var builder = new PlanBuilder(frames, statsOf);
        foreach (MontageOperation operation in parsed)
        {
            builder.Apply(operation);
        }

        return new MontagePlanOutcome(builder.Build(), []);
    }

    private static int ToFrames(double seconds, int fps)
    {
        // Almeno un frame: una dissolvenza di 0,1 s a 1 fps arrotondata a zero non avrebbe effetto.
        return Math.Max(1, (int)Math.Round(seconds * fps, MidpointRounding.AwayFromZero));
    }

    private static bool IsFade(MontageSegmentKind kind)
    {
        return kind is MontageSegmentKind.FadeIn or MontageSegmentKind.FadeOut or MontageSegmentKind.Crossfade;
    }

    private static double Median(double[] values)
    {
        double[] sorted = (double[])values.Clone();
        Array.Sort(sorted);
        int n = sorted.Length;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }

    /// <summary>Stato del piano durante l'applicazione delle operazioni.</summary>
    private sealed class PlanBuilder
    {
        private readonly Func<StudioFrame, FrameStats> _statsOf;
        private readonly Dictionary<StudioFrame, FrameStats> _statsCache = new();
        private readonly HashSet<string> _inputNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<RemovalReason, int> _removed = Enum.GetValues<RemovalReason>().ToDictionary(reason => reason, _ => 0);
        private readonly List<string> _warnings = new();
        private readonly List<PixelOp> _pixelOps = new();

        // Schede titolo e dissolvenze: la durata in frame dipende dal fps finale, quindi si calcola alla fine.
        private readonly List<MontageOperation> _timed = new();
        private readonly int _inputCount;
        private List<StudioFrame> _current;
        private int _repeated;
        private int _fps = OperationParser.DefaultFps;
        private int _speedCount;

        public PlanBuilder(IReadOnlyList<StudioFrame> frames, Func<StudioFrame, FrameStats> statsOf)
        {
            _statsOf = statsOf;
            _current = frames.ToList();
            _inputCount = frames.Count;
            foreach (StudioFrame frame in frames)
            {
                _inputNames.Add(frame.FileName);
            }
        }

        /// <summary>Applica un'operazione alla selezione corrente.</summary>
        public void Apply(MontageOperation operation)
        {
            switch (operation)
            {
                case IntervalOperation interval:
                    ApplyInterval(interval);
                    break;
                case RemoveFramesOperation remove:
                    ApplyRemoveFrames(remove);
                    break;
                case AutoRemoveOperation auto:
                    ApplyAutoRemove(auto);
                    break;
                case SpeedOperation speed:
                    ApplySpeed(speed);
                    break;
                case CropOperation crop:
                    _pixelOps.Add(new PixelCrop(crop.X, crop.Y, crop.Width, crop.Height));
                    break;
                case RotateOperation rotate:
                    if (rotate.Degrees != 0)
                    {
                        _pixelOps.Add(new PixelRotate(rotate.Degrees));
                    }

                    break;
                case MirrorOperation mirror:
                    _pixelOps.Add(new PixelMirror(mirror.Horizontal, mirror.Vertical));
                    break;
                case TitleCardOperation or FadeOperation:
                    _timed.Add(operation);
                    break;
                case InfoMaskOperation:
                    _warnings.Add("maschera informativa: non ancora disponibile, operazione ignorata.");
                    break;
                case ColorCorrectionOperation:
                    _warnings.Add("correzione del colore: non ancora disponibile, operazione ignorata.");
                    break;
                default:
                    throw new InvalidOperationException("operazione non gestita dal pianificatore.");
            }
        }

        /// <summary>Costruisce il piano finale.</summary>
        public MontagePlan Build()
        {
            int fps = _fps;
            if (_speedCount > 1)
            {
                _warnings.Add("più operazioni di velocità: il frame rate è quello dell'ultima.");
            }

            var segments = new List<MontageSegment>();
            var crossfades = new List<FadeOperation>();
            int titleFrames = 0;
            int titleCount = 0;

            foreach (MontageOperation operation in _timed)
            {
                switch (operation)
                {
                    case TitleCardOperation title:
                        int titleLength = ToFrames(title.DurationSeconds, fps);
                        var kind = title.Position == TitleCardPosition.End
                            ? MontageSegmentKind.TitleCardAtEnd
                            : MontageSegmentKind.TitleCardAtStart;
                        segments.Add(new MontageSegment(kind, titleLength, title.Text));
                        titleFrames += titleLength;
                        titleCount++;
                        break;
                    case FadeOperation { Kind: FadeKind.Crossfade } crossfade:
                        crossfades.Add(crossfade);
                        break;
                    case FadeOperation fade:
                        var fadeKind = fade.Kind == FadeKind.Out ? MontageSegmentKind.FadeOut : MontageSegmentKind.FadeIn;
                        segments.Add(new MontageSegment(fadeKind, ToFrames(fade.DurationSeconds, fps), null));
                        break;
                }
            }

            // La dissolvenza incrociata sta sul bordo tra ogni scheda titolo e il montaggio.
            if (crossfades.Count > 0 && titleCount == 0)
            {
                _warnings.Add("dissolvenza incrociata senza schede titolo: nessun bordo da sfumare, operazione ignorata.");
            }
            else
            {
                foreach (FadeOperation crossfade in crossfades)
                {
                    int length = ToFrames(crossfade.DurationSeconds, fps);
                    for (int i = 0; i < titleCount; i++)
                    {
                        segments.Add(new MontageSegment(MontageSegmentKind.Crossfade, length, null));
                    }
                }
            }

            int outputFrames = _current.Count;
            if (outputFrames == 0)
            {
                _warnings.Add("nessun frame in uscita: il montaggio è vuoto.");
            }
            else if (segments.Any(segment => IsFade(segment.Kind) && segment.DurationFrames > outputFrames))
            {
                _warnings.Add("una dissolvenza è più lunga del montaggio: il rendering la troncherà.");
            }

            double durationSeconds = (outputFrames + titleFrames) / (double)fps;
            var summary = new MontageSummary(
                _inputCount,
                new Dictionary<RemovalReason, int>(_removed),
                _repeated,
                outputFrames,
                titleFrames,
                durationSeconds);

            return new MontagePlan(
                _current.ToList(),
                fps,
                segments,
                _pixelOps.ToList(),
                _warnings.ToList(),
                summary);
        }

        private void ApplyInterval(IntervalOperation interval)
        {
            int before = _current.Count;
            _current = _current
                .Where(frame => (interval.From is null || frame.TimestampUtc >= interval.From.Value)
                                && (interval.To is null || frame.TimestampUtc <= interval.To.Value))
                .ToList();
            _removed[RemovalReason.Interval] += before - _current.Count;
        }

        private void ApplyRemoveFrames(RemoveFramesOperation remove)
        {
            var names = new HashSet<string>(remove.FileNames, StringComparer.OrdinalIgnoreCase);
            int before = _current.Count;
            _current = _current.Where(frame => !names.Contains(frame.FileName)).ToList();
            _removed[RemovalReason.ManualRemoval] += before - _current.Count;

            int missing = names.Count(name => !_inputNames.Contains(name));
            if (missing > 0)
            {
                _warnings.Add($"{missing} nomi nell'elenco di rimozione non sono presenti nella sorgente: nessun frame rimosso per quelli.");
            }
        }

        private void ApplyAutoRemove(AutoRemoveOperation auto)
        {
            if (auto.BlankFrames)
            {
                var kept = new List<StudioFrame>(_current.Count);
                foreach (StudioFrame frame in _current)
                {
                    if (Stats(frame).StdDevLuma < auto.BlankStdDevMax)
                    {
                        _removed[RemovalReason.BlankFrame]++;
                    }
                    else
                    {
                        kept.Add(frame);
                    }
                }

                _current = kept;
            }

            if (auto.ExposureOutliers)
            {
                ApplyExposureOutliers(auto.MadFactor);
            }

            if (auto.Clouds)
            {
                _warnings.Add("rilevazione delle nuvole non ancora disponibile: nessun frame rimosso per nuvole.");
            }
        }

        /// <summary>Rimuove i frame con luminanza media fuori da mediana ± k·MAD (MAD senza fattore di scala).</summary>
        private void ApplyExposureOutliers(double madFactor)
        {
            if (_current.Count < 3)
            {
                _warnings.Add("meno di tre frame: nessun outlier di esposizione valutato.");
                return;
            }

            double[] means = _current.Select(frame => Stats(frame).MeanLuma).ToArray();
            double median = Median(means);
            double mad = Median(means.Select(value => Math.Abs(value - median)).ToArray());
            if (mad == 0)
            {
                _warnings.Add("esposizione quasi costante (dispersione nulla): nessun outlier di esposizione rimosso.");
                return;
            }

            double limit = madFactor * mad;
            var kept = new List<StudioFrame>(_current.Count);
            for (int i = 0; i < means.Length; i++)
            {
                if (Math.Abs(means[i] - median) > limit)
                {
                    _removed[RemovalReason.ExposureOutlier]++;
                }
                else
                {
                    kept.Add(_current[i]);
                }
            }

            _current = kept;
        }

        private void ApplySpeed(SpeedOperation speed)
        {
            _speedCount++;
            _fps = speed.Fps;

            int n = _current.Count;
            // Con fattore f si producono ceil(n / f) frame; l'k-esimo prende il sorgente floor(k · f).
            // f = 2 tiene 1 frame ogni 2; f = 0,5 ripete ogni frame due volte.
            int outCount = (int)Math.Ceiling(Math.Round(n / speed.Factor, 9));
            var next = new List<StudioFrame>(outCount);
            for (int k = 0; k < outCount; k++)
            {
                int source = Math.Min((int)Math.Floor(Math.Round(k * speed.Factor, 9)), n - 1);
                next.Add(_current[source]);
            }

            if (outCount < n)
            {
                _removed[RemovalReason.SpeedSkipped] += n - outCount;
            }
            else
            {
                _repeated += outCount - n;
            }

            _current = next;
        }

        private FrameStats Stats(StudioFrame frame)
        {
            if (!_statsCache.TryGetValue(frame, out FrameStats? stats))
            {
                stats = _statsOf(frame);
                _statsCache[frame] = stats;
            }

            return stats;
        }
    }
}
