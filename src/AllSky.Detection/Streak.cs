namespace AllSky.Detection;

/// <summary>
/// Scia luminosa rilevata in un frame.
/// <para>
/// Coordinate in pixel del frame ridotto: x verso destra, y verso il basso. (X1, Y1) e (X2, Y2) sono
/// gli estremi lungo l'asse principale della componente.
/// </para>
/// </summary>
/// <param name="X1">Coordinata x del primo estremo.</param>
/// <param name="Y1">Coordinata y del primo estremo.</param>
/// <param name="X2">Coordinata x del secondo estremo.</param>
/// <param name="Y2">Coordinata y del secondo estremo.</param>
/// <param name="LengthPx">Lunghezza lungo l'asse principale, in pixel.</param>
/// <param name="PeakLuma">Eccesso di luminanza di picco sopra il fondo adattivo (livelli 8 bit).</param>
/// <param name="IntegratedLuma">Somma degli eccessi di luminanza su tutti i pixel della componente.</param>
/// <param name="Straightness">Scarto quadratico medio dei pixel dall'asse principale, in pixel (0 = retta perfetta).</param>
/// <param name="Score">Punteggio euristico per ordinare le rilevazioni: SNR di picco * sqrt(lunghezza) / (1 + Straightness).</param>
public sealed record Streak(
    double X1,
    double Y1,
    double X2,
    double Y2,
    double LengthPx,
    double PeakLuma,
    double IntegratedLuma,
    double Straightness,
    double Score);
