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
    public void Shortcuts_OneToSix_MapToTheSixViewsInOrder()
    {
        Assert.Equal(6, ShellNavigation.ShortcutOrder.Count);

        ShellView[] expected =
        [
            ShellView.Live, ShellView.Camera, ShellView.Nights,
            ShellView.Publication, ShellView.Settings, ShellView.Log,
        ];
        for (int number = 1; number <= 6; number++)
        {
            Assert.True(ShellNavigation.TryGetShortcut(number, out ShellView view));
            Assert.Equal(expected[number - 1], view);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
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
