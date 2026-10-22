using AllSky.Core;
using AllSky.Core.Shell;
using Xunit;

namespace AllSky.Core.Tests;

public sealed class ShellNavigationTests
{
    [Fact]
    public void Sidebar_ListsEveryViewOnce_StartingWithTonight()
    {
        IReadOnlyList<ShellView> sidebar = ShellNavigation.SidebarOrder;

        Assert.Equal(ShellView.Tonight, sidebar[0]);
        Assert.Equal(ShellView.Log, sidebar[^1]);
        Assert.Equal(sidebar.Count, sidebar.Distinct().Count());
        Assert.Equal(Enum.GetValues<ShellView>().Length, sidebar.Count);
    }

    [Fact]
    public void Shortcuts_OneToSeven_MapToTheSevenViewsInOrder()
    {
        Assert.Equal(7, ShellNavigation.ShortcutOrder.Count);

        // Ctrl+1..6 restano come prima; Eventi (Ctrl+7) è in fondo, così i numeri già noti non cambiano.
        ShellView[] expected =
        [
            ShellView.Live, ShellView.Camera, ShellView.Nights,
            ShellView.Publication, ShellView.Settings, ShellView.Log,
            ShellView.Events,
        ];
        for (int number = 1; number <= 7; number++)
        {
            Assert.True(ShellNavigation.TryGetShortcut(number, out ShellView view));
            Assert.Equal(expected[number - 1], view);
        }
    }

    [Fact]
    public void Events_SitsAfterNightsInTheSidebar()
    {
        IReadOnlyList<ShellView> sidebar = ShellNavigation.SidebarOrder;
        int nights = sidebar.ToList().IndexOf(ShellView.Nights);

        Assert.Equal(ShellView.Events, sidebar[nights + 1]);
        Assert.Equal(ShellView.Publication, sidebar[nights + 2]);
    }

    [Fact]
    public void Events_HasSidebarNameAndEmptyTextInBothLanguages()
    {
        Assert.Equal("Eventi", DesktopTexts.Italian.ViewName(ShellView.Events));
        Assert.Equal("Events", DesktopTexts.English.ViewName(ShellView.Events));
        Assert.False(string.IsNullOrWhiteSpace(DesktopTexts.Italian.EmptyText(ShellView.Events)));
        Assert.False(string.IsNullOrWhiteSpace(DesktopTexts.English.EmptyText(ShellView.Events)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(-1)]
    public void Shortcuts_OutOfRange_HaveNoView(int number)
    {
        Assert.False(ShellNavigation.TryGetShortcut(number, out ShellView view));
        Assert.Equal(ShellNavigation.Default, view);
    }

    [Fact]
    public void ShortcutNumber_IsInverseOfTryGetShortcut_AndZeroForTonight()
    {
        Assert.Equal(0, ShellNavigation.ShortcutNumber(ShellView.Tonight));

        foreach (ShellView view in ShellNavigation.ShortcutOrder)
        {
            int number = ShellNavigation.ShortcutNumber(view);
            Assert.True(ShellNavigation.TryGetShortcut(number, out ShellView back));
            Assert.Equal(view, back);
        }
    }

    [Fact]
    public void Move_ForwardAndBack_StepsThroughTheSidebar()
    {
        Assert.Equal(ShellView.Live, ShellNavigation.Move(ShellView.Tonight, 1));
        Assert.Equal(ShellView.Camera, ShellNavigation.Move(ShellView.Live, 1));
        Assert.Equal(ShellView.Live, ShellNavigation.Move(ShellView.Camera, -1));
    }

    [Fact]
    public void Move_WrapsAroundAtBothEnds()
    {
        Assert.Equal(ShellView.Tonight, ShellNavigation.Move(ShellView.Log, 1));
        Assert.Equal(ShellView.Log, ShellNavigation.Move(ShellView.Tonight, -1));
    }

    [Fact]
    public void Move_ByLargeStep_StillStaysInRange()
    {
        int count = ShellNavigation.SidebarOrder.Count;

        Assert.Equal(ShellView.Live, ShellNavigation.Move(ShellView.Tonight, count + 1));
        Assert.Equal(ShellView.Log, ShellNavigation.Move(ShellView.Tonight, -count - 1));
    }
}
