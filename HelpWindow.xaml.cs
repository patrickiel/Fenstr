using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Fenstr;

public sealed partial class HelpWindow : Window
{
    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    public HelpWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "tray-dark.ico");
        if (System.IO.File.Exists(iconPath))
            AppWindow.SetIcon(iconPath);

        var hwnd = WindowNative.GetWindowHandle(this);
        var dpi = GetDpiForWindow(hwnd);
        var scale = dpi / 96.0;
        var w = (int)(620 * scale);
        var h = (int)(640 * scale);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h));

        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
        {
            presenter.PreferredMinimumWidth = (int)(560 * scale);
            presenter.PreferredMinimumHeight = (int)(480 * scale);
            presenter.IsMaximizable = false;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
