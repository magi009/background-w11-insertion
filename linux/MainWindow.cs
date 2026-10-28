using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace FondoClaroLinux;

internal sealed class MainWindow : Window
{
    private readonly TextBlock _fileLabel = new() { Text = "Todavía no has elegido un archivo", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { Text = "Fondo estático: GNOME, KDE Plasma, Cinnamon, MATE o XFCE. HTML animado: X11." , TextWrapping = TextWrapping.Wrap };
    private readonly Image _previewImage = new() { Stretch = Stretch.Uniform, IsVisible = false };
    private readonly NativeWebView _previewWeb = new() { IsVisible = false };
    private readonly RadioButton _fit = new() { Content = "Ajustar · imagen completa", GroupName = "mode", IsChecked = true };
    private readonly RadioButton _fill = new() { Content = "Rellenar · recortar bordes", GroupName = "mode" };
    private string? _path;
    private string? _originalWallpaper;
    private WallpaperHostWindow? _host;

    public MainWindow()
    {
        Title = "FondoClaro Linux";
        Width = 1120;
        Height = 760;
        MinWidth = 900;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brush("#080E18");
        Foreground = Brush("#EAF0FA");
        Content = BuildLayout();
    }

    private Control BuildLayout()
    {
        var columns = new ColumnDefinitions("340,*");
        var grid = new Grid { ColumnDefinitions = columns, ColumnSpacing = 22, Margin = new Thickness(24) };
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(4, 0, 8, 0) };
        panel.Children.Add(new TextBlock { Text = "FONDOCLARO", Foreground = Brush("#8EF0D0"), FontWeight = FontWeight.Bold });
        panel.Children.Add(new TextBlock { Text = "Tu imagen, bien puesta.", FontSize = 27, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Fondos para Linux con ajuste de imagen y páginas HTML animadas en X11.", TextWrapping = TextWrapping.Wrap, Opacity = .75 });
        panel.Children.Add(new Border { Background = Brush("#101827"), CornerRadius = new CornerRadius(14), Padding = new Thickness(14), Child = new StackPanel { Spacing = 10, Children = { _fileLabel, Button("Elegir archivo", ChooseFile) } } });
        panel.Children.Add(new Border { Background = Brush("#101827"), CornerRadius = new CornerRadius(14), Padding = new Thickness(14), Child = new StackPanel { Spacing = 9, Children = { new TextBlock { Text = "AJUSTE DE IMAGEN", FontWeight = FontWeight.Bold, Opacity = .7 }, _fit, _fill } } });
        panel.Children.Add(Button("Aplicar fondo", ApplySelected, accent: true));
        panel.Children.Add(Button("Enviar detrás · mostrar apps", SendBehind));
        panel.Children.Add(Button("Pantalla completa", Fullscreen));
        panel.Children.Add(Button("Eliminar y restaurar fondo anterior", RemoveBackground));
        panel.Children.Add(_status);
        Grid.SetColumn(panel, 0);
        grid.Children.Add(panel);

        var preview = new Border { Background = Brush("#0C1422"), BorderBrush = Brush("#1C2A3E"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(20), Padding = new Thickness(12) };
        var previewGrid = new Grid { ClipToBounds = true };
        previewGrid.Children.Add(_previewImage);
        previewGrid.Children.Add(_previewWeb);
        preview.Child = previewGrid;
        Grid.SetColumn(preview, 1);
        grid.Children.Add(preview);
        return grid;
    }

    private async void ChooseFile(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Elige una imagen o HTML",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Imágenes y HTML") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif", "*.svg", "*.html", "*.htm", "*.mp4", "*.webm", "*.pdf"] }, FilePickerFileTypes.All]
        });
        if (files.Count == 0) return;
        _path = files[0].Path.LocalPath;
        _fileLabel.Text = Path.GetFileName(_path);
        ShowPreview(_path);
        _status.Text = "Vista previa cargada. Ajuste completo conserva la proporción; rellenar recorta sin deformar.";
    }

    private void ShowPreview(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif")
        {
            try
            {
                _previewImage.Source = new Bitmap(path);
                _previewImage.Stretch = _fill.IsChecked == true ? Stretch.UniformToFill : Stretch.Uniform;
                _previewImage.IsVisible = true;
                _previewWeb.IsVisible = false;
                return;
            }
            catch { }
        }
        _previewWeb.Source = new Uri(path);
        _previewWeb.IsVisible = true;
        _previewImage.IsVisible = false;
    }

    private void ApplySelected(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_path is null) { _status.Text = "Elige un archivo primero."; return; }
        try
        {
            if (_originalWallpaper is null) _originalWallpaper = LinuxDesktopWallpaper.GetCurrent();
            if (IsImage(_path))
            {
                LinuxDesktopWallpaper.Apply(_path, _fill.IsChecked == true);
                _host?.Close();
                _host = null;
                _status.Text = "Fondo aplicado por el escritorio Linux. El modo de ajuste conserva las proporciones.";
                return;
            }

            if (!X11DesktopLayer.IsX11)
                throw new PlatformNotSupportedException("Wayland no permite que una app común controle la capa del escritorio. Los fondos HTML animados requieren una sesión X11; la vista previa sí funciona.");
            _host?.Close();
            _host = new WallpaperHostWindow(_path);
            _host.Show();
            _host.SendBehind();
            _status.Text = "HTML activo detrás de las apps en X11. En Wayland la capa animada depende del entorno de escritorio.";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    private void SendBehind(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (_host is null) { _status.Text = "Aplica primero un fondo HTML en sesión X11."; return; }
            _host.SendBehind();
            _status.Text = "Fondo HTML detrás; el panel permanece abierto.";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    private void Fullscreen(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (_host is null)
            {
                if (_path is null) { _status.Text = "Elige y aplica un fondo primero."; return; }
                _host = new WallpaperHostWindow(_path);
                _host.Show();
            }
            _host.BringFullscreen();
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    private void RemoveBackground(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _host?.Close();
        _host = null;
        if (_originalWallpaper is not null && File.Exists(_originalWallpaper))
        {
            try { LinuxDesktopWallpaper.Apply(_originalWallpaper, fill: false); }
            catch (Exception ex) { _status.Text = $"No se pudo restaurar el fondo anterior: {ex.Message}"; return; }
        }
        _originalWallpaper = null;
        _status.Text = "Fondo eliminado.";
    }

    private static bool IsImage(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif";

    private static Button Button(string label, EventHandler<Avalonia.Interactivity.RoutedEventArgs> click, bool accent = false)
    {
        var button = new Button { Content = label, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(12, 11), Background = Brush(accent ? "#117A68" : "#202E41"), Foreground = Brush("#EAF0FA"), BorderBrush = Brush(accent ? "#22A88F" : "#35455B"), CornerRadius = new CornerRadius(10) };
        button.Click += click;
        return button;
    }

    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));
}
