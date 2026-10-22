using AllSky.Astro;
using AllSky.Sky;

namespace AllSky.Nina;

/// <summary>
/// Dati del telescopio e della camera per disegnare il campo inquadrato sull'all-sky.
/// Tutti i valori arrivano dall'utente (o da NINA quando l'API li fornisce, [NV] in P11.0): nessun default inventato.
/// Un valore null, non finito o non positivo rende il campo non calcolabile (<see cref="FieldOfViewStatus.MissingInput"/>).
/// </summary>
/// <param name="FocalLengthMm">Focale del telescopio in millimetri (obbligatoria, maggiore di zero).</param>
/// <param name="SensorWidthMm">Larghezza attiva del sensore in millimetri (obbligatoria, maggiore di zero).</param>
/// <param name="SensorHeightMm">Altezza attiva del sensore in millimetri (obbligatoria, maggiore di zero).</param>
/// <param name="PixelSizeUm">Dimensione del pixel in micrometri. Facoltativa: validata se presente, non usata nella geometria
/// (il campo e' calcolato dalle dimensioni fisiche del sensore, non dal numero di pixel).</param>
/// <param name="RotatorAngleDeg">Angolo del rotatore in gradi. Facoltativo: se assente il rettangolo e' disegnato senza rotazione
/// aggiuntiva e il risultato lo dichiara con <see cref="FieldOfViewResult.RotatorAssumed"/>.</param>
public sealed record FovInput(
    double? FocalLengthMm,
    double? SensorWidthMm,
    double? SensorHeightMm,
    double? PixelSizeUm = null,
    double? RotatorAngleDeg = null);

/// <summary>Esito del calcolo del campo inquadrato.</summary>
public enum FieldOfViewStatus
{
    /// <summary>Campo calcolato: spigoli, contorno e centro in pixel disponibili.</summary>
    Ok,

    /// <summary>Manca un dato obbligatorio (focale, sensore, centro), o un dato fornito non e' valido.</summary>
    MissingInput,

    /// <summary>Il centro (soggetto puntato) e' sotto l'orizzonte o cade fuori dal frame: il campo non si disegna.</summary>
    OutsideFrame,

    /// <summary>Nessuna calibrazione ottica accettata: invitare a calibrare.</summary>
    NoCalibration,
}

/// <summary>
/// Esito di <see cref="FieldOfView.Compute"/>. Gli spigoli e il contorno sono vuoti tranne che con <see cref="Status"/> = Ok.
/// </summary>
/// <param name="Status">Motivo dell'esito.</param>
/// <param name="Corners">Quattro spigoli in pixel nell'ordine TL, TR, BR, BL del sensore (vuoti se non Ok).</param>
/// <param name="Outline">Contorno campionato in senso orario a partire da TL, con <see cref="FieldOfView.SamplesPerSide"/> punti per lato
/// (gli spigoli sono inclusi). Sull'all-sky la forma puo' apparire curva: il campo e' rettilineo sul sensore, ma la lente
/// fisheye lo deforma. Vuoto se non Ok.</param>
/// <param name="CentrePx">Centro del campo (soggetto puntato) in pixel, se il centro cade nel frame.</param>
/// <param name="Rotation">Angolo in gradi del lato superiore del campo rispetto all'orizzontale dell'immagine, normalizzato in [-90, 90).
/// Misurato sui pixel, quindi tiene conto della rotazione dell'immagine (<see cref="FisheyeModel.NorthRotationDeg"/>).</param>
/// <param name="FovWidthDeg">Campo angolare in gradi sul lato lungo della larghezza del sensore: 2 atan(larghezza / 2 focale).</param>
/// <param name="FovHeightDeg">Campo angolare in gradi sull'altezza del sensore: 2 atan(altezza / 2 focale).</param>
/// <param name="RotatorAssumed">True quando il rotatore non e' stato fornito e la rotazione aggiuntiva e' zero per convenzione.</param>
/// <param name="ClippedAtHorizon">True quando almeno un punto del contorno sta sotto l'orizzonte: quei punti sono portati
/// sull'orizzonte alla stessa azimut (approssimazione), non si disegnano sotto.</param>
public sealed record FieldOfViewResult(
    FieldOfViewStatus Status,
    IReadOnlyList<PixelPosition> Corners,
    IReadOnlyList<PixelPosition> Outline,
    PixelPosition? CentrePx,
    double? Rotation,
    double? FovWidthDeg,
    double? FovHeightDeg,
    bool RotatorAssumed,
    bool ClippedAtHorizon);

