using Avalonia.Controls;

namespace FondoClaroLinux;

internal sealed class WallpaperHostWindow : Window
{
    public WallpaperHostWindow(string path)
    {
        Title = "FondoClaro Linux";
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowState = WindowState.Maximized;
        Content = new NativeWebView { Source = new Uri(path) };
    }

    public void SendBehind()
    {
        Topmost = false;
        WindowState = WindowState.Maximized;
        X11DesktopLayer.SetDesktop(this, desktop: true);
    }

    public void BringFullscreen()
    {
        X11DesktopLayer.SetDesktop(this, desktop: false);
        Topmost = true;
        WindowState = WindowState.FullScreen;
        Activate();
    }
}
