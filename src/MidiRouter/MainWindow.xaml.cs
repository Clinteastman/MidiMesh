using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using MidiRouter.Midi;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;

namespace MidiRouter;

/// <summary>
/// The routing grid: inputs as rows, outputs as columns, a tick box where they meet.
/// Closing the window hides it to the tray; routing only stops on Quit.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const double CellWidth = 112;
    private const double RowHeight = 44;
    private const double LightSize = 10;
    private const long LightHoldMs = 150;
    private const double MinWindowWidth = 560;

    private readonly MidiEngine _engine;
    private readonly DispatcherQueueTimer _lightTimer;
    private readonly List<(MidiEndpoint Endpoint, Ellipse Light)> _lights = [];
    private readonly Brush _litBrush = (Brush)Application.Current.Resources["LightOnBrush"];
    private readonly Brush _idleBrush = (Brush)Application.Current.Resources["LightIdleBrush"];
    private readonly Brush _warningBrush = (Brush)Application.Current.Resources["WarningTextBrush"];
    private readonly Brush _offlineFill = new SolidColorBrush(Colors.Transparent);
    private readonly Brush _offlineStroke = new SolidColorBrush(Color.FromArgb(120, 160, 160, 160));
    private readonly Brush _stripeBrush = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255));
    private bool _rebuildQueued;
    private string _layoutKey = "";
    private bool _shownOnce;
    private bool _sizingInCode;
    private bool _userSized;

    public MainWindow(MidiEngine engine)
    {
        _engine = engine;
        InitializeComponent();

        // The app draws its own dark title bar; Windows keeps the caption buttons.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(App.IconPath);
        TitleIcon.Source = new BitmapImage(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "MidiRouter.png")));
        var bar = AppWindow.TitleBar;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = Colors.White;
        bar.ButtonInactiveForegroundColor = Color.FromArgb(255, 140, 140, 140);
        bar.ButtonHoverBackgroundColor = Color.FromArgb(30, 255, 255, 255);
        bar.ButtonPressedBackgroundColor = Color.FromArgb(50, 255, 255, 255);

        AppWindow.Closing += OnClosing;
        AppWindow.Changed += (_, args) =>
        {
            // Once the user drags the edge, stop resizing the window for them.
            if (args.DidSizeChange && !_sizingInCode && _shownOnce) _userSized = true;
        };

        _engine.EndpointsChanged += QueueRebuild;
        Log.Written += line =>
        {
            if (line.Contains("Traffic:", StringComparison.Ordinal)) return;
            // Log lines start "yyyy-MM-dd HH:mm:ss.fff"; the time alone is enough here.
            var shown = line.Length > 19 ? line[11..19] + "  " + line[24..].TrimStart() : line;
            DispatcherQueue.TryEnqueue(() => StatusText.Text = shown);
        };

        _lightTimer = DispatcherQueue.CreateTimer();
        _lightTimer.Interval = TimeSpan.FromMilliseconds(40);
        _lightTimer.Tick += (_, _) => UpdateLights();
        _lightTimer.Start();
        Closed += (_, _) => _lightTimer.Stop();

        Rebuild();
    }

    /// <summary>Shows the window (from the tray, a second launch or start-up) sized to fit the grid.</summary>
    public void ShowAndFocus()
    {
        Rebuild();
        if (!_shownOnce)
        {
            _shownOnce = true;
            // First show: open off-screen, then size and centre once the content has loaded,
            // so the window never appears at the wrong size.
            AppWindow.Move(new PointInt32(-32000, -32000));
            if (Root.IsLoaded) DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, PlaceFirstTime);
            else Root.Loaded += OnFirstLoaded;
        }
        AppWindow.Show();
        Activate();
    }

    private void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Root.Loaded -= OnFirstLoaded;
        PlaceFirstTime();
    }

    private void PlaceFirstTime()
    {
        FitToContent(centre: true);
        if (AppWindow.Position.X > -30000) return;

        // Sizing could not run (content not ready): still bring the window on screen, centred.
        Log.Info("Could not size the window to the grid; centring it at its current size.");
        var work = DisplayArea.Primary.WorkArea;
        var size = AppWindow.Size;
        AppWindow.Move(new PointInt32(work.X + Math.Max(0, (work.Width - size.Width) / 2), work.Y + Math.Max(0, (work.Height - size.Height) / 2)));
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (App.Quitting) return;
        args.Cancel = true;
        sender.Hide();
        Log.Info("Window hidden to the tray; still routing.");
        App.NotifyHiddenToTray();
    }

    // ---- Toolbar ---------------------------------------------------------------------------

    private void OnPanicClick(object sender, RoutedEventArgs e) => _engine.Panic();

    private async void OnReconnectClick(object sender, RoutedEventArgs e)
    {
        ReconnectButton.IsEnabled = false;
        try
        {
            await _engine.ReconnectAllAsync("Reconnect button");
        }
        finally
        {
            ReconnectButton.IsEnabled = true;
        }
    }

    private void OnMoreOpening(object? sender, object e) => StartupItem.IsChecked = StartupRegistration.IsEnabled;

    private void OnStartupClick(object sender, RoutedEventArgs e)
    {
        try
        {
            StartupRegistration.Set(StartupItem.IsChecked);
        }
        catch (Exception ex)
        {
            Log.Info($"Could not change Start with Windows: {ex.Message}");
            StartupItem.IsChecked = StartupRegistration.IsEnabled;
        }
    }

    private void OnOpenLogClick(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppPaths.DataDir) { UseShellExecute = true });

    private void OnQuitClick(object sender, RoutedEventArgs e) => App.Quit();

    // ---- Grid ------------------------------------------------------------------------------

    /// <summary>Device events arrive on background threads, often in bursts. Rebuild once per burst.</summary>
    private void QueueRebuild()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_rebuildQueued) return;
            _rebuildQueued = true;
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                _rebuildQueued = false;
                Rebuild();
            });
        });
    }

    private void Rebuild()
    {
        // Shown: every device that is plugged in, plus any unplugged one a route still points at.
        var inputs = _engine.Inputs.Where(e => e.IsPresent || HasRoute(e)).ToList();
        var outputs = _engine.Outputs.Where(e => e.IsPresent || HasRoute(e)).ToList();
        UpdateSummary(inputs.Count, outputs.Count);

        var key = string.Join("|", inputs.Select(Describe)) + "||" + string.Join("|", outputs.Select(Describe));
        if (key == _layoutKey) return;
        _layoutKey = key;

        Matrix.Children.Clear();
        Matrix.RowDefinitions.Clear();
        Matrix.ColumnDefinitions.Clear();
        _lights.Clear();

        if (inputs.Count == 0 && outputs.Count == 0)
        {
            Matrix.Children.Add(new TextBlock
            {
                Text = "No MIDI devices found yet. Plug one in and it shows up here.",
                Opacity = 0.7,
                Margin = new Thickness(4, 8, 4, 8),
            });
            FitToContent(centre: false);
            return;
        }

        Matrix.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 180 });
        foreach (var _ in outputs) Matrix.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CellWidth) });
        Matrix.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var _ in inputs) Matrix.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });

        var corner = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 16, 10) };
        corner.Children.Add(Caption("FROM (inputs)"));
        corner.Children.Add(Caption("TO (outputs) →"));
        Matrix.Children.Add(corner);

        for (var c = 0; c < outputs.Count; c++)
        {
            var header = Label(outputs[c], vertical: true);
            Grid.SetColumn(header, c + 1);
            Matrix.Children.Add(header);
        }

        for (var r = 0; r < inputs.Count; r++)
        {
            var input = inputs[r];

            // Faint stripe behind every other row, so a long row is easy to follow across.
            if (r % 2 == 0)
            {
                var stripe = new Border { Background = _stripeBrush, CornerRadius = new CornerRadius(6) };
                Grid.SetRow(stripe, r + 1);
                Grid.SetColumnSpan(stripe, outputs.Count + 1);
                Matrix.Children.Add(stripe);
            }

            var rowLabel = Label(input, vertical: false);
            Grid.SetRow(rowLabel, r + 1);
            Matrix.Children.Add(rowLabel);

            for (var c = 0; c < outputs.Count; c++)
            {
                var output = outputs[c];
                var box = new CheckBox
                {
                    IsChecked = _engine.IsRouted(input.Name, output.Name),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    MinWidth = 0,
                    Padding = new Thickness(0),
                };
                ToolTipService.SetToolTip(box, $"{input.Name} → {output.Name}");
                AutomationProperties.SetName(box, $"Route {input.Name} to {output.Name}");
                box.Checked += (_, _) => OnRouteToggled(input.Name, output.Name, true);
                box.Unchecked += (_, _) => OnRouteToggled(input.Name, output.Name, false);
                // Centre the 20px box in the cell; CheckBox reserves room for a label otherwise.
                var cell = new Grid();
                cell.Children.Add(box);
                box.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetRow(cell, r + 1);
                Grid.SetColumn(cell, c + 1);
                Matrix.Children.Add(cell);
            }
        }
        UpdateLights();
        FitToContent(centre: false);
    }

    private void OnRouteToggled(string from, string to, bool on)
    {
        _engine.SetRoute(from, to, on);
        UpdateSummary(null, null);
    }

    private int _shownInputs, _shownOutputs;

    private void UpdateSummary(int? inputs, int? outputs)
    {
        _shownInputs = inputs ?? _shownInputs;
        _shownOutputs = outputs ?? _shownOutputs;
        var routes = _engine.RouteCount;
        SummaryText.Text = $"{Plural(_shownInputs, "input")}, {Plural(_shownOutputs, "output")}, {Plural(routes, "route")} on";
    }

    private static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";

    private bool HasRoute(MidiEndpoint endpoint)
    {
        var others = endpoint.Kind == PortKind.Input ? _engine.Outputs : _engine.Inputs;
        return others.Any(o => endpoint.Kind == PortKind.Input
            ? _engine.IsRouted(endpoint.Name, o.Name)
            : _engine.IsRouted(o.Name, endpoint.Name));
    }

    private static string Describe(MidiEndpoint e) => $"{e.Name}:{e.IsPresent}:{e.IsOpen}";

    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        FontSize = 11,
        Opacity = 0.5,
        CharacterSpacing = 40,
    };

    private FrameworkElement Label(MidiEndpoint endpoint, bool vertical)
    {
        var light = new Ellipse
        {
            Width = LightSize,
            Height = LightSize,
            StrokeThickness = 1,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _lights.Add((endpoint, light));

        var state = !endpoint.IsPresent ? "Unplugged" : endpoint.IsOpen ? null : "Opening…";
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = endpoint.Name,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = vertical ? 3 : 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = vertical ? CellWidth - 12 : 280,
            TextAlignment = vertical ? TextAlignment.Center : TextAlignment.Left,
            HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Left,
        });
        if (state is not null)
        {
            text.Children.Add(new TextBlock
            {
                Text = state,
                FontSize = 11,
                Foreground = _warningBrush,
                HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            });
        }

        var panel = new StackPanel
        {
            Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
            Spacing = vertical ? 8 : 10,
            Opacity = endpoint.IsOpen ? 1.0 : 0.55,
            VerticalAlignment = vertical ? VerticalAlignment.Bottom : VerticalAlignment.Center,
            HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            Margin = vertical ? new Thickness(4, 0, 4, 10) : new Thickness(10, 0, 16, 0),
        };
        if (vertical)
        {
            panel.Children.Add(text);
            panel.Children.Add(light);
        }
        else
        {
            light.Margin = new Thickness(0, 2, 0, 0);
            panel.Children.Add(light);
            panel.Children.Add(text);
        }
        return panel;
    }

    private void UpdateLights()
    {
        if (!AppWindow.IsVisible) return;
        var now = Environment.TickCount64;
        foreach (var (endpoint, light) in _lights)
        {
            if (!endpoint.IsOpen)
            {
                light.Fill = _offlineFill;
                light.Stroke = _offlineStroke;
                continue;
            }
            var lit = endpoint.LastActivity != 0 && now - endpoint.LastActivity < LightHoldMs;
            light.Fill = lit ? _litBrush : _idleBrush;
            light.Stroke = lit ? _litBrush : _idleBrush;
        }
    }

    // ---- Window size -----------------------------------------------------------------------

    /// <summary>
    /// Sizes the window to fit the grid: small for two devices, big for many, never bigger than
    /// 90% of the screen (the grid scrolls past that). Stops once the user resizes by hand.
    /// </summary>
    private void FitToContent(bool centre)
    {
        if (!_shownOnce || (_userSized && !centre) || Root.XamlRoot is null) return;
        var scale = Root.XamlRoot.RasterizationScale;

        Matrix.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // Card margin 2x20, padding 2x16, border 2x1, plus room for a vertical scroll bar.
        var widthDip = Math.Max(MinWindowWidth, Matrix.DesiredSize.Width + 40 + 32 + 2 + 16);
        Root.Measure(new Size(widthDip, double.PositiveInfinity));
        var heightDip = Root.DesiredSize.Height + 4;

        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        if (AppWindow.Position.X <= -30000) display = DisplayArea.Primary;
        var work = display.WorkArea;
        var width = (int)Math.Min(Math.Ceiling(widthDip * scale), work.Width * 0.9);
        var height = (int)Math.Min(Math.Ceiling(heightDip * scale), work.Height * 0.9);

        _sizingInCode = true;
        try
        {
            AppWindow.ResizeClient(new SizeInt32(width, height));
            // With the title bar drawn by the app, ResizeClient overshoots by the height of the
            // system title bar it no longer has. Correct by what the client area actually got.
            var client = AppWindow.ClientSize;
            if (client.Width != width || client.Height != height)
            {
                var outer = AppWindow.Size;
                AppWindow.Resize(new SizeInt32(outer.Width + width - client.Width, outer.Height + height - client.Height));
            }
            var size = AppWindow.Size;
            var pos = AppWindow.Position;
            if (centre)
            {
                pos = new PointInt32(work.X + (work.Width - size.Width) / 2, work.Y + (work.Height - size.Height) / 2);
            }
            else
            {
                // Keep the whole window on screen after it grows.
                pos.X = Math.Clamp(pos.X, work.X, Math.Max(work.X, work.X + work.Width - size.Width));
                pos.Y = Math.Clamp(pos.Y, work.Y, Math.Max(work.Y, work.Y + work.Height - size.Height));
            }
            AppWindow.Move(pos);
        }
        finally
        {
            _sizingInCode = false;
        }
    }
}
