using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Tests.Welcome;

namespace TimeTug.Presentation.Tests.Settings;

public sealed class HotkeyRecorderViewModelTests
{
    private const int KeyT = 'T';
    private const int Escape = 0x1B;
    private const int Backspace = 0x08;
    private const int Delete = 0x2E;
    private const int Tab = 0x09;
    private const int Control = 0x11;
    private const int Shift = 0x10;
    private const int Menu = 0x12;
    private const int LeftWindows = 0x5B;
    private const HotkeyModifiers CtrlAlt = HotkeyModifiers.Control | HotkeyModifiers.Alt;

    private static readonly HotkeyBinding Chord = new(CtrlAlt, KeyT);
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeHotkeys _hotkeys = new();

    private HotkeyRecorderViewModel Model(HotkeyState? state = null)
    {
        if (state is not null)
        {
            _hotkeys.State = state;
        }

        return new HotkeyRecorderViewModel(_hotkeys, new ImmediateDispatcher(), new ReswLocalizer());
    }

    [Fact]
    public void The_row_and_its_help_use_the_Macs_words()
    {
        var model = Model();

        model.Label.Should().Be("Show today’s meetings");
        model.Description.Should().Be("Press a shortcut to show or hide the popup from anywhere. Click ✕ to clear it.");
    }

    [Fact]
    public void With_no_shortcut_the_button_invites_recording_and_nothing_is_said()
    {
        var model = Model();

        model.DisplayText.Should().Be("Record shortcut");
        model.HasShortcut.Should().BeFalse();
        model.IsRecording.Should().BeFalse();
        model.Status.Should().Be(HotkeyStatusKind.None);
        model.StatusText.Should().BeNull();
    }

    [Fact]
    public void A_registered_shortcut_is_shown_and_reported_active()
    {
        var model = Model(new HotkeyState(Chord, HotkeyStatus.Registered, Now));

        model.DisplayText.Should().Be("Ctrl+Alt+T");
        model.HasShortcut.Should().BeTrue();
        model.Status.Should().Be(HotkeyStatusKind.Registered);
        model.StatusText.Should().Be("Shortcut is active.");
        model.IsProblem.Should().BeFalse();
    }

    [Fact]
    public async Task Recording_suspends_the_current_registration_once()
    {
        var model = Model(new HotkeyState(Chord, HotkeyStatus.Registered, Now));

        await model.BeginRecordingAsync();
        await model.BeginRecordingAsync();

        model.IsRecording.Should().BeTrue();
        model.DisplayText.Should().Be("Press a shortcut…");
        _hotkeys.Calls.Should().Equal("suspend");
        _hotkeys.Suspended.Should().BeTrue("the chord that is registered must reach the recorder, not Windows");
    }

    [Fact]
    public async Task Ctrl_Alt_T_is_recorded_and_registered_and_the_registration_is_restored()
    {
        var model = Model();
        await model.BeginRecordingAsync();

        var handled = await model.HandleKeyDownAsync(KeyT, CtrlAlt);

        handled.Should().BeTrue();
        _hotkeys.Calls.Should().Equal("suspend", "set:Ctrl+Alt+T", "resume");
        _hotkeys.Suspended.Should().BeFalse();
        model.IsRecording.Should().BeFalse();
        model.DisplayText.Should().Be("Ctrl+Alt+T");
        model.Status.Should().Be(HotkeyStatusKind.Registered);
    }

    [Fact]
    public async Task The_chord_that_is_already_registered_can_be_recorded_again()
    {
        var model = Model(new HotkeyState(Chord, HotkeyStatus.Registered, Now));
        await model.BeginRecordingAsync();
        _hotkeys.Suspended.Should().BeTrue();

        var handled = await model.HandleKeyDownAsync(KeyT, CtrlAlt);

        handled.Should().BeTrue("the recorder got the key because nothing was registered meanwhile");
        _hotkeys.Calls.Should().Equal("suspend", "set:Ctrl+Alt+T", "resume");
        model.DisplayText.Should().Be("Ctrl+Alt+T");
        model.Status.Should().Be(HotkeyStatusKind.Registered);
        _hotkeys.Suspended.Should().BeFalse();
    }

