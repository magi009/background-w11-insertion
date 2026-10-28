using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace FondoClaroLinux;

internal sealed class MainWindow : Window
{
    private readonly TextBlock _fileLabel = new() { Text = "Todavía no has elegido un archivo", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { Text = "Fondo estático: GNOME, KDE Plasma, Cinnamon, MATE o XFCE. HTML animado: X11." , TextWrapping = TextWrapping.Wrap };
    private readonly Image _previewImage = new() { Stretch = Stretch.Uniform, IsVisible = false };
    private readonly NativeWebView _previewWeb = new() { IsVisible = false };
    private readonly RadioButton _fit = new() { Content = "Ajustar · imagen completa", GroupName = "mode", IsChecked = true };
    private readonly RadioButton _fill = new() { Content = "Rellenar · recortar bordes", GroupName = "mode" };
    private readonly TextBox _intervalValue = new() { Text = "30", PlaceholderText = "Cantidad", Width = 95 };
    private readonly ComboBox _intervalUnit = new() { ItemsSource = new[] { "segundos", "minutos", "horas", "días" }, SelectedIndex = 0, Width = 115 };
    private readonly ComboBox _timingMode = new() { ItemsSource = new[] { "Manual", "Automático · 5 minutos" }, SelectedIndex = 0 };
    private readonly TextBlock _transitionStatus = new() { Text = "Transición detenida", Foreground = Brush("#8EF0D0"), TextWrapping = TextWrapping.Wrap };
    private readonly List<string> _transitionFiles = new();
    private readonly DispatcherTimer _transitionTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private Button? _pauseBackgroundButton;
    private Button? _pauseTransitionButton;
    private string? _path;
    private string? _originalWallpaper;
    private WallpaperHostWindow? _host;
    private int _transitionIndex = -1;
    private TimeSpan _transitionInterval;
    private TimeSpan _transitionRemaining;
    private bool _transitionPaused;

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
        _pauseTransitionButton!.IsEnabled = false;
        _pauseBackgroundButton!.IsEnabled = false;
        _transitionTimer.Tick += TransitionTick;
        Closed += (_, _) => _transitionTimer.Stop();
        _timingMode.SelectionChanged += (_, _) =>
        {
            var manual = _timingMode.SelectedIndex == 0;
            _intervalValue.IsEnabled = manual;
            _intervalUnit.IsEnabled = manual;
        };
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
        panel.Children.Add(BuildTransitionPanel());
        panel.Children.Add(Button("Aplicar fondo", ApplySelected, accent: true));
        panel.Children.Add(_pauseBackgroundButton = Button("Pausar fondo animado", PauseBackground));
        panel.Children.Add(Button("Enviar detrás · mostrar apps", SendBehind));
        panel.Children.Add(Button("Pantalla completa", Fullscreen));
        panel.Children.Add(Button("Eliminar y restaurar fondo anterior", RemoveBackground));
        panel.Children.Add(_status);
        var sideScroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Grid.SetColumn(sideScroll, 0);
        grid.Children.Add(sideScroll);

        var preview = new Border { Background = Brush("#0C1422"), BorderBrush = Brush("#1C2A3E"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(20), Padding = new Thickness(12) };
        var previewGrid = new Grid { ClipToBounds = true };
        previewGrid.Children.Add(_previewImage);
        previewGrid.Children.Add(_previewWeb);
        preview.Child = previewGrid;
        Grid.SetColumn(preview, 1);
        grid.Children.Add(preview);
        return grid;
    }

    private Control BuildTransitionPanel()
    {
        _pauseTransitionButton = Button("Pausar transición", PauseTransition);
        var controls = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "TRANSICIÓN DE IMÁGENES", FontWeight = FontWeight.Bold, Opacity = .7 },
                Button("Elegir imágenes", ChooseTransition),
                _transitionStatus,
                _timingMode,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _intervalValue, _intervalUnit } },
                Button("Iniciar transición", StartTransition, accent: true),
                _pauseTransitionButton,
                Button("Eliminar transición", RemoveTransition)
            }
        };
        return new Border { Background = Brush("#101827"), CornerRadius = new CornerRadius(14), Padding = new Thickness(14), Child = controls };
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
                _pauseBackgroundButton!.IsEnabled = false;
                _status.Text = "Fondo aplicado por el escritorio Linux. El modo de ajuste conserva las proporciones.";
                return;
            }

            if (!X11DesktopLayer.IsX11)
                throw new PlatformNotSupportedException("Wayland no permite que una app común controle la capa del escritorio. Los fondos HTML animados requieren una sesión X11; la vista previa sí funciona.");
            _host?.Close();
            _host = new WallpaperHostWindow(_path);
            _host.Show();
            _host.SendBehind();
            _pauseBackgroundButton!.IsEnabled = true;
            _pauseBackgroundButton.Content = "Pausar fondo animado";
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
        _transitionTimer.Stop();
        _host?.Close();
        _host = null;
        _pauseBackgroundButton!.IsEnabled = false;
        if (_originalWallpaper is not null && File.Exists(_originalWallpaper))
        {
            try { LinuxDesktopWallpaper.Apply(_originalWallpaper, fill: false); }
            catch (Exception ex) { _status.Text = $"No se pudo restaurar el fondo anterior: {ex.Message}"; return; }
        }
        _transitionFiles.Clear();
        _pauseTransitionButton!.IsEnabled = false;
        _transitionIndex = -1;
        _originalWallpaper = null;
        _status.Text = "Fondo eliminado.";
    }

    private async void PauseBackground(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_host is null) return;
        try
        {
            var paused = !_host.IsPaused;
            await _host.SetPausedAsync(paused);
            _pauseBackgroundButton!.Content = paused ? "Reanudar fondo animado" : "Pausar fondo animado";
            _status.Text = paused ? "Animaciones y medios pausados." : "Animaciones reanudadas.";
        }
        catch (Exception ex) { _status.Text = $"No se pudo controlar la animación: {ex.Message}"; }
    }

    private async void ChooseTransition(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Elige imágenes para la transición",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Imágenes") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif"] }]
        });
        _transitionFiles.Clear();
        _transitionFiles.AddRange(files.Select(x => x.Path.LocalPath).Where(IsImage));
        _transitionStatus.Text = _transitionFiles.Count >= 2 ? $"{_transitionFiles.Count} imágenes listas." : "Elige al menos dos imágenes.";
    }

    private void StartTransition(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_transitionFiles.Count < 2) { _transitionStatus.Text = "Elige al menos dos imágenes."; return; }
        if (_timingMode.SelectedIndex == 1) _transitionInterval = TimeSpan.FromMinutes(5);
        else
        {
            if (!int.TryParse(_intervalValue.Text, out var amount) || amount < 1) { _transitionStatus.Text = "Indica un intervalo de 1 o más."; return; }
            _transitionInterval = _intervalUnit.SelectedIndex switch
            {
                0 => TimeSpan.FromSeconds(amount), 1 => TimeSpan.FromMinutes(amount),
                2 => TimeSpan.FromHours(amount), 3 => TimeSpan.FromDays(amount), _ => TimeSpan.FromSeconds(amount)
            };
        }
        _transitionIndex = -1;
        _transitionPaused = false;
        _transitionRemaining = _transitionInterval;
        try
        {
            AdvanceTransition();
            _transitionTimer.Start();
            _pauseTransitionButton!.Content = "Pausar transición";
            _pauseTransitionButton.IsEnabled = true;
            _transitionStatus.Text = $"Siguiente cambio en {Format(_transitionRemaining)}";
        }
        catch (Exception ex) { _transitionStatus.Text = $"No se pudo iniciar: {ex.Message}"; }
    }

    private void PauseTransition(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_transitionTimer.IsEnabled) return;
        _transitionPaused = !_transitionPaused;
        if (_transitionPaused) _transitionTimer.Stop(); else _transitionTimer.Start();
        _pauseTransitionButton!.Content = _transitionPaused ? "Reanudar transición" : "Pausar transición";
        _transitionStatus.Text = _transitionPaused ? $"Pausada · {Format(_transitionRemaining)}" : $"Siguiente cambio en {Format(_transitionRemaining)}";
    }

    private void RemoveTransition(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _transitionTimer.Stop();
        _transitionFiles.Clear();
        _transitionIndex = -1;
        _transitionPaused = false;
        _transitionRemaining = TimeSpan.Zero;
        _pauseTransitionButton!.IsEnabled = false;
        _pauseTransitionButton.Content = "Pausar transición";
        _transitionStatus.Text = "Transición eliminada; el fondo actual se conserva.";
    }

    private void TransitionTick(object? sender, EventArgs e)
    {
        if (_transitionPaused) return;
        _transitionRemaining -= TimeSpan.FromSeconds(1);
        if (_transitionRemaining <= TimeSpan.Zero)
        {
            try { AdvanceTransition(); }
            catch (Exception ex) { _transitionTimer.Stop(); _transitionStatus.Text = $"Transición detenida: {ex.Message}"; return; }
        }
        _transitionStatus.Text = $"Siguiente cambio en {Format(_transitionRemaining)}";
    }

    private void AdvanceTransition()
    {
        _transitionIndex = (_transitionIndex + 1) % _transitionFiles.Count;
        var next = _transitionFiles[_transitionIndex];
        if (_originalWallpaper is null) _originalWallpaper = LinuxDesktopWallpaper.GetCurrent();
        LinuxDesktopWallpaper.Apply(next, _fill.IsChecked == true);
        _path = next;
        _fileLabel.Text = Path.GetFileName(next);
        ShowPreview(next);
        _transitionRemaining = _transitionInterval;
    }

    private static string Format(TimeSpan time) => time.TotalDays >= 1
        ? $"{(int)time.TotalDays} d {time.Hours:00}:{time.Minutes:00}:{time.Seconds:00}"
        : time.TotalHours >= 1 ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
        : time.TotalMinutes >= 1 ? $"{(int)time.TotalMinutes:00}:{time.Seconds:00} min" : $"{Math.Max(0, time.Seconds)} s";

    private static bool IsImage(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif";

    private static Button Button(string label, EventHandler<Avalonia.Interactivity.RoutedEventArgs> click, bool accent = false)
    {
        var button = new Button { Content = label, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(12, 11), Background = Brush(accent ? "#117A68" : "#202E41"), Foreground = Brush("#EAF0FA"), BorderBrush = Brush(accent ? "#22A88F" : "#35455B"), CornerRadius = new CornerRadius(10) };
        button.Click += click;
        return button;
    }

    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));
}
