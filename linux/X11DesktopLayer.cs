using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace FondoClaroLinux;

internal static class X11DesktopLayer
{
    private const int PropModeReplace = 0;

    public static bool IsX11 => string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "x11", StringComparison.OrdinalIgnoreCase);

    public static void SetDesktop(Window window, bool desktop)
    {
        var handle = window.TryGetPlatformHandle();
        if (handle is null || handle.HandleDescriptor != "XID")
            throw new PlatformNotSupportedException("El escritorio no expone una ventana X11. El modo HTML debajo de iconos requiere sesión X11.");

        var display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero) throw new InvalidOperationException("No se pudo conectar con X11.");
        try
        {
            var windowType = Atom(display, "_NET_WM_WINDOW_TYPE");
            var atomType = Atom(display, "ATOM");
            var kind = Atom(display, desktop ? "_NET_WM_WINDOW_TYPE_DESKTOP" : "_NET_WM_WINDOW_TYPE_NORMAL");
            SetAtomList(display, handle.Handle, windowType, atomType, [kind]);

            var state = Atom(display, "_NET_WM_STATE");
            var stateValues = desktop
                ? new[] { Atom(display, "_NET_WM_STATE_BELOW"), Atom(display, "_NET_WM_STATE_STICKY"), Atom(display, "_NET_WM_STATE_SKIP_TASKBAR"), Atom(display, "_NET_WM_STATE_SKIP_PAGER") }
                : new[] { Atom(display, "_NET_WM_STATE_ABOVE"), Atom(display, "_NET_WM_STATE_STICKY"), Atom(display, "_NET_WM_STATE_SKIP_TASKBAR"), Atom(display, "_NET_WM_STATE_SKIP_PAGER") };
            SetAtomList(display, handle.Handle, state, atomType, stateValues);
            if (desktop) XLowerWindow(display, handle.Handle);
            else XRaiseWindow(display, handle.Handle);
            XFlush(display);
        }
        finally { XCloseDisplay(display); }
    }

    private static UIntPtr Atom(IntPtr display, string name) => XInternAtom(display, name, false);

    private static void SetAtomList(IntPtr display, IntPtr window, UIntPtr property, UIntPtr type, UIntPtr[] atoms)
    {
        var memory = Marshal.AllocHGlobal(IntPtr.Size * atoms.Length);
        try
        {
            for (var i = 0; i < atoms.Length; i++)
                Marshal.WriteIntPtr(memory, i * IntPtr.Size, new IntPtr(unchecked((long)atoms[i].ToUInt64())));
            XChangeProperty(display, window, property, type, 32, PropModeReplace, memory, atoms.Length);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr XOpenDisplay(IntPtr displayName);
    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)] private static extern UIntPtr XInternAtom(IntPtr display, [MarshalAs(UnmanagedType.LPUTF8Str)] string atomName, bool onlyIfExists);
    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)] private static extern int XChangeProperty(IntPtr display, IntPtr window, UIntPtr property, UIntPtr type, int format, int mode, IntPtr data, int elementCount);
    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)] private static extern int XLowerWindow(IntPtr display, IntPtr window);
    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)] private static extern int XRaiseWindow(IntPtr display, IntPtr window);
    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)] private static extern int XFlush(IntPtr display);
}
