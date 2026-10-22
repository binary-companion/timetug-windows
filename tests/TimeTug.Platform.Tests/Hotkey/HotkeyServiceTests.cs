using Microsoft.Extensions.Time.Testing;
using TimeTug.Platform.Hotkey;
using TimeTug.Platform.Tests.Instance;
using TimeTug.Presentation.Settings;

namespace TimeTug.Platform.Tests.Hotkey;

public sealed class HotkeyServiceTests : IAsyncDisposable
{
    private static readonly HotkeyBinding A = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'T');
    private static readonly HotkeyBinding B = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'Y');

    private readonly FakeRegistrar _registrar = new();
    private readonly FakeStore _store = new();
    private readonly FakeTug _tug = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero));
    private readonly RecordingDiagnostics _log = new();
    private readonly List<HotkeyService> _services = [];
    private int _presses;

    private HotkeyService Create(FakeRegistrar? registrar = null)
    {
        var service = new HotkeyService(registrar ?? _registrar, _store, _tug, new ImmediateDispatcher(), _time, _log);
        service.Pressed += () => _presses++;
        _services.Add(service);
        return service;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var service in _services)
        {
            await service.DisposeAsync();
        }
    }

    // ---- start ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task With_nothing_stored_nothing_is_registered_as_on_the_Mac()
    {
        var service = Create();

        await service.StartAsync();

        _registrar.Calls.Should().BeEmpty();
        service.State.Should().Be(HotkeyState.None);
        _store.Saves.Should().Be(0);
    }

    [Fact]
    public async Task A_stored_shortcut_is_registered_once_at_start()
    {
        _store.Current = new HotkeyState(A, HotkeyStatus.Registered, _time.GetUtcNow());
        var service = Create();

        await service.StartAsync();
        await service.StartAsync();

        _registrar.Calls.Should().Equal("register Ctrl+Alt+T");
        _registrar.Active.Should().Be(A);
        service.State.BindingIsActive.Should().BeTrue();
    }

    [Fact]
    public async Task A_stored_shortcut_that_another_program_now_holds_is_recorded_as_a_conflict_and_never_half_registered()
    {
        _store.Current = new HotkeyState(A, HotkeyStatus.Registered, _time.GetUtcNow());
        _registrar.HeldElsewhere.Add(A);
        var service = Create();

        await service.StartAsync();

        _registrar.Active.Should().BeNull();
        service.State.Binding.Should().Be(A);
        service.State.Refused.Should().Be(A);
        service.State.LastResult.Should().Be(HotkeyStatus.Conflict);
        service.State.BindingIsActive.Should().BeFalse();
        _store.Current.Should().Be(service.State, "the conflict is written down for the Settings page");
        _log.Entries.Should().Contain(e => e.Level == "warning" && e.Category == "hotkey" && e.Message.Contains("another program", StringComparison.Ordinal));
    }

    [Fact]
    public async Task When_the_other_program_lets_go_the_next_start_registers_and_clears_the_conflict()
    {
        _store.Current = new HotkeyState(A, HotkeyStatus.Conflict, _time.GetUtcNow(), A);
        var service = Create();

        await service.StartAsync();

        _registrar.Active.Should().Be(A);
        service.State.HasConflict.Should().BeFalse();
        service.State.LastResult.Should().Be(HotkeyStatus.Registered);
        _store.Current.Should().Be(service.State);
    }

    [Fact]
    public async Task A_conflict_over_a_new_shortcut_is_still_reported_after_a_restart()
    {
        _registrar.HeldElsewhere.Add(B);
        var first = Create();
        await first.StartAsync();
        await first.SetAsync(A);
        (await first.SetAsync(B)).Should().Be(HotkeyResult.Conflict);
        await first.DisposeAsync();

        var restartedRegistrar = new FakeRegistrar();
        var restarted = Create(restartedRegistrar);
        await restarted.StartAsync();

        restarted.State.HasConflict.Should().BeTrue();
        restarted.State.Refused.Should().Be(B);
        restarted.State.Binding.Should().Be(A);
        restartedRegistrar.Calls.Should().Equal("register Ctrl+Alt+T");
    }

    [Fact]
    public async Task Setting_before_start_first_loads_what_is_stored()
    {
        _store.Current = new HotkeyState(A, HotkeyStatus.Registered, _time.GetUtcNow());
        var service = Create();

        await service.SetAsync(B);

        _registrar.Calls.Should().Equal("register Ctrl+Alt+T", "unregister", "register Ctrl+Shift+Y");
    }

    // ---- setting --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Setting_a_shortcut_registers_and_stores_it()
    {
        var service = Create();
        await service.StartAsync();

        var result = await service.SetAsync(A);

        result.Should().Be(HotkeyResult.Registered);
        _registrar.Active.Should().Be(A);
        service.State.Should().Be(new HotkeyState(A, HotkeyStatus.Registered, _time.GetUtcNow()));
        _store.Current.Should().Be(service.State);
    }

    [Fact]
    public async Task Changing_the_shortcut_unregisters_the_old_one_first_and_never_holds_two()
    {
        var service = Create();
        await service.StartAsync();
        await service.SetAsync(A);

        var result = await service.SetAsync(B);

        result.Should().Be(HotkeyResult.Registered);
        _registrar.Calls.Should().Equal("register Ctrl+Alt+T", "unregister", "register Ctrl+Shift+Y");
        _registrar.Active.Should().Be(B);
        _registrar.MostHeld.Should().Be(1);
    }

    [Fact]
    public async Task Setting_the_same_shortcut_again_touches_nothing()
    {
        var service = Create();
        await service.StartAsync();
        await service.SetAsync(A);
        _registrar.Calls.Clear();

        var result = await service.SetAsync(A);

        result.Should().Be(HotkeyResult.Registered);
        _registrar.Calls.Should().BeEmpty("the same shortcut is not a conflict with itself");
    }

    [Fact]
    public async Task A_conflict_keeps_the_previous_shortcut_active_and_is_persisted()
    {
        var service = Create();
        await service.StartAsync();
        await service.SetAsync(A);
        _registrar.HeldElsewhere.Add(B);
        _time.Advance(TimeSpan.FromMinutes(5));

        var result = await service.SetAsync(B);

        result.Should().Be(HotkeyResult.Conflict);
        result.Reason.Should().Be(HotkeyReason.OtherAppHoldsIt);
        _registrar.Active.Should().Be(A, "the shortcut the person had keeps working");
        _registrar.MostHeld.Should().Be(1);
        service.State.Binding.Should().Be(A);
        service.State.Refused.Should().Be(B);
        service.State.LastResult.Should().Be(HotkeyStatus.Conflict);
        service.State.At.Should().Be(_time.GetUtcNow());
        service.State.BindingIsActive.Should().BeTrue();
        _store.Current.Should().Be(service.State);
        _log.Entries.Should().Contain(e => e.Level == "warning" && e.Category == "hotkey" && e.Message.Contains("another program", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_conflict_with_nothing_set_before_leaves_nothing_registered()
    {
        _registrar.HeldElsewhere.Add(A);
        var service = Create();
        await service.StartAsync();

        var result = await service.SetAsync(A);

        result.Should().Be(HotkeyResult.Conflict);
        _registrar.Active.Should().BeNull();
        service.State.Binding.Should().BeNull();
        service.State.Refused.Should().Be(A);
        _store.Current.HasConflict.Should().BeTrue();
    }

    [Fact]
    public async Task If_the_previous_shortcut_cannot_be_taken_back_after_a_conflict_it_is_reported_inactive()
    {
        var service = Create();
        await service.StartAsync();
        await service.SetAsync(A);
        _registrar.HeldElsewhere.Add(B);
        _registrar.HeldElsewhere.Add(A);   // another program grabbed the old one while it was released

        var result = await service.SetAsync(B);

        result.Should().Be(HotkeyResult.Conflict);
        _registrar.Active.Should().BeNull();
        service.State.Binding.Should().Be(A);
        service.State.BindingIsActive.Should().BeFalse();
    }

    [Fact]
    public async Task A_later_success_clears_an_earlier_conflict()
    {
        var service = Create();
        await service.StartAsync();
        _registrar.HeldElsewhere.Add(B);
        await service.SetAsync(B);

        var result = await service.SetAsync(A);

        result.Should().Be(HotkeyResult.Registered);
        service.State.HasConflict.Should().BeFalse();
        service.State.Refused.Should().BeNull();
    }

    [Fact]
    public async Task A_registrar_that_throws_is_a_conflict_not_a_crash()
    {
        _registrar.RegisterThrows = new ObjectDisposedException("window");
        var service = Create();
        await service.StartAsync();

        var result = await service.SetAsync(A);

        result.Should().Be(HotkeyResult.Conflict);
        _log.Entries.Should().Contain(e => e.Level == "error" && e.Message.Contains("ObjectDisposedException", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Clearing_unregisters_and_stores_that_there_is_none()
    {
        var service = Create();
        await service.StartAsync();
        await service.SetAsync(A);

        var result = await service.SetAsync(null);

        result.Should().Be(HotkeyResult.Cleared);
        _registrar.Active.Should().BeNull();
        service.State.Binding.Should().BeNull();
        service.State.LastResult.Should().Be(HotkeyStatus.Cleared);
        _store.Current.Should().Be(service.State);
    }

    [Fact]
    public async Task Clearing_when_nothing_is_set_is_fine()
    {
        var service = Create();
        await service.StartAsync();

        (await service.SetAsync(null)).Should().Be(HotkeyResult.Cleared);

        _registrar.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_conflict_is_cleared_by_clearing()
    {
        _registrar.HeldElsewhere.Add(A);
        var service = Create();
        await service.StartAsync();
        await service.SetAsync(A);

        await service.SetAsync(null);

        service.State.HasConflict.Should().BeFalse();
    }

    [Fact]
    public async Task A_shortcut_without_a_modifier_is_invalid_and_changes_nothing()
    {
        var service = Create();
        await service.StartAsync();
        await service.SetAsync(A);
        var before = _store.Current;
        _registrar.Calls.Clear();

        var result = await service.SetAsync(new HotkeyBinding(HotkeyModifiers.None, 'T'));

        result.Should().Be(HotkeyResult.Invalid(HotkeyReason.NoModifier));
        result.Reason.Should().Be(HotkeyReason.NoModifier);
        _registrar.Calls.Should().BeEmpty("Windows is not even asked");
        _registrar.Active.Should().Be(A);
        _store.Current.Should().Be(before);
    }

    [Fact]
    public async Task A_modifier_key_alone_is_invalid()
    {
        var service = Create();
        await service.StartAsync();

        var result = await service.SetAsync(new HotkeyBinding(HotkeyModifiers.Control, 0x11));

        result.Should().Be(HotkeyResult.Invalid(HotkeyReason.NoKey));
    }

    [Fact]
    public async Task The_state_changed_event_follows_each_change_and_nothing_else()
    {
        var service = Create();
        var changes = 0;
        service.StateChanged += () => changes++;
        await service.StartAsync();
        changes.Should().Be(0);

        await service.SetAsync(A);
        await service.SetAsync(new HotkeyBinding(HotkeyModifiers.None, 'T'));
        await service.SetAsync(null);

        changes.Should().Be(2);
    }

    // ---- pressing and disposing -----------------------------------------------------------------------------------

    [Fact]
    public async Task Pressing_the_shortcut_raises_pressed_once()
    {
        var service = Create();
        await service.StartAsync();
        await service.SetAsync(A);

        _registrar.Press();

        _presses.Should().Be(1);
    }

    [Fact]
    public async Task Pressing_while_a_tug_shows_does_nothing_and_is_logged()
    {
        var service = Create();
        await service.StartAsync();
        await service.SetAsync(A);
        _tug.IsTugShowing = true;

        _registrar.Press();

        _presses.Should().Be(0);
        _log.Entries.Should().Contain(e => e.Message.Contains("tug is showing", StringComparison.Ordinal));

        _tug.IsTugShowing = false;
        _registrar.Press();
        _presses.Should().Be(1);
    }

    [Fact]
    public async Task A_press_with_no_shortcut_set_is_ignored()
    {
        var service = Create();
        await service.StartAsync();

        _registrar.Press();

        _presses.Should().Be(0);
    }

    [Fact]
    public async Task Disposing_unregisters_and_later_presses_do_nothing()
    {
        var service = Create();
        await service.StartAsync();
        await service.SetAsync(A);

        await service.DisposeAsync();
        _registrar.Press();

        _registrar.Active.Should().BeNull();
        _presses.Should().Be(0);
        await service.DisposeAsync();   // twice is fine
    }

    [Fact]
    public async Task Setting_after_disposal_is_refused_quietly()
    {
        var service = Create();
        await service.StartAsync();
        await service.DisposeAsync();

        var result = await service.SetAsync(A);

        result.Should().Be(HotkeyResult.Cleared);
        _registrar.Active.Should().BeNull();
    }

    [Fact]
    public async Task The_synchronous_dispose_unregisters_too()
    {
        var service = Create();
        await service.StartAsync();
        await service.SetAsync(A);

        service.Dispose();

        _registrar.Active.Should().BeNull();
    }

    // ---- suspending for the recorder ----------------------------------------------------------------------------------

    [Fact]
    public async Task Suspending_releases_the_registration_without_touching_the_state_or_the_store()
    {
        var service = Create();
        await service.StartAsync();
        await service.SetAsync(A);
        var saves = _store.Saves;
        var state = service.State;

        await service.SuspendForRecording();

        _registrar.Active.Should().BeNull("the chord must reach the recorder, not Windows");
        service.State.Should().Be(state);
        _store.Saves.Should().Be(saves);
    }

    [Fact]
    public async Task Resuming_registers_the_same_shortcut_again()
    {
        var service = Create();
        await service.StartAsync();
        await service.SetAsync(A);
        await service.SuspendForRecording();
        _registrar.Calls.Clear();

        await service.Resume();

        _registrar.Calls.Should().Equal("register Ctrl+Alt+T");
        _registrar.Active.Should().Be(A);
        service.State.BindingIsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Suspending_twice_releases_once_and_resuming_without_suspending_does_nothing()
    {
        var service = Create();
        await service.StartAsync();
        await service.SetAsync(A);
        await service.Resume();
        _registrar.Calls.Clear();

        await service.SuspendForRecording();
        await service.SuspendForRecording();
        await service.Resume();
        await service.Resume();

        _registrar.Calls.Should().Equal("unregister", "register Ctrl+Alt+T");
    }

    [Fact]
    public async Task With_no_shortcut_suspending_and_resuming_touch_nothing()
    {
        var service = Create();
        await service.StartAsync();

        await service.SuspendForRecording();
        await service.Resume();

        _registrar.Calls.Should().BeEmpty();
        service.State.Should().Be(HotkeyState.None);
    }

    [Fact]
    public async Task Suspending_before_start_loads_the_stored_shortcut_and_releases_it()
    {
        _store.Current = new HotkeyState(A, HotkeyStatus.Registered, _time.GetUtcNow());
        var service = Create();

        await service.SuspendForRecording();

        _registrar.Calls.Should().Equal("register Ctrl+Alt+T", "unregister");
        await service.Resume();
        _registrar.Active.Should().Be(A);
    }

    [Fact]
    public async Task A_press_that_was_already_queued_when_the_registration_was_released_does_nothing()
    {
        var service = Create();
        await service.StartAsync();
        await service.SetAsync(A);
        await service.SuspendForRecording();

        _registrar.Press();

        _presses.Should().Be(0);
        await service.Resume();
        _registrar.Press();
        _presses.Should().Be(1, "after resuming the shortcut works again");
    }

    [Fact]
    public async Task Recording_the_chord_that_is_already_registered_ends_with_it_registered_once()
    {
        var service = Create();
        await service.StartAsync();
        await service.SetAsync(A);
        await service.SuspendForRecording();
        _registrar.Calls.Clear();

        var result = await service.SetAsync(A);

        result.Should().Be(HotkeyResult.Registered);
        _registrar.Active.Should().Be(A);
        _registrar.Calls.Count(c => c.StartsWith("register ", StringComparison.Ordinal)).Should().Be(1, "the registrar is asked once, not twice");
        await service.Resume();
        _registrar.Calls.Count(c => c.StartsWith("register ", StringComparison.Ordinal)).Should().Be(1, "and resuming after the set is a no-op");
    }

    [Fact]
    public async Task Recording_a_different_chord_replaces_the_suspended_one()
    {
        var service = Create();
        await service.StartAsync();
        await service.SetAsync(A);
        await service.SuspendForRecording();

        var result = await service.SetAsync(B);

        result.Should().Be(HotkeyResult.Registered);
        _registrar.Active.Should().Be(B);
        _registrar.MostHeld.Should().Be(1);
        service.State.Binding.Should().Be(B);
        await service.Resume();
        _registrar.Active.Should().Be(B, "a later Resume does not bring the old one back");
    }

    [Fact]
    public async Task A_conflict_while_suspended_takes_the_earlier_shortcut_back()
    {
        var service = Create();
        await service.StartAsync();
        await service.SetAsync(A);
        await service.SuspendForRecording();
        _registrar.HeldElsewhere.Add(B);

        var result = await service.SetAsync(B);

        result.Should().Be(HotkeyResult.Conflict);
        _registrar.Active.Should().Be(A, "the shortcut the person had works again");
        service.State.Refused.Should().Be(B);
        service.State.Binding.Should().Be(A);
    }

    [Fact]
    public async Task Clearing_while_suspended_leaves_nothing_registered()
    {
        var service = Create();
        await service.StartAsync();
        await service.SetAsync(A);
        await service.SuspendForRecording();

        var result = await service.SetAsync(null);

        result.Should().Be(HotkeyResult.Cleared);
        _registrar.Active.Should().BeNull();
        await service.Resume();
        _registrar.Active.Should().BeNull("there is nothing left to resume");
        service.State.Binding.Should().BeNull();
    }

    [Fact]
    public async Task Resuming_when_another_program_took_the_chord_meanwhile_stores_a_conflict()
    {
        var service = Create();
        await service.StartAsync();
        await service.SetAsync(A);
        await service.SuspendForRecording();
        _registrar.HeldElsewhere.Add(A);

        await service.Resume();

        _registrar.Active.Should().BeNull();
        service.State.Binding.Should().Be(A);
        service.State.Refused.Should().Be(A);
        service.State.LastResult.Should().Be(HotkeyStatus.Conflict);
        _store.Current.Should().Be(service.State, "the conflict is written down, and Settings shows it after a restart");
        _log.Entries.Should().Contain(e => e.Level == "warning" && e.Category == "hotkey" && e.Message.Contains("another program", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Resuming_when_the_window_is_gone_does_not_throw_and_stores_a_conflict()
    {
        var service = Create();
        await service.StartAsync();
        await service.SetAsync(A);
        await service.SuspendForRecording();
        _registrar.RegisterThrows = new ObjectDisposedException("window");

        await service.Resume();

        service.State.HasConflict.Should().BeTrue();
        _log.Entries.Should().Contain(e => e.Level == "error" && e.Category == "hotkey");
    }

    [Fact]
    public async Task Suspending_and_resuming_after_disposal_are_quiet()
    {
        var service = Create();
        await service.StartAsync();
        await service.SetAsync(A);
        await service.SuspendForRecording();
        await service.DisposeAsync();
        _registrar.Calls.Clear();

        await service.SuspendForRecording();
        await service.Resume();

        _registrar.Calls.Should().BeEmpty();
        _registrar.Active.Should().BeNull();
    }

    [Fact]
    public async Task Disposing_while_suspended_leaves_nothing_registered()
    {
        var service = Create();
        await service.StartAsync();
        await service.SetAsync(A);
        await service.SuspendForRecording();

        await service.DisposeAsync();

        _registrar.Active.Should().BeNull();
    }
}
