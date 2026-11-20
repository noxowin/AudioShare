using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AudioShare.App;

/// <summary>让系统标题栏和窗口内容融为一体：深色标题栏，Windows 11 上标题栏颜色与背景一致。</summary>
public static class WindowTheme
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWCP_ROUND = 2;

    // 与 Themes/Dark.xaml 里的 WindowBrush (#191A1F) 一致，COLORREF 是 0x00BBGGRR
    private const int CaptionColor = 0x001F1A19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window)
    {
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) ApplyNow(window);
        else window.SourceInitialized += (_, _) => ApplyNow(window);
    }

    private static void ApplyNow(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        // 旧版本系统不支持的属性会返回错误码，忽略即可
        int on = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
        int round = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        int caption = CaptionColor;
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
    }
}
