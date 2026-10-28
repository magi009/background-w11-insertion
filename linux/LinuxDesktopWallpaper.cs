using System.Diagnostics;
using System.Text.RegularExpressions;

namespace FondoClaroLinux;

internal static class LinuxDesktopWallpaper
{
    private static readonly string Desktop =
        (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").ToLowerInvariant();

    public static string? GetCurrent()
    {
        if (Desktop.Contains("gnome") || Desktop.Contains("ubuntu"))
            return ReadGSettings("org.gnome.desktop.background", "picture-uri");
        if (Desktop.Contains("cinnamon"))
            return ReadGSettings("org.cinnamon.desktop.background", "picture-uri");
        if (Desktop.Contains("mate"))
            return ReadGSettings("org.mate.background", "picture-filename");
        if (Desktop.Contains("kde") || Environment.GetEnvironmentVariable("KDE_FULL_SESSION") == "true")
            return ReadPlasmaConfig();
        if (Desktop.Contains("xfce"))
            return ReadXfceImage();
        return null;
    }

    public static void Apply(string path, bool fill)
    {
        if (Desktop.Contains("gnome") || Desktop.Contains("ubuntu"))
        {
            SetGSettings("org.gnome.desktop.background", "picture-uri", new Uri(path).AbsoluteUri);
            TrySetGSettings("org.gnome.desktop.background", "picture-uri-dark", new Uri(path).AbsoluteUri);
            TrySetGSettings("org.gnome.desktop.background", "picture-options", fill ? "zoom" : "scaled");
            return;
        }
        if (Desktop.Contains("cinnamon"))
        {
            SetGSettings("org.cinnamon.desktop.background", "picture-uri", new Uri(path).AbsoluteUri);
            TrySetGSettings("org.cinnamon.desktop.background", "picture-options", fill ? "zoom" : "scaled");
            return;
        }
        if (Desktop.Contains("mate"))
        {
            SetGSettings("org.mate.background", "picture-filename", path);
            TrySetGSettings("org.mate.background", "picture-options", fill ? "zoom" : "scaled");
            return;
        }
        if (Desktop.Contains("kde") || Environment.GetEnvironmentVariable("KDE_FULL_SESSION") == "true")
        {
            Run("plasma-apply-wallpaperimage", path);
            return;
        }
        if (Desktop.Contains("xfce"))
        {
            var properties = Run("xfconf-query", "--channel", "xfce4-desktop", "--list")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(x => x.Contains("last-image", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (properties.Length == 0) throw new InvalidOperationException("XFCE no reportó propiedades de fondo last-image.");
            foreach (var property in properties)
                Run("xfconf-query", "--channel", "xfce4-desktop", "--property", property.Trim(), "--set", path);
            return;
        }
        throw new InvalidOperationException($"Entorno no compatible para establecer imágenes: {Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "desconocido"}.");
    }

    private static string? ReadGSettings(string schema, string key)
    {
        try
        {
            var value = Run("gsettings", "get", schema, key).Trim();
            value = Regex.Replace(value, "^'|'$", "");
            return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : value;
        }
        catch { return null; }
    }

    private static void SetGSettings(string schema, string key, string value) => Run("gsettings", "set", schema, key, value);

    private static void TrySetGSettings(string schema, string key, string value)
    {
        try { SetGSettings(schema, key, value); } catch { }
    }

    private static string? ReadXfceImage()
    {
        try
        {
            var properties = Run("xfconf-query", "--channel", "xfce4-desktop", "--list")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(x => x.Contains("last-image", StringComparison.OrdinalIgnoreCase));
            if (properties is null) return null;
            return Run("xfconf-query", "--channel", "xfce4-desktop", "--property", properties.Trim()).Trim();
        }
        catch { return null; }
    }

    private static string? ReadPlasmaConfig()
    {
        var config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "plasma-org.kde.plasma.desktop-appletsrc");
        if (!File.Exists(config)) return null;
        foreach (var line in File.ReadLines(config).Where(x => x.StartsWith("Image=file://", StringComparison.OrdinalIgnoreCase)))
        {
            var value = line["Image=".Length..];
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile) return uri.LocalPath;
        }
        return null;
    }

    private static string Run(string file, params string[] args)
    {
        var info = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"No se pudo iniciar {file}.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(5000);
        if (!process.HasExited) { process.Kill(true); throw new TimeoutException($"{file} tardó demasiado."); }
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? $"{file} terminó con código {process.ExitCode}." : stderr.Trim());
        return stdout;
    }
}
