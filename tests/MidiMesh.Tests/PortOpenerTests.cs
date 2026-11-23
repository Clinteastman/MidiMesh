using MidiMesh.Midi;
using Xunit;

namespace MidiMesh.Tests;

public class PortOpenerTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(50);

    private sealed class FakePort : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    /// <summary>A request that only answers when the test says so, counting how often it was asked.</summary>
    private sealed class SlowDevice
    {
        public int Requests { get; private set; }
        public TaskCompletionSource<IDisposable?> Answer { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IDisposable?> Request(string id)
        {
            Requests++;
            Answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return Answer.Task;
        }
    }

    [Fact]
    public async Task A_quick_answer_opens_the_port()
    {
        var port = new FakePort();
        var (outcome, opened) = await new PortOpener(Short).OpenAsync("dev", _ => Task.FromResult<IDisposable?>(port));
        Assert.Equal(OpenOutcome.Opened, outcome);
        Assert.Same(port, opened);
    }

    [Fact]
    public async Task No_port_from_Windows_is_a_failure()
    {
        var (outcome, opened) = await new PortOpener(Short).OpenAsync("dev", _ => Task.FromResult<IDisposable?>(null));
        Assert.Equal(OpenOutcome.Failed, outcome);
        Assert.Null(opened);
    }

    [Fact]
    public async Task A_request_that_never_answers_times_out_and_is_not_repeated()
    {
        var device = new SlowDevice();
        var opener = new PortOpener(Short);

        Assert.Equal(OpenOutcome.TimedOut, (await opener.OpenAsync("dev", device.Request)).Outcome);
        Assert.Equal(OpenOutcome.StillWaiting, (await opener.OpenAsync("dev", device.Request)).Outcome);
        Assert.Equal(1, device.Requests);
    }

    [Fact]
    public async Task A_late_answer_is_used_by_the_next_try()
    {
        var device = new SlowDevice();
        var opener = new PortOpener(Short);
        await opener.OpenAsync("dev", device.Request);

        var port = new FakePort();
        device.Answer.SetResult(port);
        var (outcome, opened) = await opener.OpenAsync("dev", device.Request);

        Assert.Equal(OpenOutcome.Opened, outcome);
        Assert.Same(port, opened);
        Assert.Equal(1, device.Requests);
        Assert.False(port.Disposed);
    }

    [Fact]
    public async Task A_late_answer_after_discard_is_closed()
    {
        var device = new SlowDevice();
        var opener = new PortOpener(Short);
        await opener.OpenAsync("dev", device.Request);

        opener.Discard();
        var port = new FakePort();
        device.Answer.SetResult(port);

        await WaitUntil(() => port.Disposed);
        Assert.True(port.Disposed);
    }

    [Fact]
    public async Task A_late_answer_for_another_device_is_closed_and_the_new_device_is_asked()
    {
        var device = new SlowDevice();
        var opener = new PortOpener(Short);
        await opener.OpenAsync("old", device.Request);
        var late = new FakePort();
        var pending = device.Answer;
        pending.SetResult(late);

        var fresh = new FakePort();
        var (outcome, opened) = await opener.OpenAsync("new", _ => Task.FromResult<IDisposable?>(fresh));

        Assert.Equal(OpenOutcome.Opened, outcome);
        Assert.Same(fresh, opened);
        await WaitUntil(() => late.Disposed);
        Assert.True(late.Disposed);
    }

    [Fact]
    public async Task A_late_empty_answer_lets_the_next_try_ask_again()
    {
        var device = new SlowDevice();
        var opener = new PortOpener(Short);
        await opener.OpenAsync("dev", device.Request);
        device.Answer.SetResult(null);

        var port = new FakePort();
        var (outcome, opened) = await opener.OpenAsync("dev", _ => Task.FromResult<IDisposable?>(port));

        Assert.Equal(OpenOutcome.Opened, outcome);
        Assert.Same(port, opened);
    }

    [Fact]
    public async Task A_late_failure_lets_the_next_try_ask_again()
    {
        var device = new SlowDevice();
        var opener = new PortOpener(Short);
        await opener.OpenAsync("dev", device.Request);
        device.Answer.SetException(new InvalidOperationException("gone"));

        Assert.Equal(OpenOutcome.TimedOut, (await opener.OpenAsync("dev", device.Request)).Outcome);
        Assert.Equal(2, device.Requests);
    }

    [Fact]
    public async Task A_request_that_fails_quickly_throws()
    {
        var opener = new PortOpener(Short);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            opener.OpenAsync("dev", _ => Task.FromException<IDisposable?>(new InvalidOperationException("busy"))));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(10);
    }
}
