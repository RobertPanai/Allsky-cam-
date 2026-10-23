using Xunit;
using static AllSky.Studio.Tests.MontageFixtures;

namespace AllSky.Studio.Tests;

/// <summary>
/// Keogram e startrails da serie sintetiche. I valori attesi sono scritti a mano: colonna di frame f,
/// riga j del keogram = pixel (colonna del centro, riga di partenza + j) del frame f.
/// </summary>
public sealed class SeriesProductBuilderTests
{
    // Cerchio di riferimento: centro (30, 34), raggio 20 → quadrato [10,50) × [14,54), keogram 2r = 40 righe.
    private static readonly CircleSpec Reference = new(30, 34, 20);

    private static SeriesProductBuilder Builder(SyntheticFrameDecoder decoder) => new(decoder);

    private static int[] Range(int count) => Enumerable.Range(0, count).ToArray();

    /// <summary>
    /// Verifica ogni pixel del keogram: colonna k = frame frameIndices[k], riga j = pixel (columnX, firstRow + j).
    /// </summary>
    private static void AssertKeogram(ProductResult result, IReadOnlyList<int> frameIndices, int columnX, int firstRow, int height)
    {
        Assert.True(result.IsSuccess, result.Error ?? "esito non riuscito");
        Assert.Equal(frameIndices.Count, result.Width);
        Assert.Equal(height, result.Height);
        Assert.Equal(result.Width * result.Height * 3, result.Pixels!.Length);

        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < frameIndices.Count; col++)
            {
                (byte r, byte g, byte b) = SyntheticFrameDecoder.DefaultPixel(frameIndices[col], columnX, firstRow + row);
                int p = (row * result.Width + col) * 3;
                Assert.True(
                    result.Pixels[p] == r && result.Pixels[p + 1] == g && result.Pixels[p + 2] == b,
                    $"pixel keogram riga {row}, colonna {col}: atteso ({r},{g},{b}), ricevuto "
                    + $"({result.Pixels[p]},{result.Pixels[p + 1]},{result.Pixels[p + 2]})");
            }
        }
    }

    private static void AssertNoPaths(ProductResult result)
    {
        foreach (string message in result.Warnings.Append(result.Error ?? string.Empty))
        {
            Assert.DoesNotContain("/", message);
            Assert.DoesNotContain("\\", message);
            Assert.DoesNotContain(".jpg", message);
            Assert.DoesNotContain("frame_", message);
        }
    }

    [Fact]
    public async Task Keogram_SerieSintetica_PixelPerPixelSu30x40()
    {
        var decoder = new SyntheticFrameDecoder();
        ProductResult result = await Builder(decoder).BuildKeogramAsync(
            Frames(30),
            new SeriesProductOptions { Circle = Reference });

        AssertKeogram(result, Range(30), columnX: 30, firstRow: 14, height: 40);
        Assert.Equal(30, result.FramesUsed);
        Assert.Equal(0, result.FramesSkipped);
        Assert.Empty(result.Warnings);
        Assert.False(result.Cancelled);
    }

    [Fact]
    public async Task Keogram_CerchioFuoriImmagine_RestituisceErroreSenzaBuffer()
    {
        var decoder = new SyntheticFrameDecoder();
        ProductResult result = await Builder(decoder).BuildKeogramAsync(
            Frames(30),
            new SeriesProductOptions { Circle = new CircleSpec(30, 34, 40) });

        Assert.NotNull(result.Error);
        Assert.Contains("esce dall'immagine", result.Error!);
        Assert.Null(result.Pixels);
        Assert.False(result.Cancelled);
        AssertNoPaths(result);
    }

    [Fact]
    public async Task Keogram_RaggioSottoIlMinimo_RestituisceErroreSenzaLeggereFrame()
    {
        var decoder = new SyntheticFrameDecoder();
        ProductResult result = await Builder(decoder).BuildKeogramAsync(
            Frames(30),
            new SeriesProductOptions { Circle = new CircleSpec(30, 34, 7) });

        Assert.NotNull(result.Error);
        Assert.Contains("almeno 8", result.Error!);
        Assert.Null(result.Pixels);
        Assert.Empty(decoder.DecodedIndices);
    }

    [Fact]
    public async Task Keogram_SenzaCerchioUtente_UsaIlModelloOttico()
    {
        // Modello (20, 20, 10): quadrato [10,30), keogram 20 righe, colonna x = 20.
        var decoder = new SyntheticFrameDecoder();
        ProductResult result = await Builder(decoder).BuildKeogramAsync(
            Frames(30),
            new SeriesProductOptions { FromOpticalModel = new CircleSpec(20, 20, 10) });

        AssertKeogram(result, Range(30), columnX: 20, firstRow: 10, height: 20);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Keogram_CerchioUtente_HaPrecedenzaSulModelloOttico()
    {
        var decoder = new SyntheticFrameDecoder();
        ProductResult result = await Builder(decoder).BuildKeogramAsync(
            Frames(30),
            new SeriesProductOptions { Circle = Reference, FromOpticalModel = new CircleSpec(20, 20, 10) });

        AssertKeogram(result, Range(30), columnX: 30, firstRow: 14, height: 40);
    }

    [Fact]
    public async Task Keogram_SenzaCerchioNeModello_UsaIlInscrittoConAvviso()
    {
        // Immagine 64×64: centro (32, 32), raggio 32, quadrato [0,64).
        var decoder = new SyntheticFrameDecoder();
        ProductResult result = await Builder(decoder).BuildKeogramAsync(Frames(30), new SeriesProductOptions());

        AssertKeogram(result, Range(30), columnX: 32, firstRow: 0, height: 64);
        Assert.Single(result.Warnings);
        Assert.Contains("inscritto", result.Warnings.Single());
        AssertNoPaths(result);
    }

    [Fact]
    public async Task Keogram_OperazioniSuiPixel_SoloSeAttive()
    {
        // Con lo specchio orizzontale il pixel x = 30 finisce in x = 33 (63 - 30).
        var circleAt33 = new CircleSpec(33, 34, 20);

        // Senza applyPixelOps il keogram sta sul frame originale: colonna 33 reale.
        var original = await Builder(new SyntheticFrameDecoder()).BuildKeogramAsync(
            Frames(30),
            new SeriesProductOptions { Circle = circleAt33 });
        AssertKeogram(original, Range(30), columnX: 33, firstRow: 14, height: 40);

        // Con applyPixelOps il frame è specchiato prima: la colonna 33 specchiata è la 30 originale.
        var mirrored = await Builder(new SyntheticFrameDecoder()).BuildKeogramAsync(
            Frames(30),
            new SeriesProductOptions
            {
                Circle = circleAt33,
                ApplyPixelOps = true,
                PixelOperations = [new PixelMirror(Horizontal: true, Vertical: false)],
            });
        AssertKeogram(mirrored, Range(30), columnX: 30, firstRow: 14, height: 40);
    }

    [Fact]
    public async Task Anteprima_MaxFrames10_Downscale2_DimensioniIndiciEValori()
    {
        // Immagine ridotta 32×32: cerchio (15, 17, 10), quadrato [5,25) × [7,27), keogram 20 righe.
        // Frame scelti: floor(k·30/10) = 0, 3, ..., 27.
        // Colonna x' = 15 (blocco di pixel x 30 e 31); riga y' = 7 + j (righe 2y' e 2y'+1).
        // R: uniforme nel blocco, vale 8·f con f = 3k.
        // G: (4·2y' + 4·2y' + 4·(2y'+1) + 4·(2y'+1) + 2) / 4 = 8y' + 2.
        // B: (4·30 + 4·30 + 4·31 + 4·31 + 2) / 4 = 122.
        var decoder = new SyntheticFrameDecoder();
        var options = new SeriesProductOptions
        {
            Circle = Reference,
            Preview = new PreviewOptions { MaxFrames = 10, DownscaleFactor = 2 },
        };

        ProductResult result = await Builder(decoder).BuildKeogramAsync(Frames(30), options);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(10, result.Width);
        Assert.Equal(20, result.Height);
        Assert.Equal(10, result.FramesUsed);
        Assert.Equal(new[] { 0, 3, 6, 9, 12, 15, 18, 21, 24, 27 }, decoder.DecodedIndices);

        for (int row = 0; row < result.Height; row++)
        {
            for (int col = 0; col < result.Width; col++)
            {
                int p = (row * result.Width + col) * 3;
                Assert.Equal((byte)(8 * 3 * col), result.Pixels![p]);
                Assert.Equal((byte)(8 * (7 + row) + 2), result.Pixels[p + 1]);
                Assert.Equal((byte)122, result.Pixels[p + 2]);
            }
        }
    }

    [Fact]
    public async Task Anteprima_OpzioniNonValide_RestituisconoErrore()
    {
        var decoder = new SyntheticFrameDecoder();
        ProductResult zeroFrames = await Builder(decoder).BuildKeogramAsync(
            Frames(30),
            new SeriesProductOptions { Circle = Reference, Preview = new PreviewOptions { MaxFrames = 0 } });
        ProductResult zeroFactor = await Builder(decoder).BuildKeogramAsync(
            Frames(30),
            new SeriesProductOptions { Circle = Reference, Preview = new PreviewOptions { DownscaleFactor = 0 } });

        Assert.NotNull(zeroFrames.Error);
        Assert.NotNull(zeroFactor.Error);
        Assert.Empty(decoder.DecodedIndices);
    }

    [Fact]
    public async Task Annullamento_AMeta_NessunBufferENessunaLetturaInPiu()
    {
        using var cts = new CancellationTokenSource();
        var decoder = new SyntheticFrameDecoder(onDecode: call =>
        {
            if (call == 15)
            {
                cts.Cancel();
            }
        });

        ProductResult result = await Builder(decoder).BuildKeogramAsync(
            Frames(30),
            new SeriesProductOptions { Circle = Reference },
            progress: null,
            cancellationToken: cts.Token);

        Assert.True(result.Cancelled);
        Assert.Null(result.Pixels);
        Assert.Null(result.Error);
        Assert.Equal(15, decoder.DecodedIndices.Count);
    }

    [Fact]
    public async Task Annullamento_GiaRichiestoPrimaDelPrimoFrame_NonLeggeNulla()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var decoder = new SyntheticFrameDecoder();

        ProductResult result = await Builder(decoder).BuildStartrailsAsync(
            Frames(30),
            new SeriesProductOptions(),
            progress: null,
            cancellationToken: cts.Token);

        Assert.True(result.Cancelled);
        Assert.Null(result.Pixels);
        Assert.Empty(decoder.DecodedIndices);
    }

    [Fact]
    public async Task FrameIllegibili_SaltatiEContatiSenzaPercorsi()
    {
        var unreadable = new HashSet<int> { 3, 4, 10 };
        var decoder = new SyntheticFrameDecoder(unreadable: index => unreadable.Contains(index));

        ProductResult result = await Builder(decoder).BuildKeogramAsync(
            Frames(30),
            new SeriesProductOptions { Circle = Reference });

        var readable = Range(30).Where(index => !unreadable.Contains(index)).ToArray();
        AssertKeogram(result, readable, columnX: 30, firstRow: 14, height: 40);
        Assert.Equal(27, result.FramesUsed);
        Assert.Equal(3, result.FramesSkipped);
        Assert.Single(result.Warnings);
        Assert.Contains("3 frame non leggibili", result.Warnings.Single());
        AssertNoPaths(result);
    }

    [Fact]
    public async Task Progresso_ArrivaADueUnoENonDecresce()
    {
        var unreadable = new HashSet<int> { 3, 4, 10 };
        var decoder = new SyntheticFrameDecoder(unreadable: index => unreadable.Contains(index));
        var progress = new ProgressRecorder();

        ProductResult result = await Builder(decoder).BuildKeogramAsync(
            Frames(30),
            new SeriesProductOptions { Circle = Reference },
            progress);

        Assert.True(result.IsSuccess);
        Assert.Equal(30, progress.Values.Count);
        Assert.Equal(1.0, progress.Values[^1]);
        for (int i = 1; i < progress.Values.Count; i++)
        {
            Assert.True(progress.Values[i] >= progress.Values[i - 1], $"il progresso è decresciuto alla posizione {i}");
        }

        Assert.All(progress.Values, value => Assert.InRange(value, 0.0, 1.0));
    }

    /// <summary>Tre frame 2×2, pixel in ordine (0,0), (1,0), (0,1), (1,1).</summary>
    private static readonly (byte R, byte G, byte B)[][] ThreeFrames =
    {
        new (byte, byte, byte)[] { (10, 20, 30), (200, 0, 0), (0, 0, 0), (5, 5, 5) },
        new (byte, byte, byte)[] { (50, 10, 0), (100, 255, 0), (0, 0, 40), (5, 6, 7) },
        new (byte, byte, byte)[] { (0, 0, 0), (150, 1, 2), (9, 9, 9), (4, 8, 200) },
    };

    [Fact]
    public async Task Startrails_DecayUno_MassimoPerPixelSuTreFrame()
    {
        // Massimo per canale a mano:
        // (0,0): R max(10,50,0)=50, G max(20,10,0)=20, B max(30,0,0)=30 → (50,20,30)
        // (1,0): R max(200,100,150)=200, G max(0,255,1)=255, B max(0,0,2)=2 → (200,255,2)
        // (0,1): R max(0,0,9)=9, G max(0,0,9)=9, B max(0,40,9)=40 → (9,9,40)
        // (1,1): R max(5,5,4)=5, G max(5,6,8)=8, B max(5,7,200)=200 → (5,8,200)
        var decoder = new SyntheticFrameDecoder(
            width: 2,
            height: 2,
            pixel: (f, x, y) => ThreeFrames[f][y * 2 + x]);

        ProductResult result = await Builder(decoder).BuildStartrailsAsync(Frames(3), new SeriesProductOptions { Decay = 1.0 });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Width);
        Assert.Equal(2, result.Height);
        Assert.Equal(3, result.FramesUsed);
        byte[] expected =
        {
            50, 20, 30,
            200, 255, 2,
            9, 9, 40,
            5, 8, 200,
        };
        Assert.Equal(expected, result.Pixels);
    }

    [Fact]
    public async Task Startrails_RistrettoAlCerchio_QuadratoDelCerchioConMassimoDelFrame()
    {
        // Il quadrato è [10,50) × [14,54). Il valore R massimo è 8·29 = 232 (frame 29); G e B non cambiano
        // tra i frame, quindi il massimo è il valore del frame: G = 4·(14 + j), B = 4·(10 + i).
        var decoder = new SyntheticFrameDecoder();

        ProductResult result = await Builder(decoder).BuildStartrailsAsync(
            Frames(30),
            new SeriesProductOptions { Circle = Reference, RestrictToCircle = true });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(40, result.Width);
        Assert.Equal(40, result.Height);
        Assert.Equal(30, result.FramesUsed);
        for (int j = 0; j < 40; j++)
        {
            for (int i = 0; i < 40; i++)
            {
                int p = (j * 40 + i) * 3;
                Assert.Equal((byte)232, result.Pixels![p]);
                Assert.Equal((byte)(4 * (14 + j)), result.Pixels[p + 1]);
                Assert.Equal((byte)(4 * (10 + i)), result.Pixels[p + 2]);
            }
        }
    }

    [Fact]
    public async Task SerieDalPianoDiMontaggio_EntraNelKeogram()
    {
        MontagePlanOutcome outcome = MontagePlanner.Plan(
            Frames(30),
            Array.Empty<StudioOperation>(),
            UniformStats());
        Assert.True(outcome.IsValid, string.Join(" ", outcome.Errors));

        ProductResult result = await Builder(new SyntheticFrameDecoder()).BuildKeogramAsync(
            outcome.Plan!.Frames,
            new SeriesProductOptions { Circle = Reference });

        AssertKeogram(result, Range(30), columnX: 30, firstRow: 14, height: 40);
    }
}
