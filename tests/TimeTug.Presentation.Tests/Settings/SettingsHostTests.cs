using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Tests.Welcome;

namespace TimeTug.Presentation.Tests.Settings;

internal sealed class FakeSettingsWindow(SettingsShellViewModel shell) : ISettingsWindow
{
    public SettingsShellViewModel Shell { get; } = shell;

    public int Shows { get; private set; }

    public int Closes { get; private set; }

    public bool ThrowOnShow { get; set; }

    public event Action? Closed;

    public void Show()
    {
        if (ThrowOnShow)
        {
            throw new InvalidOperationException("no window");
        }

        Shows++;
    }

    public void Close()
    {
        Closes++;
        Closed?.Invoke();
    }

    public void RaiseClosedByThePerson() => Closed?.Invoke();
}

internal sealed class FakeSettingsWindowFactory : ISettingsWindowFactory
{
    public List<FakeSettingsWindow> Created { get; } = [];

    public bool ThrowOnCreate { get; set; }

    public bool ThrowOnShow { get; set; }

    public ISettingsWindow Create(SettingsShellViewModel shell)
    {
        if (ThrowOnCreate)
        {
            throw new InvalidOperationException("no window");
        }

        var window = new FakeSettingsWindow(shell) { ThrowOnShow = ThrowOnShow };
        Created.Add(window);
        return window;
    }
}

public class SettingsHostTests
{
    private readonly FakeSettingsWindowFactory _factory = new();
    private readonly List<SettingsShellViewModel> _shells = [];
    private readonly RecordingDiag _log = new();

    private SettingsHost Host() => new(
        _factory,
        () =>
        {
            var shell = new SettingsShellViewModel(new FakeEngineClient(), new SettingsText(new ReswLocalizer()), new Tug.FakeUiSettings(), new ImmediateDispatcher(), TimeProvider.System);
            _shells.Add(shell);
            return shell;
        },
        new ImmediateDispatcher(),
        _log);

    private sealed class RecordingDiag : Abstractions.IDiagnostics
    {
        public List<(string Level, string Category, string Message)> Entries { get; } = [];

        public void Record(string level, string category, string message) => Entries.Add((level, category, message));
    }

    [Fact]
    public void The_first_show_opens_one_window_on_general()
    {
        using var host = Host();

        host.Show();

        host.IsOpen.Should().BeTrue();
        _factory.Created.Should().ContainSingle().Which.Shows.Should().Be(1);
        host.Shell!.SelectedPane.Should().Be(SettingsPane.General);
    }

    [Fact]
    public void Show_opens_the_requested_pane_and_highlight_before_the_window_appears()
    {
        using var host = Host();

        host.Show(SettingsPane.Accounts, "diagnostics");

        host.Shell!.SelectedPane.Should().Be(SettingsPane.Accounts);
        host.Shell.HighlightedId.Should().Be("diagnostics");
    }

    [Fact]
    public void A_second_show_focuses_the_existing_window_instead_of_opening_another()
    {
        using var host = Host();
        host.Show();

        host.Show();
        host.Show();

        _factory.Created.Should().ContainSingle();
        _factory.Created[0].Shows.Should().Be(3);
        _shells.Should().ContainSingle();
    }

    [Fact]
    public void A_second_show_with_a_pane_moves_the_open_window_to_it()
    {
        using var host = Host();
        host.Show();

        host.Show(SettingsPane.Calendars, "skip-all-day");

        _factory.Created.Should().ContainSingle();
        host.Shell!.SelectedPane.Should().Be(SettingsPane.Calendars);
        host.Shell.HighlightedId.Should().Be("skip-all-day");
    }

    [Fact]
    public void A_second_show_with_nothing_asked_leaves_the_pane_where_the_person_was()
    {
        using var host = Host();
        host.Show(SettingsPane.TugRules);

        host.Show();

        host.Shell!.SelectedPane.Should().Be(SettingsPane.TugRules);
    }

    [Fact]
    public void After_the_person_closes_it_the_next_show_opens_a_new_window()
    {
        using var host = Host();
        host.Show(SettingsPane.Appearance);

        _factory.Created[0].RaiseClosedByThePerson();
        host.IsOpen.Should().BeFalse();
        host.Shell.Should().BeNull();
        host.Show();

        _factory.Created.Should().HaveCount(2);
        host.Shell!.SelectedPane.Should().Be(SettingsPane.General, "a new window starts on General");
    }

    [Fact]
    public void Close_closes_the_window_and_is_harmless_when_none_is_open()
    {
        using var host = Host();
        host.Close();
        host.Show();

        host.Close();

        _factory.Created[0].Closes.Should().Be(1);
        host.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void A_window_that_cannot_be_made_or_shown_is_logged_and_the_next_show_tries_again()
    {
        using var host = Host();
        _factory.ThrowOnCreate = true;
        host.Show();
        host.IsOpen.Should().BeFalse();

        _factory.ThrowOnCreate = false;
        _factory.ThrowOnShow = true;
        host.Show();
        host.IsOpen.Should().BeFalse();

        _factory.ThrowOnShow = false;
        host.Show();
        host.IsOpen.Should().BeTrue();
        _log.Entries.Should().HaveCount(2).And.OnlyContain(e => e.Level == "error" && e.Category == "settings");
    }

    [Fact]
    public void A_show_from_another_thread_is_posted_to_the_ui_thread()
    {
        var queue = new QueueingDispatcher();
        using var host = new SettingsHost(_factory, () => new SettingsShellViewModel(new FakeEngineClient(), new SettingsText(new ReswLocalizer()), new Tug.FakeUiSettings(), new ImmediateDispatcher(), TimeProvider.System), queue, _log);

        host.Show(SettingsPane.Accounts);
        host.IsOpen.Should().BeFalse("nothing happens off the ui thread");
        queue.RunAll();

        host.IsOpen.Should().BeTrue();
        host.Shell!.SelectedPane.Should().Be(SettingsPane.Accounts);
    }

    private sealed class QueueingDispatcher : Abstractions.IUiDispatcher
    {
        private readonly Queue<Action> _queue = new();
        private bool _running;

        /// <summary>False for a caller on another thread; true while the queued work runs "on the UI thread".</summary>
        public bool HasThreadAccess => _running;

        public void Post(Action action) => _queue.Enqueue(action);

        public Task InvokeAsync(Action action)
        {
            _queue.Enqueue(action);
            return Task.CompletedTask;
        }

        public void RunAll()
        {
            _running = true;
            while (_queue.TryDequeue(out var action))
            {
                action();
            }

            _running = false;
        }
    }

    [Fact]
    public void Disposing_the_host_closes_the_window_and_later_shows_do_nothing()
    {
        var host = Host();
        host.Show();

        host.Dispose();
        host.Show();

        _factory.Created.Should().ContainSingle().Which.Closes.Should().Be(1);
    }
}
