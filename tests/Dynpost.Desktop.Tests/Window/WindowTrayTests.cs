using Dynpost.Application.Settings;
using Dynpost.Desktop.Window;
using NSubstitute;

namespace Dynpost.Desktop.Tests.Window;

/// <summary>When closing the window hides it to the tray instead of ending the app.</summary>
public sealed class WindowTrayTests
{
    private readonly TrayIcon _icon = Substitute.For<TrayIcon>();

    public WindowTrayTests() => _icon.IsSupported.Returns(true);

    [Fact]
    public void ClosingEndsTheApp_UntilTheUserChoosesToCloseToTheTray()
    {
        var tray = new WindowTray(_icon);
        Assert.False(tray.ClosesToTray);

        tray.Apply(new TraySettings(MinimizeToTray: true));
        Assert.False(tray.ClosesToTray);

        tray.Apply(new TraySettings(CloseToTray: true));
        Assert.True(tray.ClosesToTray);
    }

    [Fact]
    public void Quitting_ClosesForGood()
    {
        var tray = new WindowTray(_icon);
        tray.Apply(new TraySettings(CloseToTray: true));

        tray.Quitting();

        Assert.False(tray.ClosesToTray);
    }

    [Fact]
    public void WithoutATray_ClosingAlwaysEndsTheApp()
    {
        _icon.IsSupported.Returns(false);
        var tray = new WindowTray(_icon);
        tray.Apply(new TraySettings(CloseToTray: true));

        Assert.False(tray.IsSupported);
        Assert.False(tray.ClosesToTray);
        Assert.False(new WindowTray(icon: null).IsSupported);
    }
}
