using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FondoClaro;

public partial class MainWindow : Window
{
    private string? _imagePath;
    private string? _dynamicPath;
    private string? _originalWallpaperPath;
    private bool _capturedOriginalWallpaper;
    private WallpaperHostWindow? _wallpaperHost;
    private DispatcherTimer? _transitionTimer;
    private readonly List<string> _transitionFiles = new();
    private int _transitionIndex = -1;
    private TimeSpan _transitionInterval;
    private TimeSpan _transitionRemaining;
    private bool _transitionPaused;
    private bool _fill;

    public MainWindow() => InitializeComponent();

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
                await PreviewWeb.EnsureCoreWebView2Async();
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
                RememberOriginalWallpaper();
                _wallpaperHost?.Close();
                _wallpaperHost = new WallpaperHostWindow(_dynamicPath);
                _wallpaperHost.Show();
                SetDynamicButtons(true);
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
        _wallpaperHost?.Close(); _wallpaperHost = null;
        _dynamicPath = null; _imagePath = path;
        SetDynamicButtons(false);
        var source = LoadImage(path);
        var screens = System.Windows.Forms.Screen.AllScreens;
        var screen = System.Windows.Forms.Screen.PrimaryScreen ?? screens[0];
        var width = screen.Bounds.Width; var height = screen.Bounds.Height;
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
        RemoveBackgroundButton.IsEnabled = true;
        StatusText.Text = $"Fondo aplicado a {width} × {height}.";
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
        StatusText.Text = "Fondo colocado detrás de las ventanas.";
    }

    private void BringFront_Click(object sender, RoutedEventArgs e)
    {
        _wallpaperHost?.BringAboveApps();
        StatusText.Text = "Fondo colocado delante de las ventanas.";
    }

    private void RemoveBackground_Click(object sender, RoutedEventArgs e)
    {
        _transitionTimer?.Stop(); _transitionTimer = null;
        _wallpaperHost?.Close(); _wallpaperHost = null;
        SetDynamicButtons(false);
        try
        {
            if (!string.IsNullOrWhiteSpace(_originalWallpaperPath) && File.Exists(_originalWallpaperPath))
                ApplyWindowsWallpaper(_originalWallpaperPath);
            else if (!SystemParametersInfo(20, 0, string.Empty, 3))
                throw new InvalidOperationException($"Windows no pudo quitar el fondo. Error {Marshal.GetLastWin32Error()}.");
            _originalWallpaperPath = null; _capturedOriginalWallpaper = false;
            _imagePath = null; _dynamicPath = null;
            RemoveBackgroundButton.IsEnabled = false;
            RemoveTransition_Click(this, new RoutedEventArgs());
            StatusText.Text = "Fondo eliminado y fondo anterior restaurado.";
        }
        catch (Exception ex) { StatusText.Text = $"No se pudo eliminar el fondo: {ex.Message}"; }
    }

    private void ToggleHtml_Click(object sender, RoutedEventArgs e)
    {
        if (_wallpaperHost is null) return;
        var pause = ToggleHtmlButton.Content?.ToString() == "Pausar fondo";
        _ = _wallpaperHost.SetPausedAsync(pause);
        ToggleHtmlButton.Content = pause ? "Reanudar fondo" : "Pausar fondo";
        StatusText.Text = pause ? "Contenido en pausa." : "Contenido reanudado.";
    }

    private void SetDynamicButtons(bool enabled)
    {
        ToggleHtmlButton.IsEnabled = enabled;
        SendBackButton.IsEnabled = enabled;
        BringFrontButton.IsEnabled = enabled;
        if (enabled) ToggleHtmlButton.Content = "Pausar fondo";
    }

    private void RememberOriginalWallpaper()
    {
        if (_capturedOriginalWallpaper) return;
        _capturedOriginalWallpaper = true;
        var path = new StringBuilder(32768);
        if (SystemParametersInfoGet(0x73, (uint)path.Capacity, path, 0) && path.Length > 0)
            _originalWallpaperPath = path.ToString();
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
            canvas.Children.Add(new System.Windows.Controls.Image { Source = source, Stretch = Stretch.UniformToFill, Opacity = .72, Effect = new BlurEffect { Radius = 32, KernelType = KernelType.Gaussian } });
            canvas.Children.Add(new System.Windows.Shapes.Rectangle { Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(64, 5, 8, 23)) });
        }
        canvas.Children.Add(new System.Windows.Controls.Image { Source = source, Stretch = fill ? Stretch.UniformToFill : Stretch.Uniform, Margin = fill ? new Thickness(0) : new Thickness(width * .04, height * .04, width * .04, height * .04) });
        canvas.Measure(new System.Windows.Size(width, height)); canvas.Arrange(new Rect(0, 0, width, height)); canvas.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(canvas); bitmap.Freeze(); return bitmap;
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
    }

    protected override void OnClosed(EventArgs e) { _transitionTimer?.Stop(); _wallpaperHost?.Close(); base.OnClosed(e); }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, string value, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfoGet(uint action, uint param, StringBuilder value, uint flags);
}
