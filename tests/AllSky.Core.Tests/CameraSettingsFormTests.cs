using System.Globalization;
using AllSky.Core.CameraView;
using Xunit;

namespace AllSky.Core.Tests;

public sealed class CameraSettingsFormTests
{
    private static readonly CultureInfo Italian = new("it-IT");
    private static readonly CultureInfo English = new("en-US");

    [Fact]
    public void Check_ValidValues_ParsesBothFields()
    {
        CameraSettingsCheck check = CameraSettingsForm.Check("250", "30000", CameraExposureMode.Auto, null, Italian);

        Assert.True(check.IsValid);
        Assert.Equal(250, check.Gain);
        Assert.Equal(30000d, check.MaxExposureMs);
    }

    [Fact]
    public void Check_TrimsSpaces()
    {
        CameraSettingsCheck check = CameraSettingsForm.Check("  12 ", " 3 ", CameraExposureMode.Auto, null, English);

        Assert.True(check.IsValid);
        Assert.Equal(12, check.Gain);
        Assert.Equal(3d, check.MaxExposureMs);
    }

    [Fact]
    public void Check_DecimalComma_IsAcceptedInItalian()
    {
        CameraSettingsCheck check = CameraSettingsForm.Check("0", "1,5", CameraExposureMode.Auto, null, Italian);

        Assert.True(check.IsValid);
        Assert.Equal(1.5, check.MaxExposureMs);
    }

    [Fact]
    public void Check_DecimalPoint_IsAcceptedAsFallback()
    {
        CameraSettingsCheck check = CameraSettingsForm.Check("0", "1.5", CameraExposureMode.Auto, null, Italian);

        Assert.True(check.IsValid);
        Assert.Equal(1.5, check.MaxExposureMs);
    }

    [Fact]
    public void Check_EmptyFields_AreEmptyProblems()
    {
        CameraSettingsCheck check = CameraSettingsForm.Check("", null, CameraExposureMode.Auto, null, English);

        Assert.False(check.IsValid);
        Assert.Equal(CameraFieldProblem.Empty, check.GainProblem);
        Assert.Equal(CameraFieldProblem.Empty, check.MaxExposureProblem);
        Assert.Null(check.Gain);
        Assert.Null(check.MaxExposureMs);
    }

    [Fact]
    public void Check_NotANumber_IsNotNumberProblem()
    {
        CameraSettingsCheck check = CameraSettingsForm.Check("molto", "abc", CameraExposureMode.Auto, null, English);

        Assert.Equal(CameraFieldProblem.NotNumber, check.GainProblem);
        Assert.Equal(CameraFieldProblem.NotNumber, check.MaxExposureProblem);
    }

    [Fact]
    public void Check_GainWithDecimals_IsNotInteger()
    {
        CameraSettingsCheck check = CameraSettingsForm.Check("2,5", "10", CameraExposureMode.Auto, null, Italian);

        Assert.Equal(CameraFieldProblem.NotInteger, check.GainProblem);
        Assert.Equal(CameraFieldProblem.None, check.MaxExposureProblem);
        Assert.False(check.IsValid);
    }

