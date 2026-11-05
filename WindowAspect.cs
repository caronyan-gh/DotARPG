using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DotARPG;

internal static class WindowAspect
{
    internal const double ContentWidth = 1328, ContentHeight = 768;
    private const double Ratio = ContentWidth / ContentHeight;

    internal static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            nint handle = new WindowInteropHelper(window).Handle;
            HwndSource.FromHwnd(handle)?.AddHook((nint hwnd, int message, nint edge, nint rectangle, ref bool handled) =>
            {
                if (message != 0x0214 || window.WindowState != WindowState.Normal) return 0; // WM_SIZING
                GetWindowRect(hwnd, out var outer); GetClientRect(hwnd, out var client);
                int frameWidth = outer.Right - outer.Left - client.Right;
                int frameHeight = outer.Bottom - outer.Top - client.Bottom;
                var proposed = Marshal.PtrToStructure<PixelRect>(rectangle);
                var dpi = VisualTreeHelper.GetDpi(window);
                double minimumWidth = Math.Max(window.MinWidth * dpi.DpiScaleX - frameWidth,
                    (window.MinHeight * dpi.DpiScaleY - frameHeight) * Ratio);
                double width = (int)edge is 3 or 6
                    ? (proposed.Bottom - proposed.Top - frameHeight) * Ratio
                    : proposed.Right - proposed.Left - frameWidth;
                int targetWidth = (int)Math.Round(Math.Max(minimumWidth, width)) + frameWidth;
                int targetHeight = (int)Math.Round((targetWidth - frameWidth) / Ratio) + frameHeight;
                if ((int)edge is 1 or 4 or 7) proposed.Left = proposed.Right - targetWidth;
                else proposed.Right = proposed.Left + targetWidth;
                if ((int)edge is 3 or 4 or 5) proposed.Top = proposed.Bottom - targetHeight;
                else proposed.Bottom = proposed.Top + targetHeight;
                Marshal.StructureToPtr(proposed, rectangle, false);
                handled = true;
                return 1;
            });
            GetWindowRect(handle, out var initialOuter); GetClientRect(handle, out var initialClient);
            var scale = VisualTreeHelper.GetDpi(window);
            double frameX = (initialOuter.Right - initialOuter.Left - initialClient.Right) / scale.DpiScaleX;
            double frameY = (initialOuter.Bottom - initialOuter.Top - initialClient.Bottom) / scale.DpiScaleY;
            double clientWidth = Math.Min(window.Width - frameX,
                (SystemParameters.WorkArea.Height * .94 - frameY) * Ratio);
            window.Width = clientWidth + frameX;
            window.Height = clientWidth / Ratio + frameY;
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PixelRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out PixelRect rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out PixelRect rect);
}