    [Fact]
    public async Task A_bare_key_is_refused_and_recording_goes_on()
    {
        var model = Model();
        await model.BeginRecordingAsync();

        var handled = await model.HandleKeyDownAsync(KeyT, HotkeyModifiers.None);

        handled.Should().BeTrue("the key is not passed on to the page");
        model.IsRecording.Should().BeTrue();
        model.Status.Should().Be(HotkeyStatusKind.Invalid);
        model.StatusText.Should().Be("A shortcut needs Ctrl, Alt, Shift or the Windows key as well as another key.");
        model.IsProblem.Should().BeTrue();
        _hotkeys.Calls.Should().Equal("suspend");
    }

    [Fact]
    public async Task Shift_alone_with_a_key_is_a_modifier_and_counts()
    {
        var model = Model();
        await model.BeginRecordingAsync();

        await model.HandleKeyDownAsync(KeyT, HotkeyModifiers.Shift | HotkeyModifiers.Windows);

        _hotkeys.Calls.Should().Contain("set:Shift+Win+T");
    }

    [Theory]
    [InlineData(Control, HotkeyModifiers.Control, "Ctrl+…")]
    [InlineData(Menu, HotkeyModifiers.Control | HotkeyModifiers.Alt, "Ctrl+Alt+…")]
    [InlineData(Shift, HotkeyModifiers.Shift, "Shift+…")]
    [InlineData(LeftWindows, HotkeyModifiers.Windows, "Win+…")]
    public async Task A_modifier_pressed_alone_is_ignored_until_a_key_arrives(int key, HotkeyModifiers held, string preview)
    {
        var model = Model();
        await model.BeginRecordingAsync();

        var handled = await model.HandleKeyDownAsync(key, held);

        handled.Should().BeTrue();
        model.IsRecording.Should().BeTrue();
        model.DisplayText.Should().Be(preview);
        _hotkeys.Calls.Should().Equal("suspend");
        model.Status.Should().Be(HotkeyStatusKind.None, "holding Ctrl is not a mistake");

        await model.HandleKeyDownAsync(KeyT, held);
        _hotkeys.Calls.Should().Contain("set:" + new HotkeyBinding(held, KeyT).ToText());
    }

    [Fact]
    public async Task Escape_cancels_and_restores_the_shortcut_that_was_registered()
    {
        var model = Model(new HotkeyState(Chord, HotkeyStatus.Registered, Now));
        await model.BeginRecordingAsync();

        var handled = await model.HandleKeyDownAsync(Escape, HotkeyModifiers.None);

        handled.Should().BeTrue();
        _hotkeys.Calls.Should().Equal("suspend", "resume");
        _hotkeys.Suspended.Should().BeFalse();
        model.IsRecording.Should().BeFalse();
        model.DisplayText.Should().Be("Ctrl+Alt+T", "the shortcut is unchanged");
    }

    [Fact]
    public async Task Leaving_the_recorder_without_a_shortcut_restores_the_registration()
    {
        var model = Model(new HotkeyState(Chord, HotkeyStatus.Registered, Now));
        await model.BeginRecordingAsync();

        await model.CancelRecordingAsync();
        await model.CancelRecordingAsync();

        _hotkeys.Calls.Should().Equal("suspend", "resume");
        model.IsRecording.Should().BeFalse();
    }

    [Theory]
    [InlineData(Backspace)]
    [InlineData(Delete)]
    public async Task Backspace_and_Delete_clear_the_shortcut(int key)
    {
        var model = Model(new HotkeyState(Chord, HotkeyStatus.Registered, Now));
        await model.BeginRecordingAsync();

        var handled = await model.HandleKeyDownAsync(key, HotkeyModifiers.None);

        handled.Should().BeTrue();
        _hotkeys.Calls.Should().Equal("suspend", "set:none", "resume");
        model.HasShortcut.Should().BeFalse();
        model.DisplayText.Should().Be("Record shortcut");
        model.Status.Should().Be(HotkeyStatusKind.Cleared);
        model.StatusText.Should().Be("Shortcut cleared.");
        model.IsRecording.Should().BeFalse();
    }

