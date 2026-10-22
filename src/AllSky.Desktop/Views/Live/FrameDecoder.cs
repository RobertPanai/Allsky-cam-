using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace AllSky.Desktop.Views.Live;

/// <summary>
/// Decodifica un JPEG in un <see cref="BitmapSource"/> congelato, pronto per essere usato sul thread dell'interfaccia.
/// </summary>
/// <remarks>
/// Va chiamato fuori dal thread UI. Lo stream è di sola lettura in memoria e viene chiuso subito: con
/// <see cref="BitmapCacheOption.OnLoad"/> i pixel sono già nel frame, quindi non resta alcun riferimento allo stream.
/// Il frame viene congelato (<see cref="System.Windows.Freezable.Freeze"/>) per poterlo passare tra thread senza copie.
/// </remarks>
internal static class FrameDecoder
{
    /// <summary>Decodifica i byte di un JPEG.</summary>
    /// <param name="jpeg">Byte del JPEG elaborato o grezzo.</param>
    /// <exception cref="ArgumentNullException"><paramref name="jpeg"/> è nullo.</exception>
    /// <exception cref="NotSupportedException">I byte non sono un'immagine decodificabile.</exception>
    public static BitmapSource Decode(byte[] jpeg)
    {
        ArgumentNullException.ThrowIfNull(jpeg);

        using var stream = new MemoryStream(jpeg, writable: false);
        BitmapFrame frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }
}
