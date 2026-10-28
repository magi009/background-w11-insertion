using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace FondoClaro;

public partial class MainWindow : Window
{
    private string? _imagePath;
    private string? _dynamicPath;
    private WallpaperHostWindow? _wallpaperHost;
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
                PreviewBackground.Source = source;
                PreviewImage.Source = source;
                ApplyPreviewMode();
                ToggleHtmlButton.IsEnabled = false;
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
                ToggleHtmlButton.IsEnabled = true;
                ToggleHtmlButton.Content = "Pausar fondo";
                PreviewLabel.Text = $"VISTA PREVIA · {Path.GetExtension(dialog.FileName).TrimStart('.').ToUpperInvariant()}";
                StatusText.Text = "Archivo abierto con WebView2. Si Windows o Edge pueden mostrarlo, podrás usarlo como fondo.";
            }
        }
        catch (Exception ex)
        {
            _imagePath = null; _dynamicPath = null; ToggleHtmlButton.IsEnabled = false;
            System.Windows.MessageBox.Show($"No pude abrir este archivo como imagen ni con WebView2.\n\n{ex.Message}", "FondoClaro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void FitMode_Checked(object sender, RoutedEventArgs e) { _fill = false; if (PreviewImage is not null) ApplyPreviewMode(); }
    private void FillMode_Checked(object sender, RoutedEventArgs e) { _fill = true; if (PreviewImage is not null) ApplyPreviewMode(); }

    private void ApplyPreviewMode()
    {
        PreviewImage.Stretch = _fill ? Stretch.UniformToFill : Stretch.Uniform;
        PreviewImage.Margin = _fill ? new Thickness(0) : new Thickness(28);
        PreviewBackground.Effect = _fill ? null : new BlurEffect { Radius = 25, KernelType = KernelType.Gaussian };
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_dynamicPath is not null)
            {
                _wallpaperHost?.Close();
                _wallpaperHost = new WallpaperHostWindow(_dynamicPath);
                _wallpaperHost.Show();
                ToggleHtmlButton.IsEnabled = true;
                ToggleHtmlButton.Content = "Pausar fondo";
                StatusText.Text = "Fondo activo. Cierra FondoClaro para quitarlo.";
                return;
            }
            if (_imagePath is null) { StatusText.Text = "Elige primero un archivo de fondo."; return; }
            _wallpaperHost?.Close(); _wallpaperHost = null;
            ToggleHtmlButton.IsEnabled = false;
            var source = LoadImage(_imagePath);
            var screens = System.Windows.Forms.Screen.AllScreens;
            var screen = System.Windows.Forms.Screen.PrimaryScreen ?? screens[0];
            var width = screen.Bounds.Width; var height = screen.Bounds.Height;
            var wallpaper = RenderWallpaper(source, width, height, _fill);
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FondoClaro");
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, "fondo-adaptado.jpg");
            using (var stream = File.Create(target))
            {
                var encoder = new JpegBitmapEncoder { QualityLevel = 94 };
                encoder.Frames.Add(BitmapFrame.Create(wallpaper)); encoder.Save(stream);
            }
            ApplyWindowsWallpaper(target);
            StatusText.Text = $"Fondo aplicado a {width} × {height}. Se guardó una copia adaptada.";
        }
        catch (Exception ex) { System.Windows.MessageBox.Show($"No se pudo aplicar el fondo.\n\n{ex.Message}", "FondoClaro", MessageBoxButton.OK, MessageBoxImage.Error); }
        await Task.CompletedTask;
    }

    private async void ToggleHtml_Click(object sender, RoutedEventArgs e)
    {
        if (_wallpaperHost is null) return;
        var paused = ToggleHtmlButton.Content?.ToString() == "Pausar fondo";
        await _wallpaperHost.SetPausedAsync(paused);
        ToggleHtmlButton.Content = paused ? "Reanudar fondo" : "Pausar fondo";
        StatusText.Text = paused ? "Contenido en pausa." : "Contenido reanudado.";
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

    protected override void OnClosed(EventArgs e) { _wallpaperHost?.Close(); base.OnClosed(e); }

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

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, string value, uint flags);
}
