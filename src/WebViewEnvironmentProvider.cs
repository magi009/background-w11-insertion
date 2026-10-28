using Microsoft.Web.WebView2.Core;

namespace FondoClaro;

internal static class WebViewEnvironmentProvider
{
    private static Task<CoreWebView2Environment>? _environmentTask;

    public static Task<CoreWebView2Environment> GetAsync()
        => _environmentTask ??= CreateAsync();

    private static async Task<CoreWebView2Environment> CreateAsync()
    {
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FondoClaro",
            "WebView2");
        Directory.CreateDirectory(userDataFolder);

        // Give the preview and the desktop host the same environment and profile.
        return await CoreWebView2Environment.CreateAsync(null, userDataFolder);
    }
}
