using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FondoClaro;

public partial class WallpaperHostWindow : Window
{
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsClipSiblings = 0x04000000;
    private const int WsClipChildren = 0x02000000;
    private const int WsPopup = unchecked((int)0x80000000);
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;

    private readonly string _path;
    private IntPtr _desktopParent;

    public event Action? TaskbarShown;

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
            var environment = await WebViewEnvironmentProvider.GetAsync();
            await Browser.EnsureCoreWebView2Async(environment);
            Browser.CoreWebView2.WebMessageReceived += (_, args) =>
            {
                if (args.TryGetWebMessageAsString() == "fc:taskbar")
                {
                    SendBehindApps();
                    TaskbarShown?.Invoke();
                }
            };
            await Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("""
                (() => {
                  const send = value => { try { window.chrome.webview.postMessage(value); } catch (_) {} };
                  const install = () => {
                    if (!document.documentElement || document.getElementById('__fondoclaro_taskbar')) return;
                    const button = document.createElement('button');
                    button.id = '__fondoclaro_taskbar';
                    button.type = 'button';
                    button.textContent = 'Mostrar barra de tareas';
                    button.style.cssText = 'display:none;position:fixed;z-index:2147483647;left:50%;bottom:24px;transform:translateX(-50%);padding:12px 20px;border:1px solid #33c7a5;border-radius:10px;background:rgba(17,24,39,.94);color:#fff;font:600 14px Segoe UI,Arial,sans-serif;cursor:pointer;box-shadow:0 8px 28px rgba(0,0,0,.45)';
                    let hideTimer;
                    const show = () => {
                      button.style.display = 'block';
                      clearTimeout(hideTimer);
                      hideTimer = setTimeout(() => { button.style.display = 'none'; }, 3000);
                    };
                    document.addEventListener('click', event => {
                      if (event.target === button || button.contains(event.target)) return;
                      show();
                    }, true);
                    document.addEventListener('pointermove', () => {
                      if (button.style.display === 'block') show();
                    }, {capture:true,passive:true});
                    button.addEventListener('click', event => {
                      event.preventDefault();
                      event.stopPropagation();
                      button.style.display = 'none';
                      clearTimeout(hideTimer);
                      send('fc:taskbar');
                    });
                    document.documentElement.appendChild(button);
                  };
                  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', install, {once:true});
                  else install();
                })();
                """);
            Browser.NavigationCompleted += async (_, _) => await SetPausedAsync(false);
            Browser.Source = new Uri(_path);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"WebView2 no pudo iniciar el fondo HTML. Verifica que Microsoft Edge WebView2 Runtime esté instalado.\n\n{ex.Message}\nCódigo: 0x{ex.HResult:X8}",
                "FondoClaro",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
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
        _desktopParent = FindDesktopParent();
        SetParent(hwnd, _desktopParent);
        SetWindowLong(hwnd, -16, WsChild | WsVisible | WsClipSiblings | WsClipChildren);
        PositionAcrossDesktop(hwnd, _desktopParent, IntPtr.Zero, activate: false);
    }

    public void BringAboveApps()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        WindowState = WindowState.Normal;
        SetParent(hwnd, IntPtr.Zero);
        SetWindowLong(hwnd, -16, WsPopup | WsVisible);
        Topmost = true;
        PositionAcrossDesktop(hwnd, IntPtr.Zero, new IntPtr(-1), activate: true);
        SetForegroundWindow(hwnd);
    }

    private void AttachBehindIcons(object? sender, EventArgs e) => SendBehindApps();

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

    private static void PositionAcrossDesktop(IntPtr hwnd, IntPtr parent, IntPtr insertAfter, bool activate)
    {
        var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        var x = bounds.Left;
        var y = bounds.Top;
        if (parent != IntPtr.Zero)
        {
            var parentOrigin = new NativePoint(0, 0);
            if (ClientToScreen(parent, ref parentOrigin))
            {
                x -= parentOrigin.X;
                y -= parentOrigin.Y;
            }
        }

        var flags = SwpFrameChanged | SwpShowWindow;
        if (!activate) flags |= SwpNoActivate;
        if (!SetWindowPos(hwnd, insertAfter, x, y, bounds.Width, bounds.Height, flags))
            throw new InvalidOperationException($"Windows no pudo cambiar la capa del fondo. Error {Marshal.GetLastWin32Error()}.");
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr extraData);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr FindWindow(string className, string? windowName);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? windowName);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong32(IntPtr hWnd, int index, int value);
    private static void SetWindowLong(IntPtr hWnd, int index, int value)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr64(hWnd, index, new IntPtr(value));
        else SetWindowLong32(hWnd, index, value);
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
}
