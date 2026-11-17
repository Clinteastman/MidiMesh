using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using MidiRouter.Midi;
using Windows.Graphics;
using Windows.UI;

namespace MidiRouter;

/// <summary>The routing grid: inputs as rows, outputs as columns, a tick box where they meet.</summary>
public sealed partial class MainWindow : Window
{
    private const double CellWidth = 120;
    private const double LightSize = 10;
    private const long LightHoldMs = 150;

    private static readonly Color LitColor = Color.FromArgb(255, 80, 220, 110);
    private static readonly Color IdleColor = Color.FromArgb(255, 70, 80, 75);

    private readonly MidiEngine _engine;
    private readonly DispatcherQueueTimer _lightTimer;
    private readonly List<(MidiEndpoint Endpoint, Ellipse Light)> _lights = [];
    private readonly SolidColorBrush _litBrush = new(LitColor);
    private readonly SolidColorBrush _idleBrush = new(IdleColor);
    private readonly SolidColorBrush _offlineBrush = new(Colors.Transparent);
    private bool _rebuildQueued;
    private string _layoutKey = "";

    public MainWindow(MidiEngine engine)
    {
        _engine = engine;
        InitializeComponent();
        ExtendsContentIntoTitleBar = false;
        AppWindow.Resize(new SizeInt32(960, 600));

        _engine.EndpointsChanged += QueueRebuild;
        Log.Written += line => DispatcherQueue.TryEnqueue(() => StatusText.Text = line);

        _lightTimer = DispatcherQueue.CreateTimer();
        _lightTimer.Interval = TimeSpan.FromMilliseconds(40);
        _lightTimer.Tick += (_, _) => UpdateLights();
        _lightTimer.Start();

        Closed += (_, _) => _lightTimer.Stop();
        Rebuild();
    }

    public void ShowAndFocus()
    {
        AppWindow.Show();
        Activate();
    }

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

        var key = string.Join("|", inputs.Select(Describe)) + "||" + string.Join("|", outputs.Select(Describe));
        if (key == _layoutKey) return;
        _layoutKey = key;

        Matrix.Children.Clear();
        Matrix.RowDefinitions.Clear();
        Matrix.ColumnDefinitions.Clear();
        _lights.Clear();

        if (inputs.Count == 0 && outputs.Count == 0)
        {
            Matrix.Children.Add(new TextBlock { Text = "No MIDI devices found yet. Plug one in.", Opacity = 0.7 });
            return;
        }

        Matrix.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (var _ in outputs) Matrix.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CellWidth) });
        Matrix.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var _ in inputs) Matrix.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });

        var corner = new TextBlock
        {
            Text = "In \\ Out",
            Opacity = 0.5,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 16, 8),
        };
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
            var rowLabel = Label(input, vertical: false);
            Grid.SetRow(rowLabel, r + 1);
            Matrix.Children.Add(rowLabel);

            for (var c = 0; c < outputs.Count; c++)
            {
                var output = outputs[c];
                var cell = new Border
                {
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
                };
                var box = new CheckBox
                {
                    IsChecked = _engine.IsRouted(input.Name, output.Name),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    MinWidth = 0,
                    Padding = new Thickness(0),
                };
                ToolTipService.SetToolTip(box, $"{input.Name} to {output.Name}");
                AutomationProperties.SetName(box, $"Route {input.Name} to {output.Name}");
                box.Checked += (_, _) => _engine.SetRoute(input.Name, output.Name, true);
                box.Unchecked += (_, _) => _engine.SetRoute(input.Name, output.Name, false);
                cell.Child = box;
                Grid.SetRow(cell, r + 1);
                Grid.SetColumn(cell, c + 1);
                Matrix.Children.Add(cell);
            }
        }
        UpdateLights();
    }

    private bool HasRoute(MidiEndpoint endpoint)
    {
        var others = endpoint.Kind == PortKind.Input ? _engine.Outputs : _engine.Inputs;
        return others.Any(o => endpoint.Kind == PortKind.Input
            ? _engine.IsRouted(endpoint.Name, o.Name)
            : _engine.IsRouted(o.Name, endpoint.Name));
    }

    private static string Describe(MidiEndpoint e) => $"{e.Name}:{e.IsPresent}:{e.IsOpen}";

    private FrameworkElement Label(MidiEndpoint endpoint, bool vertical)
    {
        var light = new Ellipse
        {
            Width = LightSize,
            Height = LightSize,
            StrokeThickness = 1.5,
            Stroke = _idleBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _lights.Add((endpoint, light));

        var state = !endpoint.IsPresent ? "unplugged" : endpoint.IsOpen ? null : "opening...";
        var name = new StackPanel { Spacing = 0 };
        name.Children.Add(new TextBlock
        {
            Text = endpoint.Name,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = vertical ? CellWidth - 28 : 260,
            TextAlignment = vertical ? TextAlignment.Center : TextAlignment.Left,
        });
        if (state is not null)
        {
            name.Children.Add(new TextBlock
            {
                Text = state,
                FontSize = 11,
                Opacity = 0.7,
                TextAlignment = vertical ? TextAlignment.Center : TextAlignment.Left,
            });
        }

        var panel = new StackPanel
        {
            Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
            Spacing = 8,
            Opacity = endpoint.IsOpen ? 1.0 : 0.5,
            VerticalAlignment = vertical ? VerticalAlignment.Bottom : VerticalAlignment.Center,
            HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            Margin = vertical ? new Thickness(4, 0, 4, 8) : new Thickness(0, 0, 16, 0),
        };
        if (vertical)
        {
            light.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(name);
            panel.Children.Add(light);
        }
        else
        {
            panel.Children.Add(light);
            panel.Children.Add(name);
        }
        ToolTipService.SetToolTip(panel, endpoint.Kind == PortKind.Input ? "Input" : "Output");
        return panel;
    }

    private void UpdateLights()
    {
        var now = Environment.TickCount64;
        foreach (var (endpoint, light) in _lights)
        {
            var lit = endpoint.IsOpen && endpoint.LastActivity != 0 && now - endpoint.LastActivity < LightHoldMs;
            light.Fill = lit ? _litBrush : endpoint.IsOpen ? _idleBrush : _offlineBrush;
        }
    }
}
