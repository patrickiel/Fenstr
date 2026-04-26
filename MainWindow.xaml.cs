using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Win32;
using Windows.System;
using WinRT.Interop;

namespace Fenstr;

public sealed partial class MainWindow : Window
{
    private int _capturedVk;
    private bool _capturing;
    private bool _loading;

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    public MainWindow()
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
        var w = (int)(600 * scale);
        var h = (int)(520 * scale);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h));

        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
        {
            presenter.PreferredMinimumWidth = (int)(560 * scale);
            presenter.PreferredMinimumHeight = (int)(480 * scale);
            presenter.IsMaximizable = false;
        }

        LoadFromConfig();
        LoadStartupState();
    }

    private void LoadFromConfig()
    {
        var cfg = ((App)Application.Current).Cfg;
        if (cfg == null) return;

        _loading = true;
        try
        {
            MaximizeFullScreenToggle.IsOn = cfg.MaximizeWhenFullScreen;
            MaximizeDragToggle.IsOn = cfg.MaximizeDragEnabled;
            MaximizeWidthSlider.Value = cfg.MaximizeDragWidthPercent;
            MaximizeWidthLabel.Text = cfg.MaximizeDragWidthPercent + "%";
            SetMaximizeWidthPanelEnabled(cfg.MaximizeDragEnabled);

            PlacementEnabledToggle.IsOn = cfg.PlacementHotkeyEnabled;
            SetHotkeyPanelEnabled(cfg.PlacementHotkeyEnabled);

            var hk = cfg.PlacementHotkeyOrDefault;
            WinToggle.IsChecked = hk.Win;
            CtrlToggle.IsChecked = hk.Ctrl;
            AltToggle.IsChecked = hk.Alt;
            ShiftToggle.IsChecked = hk.Shift;
            _capturedVk = hk.VkCode;
            KeyButton.Content = VkName(hk.VkCode);
        }
        finally
        {
            _loading = false;
        }
    }

    private void MaximizeFullScreenToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var cfg = ((App)Application.Current).Cfg;
        if (cfg == null) return;

        var updated = cfg with { MaximizeWhenFullScreen = MaximizeFullScreenToggle.IsOn };
        updated.Save();

        ((App)Application.Current).Cfg = updated;
        DragTracker.UpdateConfig(updated);
    }

    private void MaximizeDragToggle_Toggled(object sender, RoutedEventArgs e)
    {
        SetMaximizeWidthPanelEnabled(MaximizeDragToggle.IsOn);
        PersistMaximizeSettings();
    }

    private void MaximizeWidthSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (MaximizeWidthLabel != null)
            MaximizeWidthLabel.Text = (int)e.NewValue + "%";
        PersistMaximizeSettings();
    }

    private void SetMaximizeWidthPanelEnabled(bool enabled)
    {
        MaximizeWidthSlider.IsEnabled = enabled;
        MaximizeWidthPanel.Opacity = enabled ? 1.0 : 0.4;
    }

    private void PersistMaximizeSettings()
    {
        if (_loading) return;
        var cfg = ((App)Application.Current).Cfg;
        if (cfg == null) return;

        var updated = cfg with
        {
            MaximizeDragEnabled = MaximizeDragToggle.IsOn,
            MaximizeDragWidthPercent = (int)MaximizeWidthSlider.Value,
        };
        updated.Save();

        ((App)Application.Current).Cfg = updated;
        DragTracker.UpdateConfig(updated);
    }

    private void PlacementEnabledToggle_Toggled(object sender, RoutedEventArgs e)
    {
        SetHotkeyPanelEnabled(PlacementEnabledToggle.IsOn);
        PersistPlacementSettings();
    }

    private void HotkeyChanged(object sender, RoutedEventArgs e) => PersistPlacementSettings();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void SetHotkeyPanelEnabled(bool enabled)
    {
        WinToggle.IsEnabled = enabled;
        CtrlToggle.IsEnabled = enabled;
        AltToggle.IsEnabled = enabled;
        ShiftToggle.IsEnabled = enabled;
        KeyButton.IsEnabled = enabled;
    }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "Fenstr";

    private void LoadStartupState()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        StartupToggle.IsOn = key?.GetValue(RunValueName) != null;
        _startupLoaded = true;
    }

    private bool _startupLoaded;

    private void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_startupLoaded) return;

        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key == null) return;

        if (StartupToggle.IsOn)
        {
            var exePath = System.IO.Path.Combine(AppContext.BaseDirectory, "Fenstr.exe");
            key.SetValue(RunValueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }

    private void KeyButton_Click(object sender, RoutedEventArgs e)
    {
        _capturing = true;
        KeyButton.Content = "Press a key…";
        KeyButton.Focus(FocusState.Programmatic);
        KeyButton.PreviewKeyDown += OnKeyCaptured;
    }

    private void OnKeyCaptured(object sender, KeyRoutedEventArgs e)
    {
        if (!_capturing) return;

        var vk = (int)e.Key;
        if (e.Key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu or
            VirtualKey.LeftWindows or VirtualKey.RightWindows)
            return;

        _capturedVk = vk;
        KeyButton.Content = VkName(vk);
        _capturing = false;
        KeyButton.PreviewKeyDown -= OnKeyCaptured;
        e.Handled = true;
        PersistPlacementSettings();
    }

    private void PersistPlacementSettings()
    {
        if (_loading) return;
        var cfg = ((App)Application.Current).Cfg;
        if (cfg == null) return;

        var newHotkey = new HotkeyBinding(
            _capturedVk,
            WinToggle.IsChecked == true,
            CtrlToggle.IsChecked == true,
            AltToggle.IsChecked == true,
            ShiftToggle.IsChecked == true);

        var updated = cfg with
        {
            PlacementHotkey = newHotkey,
            PlacementHotkeyEnabled = PlacementEnabledToggle.IsOn,
        };
        updated.Save();

        ((App)Application.Current).Cfg = updated;
        KeyboardHook.UpdateConfig(updated);
        DragTracker.UpdateConfig(updated);
    }

    private static string VkName(int vk) => vk switch
    {
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x70 and <= 0x7B => "F" + (vk - 0x70 + 1),
        0x20 => "Space",
        0x09 => "Tab",
        0x1B => "Esc",
        0x0D => "Enter",
        0x08 => "Backspace",
        0x2E => "Delete",
        0xC0 => "`",
        0xBD => "-",
        0xBB => "=",
        0xDB => "[",
        0xDD => "]",
        0xDC => "\\",
        0xBA => ";",
        0xDE => "'",
        0xBC => ",",
        0xBE => ".",
        0xBF => "/",
        _ => $"Key {vk}"
    };
}
