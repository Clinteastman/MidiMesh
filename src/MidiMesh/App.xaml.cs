using Microsoft.UI.Xaml;
using Microsoft.Windows.System.Power;
using MidiMesh.Appearance;
using MidiMesh.Midi;
using MidiMesh.Routing;

namespace MidiMesh;

public partial class App : Application
{
    private static MainWindow? _window;
    private static App? _current;
    private static bool _hintShown;
    private MidiEngine? _engine;
    private TrayIcon? _tray;

    public App()
    {
        _current = this;
        InitializeComponent();
        AppearanceManager.LoadAtStart(this);
        UnhandledException += (_, e) => Log.Info($"Unhandled: {e.Exception}");
    }

    public static string IconPath => Path.Combine(AppContext.BaseDirectory, "Assets", "MidiMesh.ico");

    /// <summary>True once Quit was chosen; until then closing the window only hides it.</summary>
    public static bool Quitting { get; private set; }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var engine = _engine = new MidiEngine();
        AppearanceManager.ApplyAccentColours(); // Before the window, so its controls start in the right colour.
        _window = new MainWindow(engine);

        // Sleep can leave ports looking open but silent, so reopen everything on wake.
        PowerManager.SystemSuspendStatusChanged += (_, _) =>
        {
            var status = PowerManager.SystemSuspendStatus;
            Log.Info($"Power: {status}");
            if (status is SystemSuspendStatus.AutoResume or SystemSuspendStatus.ManualResume)
            {
                _ = engine.ReconnectAllAsync("woke from sleep");
            }
        };

        _tray = new TrayIcon(AppInfo.Name, IconPath,
        [
            ($"Open {AppInfo.Name}", ShowRunningWindow),
            ("Panic (all notes off)", () => engine.Panic()),
            ("Reconnect all", () => _ = engine.ReconnectAllAsync("tray menu")),
            (null, null),
            ("Quit", Quit),
        ]);
        _tray.Open += ShowRunningWindow;
        AppearanceManager.Attach(_window, path => _tray?.SetIcon(path));

        Log.Info($"{AppInfo.Name} {AppInfo.Version} started.");
        engine.Start(RouteStore.Load());
        StartUpdateChecks();

        // Started by Windows at sign-in: stay in the tray.
        if (Environment.GetCommandLineArgs().Contains(StartupRegistration.TrayArgument)) return;

        // Open once the devices are known, so the window opens at its final size.
        await Task.WhenAny(engine.InitialScan, Task.Delay(TimeSpan.FromSeconds(3)));
        _window.ShowAndFocus();
    }

    /// <summary>Called when a second copy is launched or the tray icon is clicked: bring the window forward.</summary>
    public static void ShowRunningWindow() =>
        _window?.DispatcherQueue.TryEnqueue(() => _window.ShowAndFocus());

    /// <summary>The first time the window is closed to the tray, say that routing carries on.</summary>
    public static void NotifyHiddenToTray()
    {
        if (_current?._tray is not { } tray || _hintShown) return;
        _hintShown = true;
        tray.ShowBalloon($"{AppInfo.Name} is still routing", "It's in the tray. Right-click the icon to quit.");
    }

    /// <summary>
    /// Quiet update check a minute after start, then once a day, since the app can sit in the tray
    /// for weeks. A found update downloads in the background and then waits for the user to restart.
    /// </summary>
    private void StartUpdateChecks()
    {
        if (!Updater.IsInstalled) return;
        Updater.UpdateReady += version => _window?.DispatcherQueue.TryEnqueue(() =>
        {
            _window.ShowUpdateReady(version);
            _tray?.ShowBalloon($"{AppInfo.Name} {version} is ready", $"Open {AppInfo.Name} and click Restart now when it suits you.");
        });
        _ = Task.Run(async () =>
        {
            await Task.Delay(StartupCheckDelay);
            while (!Quitting && Updater.ReadyVersion is null)
            {
                await Updater.CheckAndDownloadAsync();
                await Task.Delay(TimeSpan.FromHours(24));
            }
        });
    }

    /// <summary>A minute, so start-up and the device scan come first. MIDIMESH_UPDATE_DELAY (seconds) shortens it for tests.</summary>
    private static TimeSpan StartupCheckDelay =>
        int.TryParse(Environment.GetEnvironmentVariable("MIDIMESH_UPDATE_DELAY"), out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromMinutes(1);

    public static void Quit()
    {
        if (Quitting) return;
        Quitting = true;
        Log.Info("Quit.");
        var app = _current;
        app?._tray?.Dispose();
        app?._engine?.Dispose();
        _window?.Close();
        app?.Exit();
    }
}
