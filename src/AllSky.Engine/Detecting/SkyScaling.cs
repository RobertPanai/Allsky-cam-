using AllSky.Core.Calibration;
using AllSky.Events;
using AllSky.Sky;
using AllSky.Sky.Calibration;

namespace AllSky.Engine.Detecting;

/// <summary>
/// Calibrazione accettata portata sulle coordinate del frame ridotto del rilevatore (P5.5).
/// </summary>
/// <remarks>
/// Convenzione: il modello in <see cref="OpticalCalibrationConfig"/> è nel frame di riferimento (<c>FrameWidthPx</c> x
/// <c>FrameHeightPx</c>, cioè il frame intero della camera). Il rilevatore lavora sul frame ridotto, quindi centro e raggio
/// si scalano con il rapporto tra le dimensioni. La calibrazione vale solo se il frame intero ha le stesse dimensioni
/// di quelle di riferimento: con binning o risoluzione cambiati è come non calibrata.
/// </remarks>
internal static class SkyScaling
{
    /// <summary>
    /// Calibrazione valida per il frame ridotto di dimensioni <paramref name="detWidth"/> x <paramref name="detHeight"/>,
    /// oppure null se manca, è invalida o non corrisponde alle dimensioni del frame intero.
    /// </summary>
    public static SkyCalibrationResult? ForReducedFrame(
        OpticalCalibrationConfig? optics,
        int fullWidth,
        int fullHeight,
        int detWidth,
        int detHeight)
    {
        if (optics is null || optics.FrameWidthPx <= 0 || optics.FrameHeightPx <= 0)
        {
            return null;
        }

        if (optics.FrameWidthPx != fullWidth || optics.FrameHeightPx != fullHeight)
        {
            return null;
        }

        FisheyeModel? model = optics.ToModel();
        if (model is null)
        {
            return null;
        }

        double sx = detWidth / (double)fullWidth;
        double sy = detHeight / (double)fullHeight;
        double scale = (sx + sy) / 2.0;
        var scaled = new FisheyeModel(
            model.CenterX * sx,
            model.CenterY * sy,
            model.ScaleRadiusPx * scale,
            model.NorthRotationDeg,
            model.Mirrored,
            model.K2);
        return new SkyCalibrationResult(scaled, optics.RmsPx * scale, 0.0, 0, 0, Accepted: true);
    }
}

/// <summary>Pixel del frame ridotto → alt/az, con il modello della calibrazione. Null se la camera non è calibrata.</summary>
internal sealed class PixelToSkyAdapter : IPixelToSky
{
    private readonly FisheyeModel? _model;

    public PixelToSkyAdapter(FisheyeModel? model)
    {
        _model = model;
    }

    public AltAz? ToAltAz(double x, double y)
    {
        if (_model is null || !_model.TryPixelToAltAz(x, y, out double alt, out double az))
        {
            return null;
        }

        return new AltAz { AltDeg = alt, AzDeg = az };
    }
}
