using Microsoft.UI.Xaml;
using Microsoft.Windows.System.Power;
using MidiRouter.Midi;
using MidiRouter.Routing;

namespace MidiRouter;

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
        UnhandledException += (_, e) => Log.Info($"Unhandled: {e.Exception}");
    }

    public static string IconPath => Path.Combine(AppContext.BaseDirectory, "Assets", "MidiRouter.ico");

    /// <summary>True once Quit was chosen; until then closing the window only hides it.</summary>
    public static bool Quitting { get; private set; }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var engine = _engine = new MidiEngine();
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

        _tray = new TrayIcon("MIDI Router", IconPath,
        [
            ("Open MIDI Router", ShowRunningWindow),
            ("Panic (all notes off)", () => engine.Panic()),
            ("Reconnect all", () => _ = engine.ReconnectAllAsync("tray menu")),
            (null, null),
            ("Quit", Quit),
        ]);
        _tray.Open += ShowRunningWindow;

        engine.Start(RouteStore.Load());

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
        tray.ShowBalloon("MIDI Router is still routing", "It's in the tray. Right-click the icon to quit.");
    }

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