    [Fact]
    public async Task The_clear_button_unregisters_the_shortcut()
    {
        var model = Model(new HotkeyState(Chord, HotkeyStatus.Registered, Now));

        await model.ClearAsync();

        _hotkeys.Calls.Should().Equal("set:none");
        model.HasShortcut.Should().BeFalse();
        model.Status.Should().Be(HotkeyStatusKind.Cleared);
    }

    [Fact]
    public async Task Clearing_while_recording_ends_the_recording_and_restores()
    {
        var model = Model(new HotkeyState(Chord, HotkeyStatus.Registered, Now));
        await model.BeginRecordingAsync();

        await model.ClearAsync();

        _hotkeys.Calls.Should().Equal("suspend", "set:none", "resume");
        model.IsRecording.Should().BeFalse();
        _hotkeys.Suspended.Should().BeFalse();
    }

    [Fact]
    public async Task A_shortcut_another_program_holds_shows_the_conflict_and_keeps_the_earlier_one()
    {
        var other = new HotkeyBinding(CtrlAlt, 'M');
        var model = Model(new HotkeyState(Chord, HotkeyStatus.Registered, Now));
        _hotkeys.Answer = _ => HotkeyResult.Conflict;
        await model.BeginRecordingAsync();

        await model.HandleKeyDownAsync('M', CtrlAlt);

        model.Status.Should().Be(HotkeyStatusKind.Conflict);
        model.StatusText.Should().Be("Ctrl+Alt+M is already used by another program. Ctrl+Alt+T still works.");
        model.IsProblem.Should().BeTrue();
        model.DisplayText.Should().Be("Ctrl+Alt+T");
        _hotkeys.State.Refused.Should().Be(other);
        _hotkeys.Calls.Should().Equal("suspend", "set:Ctrl+Alt+M", "resume");
        _hotkeys.Suspended.Should().BeFalse("the registration is restored after a conflict too");
        model.IsRecording.Should().BeFalse();
    }

    [Fact]
    public async Task A_conflict_with_no_earlier_shortcut_says_none_is_set()
    {
        var model = Model();
        _hotkeys.Answer = _ => HotkeyResult.Conflict;
        await model.BeginRecordingAsync();

        await model.HandleKeyDownAsync(KeyT, CtrlAlt);

        model.StatusText.Should().Be("Ctrl+Alt+T is already used by another program. No shortcut is set.");
        model.DisplayText.Should().Be("Record shortcut");
    }

    [Fact]
    public void A_conflict_found_at_startup_is_still_shown_after_the_state_is_stored_and_reloaded()
    {
        var found = new HotkeyState(Chord, HotkeyStatus.Conflict, Now, Chord);
        var reloaded = HotkeyState.FromStorage(found.ToStorage());

        var model = Model(reloaded);

        model.Status.Should().Be(HotkeyStatusKind.Conflict);
        model.StatusText.Should().Be("Ctrl+Alt+T is already used by another program, so it is inactive. Record a different shortcut.");
        model.IsProblem.Should().BeTrue();
        model.DisplayText.Should().Be("Ctrl+Alt+T", "the stored shortcut is still the one in force, just inactive");
    }

    [Fact]
    public async Task The_service_refusing_a_shortcut_as_invalid_is_reported_and_the_registration_restored()
    {
        var model = Model();
        _hotkeys.Answer = _ => HotkeyResult.Invalid(HotkeyReason.NoKey);
        await model.BeginRecordingAsync();

        await model.HandleKeyDownAsync(KeyT, CtrlAlt);

        model.Status.Should().Be(HotkeyStatusKind.Invalid);
        model.StatusText.Should().Be("Press a key together with Ctrl, Alt, Shift or the Windows key.");
        _hotkeys.Calls.Should().Equal("suspend", "set:Ctrl+Alt+T", "resume");
        model.IsRecording.Should().BeFalse();
    }

