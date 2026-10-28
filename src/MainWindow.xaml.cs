using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;

namespace FondoClaro;

public partial class MainWindow : Window
{
    private const int WallpaperHotkeyId = 0x4FC1;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkF11 = 0x7A;
    private string? _imagePath;
    private string? _dynamicPath;
    private string? _originalWallpaperPath;
    private readonly List<(string MonitorId, string Path)> _originalMonitorWallpapers = new();
    private bool _capturedOriginalWallpaper;
    private WallpaperHostWindow? _wallpaperHost;
    private DispatcherTimer? _transitionTimer;
    private readonly List<string> _transitionFiles = new();
    private int _transitionIndex = -1;
    private TimeSpan _transitionInterval;
    private TimeSpan _transitionRemaining;
    private bool _transitionPaused;
    private bool _fill;
    private bool _hotkeyRegistered;

    public MainWindow() => InitializeComponent();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(MainWindowProc);
        _hotkeyRegistered = RegisterHotKey(hwnd, WallpaperHotkeyId, ModControl | ModAlt | ModNoRepeat, VkF11);
        HotkeyText.Text = _hotkeyRegistered
            ? "Atajo global: Ctrl + Alt + F11 · traer al frente"
            : "Ctrl + Alt + F11 está ocupado; usa el botón de arriba.";
    }

    private IntPtr MainWindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == WallpaperHotkeyId)
        {
            if (_wallpaperHost is not null)
            {
                _wallpaperHost.BringAboveApps();
                StatusText.Text = "Fondo traído al frente. Haz clic en la imagen y pulsa «Mostrar barra de tareas» para volver.";
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    private async void ChooseImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Elige un archivo que pueda mostrar una imagen",
            Filter = "Imágenes y medios|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.avif;*.heic;*.heif;*.ico;*.svg;*.pdf;*.html;*.htm;*.mp4;*.webm;*.mov;*.m4v|Todos los archivos|*.*"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            FileName.Text = Path.GetFileName(dialog.FileName);
            BitmapImage? source = null;
            try { source = LoadImage(dialog.FileName); } catch { }
            if (source is not null)
            {
                _imagePath = dialog.FileName; _dynamicPath = null;
                PreviewWeb.Visibility = Visibility.Collapsed;
                PreviewBackground.Visibility = Visibility.Visible;
                PreviewImage.Visibility = Visibility.Visible;
                PreviewBackground.Source = source; PreviewImage.Source = source; ApplyPreviewMode();
                SetDynamicButtons(false);
                PreviewLabel.Text = $"VISTA PREVIA · {source.PixelWidth} × {source.PixelHeight}";
                StatusText.Text = "Imagen cargada. Se mantendrá su proporción al aplicarla.";
            }
            else
            {
                _dynamicPath = dialog.FileName; _imagePath = null;
                PreviewImage.Visibility = Visibility.Collapsed; PreviewBackground.Visibility = Visibility.Collapsed;
                PreviewWeb.Visibility = Visibility.Visible;
                var environment = await WebViewEnvironmentProvider.GetAsync();
                await PreviewWeb.EnsureCoreWebView2Async(environment);
                PreviewWeb.Source = new Uri(dialog.FileName);
                SetDynamicButtons(false);
                PreviewLabel.Text = $"VISTA PREVIA · {Path.GetExtension(dialog.FileName).TrimStart('.').ToUpperInvariant()}";
                StatusText.Text = "Archivo abierto con WebView2. Si Windows o Edge pueden mostrarlo, podrás usarlo como fondo.";
            }
        }
        catch (Exception ex)
        {
            _imagePath = null; _dynamicPath = null; SetDynamicButtons(false);
            System.Windows.MessageBox.Show($"No pude abrir este archivo como imagen ni con WebView2.\n\n{ex.Message}", "FondoClaro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ChooseTransition_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Elige imágenes para la transición",
            Multiselect = true,
            Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.avif;*.heic;*.heif;*.ico|Todos los archivos|*.*"
        };
        if (dialog.ShowDialog() != true) return;
        var supported = new List<string>();
        foreach (var path in dialog.FileNames)
        {
            try { _ = LoadImage(path); supported.Add(path); } catch { }
        }
        _transitionFiles.Clear(); _transitionFiles.AddRange(supported);
        TransitionFilesLabel.Text = _transitionFiles.Count == 0
            ? "No se encontraron imágenes que Windows pueda abrir."
            : $"{_transitionFiles.Count} imágenes listas para rotar.";
        StartTransitionButton.IsEnabled = _transitionFiles.Count >= 2;
        RemoveTransitionButton.IsEnabled = _transitionFiles.Count > 0;
        if (_transitionFiles.Count < 2) StatusText.Text = "Elige al menos dos imágenes para iniciar la transición.";
    }

    private void FitMode_Checked(object sender, RoutedEventArgs e) { _fill = false; if (PreviewImage is not null) ApplyPreviewMode(); }
    private void FillMode_Checked(object sender, RoutedEventArgs e) { _fill = true; if (PreviewImage is not null) ApplyPreviewMode(); }

    private void ApplyPreviewMode()
    {
        PreviewImage.Stretch = _fill ? Stretch.UniformToFill : Stretch.Uniform;
        PreviewImage.Margin = _fill ? new Thickness(0) : new Thickness(28);
        PreviewBackground.Effect = _fill ? null : new BlurEffect { Radius = 25, KernelType = KernelType.Gaussian };
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_dynamicPath is not null)
            {
                _wallpaperHost?.Close();
                _wallpaperHost = new WallpaperHostWindow(_dynamicPath, htmlContent: true);
                _wallpaperHost.TaskbarShown += () => StatusText.Text = "Barra de tareas visible; fondo detrás de las ventanas.";
                _wallpaperHost.Show();
                SetDynamicButtons(true, canPause: true);
                RemoveBackgroundButton.IsEnabled = true;
                StatusText.Text = "Fondo activo detrás de las apps. Usa los botones para cambiar su capa.";
                return;
            }
            if (_imagePath is null) { StatusText.Text = "Elige primero un archivo de fondo."; return; }
            ApplyStaticImage(_imagePath);
        }
        catch (Exception ex) { System.Windows.MessageBox.Show($"No se pudo aplicar el fondo.\n\n{ex.Message}", "FondoClaro", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ApplyStaticImage(string path)
    {
        RememberOriginalWallpaper();
        _dynamicPath = null; _imagePath = path;
        var source = LoadImage(path);
        var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        var width = bounds.Width; var height = bounds.Height;
        var wallpaper = RenderWallpaper(source, width, height, _fill);
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FondoClaro");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, $"fondo-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.jpg");
        using (var stream = File.Create(target))
        {
            var encoder = new JpegBitmapEncoder { QualityLevel = 94 };
            encoder.Frames.Add(BitmapFrame.Create(wallpaper)); encoder.Save(stream);
        }
        ApplyWindowsWallpaper(target);
        if (_wallpaperHost is null || !_wallpaperHost.IsVisible)
        {
            _wallpaperHost?.Close();
            _wallpaperHost = new WallpaperHostWindow(target, htmlContent: false);
            _wallpaperHost.TaskbarShown += () => StatusText.Text = "Barra de tareas visible; fondo detrás de las ventanas.";
            _wallpaperHost.Show();
        }
        else
        {
            _wallpaperHost.ShowImage(target);
        }
        SetDynamicButtons(true, canPause: false);
        RemoveBackgroundButton.IsEnabled = true;
        StatusText.Text = $"Fondo ajustado a {width} × {height} con suavizado y nitidez automática.";
    }

    private void StartTransition_Click(object sender, RoutedEventArgs e)
    {
        if (_transitionFiles.Count < 2) { StatusText.Text = "Elige al menos dos imágenes para iniciar la transición."; return; }
        if (TimingModeCombo.SelectedIndex == 1) _transitionInterval = TimeSpan.FromMinutes(5);
        else
        {
            if (!int.TryParse(IntervalValueBox.Text, out var amount) || amount < 1)
            { StatusText.Text = "Escribe un intervalo válido de 1 o más."; return; }
            _transitionInterval = IntervalUnitCombo.SelectedIndex switch
            {
                0 => TimeSpan.FromSeconds(amount),
                1 => TimeSpan.FromMinutes(amount),
                2 => TimeSpan.FromHours(amount),
                3 => TimeSpan.FromDays(amount),
                _ => TimeSpan.FromSeconds(amount)
            };
        }
        _transitionIndex = -1; _transitionPaused = false; _transitionRemaining = _transitionInterval;
        try { AdvanceTransitionImage(); }
        catch (Exception ex) { StatusText.Text = $"No se pudo iniciar: {ex.Message}"; return; }
        _transitionTimer ??= new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _transitionTimer.Tick -= TransitionTimer_Tick;
        _transitionTimer.Tick += TransitionTimer_Tick;
        _transitionTimer.Start();
        PauseTransitionButton.IsEnabled = true; PauseTransitionButton.Content = "Pausar transición";
        RemoveTransitionButton.IsEnabled = true;
        StatusText.Text = TimingModeCombo.SelectedIndex == 1
            ? "Transición automática iniciada (5 minutos entre imágenes)."
            : "Transición iniciada con el intervalo elegido.";
    }

    private void TransitionTimer_Tick(object? sender, EventArgs e)
    {
        _transitionRemaining -= TimeSpan.FromSeconds(1);
        if (_transitionRemaining <= TimeSpan.Zero)
        {
            try { AdvanceTransitionImage(); }
            catch (Exception ex) { RemoveTransition_Click(this, new RoutedEventArgs()); StatusText.Text = $"La transición se detuvo: {ex.Message}"; return; }
        }
        CountdownText.Text = $"Siguiente cambio en: {FormatCountdown(_transitionRemaining)}";
    }

    private void AdvanceTransitionImage()
    {
        _transitionIndex = (_transitionIndex + 1) % _transitionFiles.Count;
        ApplyStaticImage(_transitionFiles[_transitionIndex]);
        _transitionRemaining = _transitionInterval;
        CountdownText.Text = $"Siguiente cambio en: {FormatCountdown(_transitionRemaining)}";
        FileName.Text = Path.GetFileName(_transitionFiles[_transitionIndex]);
    }

    private void PauseTransition_Click(object sender, RoutedEventArgs e)
    {
        if (_transitionTimer is null) return;
        _transitionPaused = !_transitionPaused;
        if (_transitionPaused)
        {
            _transitionTimer.Stop(); PauseTransitionButton.Content = "Reanudar transición";
            CountdownText.Text = $"Transición pausada · {FormatCountdown(_transitionRemaining)}";
        }
        else
        {
            _transitionTimer.Start(); PauseTransitionButton.Content = "Pausar transición";
            CountdownText.Text = $"Siguiente cambio en: {FormatCountdown(_transitionRemaining)}";
        }
    }

    private void RemoveTransition_Click(object sender, RoutedEventArgs e)
    {
        _transitionTimer?.Stop(); _transitionTimer = null; _transitionFiles.Clear();
        _transitionIndex = -1; _transitionPaused = false; _transitionRemaining = TimeSpan.Zero;
        TransitionFilesLabel.Text = "Sin imágenes seleccionadas";
        CountdownText.Text = "Transición eliminada";
        StartTransitionButton.IsEnabled = false; PauseTransitionButton.IsEnabled = false;
        PauseTransitionButton.Content = "Pausar transición"; RemoveTransitionButton.IsEnabled = false;
        StatusText.Text = "Transición eliminada. El fondo actual se conserva.";
    }

    private void TimingMode_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (IntervalValueBox is null || IntervalUnitCombo is null || TimingModeCombo is null) return;
        var manual = TimingModeCombo.SelectedIndex != 1;
        IntervalValueBox.IsEnabled = manual; IntervalUnitCombo.IsEnabled = manual;
        if (!manual) { IntervalValueBox.Text = "5"; IntervalUnitCombo.SelectedIndex = 1; }
    }

    private void SendBack_Click(object sender, RoutedEventArgs e)
    {
        _wallpaperHost?.SendBehindApps();
        StatusText.Text = "Fondo detrás de las ventanas y barra de tareas.";
    }

    private void BringFront_Click(object sender, RoutedEventArgs e)
    {
        _wallpaperHost?.BringAboveApps();
        StatusText.Text = "Fondo al frente y cubriendo la barra de tareas. Ctrl + Alt + F11 lo trae al frente otra vez.";
    }

    private void RemoveBackground_Click(object sender, RoutedEventArgs e)
    {
        _transitionTimer?.Stop(); _transitionTimer = null;
        _wallpaperHost?.Close(); _wallpaperHost = null;
        SetDynamicButtons(false);
        try
        {
            if (_capturedOriginalWallpaper) RestoreOriginalWallpaper();
            _capturedOriginalWallpaper = false;
            _originalWallpaperPath = null;
            _originalMonitorWallpapers.Clear();
            _imagePath = null; _dynamicPath = null;
            RemoveBackgroundButton.IsEnabled = false;
            RemoveTransition_Click(this, new RoutedEventArgs());
            StatusText.Text = "Fondo eliminado; se restauró el fondo de Windows que estaba antes.";
        }
        catch (Exception ex) { StatusText.Text = $"No se pudo restaurar el fondo anterior: {ex.Message}"; }
    }

    private void ToggleHtml_Click(object sender, RoutedEventArgs e)
    {
        if (_wallpaperHost is null) return;
        var pause = ToggleHtmlButton.Content?.ToString() == "Pausar fondo";
        _ = _wallpaperHost.SetPausedAsync(pause);
        ToggleHtmlButton.Content = pause ? "Reanudar fondo" : "Pausar fondo";
        StatusText.Text = pause ? "Contenido en pausa." : "Contenido reanudado.";
    }

    private void SetDynamicButtons(bool enabled, bool canPause = false)
    {
        ToggleHtmlButton.IsEnabled = enabled && canPause;
        SendBackButton.IsEnabled = enabled;
        BringFrontButton.IsEnabled = enabled;
        ToggleHtmlButton.Content = "Pausar fondo";
    }

    private static string FormatCountdown(TimeSpan value)
    {
        if (value.TotalDays >= 1) return $"{(int)value.TotalDays} d {value.Hours:00}:{value.Minutes:00}:{value.Seconds:00}";
        if (value.TotalHours >= 1) return $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
        if (value.TotalMinutes >= 1) return $"{(int)value.TotalMinutes:00}:{value.Seconds:00} min";
        return $"{Math.Max(0, value.Seconds)} s";
    }

    private static BitmapImage LoadImage(string path)
    {
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(path); image.EndInit(); image.Freeze();
        return image;
    }

    private static BitmapSource RenderWallpaper(ImageSource source, int width, int height, bool fill)
    {
        var canvas = new System.Windows.Controls.Grid { Width = width, Height = height, Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(8, 14, 24)) };
        if (!fill)
        {
            var background = new System.Windows.Controls.Image { Source = source, Stretch = Stretch.UniformToFill, Opacity = .72, Effect = new BlurEffect { Radius = 32, KernelType = KernelType.Gaussian } };
            RenderOptions.SetBitmapScalingMode(background, BitmapScalingMode.HighQuality);
            canvas.Children.Add(background);
            canvas.Children.Add(new System.Windows.Shapes.Rectangle { Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(64, 5, 8, 23)) });
        }
        var foreground = new System.Windows.Controls.Image { Source = source, Stretch = fill ? Stretch.UniformToFill : Stretch.Uniform, Margin = fill ? new Thickness(0) : new Thickness(width * .04, height * .04, width * .04, height * .04) };
        RenderOptions.SetBitmapScalingMode(foreground, BitmapScalingMode.HighQuality);
        canvas.Children.Add(foreground);
        canvas.Measure(new System.Windows.Size(width, height)); canvas.Arrange(new Rect(0, 0, width, height)); canvas.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(canvas); bitmap.Freeze();
        return ApplyAutomaticSharpening(bitmap);
    }

    private static BitmapSource ApplyAutomaticSharpening(BitmapSource source)
    {
        const double amount = 0.32;
        var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var stride = checked(width * 4);
        var original = new byte[checked(stride * height)];
        var sharpened = new byte[original.Length];
        converted.CopyPixels(original, stride, 0);
        Buffer.BlockCopy(original, 0, sharpened, 0, original.Length);

        for (var y = 1; y < height - 1; y++)
        {
            var row = y * stride;
            for (var x = 1; x < width - 1; x++)
            {
                var i = row + x * 4;
                for (var channel = 0; channel < 3; channel++)
                {
                    var center = original[i + channel];
                    var blur = (center * 4
                        + original[i - stride + channel] * 2
                        + original[i + stride + channel] * 2
                        + original[i - 4 + channel] * 2
                        + original[i + 4 + channel] * 2
                        + original[i - stride - 4 + channel]
                        + original[i - stride + 4 + channel]
                        + original[i + stride - 4 + channel]
                        + original[i + stride + 4 + channel]) / 16.0;
                    sharpened[i + channel] = (byte)Math.Clamp((int)Math.Round(center + (center - blur) * amount), 0, 255);
                }
            }
        }

        var result = new WriteableBitmap(width, height, converted.DpiX, converted.DpiY, PixelFormats.Pbgra32, null);
        result.WritePixels(new Int32Rect(0, 0, width, height), sharpened, stride, 0);
        result.Freeze();
        return result;
    }

    private void RememberOriginalWallpaper()
    {
        if (_capturedOriginalWallpaper) return;
        _capturedOriginalWallpaper = true;
        var current = new StringBuilder(32768);
        if (SystemParametersInfoGet(0x73, (uint)current.Capacity, current, 0) && current.Length > 0)
            _originalWallpaperPath = current.ToString();

        object? desktopWallpaper = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"), throwOnError: true)!;
            desktopWallpaper = Activator.CreateInstance(type);
            if (desktopWallpaper is not IDesktopWallpaper desktop) return;
            desktop.GetMonitorDevicePathCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                IntPtr monitorPtr = IntPtr.Zero;
                IntPtr pathPtr = IntPtr.Zero;
                try
                {
                    desktop.GetMonitorDevicePathAt(i, out monitorPtr);
                    var monitorId = Marshal.PtrToStringUni(monitorPtr);
                    if (string.IsNullOrWhiteSpace(monitorId)) continue;
                    desktop.GetWallpaper(monitorId, out pathPtr);
                    var path = Marshal.PtrToStringUni(pathPtr);
                    _originalMonitorWallpapers.Add((monitorId, path ?? string.Empty));
                }
                finally
                {
                    if (monitorPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(monitorPtr);
                    if (pathPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(pathPtr);
                }
            }
        }
        catch { }
        finally
        {
            if (desktopWallpaper is not null && Marshal.IsComObject(desktopWallpaper)) Marshal.FinalReleaseComObject(desktopWallpaper);
        }
    }

    private void RestoreOriginalWallpaper()
    {
        if (_originalMonitorWallpapers.Count > 0)
        {
            object? desktopWallpaper = null;
            try
            {
                var type = Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"), throwOnError: true)!;
                desktopWallpaper = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Windows no creó el servicio de fondos.");
                var desktop = (IDesktopWallpaper)desktopWallpaper;
                foreach (var item in _originalMonitorWallpapers) desktop.SetWallpaper(item.MonitorId, item.Path);
                return;
            }
            finally
            {
                if (desktopWallpaper is not null && Marshal.IsComObject(desktopWallpaper)) Marshal.FinalReleaseComObject(desktopWallpaper);
            }
        }
        if (!string.IsNullOrWhiteSpace(_originalWallpaperPath) && File.Exists(_originalWallpaperPath))
        {
            ApplyWindowsWallpaper(_originalWallpaperPath);
            return;
        }
        if (!SystemParametersInfo(20, 0, string.Empty, 3))
            throw new InvalidOperationException($"Windows no pudo restaurar el fondo anterior. Error {Marshal.GetLastWin32Error()}.");
    }

    private static void ApplyWindowsWallpaper(string path)
    {
        Exception? comError = null;
        object? desktopWallpaper = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"), throwOnError: true)!;
            desktopWallpaper = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Windows no creó el servicio de fondos.");
            ((IDesktopWallpaper)desktopWallpaper).SetWallpaper(null, path);
            return;
        }
        catch (Exception ex) { comError = ex; }
        finally
        {
            if (desktopWallpaper is not null && Marshal.IsComObject(desktopWallpaper)) Marshal.FinalReleaseComObject(desktopWallpaper);
        }
        if (!SystemParametersInfo(20, 0, path, 3))
            throw new InvalidOperationException($"Windows rechazó el fondo. Error del sistema: {Marshal.GetLastWin32Error()}. {comError?.Message}");
    }

    [ComImport]
    [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        void GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, out IntPtr wallpaper);
        void GetMonitorDevicePathAt(uint monitorIndex, out IntPtr monitorId);
        void GetMonitorDevicePathCount(out uint count);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_hotkeyRegistered) UnregisterHotKey(new WindowInteropHelper(this).Handle, WallpaperHotkeyId);
        _transitionTimer?.Stop(); _wallpaperHost?.Close(); base.OnClosed(e);
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, string value, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfoGet(uint action, uint param, StringBuilder value, uint flags);
}