/// <summary>Esito del controllo di allineamento.</summary>
public enum AlignmentStatus
{
    /// <summary>Posizione del telescopio non fornita o punto puntato non disponibile: nessuna misura.</summary>
    NotMeasured,

    /// <summary>Scarto misurato in pixel e, se il punto del telescopio cade sul cielo, in gradi.</summary>
    Measured,
}

/// <summary>
/// Scarto tra il punto puntato dal mount e la posizione del telescopio vista sull'immagine.
/// Informativo: non viene applicata nessuna correzione.
/// </summary>
/// <param name="Status">NotMeasured se manca un dato, Measured altrimenti.</param>
/// <param name="OffsetPx">Distanza in pixel tra il punto puntato (proiettato) e la posizione fornita.</param>
/// <param name="OffsetDeg">Distanza angolare sul cielo in gradi. Null se la posizione fornita cade fuori dal cerchio dell'orizzonte.</param>
/// <param name="ExceedsThreshold">True se lo scarto angolare supera la soglia; false se non la supera; null se la soglia non e' impostata
/// o se lo scarto in gradi non e' noto.</param>
public sealed record AlignmentCheck(
    AlignmentStatus Status,
    double? OffsetPx,
    double? OffsetDeg,
    bool? ExceedsThreshold);

/// <summary>
/// Campo inquadrato dal telescopio proiettato sull'all-sky, e controllo sperimentale dell'allineamento.
/// Funzioni pure: nessun I/O, nessun orologio, nessuna dipendenza da WPF.
/// </summary>
/// <remarks>
/// <b>Approssimazione.</b> Il campo e' un rettangolo sulla tangente celeste centrato nel soggetto puntato. Il sensore e' piano,
/// quindi gli spigoli stanno sul piano focale a (+-larghezza/2, +-altezza/2) divisi per la focale: la proiezione gnomonica
/// rende il campo rettilineo nel cielo. Il frame all-sky usa pero' una lente fisheye: il contorno viene campionato sui lati
/// e proiettato con <see cref="FisheyeModel"/>, quindi sull'immagine puo' apparire curvo. Nessuna distorsione del telescopio
/// e nessuna correzione di campo e' modellata.
///
/// <b>Rotatore.</b> La convenzione dell'angolo (riferimento e segno rispetto a "su" e a est) e' [NV]: va verificata con il
/// rotatore reale. Con <see cref="FovInput.RotatorAngleDeg"/> assente la rotazione aggiuntiva vale zero (vedi RotatorAssumed).
///
/// <b>Parallasse.</b> La all-sky sta vicino al telescopio: per oggetti celesti la direzione coincide con quella puntata.
/// Nessuna correzione di parallasse viene applicata.
///
/// <b>Epoca.</b> Il centro arriva gia' in alt/az dal chiamante; la gestione dell'epoca delle coordinate e' a monte (vedi TargetMarker).
/// </remarks>
public static class FieldOfView
{
    /// <summary>Numero di segmenti per lato del contorno campionato.</summary>
    public const int SamplesPerSide = 16;