    [Fact]
    public void Check_NegativeGain_IsNegative()
    {
        CameraSettingsCheck check = CameraSettingsForm.Check("-1", "10", CameraExposureMode.Auto, null, English);

        Assert.Equal(CameraFieldProblem.Negative, check.GainProblem);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void Check_ExposureNotPositiveOrFinite_IsNotPositive(string text)
    {
        CameraSettingsCheck check = CameraSettingsForm.Check("0", text, CameraExposureMode.Auto, null, English);

        Assert.Equal(CameraFieldProblem.NotPositive, check.MaxExposureProblem);
    }

    [Fact]
    public void Check_GainZero_IsValid()
    {
        CameraSettingsCheck check = CameraSettingsForm.Check("0", "0.5", CameraExposureMode.Auto, null, English);

        Assert.True(check.IsValid);
        Assert.Equal(0, check.Gain);
    }

    [Fact]
    public void HasChanges_SameValues_IsFalse()
    {
        CameraSettingsCheck check = CameraSettingsForm.Check("250", "30000", CameraExposureMode.Auto, null, English);

        Assert.False(CameraSettingsForm.HasChanges(check, new AppConfig.CameraConfig { Gain = 250, MaxExposureMs = 30000 }));
    }

    [Fact]
    public void HasChanges_DifferentGain_IsTrue()
    {
        CameraSettingsCheck check = CameraSettingsForm.Check("200", "30000", CameraExposureMode.Auto, null, English);

        Assert.True(CameraSettingsForm.HasChanges(check, new AppConfig.CameraConfig { Gain = 250, MaxExposureMs = 30000 }));
    }

    [Fact]
    public void HasChanges_DifferentExposure_IsTrue()
    {
        CameraSettingsCheck check = CameraSettingsForm.Check("250", "1500", CameraExposureMode.Auto, null, English);

        Assert.True(CameraSettingsForm.HasChanges(check, new AppConfig.CameraConfig { Gain = 250, MaxExposureMs = 30000 }));
    }

    [Fact]
    public void HasChanges_InvalidForm_IsFalse()
    {
        CameraSettingsCheck check = CameraSettingsForm.Check("", "30000", CameraExposureMode.Auto, null, English);

        Assert.False(CameraSettingsForm.HasChanges(check, new AppConfig.CameraConfig { Gain = 250, MaxExposureMs = 30000 }));
    }

    [Fact]
    public void FormatExposure_RoundTripsThroughCheck()
    {
        const double exposure = 2.0005;
        string text = CameraSettingsForm.FormatExposure(exposure, Italian);

        CameraSettingsCheck check = CameraSettingsForm.Check("250", text, CameraExposureMode.Auto, null, Italian);

        Assert.Equal(exposure, check.MaxExposureMs);
        Assert.False(CameraSettingsForm.HasChanges(check, new AppConfig.CameraConfig { Gain = 250, MaxExposureMs = exposure }));
    }

    [Fact]
    public void FormatExposure_UsesCultureDecimalSeparator()
    {
        Assert.Equal("1,5", CameraSettingsForm.FormatExposure(1.5, Italian));
        Assert.Equal("1.5", CameraSettingsForm.FormatExposure(1.5, English));
    }

    [Fact]
    public void FormatGain_IsPlainInteger()
    {
        Assert.Equal("250", CameraSettingsForm.FormatGain(250));
    }

    [Fact]
    public void FieldMessages_NoneHasNoText_OtherProblemsHaveText()
    {
        Assert.Null(CameraFieldMessages.For(CameraFieldProblem.None, DesktopTexts.Italian));

        foreach (CameraFieldProblem problem in Enum.GetValues<CameraFieldProblem>())
        {
            if (problem == CameraFieldProblem.None)
            {
                continue;
            }

            Assert.False(string.IsNullOrWhiteSpace(CameraFieldMessages.For(problem, DesktopTexts.Italian)));
            Assert.NotNull(CameraFieldMessages.For(problem, DesktopTexts.English));
        }
    }

    [Fact]
    public void Check_ManualMode_ValidExposure_IsValid()
    {
        CameraSettingsCheck check = CameraSettingsForm.Check("250", "30000", CameraExposureMode.Manual, "5000", English);

        Assert.True(check.IsValid);
        Assert.Equal(CameraExposureMode.Manual, check.Mode);
        Assert.Equal(5000, check.ManualExposureMs);
    }

    [Fact]
    public void Check_ManualMode_AboveMaximum_IsProblem()
    {
        CameraSettingsCheck check = CameraSettingsForm.Check("250", "30000", CameraExposureMode.Manual, "40000", English);

        Assert.False(check.IsValid);
        Assert.Equal(CameraFieldProblem.AboveMaximum, check.ManualExposureProblem);
    }

    [Fact]
    public void Check_ManualMode_EmptyExposure_IsEmptyProblem()
    {
        CameraSettingsCheck check = CameraSettingsForm.Check("250", "30000", CameraExposureMode.Manual, "", English);

        Assert.False(check.IsValid);
        Assert.Equal(CameraFieldProblem.Empty, check.ManualExposureProblem);
    }

    [Fact]
    public void Check_AutoMode_IgnoresManualField()
    {
        CameraSettingsCheck check = CameraSettingsForm.Check("250", "30000", CameraExposureMode.Auto, "abc", English);

        Assert.True(check.IsValid);
        Assert.Null(check.ManualExposureMs);
        Assert.Equal(CameraFieldProblem.None, check.ManualExposureProblem);
    }

    [Fact]
    public void HasChanges_ModeChangedToManual_IsTrue()
    {
        CameraSettingsCheck check = CameraSettingsForm.Check("250", "30000", CameraExposureMode.Manual, "5000", English);

        Assert.True(CameraSettingsForm.HasChanges(check, new AppConfig.CameraConfig { Gain = 250, MaxExposureMs = 30000, ManualExposureMs = 5000 }));
    }

    [Fact]
    public void HasChanges_ManualSameValue_IsFalse()
    {
        CameraSettingsCheck check = CameraSettingsForm.Check("250", "30000", CameraExposureMode.Manual, "5000", English);

        Assert.False(CameraSettingsForm.HasChanges(check, new AppConfig.CameraConfig
        {
            Gain = 250,
            MaxExposureMs = 30000,
            ExposureMode = CameraExposureMode.Manual,
            ManualExposureMs = 5000,
        }));
    }

    [Fact]
    public void HasChanges_ManualDifferentValue_IsTrue()
    {
        CameraSettingsCheck check = CameraSettingsForm.Check("250", "30000", CameraExposureMode.Manual, "8000", English);

        Assert.True(CameraSettingsForm.HasChanges(check, new AppConfig.CameraConfig
        {
            Gain = 250,
            MaxExposureMs = 30000,
            ExposureMode = CameraExposureMode.Manual,
            ManualExposureMs = 5000,
        }));
    }

    [Fact]
    public void HasChanges_AutoIgnoresStoredManualValue()
    {
        CameraSettingsCheck check = CameraSettingsForm.Check("250", "30000", CameraExposureMode.Auto, null, English);

        Assert.False(CameraSettingsForm.HasChanges(check, new AppConfig.CameraConfig
        {
            Gain = 250,
            MaxExposureMs = 30000,
            ExposureMode = CameraExposureMode.Auto,
            ManualExposureMs = 999,
        }));
    }
}
