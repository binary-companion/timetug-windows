using TimeTug.Presentation.Startup;
using TimeTug.Presentation.Tests.Tug;

namespace TimeTug.Presentation.Tests.Startup;

public sealed class QuitSequenceTests
{
    private readonly List<string> _log = [];
    private readonly RecordingTugDiagnostics _diagnostics = new();
    private TaskCompletionSource? _takeoverGate;
    private Exception? _flyoutFails;
    private Exception? _takeoverFails;
    private Exception? _trayFails;

    private QuitSequence Create() => new(
        () =>
        {
            _log.Add("flyout");
            if (_flyoutFails is { } e)
            {
                throw e;
            }
        },
        async () =>
        {
            _log.Add("takeover:begin");
            if (_takeoverGate is { } gate)
            {
                await gate.Task;
            }

            _log.Add("takeover:end");
            if (_takeoverFails is { } e)
            {
                throw e;
            }
        },
        () =>
        {
            _log.Add("tray");
            if (_trayFails is { } e)
            {
                throw e;
            }
        },
        () =>
        {
            _log.Add("engine");
            return Task.CompletedTask;
        },
        _diagnostics);

    [Fact]
    public async Task Quit_closes_the_flyout_then_the_takeover_then_the_tray_icon_then_the_engine()
    {
        await Create().RunAsync();

        _log.Should().Equal("flyout", "takeover:begin", "takeover:end", "tray", "engine");
    }

    [Fact]
    public async Task The_tray_icon_and_the_engine_wait_for_the_takeover_to_be_closed()
    {
        _takeoverGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = Create().RunAsync();

        await Task.Delay(50);
        _log.Should().Equal("flyout", "takeover:begin");
        run.IsCompleted.Should().BeFalse();

        _takeoverGate.SetResult();
        await run;
        _log.Should().Equal("flyout", "takeover:begin", "takeover:end", "tray", "engine");
    }

    [Fact]
    public async Task The_flyout_is_closed_before_the_takeover_is_touched()
    {
        _takeoverGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = Create().RunAsync();

        _log.Should().StartWith(["flyout", "takeover:begin"]);

        _takeoverGate.SetResult();
        await run;
    }

    [Fact]
    public async Task A_second_quit_does_not_run_the_steps_again()
    {
        var sequence = Create();

        await Task.WhenAll(sequence.RunAsync(), sequence.RunAsync());
        await sequence.RunAsync();

        _log.Should().Equal("flyout", "takeover:begin", "takeover:end", "tray", "engine");
    }

    [Fact]
    public async Task A_step_that_fails_is_recorded_and_the_rest_still_run()
    {
        _flyoutFails = new InvalidOperationException("the window is gone");
        _takeoverFails = new TimeoutException("the engine did not answer");
        _trayFails = new InvalidOperationException("no taskbar");

        await Create().RunAsync();

        _log.Should().Equal("flyout", "takeover:begin", "takeover:end", "tray", "engine");
        _diagnostics.Entries.Where(e => e.Level == "error").Select(e => e.Message).Should().Equal(
            "closing the flyout raised InvalidOperationException",
            "closing the outstanding takeover raised TimeoutException",
            "closing the tray icon raised InvalidOperationException");
    }

    [Fact]
    public async Task A_failure_message_never_reaches_the_log()
    {
        _flyoutFails = new InvalidOperationException("Quarterly planning with Alice");

        await Create().RunAsync();

        _diagnostics.Entries.Should().NotContain(e => e.Message.Contains("Alice", StringComparison.Ordinal));
    }
}
