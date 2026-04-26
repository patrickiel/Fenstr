using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using static Fenstr.AppInterop;

namespace Fenstr;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "Fenstr.SingleInstance.{8F3B2A1C-5E4D-4C7B-9A6F-1D2E3F4A5B6C}";
    private const string QuitEventName = "Fenstr.Quit.{8F3B2A1C-5E4D-4C7B-9A6F-1D2E3F4A5B6C}";

    private readonly Mutex? _singleInstance;
    private readonly EventWaitHandle? _quitEvent;
    private RegisteredWaitHandle? _quitRegistration;
    private TaskbarIcon? _trayIcon;
    private nint _trayIconHandle;
    private readonly Dictionary<string, OverlayWindow> _overlays = [];
    private readonly Dictionary<string, GridPreviewWindow> _gridPreviews = [];
    private readonly Dictionary<string, KeyboardSelectionWindow> _selectionWindows = [];
    private MainWindow? _settingsWindow;
    private HelpWindow? _helpWindow;

    internal Config? Cfg { get; set; }

    public App()
    {
        var quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName, out var eventCreatedNew);
        if (!eventCreatedNew)
        {
            quitEvent.Set();
        }

        var singleInstance = new Mutex(false, SingleInstanceMutexName);
        bool owned;
        try
        {
            owned = singleInstance.WaitOne(TimeSpan.FromSeconds(5));
        }
        catch (AbandonedMutexException)
        {
            owned = true;
        }
        if (!owned)
        {
            singleInstance.Dispose();
            quitEvent.Dispose();
            Environment.Exit(1);
            return;
        }

        _singleInstance = singleInstance;
        _quitEvent = quitEvent;

        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        Cfg = Config.Load();
        var monitors = MonitorInfo.EnumerateMonitors();
        if (monitors.Count == 0)
        {
            Environment.Exit(1);
            return;
        }

        var regions = MonitorInfo.ComputeRegions(monitors, Cfg);

        foreach (var m in monitors)
        {
            _overlays[m.Id] = OverlayWindow.Create(m.Id, m.Bounds);
            _gridPreviews[m.Id] = GridPreviewWindow.Create(m.Id, m.Bounds);
            _selectionWindows[m.Id] = KeyboardSelectionWindow.Create(m.Id, m.Bounds);
        }

        _quitRegistration = ThreadPool.RegisterWaitForSingleObject(
            _quitEvent!,
            (_, _) => dispatcherQueue.TryEnqueue(() => { SnapGuard.Restore(); Application.Current.Exit(); }),
            state: null,
            millisecondsTimeOutInterval: -1,
            executeOnlyOnce: true);

        SetupTrayIcon(dispatcherQueue);

        void OnThemeChanged()
        {
            var next = LoadTrayIcon();
            if (next == 0) return;
            var prev = _trayIconHandle;
            _trayIconHandle = next;
            _trayIcon?.Icon = System.Drawing.Icon.FromHandle(next);
            if (prev != 0) DestroyIcon(prev);
        }
        OverlayWindow.ImmersiveColorChanged += OnThemeChanged;

        SnapGuard.RecoverFromPriorCrash();
        SnapGuard.Disable();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Cleanup(OnThemeChanged);

        DragTracker.Start(regions, monitors, _overlays, _gridPreviews, Cfg);
        KeyboardHook.Start(regions, monitors, Cfg, _selectionWindows);

        if (Cfg.AutoUpdateEnabled)
            UpdateChecker.CheckInBackground(dispatcherQueue);
    }

    private void SetupTrayIcon(DispatcherQueue dispatcherQueue)
    {
        _trayIconHandle = LoadTrayIcon();

        var settingsItem = new MenuFlyoutItem { Text = "Settings" };
        settingsItem.Click += (_, _) => ShowSettings();

        var helpItem = new MenuFlyoutItem { Text = "Help" };
        helpItem.Click += (_, _) => ShowHelp();

        var exitItem = new MenuFlyoutItem { Text = "Exit" };
        exitItem.Click += (_, _) => dispatcherQueue.TryEnqueue(() => { SnapGuard.Restore(); Application.Current.Exit(); });

        var iconHandle = _trayIconHandle != 0 ? _trayIconHandle : LoadIcon(0, IDI_APPLICATION);
        _trayIcon = new TaskbarIcon
        {
            Icon = System.Drawing.Icon.FromHandle(iconHandle),
            ToolTipText = "Fenstr",
            ContextMenuMode = ContextMenuMode.SecondWindow,
            ContextFlyout = new MenuFlyout
            {
                Items = { settingsItem, helpItem, exitItem }
            }
        };
        _trayIcon.ForceCreate();
    }

    private void ShowSettings()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new MainWindow();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Activate();
    }

    private void ShowHelp()
    {
        if (_helpWindow != null)
        {
            _helpWindow.Activate();
            return;
        }

        _helpWindow = new HelpWindow();
        _helpWindow.Closed += (_, _) => _helpWindow = null;
        _helpWindow.Activate();
    }

    private void Cleanup(Action onThemeChanged)
    {
        SnapGuard.Restore();

        KeyboardHook.Stop();
        DragTracker.Stop();
        OverlayWindow.ImmersiveColorChanged -= onThemeChanged;
        foreach (var o in _overlays.Values) o.Destroy();
        foreach (var g in _gridPreviews.Values) g.Destroy();
        foreach (var s in _selectionWindows.Values) s.Destroy();
        try { _trayIcon?.Dispose(); } catch (System.Runtime.InteropServices.COMException) { }
        if (_trayIconHandle != 0) DestroyIcon(_trayIconHandle);

        if (_quitRegistration != null && _quitEvent != null)
            _quitRegistration.Unregister(_quitEvent);
        _quitEvent?.Dispose();
        try { _singleInstance?.ReleaseMutex(); } catch (ApplicationException) { }
        _singleInstance?.Dispose();
    }

    private static nint LoadTrayIcon()
    {
        var lightTaskbar = Registry.GetValue(
            @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "SystemUsesLightTheme", 0) as int? == 1;
        var variant = lightTaskbar ? "tray-light.ico" : "tray-dark.ico";
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", variant);
        return File.Exists(path)
            ? LoadImage(0, path, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE)
            : 0;
    }
}
