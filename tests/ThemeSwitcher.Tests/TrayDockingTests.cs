using Xunit;

namespace ThemeSwitcher.Tests;

/// <summary>
/// 贴靠位置纯计算测试：以 1920x1080 屏幕、96px 底部任务栏（工作区 1920x984）、
/// 350x260 窗口、8px 边距为基准，覆盖任务栏四边停靠与无任务栏情形。
/// </summary>
public sealed class TrayDockingTests
{
    private static readonly Rectangle Bounds = new(0, 0, 1920, 1080);
    private static readonly Size WindowSize = new(350, 260);
    private const int Margin = 8;

    [Fact]
    public void BottomTaskbar_DocksAtWorkingAreaBottomRight()
    {
        var workingArea = new Rectangle(0, 0, 1920, 984);

        Point location = TrayDocking.GetDockedLocation(Bounds, workingArea, WindowSize, Margin);

        Assert.Equal(new Point(1920 - 350 - 8, 984 - 260 - 8), location);
    }

    [Fact]
    public void TopTaskbar_DocksAtWorkingAreaTopRight()
    {
        var workingArea = new Rectangle(0, 96, 1920, 984);

        Point location = TrayDocking.GetDockedLocation(Bounds, workingArea, WindowSize, Margin);

        Assert.Equal(new Point(1920 - 350 - 8, 96 + 8), location);
    }

    [Fact]
    public void LeftTaskbar_DocksAtWorkingAreaBottomLeft()
    {
        var workingArea = new Rectangle(96, 0, 1824, 1080);

        Point location = TrayDocking.GetDockedLocation(Bounds, workingArea, WindowSize, Margin);

        Assert.Equal(new Point(96 + 8, 1080 - 260 - 8), location);
    }

    [Fact]
    public void RightTaskbar_DocksAtWorkingAreaBottomRight()
    {
        var workingArea = new Rectangle(0, 0, 1824, 1080);

        Point location = TrayDocking.GetDockedLocation(Bounds, workingArea, WindowSize, Margin);

        Assert.Equal(new Point(1824 - 350 - 8, 1080 - 260 - 8), location);
    }

    [Fact]
    public void NoTaskbar_WorkingAreaEqualsBounds_StillBottomRight()
    {
        Point location = TrayDocking.GetDockedLocation(Bounds, Bounds, WindowSize, Margin);

        Assert.Equal(new Point(1920 - 350 - 8, 1080 - 260 - 8), location);
    }

    [Fact]
    public void NonZeroOriginScreen_CoordinatesFollowWorkingArea()
    {
        // 副屏坐标系（如主屏左侧的负坐标屏或右侧正坐标屏）：位置应跟随该屏工作区原点
        var bounds = new Rectangle(1920, 0, 1920, 1080);
        var workingArea = new Rectangle(1920, 0, 1920, 984);

        Point location = TrayDocking.GetDockedLocation(bounds, workingArea, WindowSize, Margin);

        Assert.Equal(new Point(1920 + 1920 - 350 - 8, 984 - 260 - 8), location);
    }
}
