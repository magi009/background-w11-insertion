using Avalonia.Controls;

namespace FondoClaroLinux;

internal sealed class WallpaperHostWindow : Window
{
    private readonly NativeWebView _browser;
    public bool IsPaused { get; private set; }

    public WallpaperHostWindow(string path)
    {
        Title = "FondoClaro Linux";
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowState = WindowState.Maximized;
        _browser = new NativeWebView { Source = new Uri(path) };
        Content = _browser;
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

    public async Task SetPausedAsync(bool paused)
    {
        var css = "*{animation-play-state:paused!important;transition-duration:0s!important;scroll-behavior:auto!important}";
        var script = "(()=>{let s=document.getElementById('__fondoclaro_pause');if(s)s.remove();" +
            (paused ? "s=document.createElement('style');s.id='__fondoclaro_pause';s.textContent=" + System.Text.Json.JsonSerializer.Serialize(css) + ";document.head.appendChild(s);" : "") +
            "document.querySelectorAll('video,audio').forEach(m=>{try{" + (paused ? "m.pause()" : "m.play()") + "}catch(e){}});})()";
        await _browser.InvokeScript(script);
        IsPaused = paused;
    }
}