    [Fact]
    public async Task A_failure_of_the_service_is_reported_and_the_registration_is_still_restored()
    {
        var model = Model(new HotkeyState(Chord, HotkeyStatus.Registered, Now));
        _hotkeys.SetThrows = new InvalidOperationException("the window is gone");
        await model.BeginRecordingAsync();

        await model.HandleKeyDownAsync('M', CtrlAlt);

        model.Status.Should().Be(HotkeyStatusKind.Error);
        model.StatusText.Should().Be("Couldn’t set the shortcut: the window is gone");
        _hotkeys.Calls.Should().Equal("suspend", "set:Ctrl+Alt+M", "resume");
        _hotkeys.Suspended.Should().BeFalse();
        model.IsRecording.Should().BeFalse();
    }

    [Fact]
    public async Task Keys_are_not_taken_unless_the_recorder_is_recording()
    {
        var model = Model();

        (await model.HandleKeyDownAsync(Escape, HotkeyModifiers.None)).Should().BeFalse();
        (await model.HandleKeyDownAsync(KeyT, CtrlAlt)).Should().BeFalse();

        _hotkeys.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HotkeyModifiers.None)]
    [InlineData(HotkeyModifiers.Shift)]
    public async Task Tab_moves_focus_on_and_is_not_recorded(HotkeyModifiers held)
    {
        var model = Model();
        await model.BeginRecordingAsync();

        var handled = await model.HandleKeyDownAsync(Tab, held);

        handled.Should().BeFalse("the page lets focus move, which ends the recording");
        _hotkeys.Calls.Should().Equal("suspend");
    }

    [Fact]
    public async Task Ctrl_Alt_Tab_is_a_shortcut_like_any_other()
    {
        var model = Model();
        await model.BeginRecordingAsync();

        await model.HandleKeyDownAsync(Tab, CtrlAlt);

        _hotkeys.Calls.Should().Contain("set:Ctrl+Alt+Tab");
    }

    [Fact]
    public async Task A_change_of_state_in_the_service_is_shown()
    {
        var model = Model();
        var raised = new List<string?>();
        model.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _hotkeys.State = new HotkeyState(Chord, HotkeyStatus.Registered, Now);
        _hotkeys.RaiseStateChanged();

        model.DisplayText.Should().Be("Ctrl+Alt+T");
        raised.Should().Contain(nameof(HotkeyRecorderViewModel.DisplayText));
        await model.ClearAsync();
    }

    [Fact]
    public async Task Disposing_while_recording_restores_the_registration()
    {
        var model = Model(new HotkeyState(Chord, HotkeyStatus.Registered, Now));
        await model.BeginRecordingAsync();

        model.Dispose();

        _hotkeys.Calls.Should().Equal("suspend", "resume");
        _hotkeys.RaiseStateChanged();
    }

    [Fact]
    public async Task A_new_recording_clears_the_last_complaint()
    {
        var model = Model();
        await model.BeginRecordingAsync();
        await model.HandleKeyDownAsync(KeyT, HotkeyModifiers.None);
        model.Status.Should().Be(HotkeyStatusKind.Invalid);
        await model.CancelRecordingAsync();

        await model.BeginRecordingAsync();

        model.Status.Should().Be(HotkeyStatusKind.None);
    }

    [Fact]
    public async Task The_page_learns_at_once_which_keys_the_recorder_will_take()
    {
        var model = Model();
        model.WillHandle(KeyT, CtrlAlt).Should().BeFalse("nothing is taken while not recording");

        await model.BeginRecordingAsync();

        model.WillHandle(KeyT, CtrlAlt).Should().BeTrue();
        model.WillHandle(Escape, HotkeyModifiers.None).Should().BeTrue();
        model.WillHandle(Control, HotkeyModifiers.Control).Should().BeTrue();
        model.WillHandle(0x20, HotkeyModifiers.None).Should().BeTrue("a bare Space is refused by the recorder, it does not press the button");
        model.WillHandle(Tab, HotkeyModifiers.None).Should().BeFalse();
        model.WillHandle(Tab, HotkeyModifiers.Shift).Should().BeFalse();
        model.WillHandle(Tab, CtrlAlt).Should().BeTrue();
    }
}
