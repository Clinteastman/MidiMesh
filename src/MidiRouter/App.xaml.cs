using Microsoft.UI.Xaml;
using Microsoft.Windows.System.Power;
using MidiRouter.Midi;
using MidiRouter.Routing;

namespace MidiRouter;

public partial class App : Application
{
    private static MainWindow? _window;
    private MidiEngine? _engine;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => Log.Info($"Unhandled: {e.Exception}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _engine = new MidiEngine();
        _window = new MainWindow(_engine);
        _window.Closed += (_, _) =>
        {
            Log.Info("Window closed; stopping.");
            _engine.Dispose();
        };
        _window.Activate();

        // Sleep can leave ports looking open but silent, so reopen everything on wake.
        PowerManager.SystemSuspendStatusChanged += (_, _) =>
        {
            var status = PowerManager.SystemSuspendStatus;
            Log.Info($"Power: {status}");
            if (status is SystemSuspendStatus.AutoResume or SystemSuspendStatus.ManualResume)
            {
                _ = _engine.ReconnectAllAsync("woke from sleep");
            }
        };

        _engine.Start(RouteStore.Load());
    }

    /// <summary>Called when a second copy is launched: bring this window forward.</summary>
    public static void ShowRunningWindow() =>
        _window?.DispatcherQueue.TryEnqueue(() => _window.ShowAndFocus());
}
