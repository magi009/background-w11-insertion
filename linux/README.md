# FondoClaro Linux

Companion desktop build using Avalonia. The Windows WPF app remains in `src/`.

## Supported behavior

- Image wallpaper setting through the desktop environment on GNOME, KDE Plasma, Cinnamon, MATE, and XFCE.
- Image preview with fit and fill modes.
- HTML wallpaper behind desktop icons on an X11 session using the EWMH desktop-window hint.
- Fullscreen HTML/image window, preview, and restoration of the previous image wallpaper when it can be read from the desktop configuration.

Wayland does not expose a portable API for an ordinary application to position a window behind desktop icons. In this version, HTML wallpaper mode reports that limitation instead of claiming that it has been applied. The image wallpaper mode still uses the desktop's native settings.

## Build

Install .NET 8 and the native web-rendering packages for Avalonia's WebView:

```sh
# Debian/Ubuntu
sudo apt install libgtk-3-0 libwebkit2gtk-4.1-0 libsoup-3.0-0

# Fedora
sudo dnf install gtk3 webkit2gtk4.1 libsoup3

# Arch Linux
sudo pacman -S gtk3 webkit2gtk-4.1 libsoup3
```

Then run:

```sh
dotnet restore FondoClaro.Linux.csproj
dotnet run --project FondoClaro.Linux.csproj
```

HTML wallpaper rendering uses the system WebKit implementation; the app does not bundle a browser engine.
