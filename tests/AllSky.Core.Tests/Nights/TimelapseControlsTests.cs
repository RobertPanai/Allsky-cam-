using AllSky.Core;
using AllSky.Core.Nights;
using Xunit;

namespace AllSky.Core.Tests.Nights;

public sealed class TimelapseControlsTests
{
    [Fact]
    public void Ready_CanPlay_AndCanRegenerate()
    {
        TimelapseControlState state = TimelapseControls.Resolve(NightTimelapseState.Ready, 300, null, DesktopTexts.Italian);

        Assert.True(state.CanPlay);
        Assert.True(state.CanRegenerate);
        Assert.False(state.IsBusy);
        Assert.Equal(DesktopTexts.Italian.NightsTimelapseReady, state.StatusText);
    }

    [Fact]
    public void Generating_BlocksRegenerateAndPlayback()
    {
        TimelapseControlState state = TimelapseControls.Resolve(NightTimelapseState.Generating, 300, 0.46, DesktopTexts.English);

        Assert.False(state.CanPlay);
        Assert.False(state.CanRegenerate);
        Assert.True(state.IsBusy);
        Assert.Equal("In progress: 46%", state.StatusText);
    }

    [Fact]
    public void Generating_WithoutProgress_SaysQueued()
    {
        TimelapseControlState queued = TimelapseControls.Resolve(NightTimelapseState.Generating, 10, null, DesktopTexts.Italian);
        TimelapseControlState zero = TimelapseControls.Resolve(NightTimelapseState.Generating, 10, 0, DesktopTexts.Italian);

        Assert.Equal(DesktopTexts.Italian.NightsTimelapseQueued, queued.StatusText);
        Assert.Equal(DesktopTexts.Italian.NightsTimelapseQueued, zero.StatusText);
    }

    [Fact]
    public void NightWithoutFrames_CannotRegenerate()
    {
        TimelapseControlState state = TimelapseControls.Resolve(NightTimelapseState.None, 0, null, DesktopTexts.Italian);

        Assert.False(state.CanRegenerate);
        Assert.False(state.CanPlay);
        Assert.Equal(DesktopTexts.Italian.NightsTimelapseNone, state.StatusText);
    }

    [Fact]
    public void Failed_AllowsRegenerate_NotPlayback()
    {
        TimelapseControlState state = TimelapseControls.Resolve(NightTimelapseState.Failed, 40, null, DesktopTexts.Italian);

        Assert.True(state.CanRegenerate);
        Assert.False(state.CanPlay);
        Assert.Equal(DesktopTexts.Italian.NightsTimelapseFailed, state.StatusText);
    }

    [Fact]
    public void Unknown_IsNotPlayableAndSaysUnknown()
    {
        TimelapseControlState state = TimelapseControls.Resolve(NightTimelapseState.Unknown, 40, null, DesktopTexts.Italian);

        Assert.False(state.CanPlay);
        Assert.Equal(DesktopTexts.Italian.NightsTimelapseUnknown, state.StatusText);
    }

    [Fact]
    public void FormatProgress_RoundsToWholePercent()
    {
        Assert.Equal("In lavorazione: 50%", TimelapseControls.FormatProgress(0.5, DesktopTexts.Italian));
        Assert.Equal("In lavorazione: 1%", TimelapseControls.FormatProgress(0.005, DesktopTexts.Italian));
        Assert.Equal("In progress: 99%", TimelapseControls.FormatProgress(0.989, DesktopTexts.English));
    }

    [Fact]
    public void FormatProgress_IsClampedToOneHundred()
    {
        Assert.Equal("In lavorazione: 100%", TimelapseControls.FormatProgress(1.7, DesktopTexts.Italian));
    }

    [Fact]
    public void FormatProgress_InvalidValues_SayQueued()
    {
        Assert.Equal(DesktopTexts.Italian.NightsTimelapseQueued, TimelapseControls.FormatProgress(double.NaN, DesktopTexts.Italian));
        Assert.Equal(DesktopTexts.Italian.NightsTimelapseQueued, TimelapseControls.FormatProgress(-0.2, DesktopTexts.Italian));
        Assert.Equal(DesktopTexts.Italian.NightsTimelapseQueued, TimelapseControls.FormatProgress(double.PositiveInfinity, DesktopTexts.Italian));
    }
}

public sealed class NightsViewStatesTests
{
    [Fact]
    public void BeforeFirstRead_IsLoading_WhateverElseIsTrue()
    {
        Assert.Equal(NightsPanelState.Loading, NightsViewStates.Resolve(false, 0, false));
        Assert.Equal(NightsPanelState.Loading, NightsViewStates.Resolve(false, 5, true));
    }

    [Fact]
    public void LoadedWithNights_IsReady_EvenAfterLaterError()
    {
        Assert.Equal(NightsPanelState.Ready, NightsViewStates.Resolve(true, 3, false));
        Assert.Equal(NightsPanelState.Ready, NightsViewStates.Resolve(true, 3, true));
    }

    [Fact]
    public void LoadedWithoutNights_IsEmptyOrError()
    {
        Assert.Equal(NightsPanelState.Empty, NightsViewStates.Resolve(true, 0, false));
        Assert.Equal(NightsPanelState.Error, NightsViewStates.Resolve(true, 0, true));
    }
}