    /// <summary>
    /// Calcola il campo inquadrato.
    /// </summary>
    /// <param name="input">Dati di camera, focale e rotatore forniti dall'utente.</param>
    /// <param name="centre">Altezza e azimut del soggetto puntato dal mount (proiettato dal modello). Null = non disponibile.</param>
    /// <param name="model">Modello fisheye della calibrazione accettata. Null = calibrazione assente.</param>
    /// <param name="frameWidthPx">Larghezza del frame in pixel (maggiore di zero).</param>
    /// <param name="frameHeightPx">Altezza del frame in pixel (maggiore di zero).</param>
    public static FieldOfViewResult Compute(
        FovInput input,
        HorizontalPosition? centre,
        FisheyeModel? model,
        int frameWidthPx,
        int frameHeightPx)
    {
        if (frameWidthPx <= 0) throw new ArgumentOutOfRangeException(nameof(frameWidthPx), frameWidthPx, "La larghezza del frame deve essere maggiore di zero.");
        if (frameHeightPx <= 0) throw new ArgumentOutOfRangeException(nameof(frameHeightPx), frameHeightPx, "L'altezza del frame deve essere maggiore di zero.");

        if (!IsPositive(input.FocalLengthMm) || !IsPositive(input.SensorWidthMm) || !IsPositive(input.SensorHeightMm)
            || (input.PixelSizeUm is double px && !IsPositive(px))
            || (input.RotatorAngleDeg is double rot && !double.IsFinite(rot)))
        {
            return Absent(FieldOfViewStatus.MissingInput);
        }

        if (centre is not HorizontalPosition c || !double.IsFinite(c.AltitudeDeg) || !double.IsFinite(c.AzimuthDeg))
        {
            return Absent(FieldOfViewStatus.MissingInput);
        }

        if (model is null)
        {
            return Absent(FieldOfViewStatus.NoCalibration);
        }

        if (!model.TryAltAzToPixel(c.AltitudeDeg, c.AzimuthDeg, out double cx, out double cy)
            || cx < 0.0 || cy < 0.0 || cx > frameWidthPx || cy > frameHeightPx)
        {
            return Absent(FieldOfViewStatus.OutsideFrame);
        }

        double focal = input.FocalLengthMm!.Value;
        double halfW = input.SensorWidthMm!.Value / 2.0;
        double halfH = input.SensorHeightMm!.Value / 2.0;
        bool rotatorAssumed = input.RotatorAngleDeg is null;
        double psi = (input.RotatorAngleDeg ?? 0.0) * Math.PI / 180.0;

        // Base tangente al centro: e1 verso l'azimut crescente (est), e2 verso l'altezza crescente (su).
        Vec3 centreVec = ToVector(c.AltitudeDeg, c.AzimuthDeg);
        Vec3 east = new(-Math.Sin(ToRad(c.AzimuthDeg)), Math.Cos(ToRad(c.AzimuthDeg)), 0.0);
        Vec3 up = new(
            -Math.Sin(ToRad(c.AltitudeDeg)) * Math.Cos(ToRad(c.AzimuthDeg)),
            -Math.Sin(ToRad(c.AltitudeDeg)) * Math.Sin(ToRad(c.AzimuthDeg)),
            Math.Cos(ToRad(c.AltitudeDeg)));

        // Assi del rettangolo: w lungo la larghezza, h lungo l'altezza, ruotati di psi nel piano tangente.
        Vec3 w = east * Math.Cos(psi) + up * Math.Sin(psi);
        Vec3 h = east * (-Math.Sin(psi)) + up * Math.Cos(psi);

        bool clipped = false;
        var outline = new List<PixelPosition>(4 * SamplesPerSide);
        // Ordine: TL -> TR -> BR -> BL -> TL. Ogni lato ha SamplesPerSide segmenti; gli spigoli sono i punti multipli di SamplesPerSide.
        (double sx, double sy)[] sides = { (-1, 1), (1, 1), (1, -1), (-1, -1) };
        for (int side = 0; side < 4; side++)
        {
            (double ax, double ay) = sides[side];
            (double bx, double by) = sides[(side + 1) % 4];
            for (int i = 0; i < SamplesPerSide; i++)
            {
                double t = (double)i / SamplesPerSide;
                double su = ax + (bx - ax) * t;
                double sv = ay + (by - ay) * t;
                // Coordinate sul piano focale (mm) -> direzione del cielo (gnomonica, rettilinea).
                Vec3 dir = centreVec + w * (su * halfW / focal) + h * (sv * halfH / focal);
                ToAltAzClamped(dir, model, out PixelPosition p, out bool wasClipped);
                clipped |= wasClipped;
                outline.Add(p);
            }
        }

        var corners = new[] { outline[0], outline[SamplesPerSide], outline[2 * SamplesPerSide], outline[3 * SamplesPerSide] };

        // Rotazione: angolo del lato TL -> TR sull'immagine, normalizzato in [-90, 90).
        double dxTop = corners[1].X - corners[0].X;
        double dyTop = corners[1].Y - corners[0].Y;
        double rotDeg = Math.Atan2(dyTop, dxTop) * 180.0 / Math.PI;
        while (rotDeg >= 90.0) rotDeg -= 180.0;
        while (rotDeg < -90.0) rotDeg += 180.0;

        double fovW = 2.0 * Math.Atan(halfW / focal) * 180.0 / Math.PI;
        double fovH = 2.0 * Math.Atan(halfH / focal) * 180.0 / Math.PI;

        return new FieldOfViewResult(
            FieldOfViewStatus.Ok,
            corners,
            outline,
            new PixelPosition(cx, cy),
            rotDeg,
            fovW,
            fovH,
            rotatorAssumed,
            clipped);
    }

