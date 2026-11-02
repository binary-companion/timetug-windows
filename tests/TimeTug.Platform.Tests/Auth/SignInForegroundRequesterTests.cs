using TimeTug.Platform.Auth;
using TimeTug.Platform.Broker;

namespace TimeTug.Platform.Tests.Auth;

public class SignInForegroundRequesterTests
{
    private sealed class FakeApi(bool refuse = false) : IForegroundWindowApi
    {
        public List<string> Calls { get; } = [];

        public HashSet<nint> Minimized { get; } = [];

        public HashSet<nint> Destroyed { get; } = [];

        public bool IsWindow(nint window)
        {
            Calls.Add($"exists {window}");
            return !Destroyed.Contains(window);
        }

        public bool IsIconic(nint window)
        {
            Calls.Add($"iconic {window}");
            return Minimized.Contains(window);
        }

        public void Restore(nint window) => Calls.Add($"restore {window}");

        public bool SetForeground(nint window)
        {
            Calls.Add($"foreground {window}");
            return !refuse;
        }
    }

    private static BrokerParentWindow Windows(params nint[] open)
    {
        var windows = new BrokerParentWindow(() => 999);   // 999 is the hidden fallback window
        foreach (var handle in open)
        {
            windows.Register(handle);
        }

        return windows;
    }

    [Fact]
    public void The_requester_does_nothing_with_no_open_window()
    {
        var api = new FakeApi();
        new SignInForegroundRequester(Windows(), api).SignInReturned();
        api.Calls.Should().BeEmpty();
    }

    [Fact]
    public void The_requester_never_uses_the_hidden_fallback_window()
    {
        var windows = Windows();
        windows.OpenWindowHandle.Should().BeNull();
        windows.Handle.Should().Be((nint)999, "the broker still parents its dialog to the fallback");
        var api = new FakeApi();
        new SignInForegroundRequester(windows, api).SignInReturned();
        api.Calls.Should().NotContain(c => c.EndsWith(" 999"));
    }

    [Fact]
    public void The_requester_activates_a_window_that_is_not_minimized_without_restoring_it()
    {
        var api = new FakeApi();
        new SignInForegroundRequester(Windows(10), api).SignInReturned();
        api.Calls.Should().Equal("exists 10", "iconic 10", "foreground 10");
    }

    [Fact]
    public void The_requester_restores_a_minimized_window_then_activates()
    {
        var api = new FakeApi();
        api.Minimized.Add(10);
        new SignInForegroundRequester(Windows(10), api).SignInReturned();
        api.Calls.Should().Equal("exists 10", "iconic 10", "restore 10", "foreground 10");
    }

    [Fact]
    public void The_requester_leaves_a_window_alone_that_was_destroyed_before_the_call()
    {
        var api = new FakeApi();
        api.Destroyed.Add(10);
        new SignInForegroundRequester(Windows(10), api).SignInReturned();
        api.Calls.Should().Equal("exists 10");
    }

    [Fact]
    public void Touching_an_older_window_makes_it_the_one_raised()
    {
        var windows = Windows(10, 20);
        windows.OpenWindowHandle.Should().Be((nint)20);
        windows.Touch(10);
        windows.OpenWindowHandle.Should().Be((nint)10);
        windows.Handle.Should().Be((nint)10, "the Windows dialog opens in front of the active window too");

        var api = new FakeApi();
        api.Minimized.Add(10);
        new SignInForegroundRequester(windows, api).SignInReturned();
        api.Calls.Should().Equal("exists 10", "iconic 10", "restore 10", "foreground 10");
    }

    [Fact]
    public void Touching_a_window_that_is_not_registered_registers_nothing()
    {
        var windows = Windows(10);
        windows.Touch(77);
        windows.OpenWindowHandle.Should().Be((nint)10);
        windows.Unregister(10);
        windows.OpenWindowHandle.Should().BeNull();
    }

    [Fact]
    public void The_requester_survives_a_refused_foreground_call()
    {
        var log = new RecordingDiagnostics();
        var api = new FakeApi(refuse: true);
        var act = () => new SignInForegroundRequester(Windows(10), api, log).SignInReturned();
        act.Should().NotThrow();
        log.Entries.Should().ContainSingle(e => e.Category == "signin" && e.Message.Contains("foreground", StringComparison.Ordinal));
    }

    [Fact]
    public void The_requester_survives_an_api_that_throws()
    {
        var api = new ThrowingApi();
        var act = () => new SignInForegroundRequester(Windows(10), api).SignInReturned();
        act.Should().NotThrow();
    }

    private sealed class ThrowingApi : IForegroundWindowApi
    {
        public bool IsWindow(nint window) => true;

        public bool IsIconic(nint window) => throw new InvalidOperationException("boom");

        public void Restore(nint window) => throw new InvalidOperationException("boom");

        public bool SetForeground(nint window) => throw new InvalidOperationException("boom");
    }
}
