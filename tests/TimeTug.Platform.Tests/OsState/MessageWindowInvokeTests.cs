using TimeTug.Platform.OsState;

namespace TimeTug.Platform.Tests.OsState;

public class MessageWindowInvokeTests
{
    [Fact]
    public async Task Work_runs_on_the_windows_own_thread_every_time()
    {
        using var window = new MessageWindow();

        var first = await window.InvokeAsync(() => Environment.CurrentManagedThreadId);
        var second = await window.InvokeAsync(() => Environment.CurrentManagedThreadId);

        first.Should().Be(second);
        first.Should().NotBe(Environment.CurrentManagedThreadId);
    }

    [Fact]
    public async Task The_result_comes_back_and_calls_run_in_order()
    {
        using var window = new MessageWindow();
        var order = new List<int>();

        var tasks = Enumerable.Range(0, 20).Select(i => window.InvokeAsync(() =>
        {
            order.Add(i);
            return i * 2;
        })).ToList();
        var results = await Task.WhenAll(tasks);

        results.Should().Equal(Enumerable.Range(0, 20).Select(i => i * 2));
        order.Should().Equal(Enumerable.Range(0, 20));
    }

    [Fact]
    public async Task An_exception_in_the_work_reaches_the_caller_and_the_window_keeps_working()
    {
        using var window = new MessageWindow();

        var failing = () => window.InvokeAsync<int>(() => throw new InvalidOperationException("boom"));

        await failing.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        (await window.InvokeAsync(() => 7)).Should().Be(7);
    }

    [Fact]
    public async Task A_window_that_is_gone_refuses_work()
    {
        var window = new MessageWindow();
        window.Dispose();

        var late = () => window.InvokeAsync(() => 1);

        await late.Should().ThrowAsync<ObjectDisposedException>();
    }
}
