using Xunit;

namespace AllSky.Imaging.Tests;

public class DarkLibraryTests
{
    private static MasterDark Dark(double exposureMs, double gain, double? tempC, ushort value = 1)
    {
        return new MasterDark(1, 1, new ushort[] { value }, exposureMs, gain, tempC);
    }

    [Fact]
    public void EmptyLibrary_FindReturnsNull()
    {
        var library = new DarkLibrary();

        Assert.Null(library.Find(1000.0, 100.0));
        Assert.Equal(0, library.Count);
    }

    [Fact]
    public void Find_ExposureWithinTenPercent_IsAccepted()
    {
        var library = new DarkLibrary();
        MasterDark dark = Dark(1000.0, 100.0, null);
        library.Add(dark);

        Assert.Same(dark, library.Find(1090.0, 100.0));
        Assert.Same(dark, library.Find(910.0, 100.0));
    }

    [Fact]
    public void Find_ExposureBeyondTenPercent_ReturnsNull()
    {
        var library = new DarkLibrary();
        library.Add(Dark(1000.0, 100.0, null));

        // Tolleranza = 10% della richiesta: per 1120 e' 112, il dark (1000) dista 120.
        Assert.Null(library.Find(1120.0, 100.0));
        // Per 890 la tolleranza e' 89, il dark dista 110.
        Assert.Null(library.Find(890.0, 100.0));
    }

    [Fact]
    public void Find_DifferentGain_ReturnsNull()
    {
        var library = new DarkLibrary();
        library.Add(Dark(1000.0, 100.0, null));

        Assert.Null(library.Find(1000.0, 200.0));
    }

    [Fact]
    public void Find_PrefersClosestTemperatureBand()
    {
        var library = new DarkLibrary();
        MasterDark cold = Dark(1000.0, 100.0, 10.0);   // fascia 2
        MasterDark warm = Dark(1000.0, 100.0, 22.0);   // fascia 4
        library.Add(cold);
        library.Add(warm);

        Assert.Same(warm, library.Find(1000.0, 100.0, 21.0));
        Assert.Same(cold, library.Find(1000.0, 100.0, 11.0));
    }

    [Fact]
    public void Find_WithoutRequestedTemperature_PicksClosestExposure()
    {
        var library = new DarkLibrary();
        MasterDark near = Dark(1000.0, 100.0, 10.0);
        MasterDark far = Dark(1080.0, 100.0, 10.0);
        library.Add(far);
        library.Add(near);

        Assert.Same(near, library.Find(1010.0, 100.0));
    }

    [Fact]
    public void Find_DarkWithoutTemperature_IsFallbackWhenNoTemperatureCandidate()
    {
        var library = new DarkLibrary();
        MasterDark noTemp = Dark(1000.0, 100.0, null);
        library.Add(noTemp);

        Assert.Same(noTemp, library.Find(1000.0, 100.0, 10.0));
    }

    [Fact]
    public void Find_DarkWithTemperature_IsPreferredOverDarkWithoutTemperature()
    {
        var library = new DarkLibrary();
        MasterDark noTemp = Dark(1000.0, 100.0, null);
        MasterDark withTemp = Dark(1000.0, 100.0, 40.0);
        library.Add(noTemp);
        library.Add(withTemp);

        Assert.Same(withTemp, library.Find(1000.0, 100.0, 10.0));
    }

    [Fact]
    public void BandOf_UsesFiveDegreeBands()
    {
        Assert.Equal(0, DarkLibrary.BandOf(0.0));
        Assert.Equal(0, DarkLibrary.BandOf(4.99));
        Assert.Equal(1, DarkLibrary.BandOf(5.0));
        Assert.Equal(-1, DarkLibrary.BandOf(-0.1));
        Assert.Null(DarkLibrary.BandOf(null));
    }

    [Fact]
    public void KeyFor_CombinesExposureGainAndBand()
    {
        var key = DarkLibrary.KeyFor(Dark(300.0, 120.0, 12.3));

        Assert.Equal(new DarkLibrary.DarkKey(300.0, 120.0, 2), key);
        Assert.Equal(new DarkLibrary.DarkKey(300.0, 120.0, null), DarkLibrary.KeyFor(Dark(300.0, 120.0, null)));
    }

    [Fact]
    public void Add_SameKey_ReplacesPreviousMaster()
    {
        var library = new DarkLibrary();
        library.Add(Dark(300.0, 120.0, 11.0, value: 1));
        MasterDark replacement = Dark(300.0, 120.0, 12.0, value: 2);

        library.Add(replacement);

        Assert.Equal(1, library.Count);
        Assert.Same(replacement, library.Find(300.0, 120.0, 11.0));
    }

    [Fact]
    public void Add_DifferentTemperatureBand_CreatesNewKey()
    {
        var library = new DarkLibrary();
        library.Add(Dark(300.0, 120.0, 11.0));
        library.Add(Dark(300.0, 120.0, 16.0));

        Assert.Equal(2, library.Count);
    }

    [Fact]
    public void Add_InconsistentData_Throws()
    {
        var library = new DarkLibrary();
        var bad = new MasterDark(2, 2, new ushort[3], 300.0, 120.0, null);

        Assert.Throws<ArgumentException>(() => library.Add(bad));
    }
}
