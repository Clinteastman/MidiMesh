using MidiRouter.Routing;
using Xunit;

namespace MidiRouter.Tests;

public class RouteFilterTests
{
    private static readonly byte[] NoteOnCh1 = [0x90, 60, 100];
    private static readonly byte[] NoteOnCh3 = [0x92, 60, 100];

    [Fact]
    public void Default_options_pass_the_same_array()
    {
        Assert.Same(NoteOnCh1, RouteFilter.Apply(RouteOptions.Default, NoteOnCh1));
    }

    [Theory]
    [InlineData(new byte[] { 0xF8 }, MessageKind.Clock)]
    [InlineData(new byte[] { 0xFA }, MessageKind.Clock)]
    [InlineData(new byte[] { 0xFC }, MessageKind.Clock)]
    [InlineData(new byte[] { 0xF2, 0, 0 }, MessageKind.Clock)]
    [InlineData(new byte[] { 0xF0, 0x7D, 1, 0xF7 }, MessageKind.SysEx)]
    [InlineData(new byte[] { 0xB0, 1, 64 }, MessageKind.ControlChange)]
    [InlineData(new byte[] { 0xE0, 0, 64 }, MessageKind.PitchBend)]
    [InlineData(new byte[] { 0xD0, 50 }, MessageKind.Aftertouch)]
    [InlineData(new byte[] { 0xA0, 60, 50 }, MessageKind.Aftertouch)]
    [InlineData(new byte[] { 0xC5, 7 }, MessageKind.ProgramChange)]
    [InlineData(new byte[] { 0x90, 60, 100 }, MessageKind.Notes)]
    [InlineData(new byte[] { 0x8F, 60, 0 }, MessageKind.Notes)]
    public void Blocked_kinds_are_dropped_and_others_kept(byte[] message, MessageKind kind)
    {
        Assert.Null(RouteFilter.Apply(new RouteOptions { Block = kind }, message));
        var everythingElse = (MessageKind)0b1111111 & ~kind;
        Assert.Same(message, RouteFilter.Apply(new RouteOptions { Block = everythingElse }, message));
    }

    [Fact]
    public void Knobs_only_route_drops_notes_but_keeps_cc()
    {
        var knobsOnly = new RouteOptions { Block = MessageKind.Notes };
        Assert.Null(RouteFilter.Apply(knobsOnly, NoteOnCh1));
        byte[] cc = [0xB0, 74, 20];
        Assert.Same(cc, RouteFilter.Apply(knobsOnly, cc));
    }

    [Fact]
    public void Tune_request_active_sensing_and_reset_always_pass()
    {
        var blockAll = new RouteOptions { Block = (MessageKind)0b1111111 };
        foreach (var status in new byte[] { 0xF6, 0xFE, 0xFF })
        {
            byte[] message = [status];
            Assert.Same(message, RouteFilter.Apply(blockAll, message));
        }
    }

    [Fact]
    public void From_channel_keeps_only_that_channel()
    {
        var onlyCh3 = new RouteOptions { FromChannel = 3 };
        Assert.Null(RouteFilter.Apply(onlyCh3, NoteOnCh1));
        Assert.Same(NoteOnCh3, RouteFilter.Apply(onlyCh3, NoteOnCh3));
    }

    [Fact]
    public void From_channel_does_not_drop_system_messages()
    {
        byte[] clock = [0xF8];
        Assert.Same(clock, RouteFilter.Apply(new RouteOptions { FromChannel = 3 }, clock));
    }

    [Fact]
    public void To_channel_rewrites_a_copy_and_leaves_the_original()
    {
        var result = RouteFilter.Apply(new RouteOptions { ToChannel = 10 }, NoteOnCh1);
        Assert.Equal(new byte[] { 0x99, 60, 100 }, result);
        Assert.Equal(0x90, NoteOnCh1[0]);
    }

    [Fact]
    public void To_channel_same_as_input_returns_original()
    {
        Assert.Same(NoteOnCh3, RouteFilter.Apply(new RouteOptions { ToChannel = 3 }, NoteOnCh3));
    }

    [Fact]
    public void Remap_applies_to_every_channel_message_kind()
    {
        var toCh2 = new RouteOptions { ToChannel = 2 };
        Assert.Equal(new byte[] { 0xB1, 1, 64 }, RouteFilter.Apply(toCh2, [0xB0, 1, 64]));
        Assert.Equal(new byte[] { 0xE1, 0, 64 }, RouteFilter.Apply(toCh2, [0xE5, 0, 64]));
        Assert.Equal(new byte[] { 0xF8 }, RouteFilter.Apply(toCh2, [0xF8]));
    }

    [Fact]
    public void From_and_to_channel_together()
    {
        var options = new RouteOptions { FromChannel = 1, ToChannel = 16 };
        Assert.Equal(new byte[] { 0x9F, 60, 100 }, RouteFilter.Apply(options, NoteOnCh1));
        Assert.Null(RouteFilter.Apply(options, NoteOnCh3));
    }

    [Fact]
    public void Empty_message_is_dropped()
    {
        Assert.Null(RouteFilter.Apply(RouteOptions.Default, []));
    }
}

public class RouteStoreTests
{
    [Fact]
    public void Old_file_without_options_loads_with_defaults()
    {
        var routes = RouteStore.Parse("""{ "version": 1, "routes": [ { "from": "KeyStep 37 mk2", "to": "M-VAVE FM-1" } ] }""");
        var (route, options) = Assert.Single(routes);
        Assert.Equal(new Route("KeyStep 37 mk2", "M-VAVE FM-1"), route);
        Assert.True(options.IsDefault);
    }

    [Fact]
    public void Options_round_trip()
    {
        var options = new RouteOptions { Block = MessageKind.SysEx | MessageKind.Clock, FromChannel = 1, ToChannel = 2 };
        var json = RouteStore.Serialize([new(new Route("A", "B"), options), new(new Route("C", "D"), RouteOptions.Default)]);
        var back = RouteStore.Parse(json).ToDictionary(r => r.Key, r => r.Value);
        Assert.Equal(options, back[new Route("A", "B")]);
        Assert.True(back[new Route("C", "D")].IsDefault);
        Assert.Contains("\"block\": \"Clock, SysEx\"", json);
        Assert.DoesNotContain("fromChannel\": null", json);
    }

    [Fact]
    public void Bad_channels_and_blank_names_are_ignored()
    {
        var routes = RouteStore.Parse("""{ "routes": [ { "from": "A", "to": "B", "fromChannel": 0, "toChannel": 17 }, { "from": "", "to": "B" } ] }""");
        var (_, options) = Assert.Single(routes);
        Assert.Null(options.FromChannel);
        Assert.Null(options.ToChannel);
    }
}
