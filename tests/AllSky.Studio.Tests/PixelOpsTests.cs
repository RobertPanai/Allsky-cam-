using Xunit;

namespace AllSky.Studio.Tests;

public sealed class PixelOpsTests
{
    /// <summary>
    /// Immagine 4×3 in cui ogni pixel porta il proprio indice nel canale R: id = y·4 + x (0..11).
    /// Gli angoli sono quindi riconoscibili: alto-sinistra 0, alto-destra 3, basso-sinistra 8, basso-destra 11.
    /// </summary>
    private static RgbImage Image4x3()
    {
        var pixels = new byte[4 * 3 * 3];
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                int id = y * 4 + x;
                int offset = id * 3;
                pixels[offset] = (byte)id;
                pixels[offset + 1] = (byte)(id + 100);
                pixels[offset + 2] = (byte)(id + 200);
            }
        }

        return new RgbImage(4, 3, pixels);
    }

    private static int Id(RgbImage image, int x, int y) => image.Pixels[(y * image.Width + x) * 3];

    [Fact]
    public void Rotate90_AngoliNelPostoGiustoOrarioEDimensioniScambiate()
    {
        RgbImage result = PixelOps.Rotate90(Image4x3());

        Assert.Equal(3, result.Width);
        Assert.Equal(4, result.Height);
        Assert.Equal(0, Id(result, 2, 0));  // alto-sinistra -> alto-destra
        Assert.Equal(3, Id(result, 2, 3));  // alto-destra -> basso-destra
        Assert.Equal(11, Id(result, 0, 3)); // basso-destra -> basso-sinistra
        Assert.Equal(8, Id(result, 0, 0));  // basso-sinistra -> alto-sinistra
    }

    [Fact]
    public void Rotate270_AngoliNelPostoGiusto()
    {
        RgbImage result = PixelOps.Rotate270(Image4x3());

        Assert.Equal(3, result.Width);
        Assert.Equal(4, result.Height);
        Assert.Equal(0, Id(result, 0, 3));  // alto-sinistra -> basso-sinistra
        Assert.Equal(3, Id(result, 0, 0));  // alto-destra -> alto-sinistra
        Assert.Equal(8, Id(result, 2, 3));  // basso-sinistra -> basso-destra
        Assert.Equal(11, Id(result, 2, 0)); // basso-destra -> alto-destra
    }

    [Fact]
    public void Rotate180_AngoliNelPostoGiusto()
    {
        RgbImage result = PixelOps.Rotate180(Image4x3());

        Assert.Equal(4, result.Width);
        Assert.Equal(3, result.Height);
        Assert.Equal(0, Id(result, 3, 2));
        Assert.Equal(11, Id(result, 0, 0));
    }

    [Fact]
    public void Rotate90Due_EquivaleA180()
    {
        RgbImage twice = PixelOps.Rotate90(PixelOps.Rotate90(Image4x3()));
        RgbImage once = PixelOps.Rotate180(Image4x3());

        Assert.Equal(once.Width, twice.Width);
        Assert.Equal(once.Height, twice.Height);
        Assert.Equal(once.Pixels, twice.Pixels);
    }

    [Fact]
    public void Rotate90Quattro_EIdentita()
    {
        RgbImage image = Image4x3();
        RgbImage result = image;
        for (int i = 0; i < 4; i++)
        {
            result = PixelOps.Rotate90(result);
        }

        Assert.Equal(image.Pixels, result.Pixels);
    }

    [Fact]
    public void Rotate270EquivaleATreVoltePiuRotate90()
    {
        RgbImage viaThree = PixelOps.Rotate90(PixelOps.Rotate90(PixelOps.Rotate90(Image4x3())));
        RgbImage direct = PixelOps.Rotate270(Image4x3());

        Assert.Equal(direct.Pixels, viaThree.Pixels);
    }

    [Fact]
    public void MirrorH_AngoliScambiatiSinistraDestra()
    {
        RgbImage result = PixelOps.MirrorH(Image4x3());

        Assert.Equal(4, result.Width);
        Assert.Equal(3, result.Height);
        Assert.Equal(0, Id(result, 3, 0));
        Assert.Equal(3, Id(result, 0, 0));
        Assert.Equal(8, Id(result, 3, 2));
    }

    [Fact]
    public void MirrorH_DueVolte_EIdentita()
    {
        RgbImage image = Image4x3();

        Assert.Equal(image.Pixels, PixelOps.MirrorH(PixelOps.MirrorH(image)).Pixels);
    }

    [Fact]
    public void MirrorV_AngoliScambiatiAltoBasso()
    {
        RgbImage result = PixelOps.MirrorV(Image4x3());

        Assert.Equal(0, Id(result, 0, 2));
        Assert.Equal(8, Id(result, 0, 0));
    }

    [Fact]
    public void MirrorV_DueVolte_EIdentita()
    {
        RgbImage image = Image4x3();

        Assert.Equal(image.Pixels, PixelOps.MirrorV(PixelOps.MirrorV(image)).Pixels);
    }

    [Fact]
    public void Crop_RettangoloDentro_ValoriAttesi()
    {
        RgbImage result = PixelOps.Crop(Image4x3(), 1, 1, 2, 2);

        Assert.Equal(2, result.Width);
        Assert.Equal(2, result.Height);
        Assert.Equal(5, Id(result, 0, 0));
        Assert.Equal(6, Id(result, 1, 0));
        Assert.Equal(9, Id(result, 0, 1));
        Assert.Equal(10, Id(result, 1, 1));
    }

    [Fact]
    public void Crop_TuttaLImmagine_EIdentita()
    {
        RgbImage image = Image4x3();

        Assert.Equal(image.Pixels, PixelOps.Crop(image, 0, 0, 4, 3).Pixels);
    }

    [Theory]
    [InlineData(3, 0, 2, 1)]
    [InlineData(0, 2, 1, 2)]
    [InlineData(-1, 0, 2, 2)]
    [InlineData(0, 0, 0, 2)]
    [InlineData(0, 0, 4, 4)]
    public void Crop_FuoriDallImmagineOVuoto_Errore(int x, int y, int width, int height)
    {
        StudioException error = Assert.Throws<StudioException>(() =>
        {
            _ = PixelOps.Crop(Image4x3(), x, y, width, height);
        });

        Assert.Contains("ritaglio", error.Message);
    }

    [Fact]
    public void Anteprima_CropPoiRotate_DimensioniEValoriAttesi()
    {
        RgbImage result = MontagePreview.Apply(
            Image4x3().Pixels,
            4,
            3,
            [new PixelCrop(1, 1, 2, 2), new PixelRotate(90)]);

        // Ritaglio 2×2 = [5 6 / 9 10]; ruotato di 90 in senso orario = [9 5 / 10 6].
        Assert.Equal(2, result.Width);
        Assert.Equal(2, result.Height);
        Assert.Equal(9, Id(result, 0, 0));
        Assert.Equal(5, Id(result, 1, 0));
        Assert.Equal(10, Id(result, 0, 1));
        Assert.Equal(6, Id(result, 1, 1));
    }

    [Fact]
    public void Anteprima_SpecchioDoppio_DueAssiNellOrdine()
    {
        RgbImage result = MontagePreview.Apply(Image4x3().Pixels, 4, 3, [new PixelMirror(true, true)]);

        Assert.Equal(4, result.Width);
        Assert.Equal(3, result.Height);
        Assert.Equal(0, Id(result, 3, 2)); // orizzontale poi verticale: alto-sinistra va in basso-destra
    }

    [Fact]
    public void Anteprima_NonModificaIlBufferDiPartenza()
    {
        byte[] source = Image4x3().Pixels;
        byte[] before = (byte[])source.Clone();

        MontagePreview.Apply(source, 4, 3, [new PixelRotate(180), new PixelCrop(0, 0, 2, 2)]);

        Assert.Equal(before, source);
    }

    [Fact]
    public void Anteprima_SenzaOperazioni_StessoContenuto()
    {
        RgbImage result = MontagePreview.Apply(Image4x3().Pixels, 4, 3, []);

        Assert.Equal(4, result.Width);
        Assert.Equal(Image4x3().Pixels, result.Pixels);
    }

    [Fact]
    public void Anteprima_Rotate90PoiRotate270_RiportaAllOriginale()
    {
        RgbImage result = MontagePreview.Apply(
            Image4x3().Pixels,
            4,
            3,
            [new PixelRotate(90), new PixelRotate(270)]);

        Assert.Equal(Image4x3().Pixels, result.Pixels);
    }

    [Fact]
    public void Anteprima_BufferDiLunghezzaSbagliata_Errore()
    {
        StudioException error = Assert.Throws<StudioException>(() =>
        {
            _ = MontagePreview.Apply(new byte[10], 4, 3, []);
        });

        Assert.Contains("non corrispondono al buffer", error.Message);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(4, 0)]
    [InlineData(-1, 3)]
    public void RgbImage_DimensioniNonPositive_Errore(int width, int height)
    {
        StudioException error = Assert.Throws<StudioException>(() =>
        {
            _ = RgbImage.Of(width, height, new byte[0]);
        });

        Assert.Contains("positive", error.Message);
    }

    [Fact]
    public void Anteprima_RotazioneNonAmmessa_Errore()
    {
        StudioException error = Assert.Throws<StudioException>(() =>
        {
            _ = MontagePreview.Apply(Image4x3().Pixels, 4, 3, [new PixelRotate(45)]);
        });

        Assert.Contains("rotazione non ammessa", error.Message);
    }

    [Fact]
    public void Anteprima_CropFuoriImmagine_Errore()
    {
        StudioException error = Assert.Throws<StudioException>(() =>
        {
            _ = MontagePreview.Apply(Image4x3().Pixels, 4, 3, [new PixelCrop(3, 0, 2, 1)]);
        });

        Assert.Contains("esce dall'immagine", error.Message);
    }
}
