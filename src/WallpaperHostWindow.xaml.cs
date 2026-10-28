using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FondoClaro;

public partial class WallpaperHostWindow : Window
{
    private readonly string _path;
    private IntPtr _desktopParent;

    public WallpaperHostWindow(string path)
    {
        _path = path;
        InitializeComponent();
        Loaded += OnLoaded;
        SourceInitialized += AttachBehindIcons;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await Browser.EnsureCoreWebView2Async();
            Browser.NavigationCompleted += async (_, _) => await SetPausedAsync(false);
            Browser.Source = new Uri(_path);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"No se pudo iniciar el fondo HTML. Verifica WebView2 Runtime.\n\n{ex.Message}", "FondoClaro", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    public async Task SetPausedAsync(bool paused)
    {
        if (Browser.CoreWebView2 is null) return;
        var css = paused ? "*{animation-play-state:paused!important;transition-duration:0s!important;scroll-behavior:auto!important}" : "";
        var script = "(()=>{let s=document.getElementById('__fondoclaro_pause');if(s)s.remove();" +
                     (paused ? "s=document.createElement('style');s.id='__fondoclaro_pause';s.textContent=" + System.Text.Json.JsonSerializer.Serialize(css) + ";document.head.appendChild(s);" : "") +
                     "document.querySelectorAll('video,audio').forEach(m=>{try{" + (paused ? "m.pause()" : "m.play()") + "}catch(e){}});" +
                     "})()";
        await Browser.CoreWebView2.ExecuteScriptAsync(script);
    }

    public void SendBehindApps()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        WindowState = WindowState.Normal;
        Topmost = false;
        if (_desktopParent == IntPtr.Zero) _desktopParent = FindDesktopParent();
        SetWindowLong(hwnd, -16, 0x52000000); // WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN
        SetParent(hwnd, _desktopParent);
        PositionAcrossDesktop(hwnd, IntPtr.Zero);
    }

    public void BringAboveApps()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        SetParent(hwnd, IntPtr.Zero);
        SetWindowLong(hwnd, -16, unchecked((int)0x90000000)); // WS_POPUP | WS_VISIBLE
        PositionAcrossDesktop(hwnd, new IntPtr(-1)); // HWND_TOPMOST
        Topmost = true;
    }

    private void AttachBehindIcons(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        WindowState = WindowState.Normal;
        _desktopParent = FindDesktopParent();
        SetWindowLong(hwnd, -16, 0x52000000); // WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN
        SetParent(hwnd, _desktopParent);
        PositionAcrossDesktop(hwnd, IntPtr.Zero);
    }

    private static IntPtr FindDesktopParent()
    {
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) throw new InvalidOperationException("Windows no encontró el escritorio.");
        SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);
        var worker = IntPtr.Zero;
        EnumWindows((top, _) =>
        {
            if (FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                worker = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
            return true;
        }, IntPtr.Zero);
        return worker == IntPtr.Zero ? progman : worker;
    }

    private static void PositionAcrossDesktop(IntPtr hwnd, IntPtr insertAfter)
    {
        var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        if (!SetWindowPos(hwnd, insertAfter, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x0040 | 0x0010 | 0x0020))
            throw new InvalidOperationException($"Windows no pudo cambiar la capa del fondo. Error {Marshal.GetLastWin32Error()}.");
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr extraData);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr FindWindow(string className, string? windowName);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? windowName);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong32(IntPtr hWnd, int index, int value);
    private static void SetWindowLong(IntPtr hWnd, int index, int value)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr64(hWnd, index, new IntPtr(value));
        else SetWindowLong32(hWnd, index, value);
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
}
