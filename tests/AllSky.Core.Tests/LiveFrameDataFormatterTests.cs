using System.Globalization;
using AllSky.Core;
using AllSky.Core.Live;
using Xunit;

namespace AllSky.Core.Tests;

public sealed class LiveFrameDataFormatterTests
{
    private static FrameDataItem Find(IReadOnlyList<FrameDataItem> items, FrameDataField field) =>
        Assert.Single(items, item => item.Field == field);

    [Fact]
    public void EmptyData_AllRowsNotAvailable_InFixedOrder()
    {
        IReadOnlyList<FrameDataItem> items = LiveFrameDataFormatter.Format(LiveFrameData.Empty, DesktopTexts.Italian);

        Assert.Equal(
            [FrameDataField.Time, FrameDataField.Exposure, FrameDataField.Gain, FrameDataField.MedianAdu, FrameDataField.SensorTemp],
            items.Select(item => item.Field).ToArray());
        foreach (FrameDataItem item in items)
        {
            Assert.False(item.Available, item.Label);
            Assert.Equal("n/d", item.Value);
            Assert.Equal(DesktopTexts.Italian.NotAvailableTooltip, item.Tooltip);
        }
    }

    [Fact]
    public void MissingData_NeverShowsZero()
    {
        IReadOnlyList<FrameDataItem> items = LiveFrameDataFormatter.Format(LiveFrameData.Empty, DesktopTexts.English);

        Assert.All(items, item => Assert.DoesNotContain("0", item.Value));
    }

    [Fact]
    public void SensorTemperature_IsNotAvailable_WhenServiceDoesNotProvideIt()
    {
        var data = new LiveFrameData("20261021T123005Z", DateTimeOffset.UtcNow, 30000, 100, 1200, null);

        FrameDataItem temperature = Find(LiveFrameDataFormatter.Format(data, DesktopTexts.Italian), FrameDataField.SensorTemp);

        Assert.False(temperature.Available);
        Assert.Equal("n/d", temperature.Value);
    }

    [Fact]
    public void Exposure_BelowOneSecond_UsesMilliseconds()
    {
        CultureInfo culture = DesktopTexts.Italian.Culture;

        Assert.Equal("120 ms", LiveFrameDataFormatter.FormatExposure(120, culture));
        Assert.Equal("0,5 ms", LiveFrameDataFormatter.FormatExposure(0.5, culture));
    }

    [Fact]
    public void Exposure_AboveOneSecond_UsesSecondsWithOneDecimal()
    {
        Assert.Equal("30,0 s", LiveFrameDataFormatter.FormatExposure(30000, DesktopTexts.Italian.Culture));
        Assert.Equal("30.0 s", LiveFrameDataFormatter.FormatExposure(30000, DesktopTexts.English.Culture));
    }

    [Fact]
    public void Exposure_NegativeOrNonFinite_IsNull()
    {
        Assert.Null(LiveFrameDataFormatter.FormatExposure(-1, DesktopTexts.Italian.Culture));
        Assert.Null(LiveFrameDataFormatter.FormatExposure(double.NaN, DesktopTexts.Italian.Culture));
        Assert.Null(LiveFrameDataFormatter.FormatExposure(null, DesktopTexts.Italian.Culture));
    }

    [Fact]
    public void Gain_UsesAtMostTwoDecimals()
    {
        CultureInfo culture = DesktopTexts.Italian.Culture;

        Assert.Equal("100", LiveFrameDataFormatter.FormatGain(100, culture));
        Assert.Equal("12,5", LiveFrameDataFormatter.FormatGain(12.5, culture));
    }

    [Fact]
    public void Median_ShowsAduUnit()
    {
        Assert.Equal("1234 ADU", LiveFrameDataFormatter.FormatMedian(1234, DesktopTexts.English.Culture));
        Assert.Null(LiveFrameDataFormatter.FormatMedian(-5, DesktopTexts.English.Culture));
    }

    [Fact]
    public void Temperature_ShowsOneDecimalWithSign()
    {
        Assert.Equal("-10,5 °C", LiveFrameDataFormatter.FormatTemperature(-10.46, DesktopTexts.Italian.Culture));
    }

    [Theory]
    [InlineData(250.0)]
    [InlineData(double.NaN)]
    public void Temperature_OutOfRangeOrNonFinite_IsNull(double celsius)
    {
        Assert.Null(LiveFrameDataFormatter.FormatTemperature(celsius, DesktopTexts.Italian.Culture));
    }

    [Fact]
    public void Time_ShowsLocalTimeAndUtcIdInTooltip()
    {
        var timestamp = new DateTimeOffset(2026, 10, 21, 12, 30, 5, TimeSpan.Zero);
        var data = new LiveFrameData("20261021T123005Z", timestamp, null, null, null, null);

        FrameDataItem time = Find(LiveFrameDataFormatter.Format(data, DesktopTexts.English), FrameDataField.Time);

        Assert.True(time.Available);
        Assert.Equal(timestamp.ToLocalTime().ToString("HH:mm:ss", DesktopTexts.English.Culture), time.Value);
        Assert.Equal("UTC identifier: 20261021T123005Z", time.Tooltip);
    }

    [Fact]
    public void Frame_WithAllValues_AllRowsAvailable()
    {
        var data = new LiveFrameData(
            "20261021T123005Z",
            new DateTimeOffset(2026, 10, 21, 12, 30, 5, TimeSpan.Zero),
            15000,
            120,
            2048,
            -8.25);

        IReadOnlyList<FrameDataItem> items = LiveFrameDataFormatter.Format(data, DesktopTexts.Italian);

        Assert.All(items, item => Assert.True(item.Available, item.Label));
        Assert.Equal("15,0 s", Find(items, FrameDataField.Exposure).Value);
        Assert.Equal("120", Find(items, FrameDataField.Gain).Value);
        Assert.Equal("2048 ADU", Find(items, FrameDataField.MedianAdu).Value);
        Assert.Equal("-8,3 °C", Find(items, FrameDataField.SensorTemp).Value);
    }

    [Fact]
    public void Labels_ComeFromTexts()
    {
        IReadOnlyList<FrameDataItem> items = LiveFrameDataFormatter.Format(LiveFrameData.Empty, DesktopTexts.English);

        Assert.Equal(DesktopTexts.English.LiveDataExposure, Find(items, FrameDataField.Exposure).Label);
        Assert.Equal(DesktopTexts.English.LiveDataTemperature, Find(items, FrameDataField.SensorTemp).Label);
    }
}