    /// <summary>
    /// Confronta il punto puntato dal mount con la posizione del telescopio sull'immagine. Solo informativo.
    /// </summary>
    /// <param name="model">Modello fisheye della calibrazione accettata. Null = nessuna misura.</param>
    /// <param name="pointed">Punto puntato dal mount (altezza e azimut, proiettato dal modello). Null = nessuna misura.</param>
    /// <param name="telescopePx">Posizione del telescopio nell'immagine, fornita dall'utente o da un marcatore. Null = nessuna misura.</param>
    /// <param name="thresholdDeg">Soglia di allarme in gradi, scelta dall'utente. Null = nessun allarme (nessun valore imposto).</param>
    public static AlignmentCheck CheckAlignment(
        FisheyeModel? model,
        HorizontalPosition? pointed,
        PixelPosition? telescopePx,
        double? thresholdDeg)
    {
        if (thresholdDeg is double t && (!double.IsFinite(t) || t < 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(thresholdDeg), thresholdDeg, "La soglia deve essere un numero finito non negativo.");
        }

        if (model is null || pointed is not HorizontalPosition p || telescopePx is not PixelPosition tp
            || !double.IsFinite(p.AltitudeDeg) || !double.IsFinite(p.AzimuthDeg))
        {
            return new AlignmentCheck(AlignmentStatus.NotMeasured, null, null, null);
        }

        if (!model.TryAltAzToPixel(p.AltitudeDeg, p.AzimuthDeg, out double px, out double py))
        {
            return new AlignmentCheck(AlignmentStatus.NotMeasured, null, null, null);
        }

        double offsetPx = Math.Sqrt((tp.X - px) * (tp.X - px) + (tp.Y - py) * (tp.Y - py));

        double? offsetDeg = null;
        if (model.TryPixelToAltAz(tp.X, tp.Y, out double telAlt, out double telAz))
        {
            offsetDeg = AngularSeparationDeg(ToVector(p.AltitudeDeg, p.AzimuthDeg), ToVector(telAlt, telAz));
        }

        bool? exceeds = thresholdDeg is double limit && offsetDeg is double off
            ? off > limit
            : null;

        return new AlignmentCheck(AlignmentStatus.Measured, offsetPx, offsetDeg, exceeds);
    }

    private static FieldOfViewResult Absent(FieldOfViewStatus status) =>
        new(status, Array.Empty<PixelPosition>(), Array.Empty<PixelPosition>(), null, null, null, null, false, false);

    private static void ToAltAzClamped(Vec3 dir, FisheyeModel model, out PixelPosition pixel, out bool clipped)
    {
        Vec3 n = dir.Normalized();
        double alt = Math.Asin(Math.Clamp(n.Z, -1.0, 1.0)) * 180.0 / Math.PI;
        double az = Math.Atan2(n.Y, n.X) * 180.0 / Math.PI;
        if (az < 0.0) az += 360.0;

        clipped = alt < 0.0;
        if (clipped) alt = 0.0;

        // Con alt in [0, 90] il modello restituisce sempre un punto; nessun ramo di errore da gestire.
        model.TryAltAzToPixel(alt, az, out double x, out double y);
        pixel = new PixelPosition(x, y);
    }

    private static Vec3 ToVector(double altDeg, double azDeg)
    {
        double a = ToRad(altDeg);
        double z = ToRad(azDeg);
        return new Vec3(Math.Cos(a) * Math.Cos(z), Math.Cos(a) * Math.Sin(z), Math.Sin(a));
    }

    // Separazione angolare tra due direzioni unitarie: atan2(|a x b|, a . b), stabile anche per angoli piccoli.
    private static double AngularSeparationDeg(Vec3 a, Vec3 b)
    {
        Vec3 an = a.Normalized();
        Vec3 bn = b.Normalized();
        double dot = an.X * bn.X + an.Y * bn.Y + an.Z * bn.Z;
        Vec3 cross = new(
            an.Y * bn.Z - an.Z * bn.Y,
            an.Z * bn.X - an.X * bn.Z,
            an.X * bn.Y - an.Y * bn.X);
        double crossLen = Math.Sqrt(cross.X * cross.X + cross.Y * cross.Y + cross.Z * cross.Z);
        return Math.Atan2(crossLen, dot) * 180.0 / Math.PI;
    }

    private static bool IsPositive(double? v) => v is double d && double.IsFinite(d) && d > 0.0;

    private static double ToRad(double deg) => deg * Math.PI / 180.0;

    private readonly record struct Vec3(double X, double Y, double Z)
    {
        public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);

        public Vec3 Normalized()
        {
            double len = Math.Sqrt(X * X + Y * Y + Z * Z);
            return len > 0.0 ? new Vec3(X / len, Y / len, Z / len) : this;
        }
    }
}
