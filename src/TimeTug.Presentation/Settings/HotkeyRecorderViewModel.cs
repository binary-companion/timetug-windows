using CommunityToolkit.Mvvm.ComponentModel;
using TimeTug.Presentation.Abstractions;

namespace TimeTug.Presentation.Settings;

/// <summary>What the recorder says about the shortcut.</summary>
public enum HotkeyStatusKind
{
    /// <summary>No shortcut is set and nothing went wrong.</summary>
    None,

    /// <summary>The shortcut is registered with Windows.</summary>
    Registered,

    /// <summary>Another program holds the shortcut (also reported after a restart).</summary>
    Conflict,

    /// <summary>The keys pressed cannot be a shortcut (no modifier, or no key).</summary>
    Invalid,

    /// <summary>The service failed.</summary>
    Error,

    /// <summary>The shortcut was cleared.</summary>
    Cleared,
}

/// <summary>
/// The Mac's shortcut recorder (the <c>KeyboardShortcuts.Recorder</c> row "Show today's meetings"). A person starts recording by focusing the
/// control; from then on a chord is taken from the key events the page passes to <see cref="HandleKeyDownAsync"/>: Ctrl, Alt, Shift or the
/// Windows key plus a key is a shortcut; a modifier held alone is ignored until a key arrives; a bare key is refused with a message; Escape
/// cancels; Backspace and Delete clear. While recording, the shortcut already registered is <b>suspended</b>
/// (<see cref="IHotkeyControl.SuspendForRecording"/>) so that pressing that same chord reaches the recorder and does not fire the popup, and it is
/// always restored (<see cref="IHotkeyControl.Resume"/>) when recording ends, whatever ended it: a commit, a conflict, a failure, Escape,
/// leaving the control or closing the window. What it says about the shortcut comes from the service's <see cref="HotkeyState"/>, so a
/// conflict found at the last start is still reported after a restart. The recorder decides nothing about what a shortcut does.
/// </summary>
public sealed class HotkeyRecorderViewModel : ObservableObject, IDisposable
{
    private const int VkBackspace = 0x08;
    private const int VkTab = 0x09;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkEscape = 0x1B;
    private const int VkDelete = 0x2E;
    private const int VkLeftWindows = 0x5B;
    private const int VkRightWindows = 0x5C;
    private const int VkLeftShift = 0xA0;
    private const int VkRightMenu = 0xA5;

    private readonly IHotkeyControl _hotkeys;
    private readonly IUiDispatcher _ui;
    private readonly ILocalizer _text;
    private HotkeyModifiers _held = HotkeyModifiers.None;
    private (HotkeyStatusKind Kind, string Text)? _note;
    private bool _disposed;

    public HotkeyRecorderViewModel(IHotkeyControl hotkeys, IUiDispatcher ui, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(text);
        _hotkeys = hotkeys;
        _ui = ui;
        _text = text;
        _hotkeys.StateChanged += OnStateChanged;
    }

    /// <summary>The row's label (the Mac's "Show today's meetings").</summary>
    public string Label => _text.GetString("SettingsText_popup-shortcut");

    /// <summary>The help under the row (the Mac's text).</summary>
    public string Description => _text.GetString("HotkeyRecorder_Description");

    /// <summary>The control has focus and is waiting for a chord.</summary>
    public bool IsRecording
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                RaiseAll();
            }
        }
    }

    /// <summary>What the button shows: the shortcut, "Record shortcut" when there is none, or the prompt (with the modifiers held so far) while recording.</summary>
    public string DisplayText
    {
        get
        {
            if (IsRecording)
            {
                return _held == HotkeyModifiers.None
                    ? _text.GetString("HotkeyRecorder_Recording")
                    : _text.Format("HotkeyRecorder_ModifiersSoFar", ModifierText(_held));
            }

            return _hotkeys.State.Binding?.ToText() ?? _text.GetString("HotkeyRecorder_Record");
        }
    }

    /// <summary>What the button is called for a screen reader.</summary>
    public string ButtonAutomationName => IsRecording
        ? _text.GetString("HotkeyRecorder_ButtonRecordingAutomation")
        : _text.Format("HotkeyRecorder_ButtonAutomation", _hotkeys.State.Binding?.ToText() ?? _text.GetString("HotkeyRecorder_Record"));

    public string ClearAutomationName => _text.GetString("HotkeyRecorder_ClearAutomation");

    public string ClearToolTip => _text.GetString("HotkeyRecorder_ClearToolTip");

    /// <summary>A shortcut is set (the ✕ button clears it).</summary>
    public bool HasShortcut => _hotkeys.State.Binding is not null;

    public HotkeyStatusKind Status
    {
        get
        {
            if (_note is { } note)
            {
                return note.Kind;
            }

            var state = _hotkeys.State;
            return state.HasConflict ? HotkeyStatusKind.Conflict : state.BindingIsActive ? HotkeyStatusKind.Registered : HotkeyStatusKind.None;
        }
    }

    /// <summary>The line under the row for <see cref="Status"/>, or null.</summary>
    public string? StatusText
    {
        get
        {
            if (_note is { } note)
            {
                return note.Text;
            }

            var state = _hotkeys.State;
            if (state.Refused is { } refused)
            {
                return state.Binding is { } kept
                    ? (kept == refused
                        ? _text.Format("HotkeyRecorder_ConflictInactive", refused.ToText())
                        : _text.Format("HotkeyRecorder_ConflictKept", refused.ToText(), kept.ToText()))
                    : _text.Format("HotkeyRecorder_ConflictNone", refused.ToText());
            }

            return state.BindingIsActive ? _text.GetString("HotkeyRecorder_Active") : null;
        }
    }

    public bool HasStatus => StatusText is not null;

    /// <summary>The status is a complaint (conflict, invalid keys, a failure), not good news.</summary>
    public bool IsProblem => Status is HotkeyStatusKind.Conflict or HotkeyStatusKind.Invalid or HotkeyStatusKind.Error;

    /// <summary>Starts recording: the registered shortcut is suspended so the same chord reaches the recorder.</summary>
    public async Task BeginRecordingAsync()
    {
        if (IsRecording)
        {
            return;
        }

        _note = null;
        _held = HotkeyModifiers.None;
        IsRecording = true;
        try
        {
            await _hotkeys.SuspendForRecording();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            await StopRecordingAsync(restore: true);
            SetNote(HotkeyStatusKind.Error, _text.Format("HotkeyRecorder_Failed", e.Message));
        }
    }

    /// <summary>Stops recording without a change (Escape, or the control lost focus) and restores the registration.</summary>
    public Task CancelRecordingAsync() => StopRecordingAsync(restore: true);

    /// <summary>
    /// Whether <see cref="HandleKeyDownAsync"/> will use this key press. The page needs the answer at once, to mark the key event handled before
    /// the awaited work starts: everything while recording, except Tab and Shift+Tab, which move focus on.
    /// </summary>
    public bool WillHandle(int virtualKey, HotkeyModifiers modifiers) => IsRecording && !IsFocusKey(virtualKey, modifiers);

    /// <summary>
    /// Takes a key press while recording. Returns true when the recorder used it (the page marks the event handled) and false when the page should
    /// treat it as usual (not recording, or Tab, which moves focus on).
    /// </summary>
    /// <param name="virtualKey">The Windows virtual-key code of the key pressed.</param>
    /// <param name="modifiers">The modifier keys held down at that moment (a modifier key pressed alone includes itself).</param>
    public async Task<bool> HandleKeyDownAsync(int virtualKey, HotkeyModifiers modifiers)
    {
        if (!IsRecording)
        {
            return false;
        }

        if (IsModifierKey(virtualKey))
        {
            _held = modifiers;
            _note = null;
            RaiseAll();
            return true;
        }

        if (modifiers == HotkeyModifiers.None)
        {
            switch (virtualKey)
            {
                case VkEscape:
                    await StopRecordingAsync(restore: true);
                    return true;
                case VkBackspace or VkDelete:
                    await ClearCoreAsync();
                    return true;
            }
        }

        if (IsFocusKey(virtualKey, modifiers))
        {
            // Tab and Shift+Tab move focus on, which ends the recording; they are not a shortcut.
            return false;
        }

        var binding = new HotkeyBinding(modifiers, virtualKey);
        var reason = binding.Validate();
        if (reason != HotkeyReason.None)
        {
            _held = HotkeyModifiers.None;
            SetNote(HotkeyStatusKind.Invalid, ReasonText(reason));
            return true;
        }

        await CommitAsync(binding);
        return true;
    }

    /// <summary>The ✕ button (or Backspace): removes the shortcut.</summary>
    public Task ClearAsync() => ClearCoreAsync();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hotkeys.StateChanged -= OnStateChanged;
        if (IsRecording)
        {
            // The window closed while recording: the shortcut must not stay released.
            IsRecording = false;
            _ = RestoreAsync();
        }
    }

    private async Task CommitAsync(HotkeyBinding binding)
    {
        HotkeyResult? result = null;
        string? failure = null;
        try
        {
            result = await _hotkeys.SetAsync(binding);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            failure = _text.Format("HotkeyRecorder_Failed", e.Message);
        }
        finally
        {
            // Setting ends the suspension in the real service; this makes sure of it on every path (a refusal, a failure).
            await StopRecordingAsync(restore: true);
        }

        if (failure is not null)
        {
            SetNote(HotkeyStatusKind.Error, failure);
        }
        else if (result is { Status: HotkeyStatus.Invalid } invalid)
        {
            SetNote(HotkeyStatusKind.Invalid, ReasonText(invalid.Reason));
        }
        else
        {
            // Registered or Conflict: the state the service now holds says which, and says it again after a restart.
            _note = null;
            RaiseAll();
        }
    }

    private async Task ClearCoreAsync()
    {
        string? failure = null;
        try
        {
            await _hotkeys.SetAsync(null);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            failure = _text.Format("HotkeyRecorder_Failed", e.Message);
        }
        finally
        {
            await StopRecordingAsync(restore: IsRecording);
        }

        SetNote(failure is null ? HotkeyStatusKind.Cleared : HotkeyStatusKind.Error, failure ?? _text.GetString("HotkeyRecorder_Cleared"));
    }

    private async Task StopRecordingAsync(bool restore)
    {
        if (!IsRecording)
        {
            return;
        }

        _held = HotkeyModifiers.None;
        IsRecording = false;
        if (restore)
        {
            await RestoreAsync();
        }
    }

    private async Task RestoreAsync()
    {
        try
        {
            await _hotkeys.Resume();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            SetNote(HotkeyStatusKind.Error, _text.Format("HotkeyRecorder_Failed", e.Message));
        }
    }

    private void SetNote(HotkeyStatusKind kind, string text)
    {
        _note = (kind, text);
        RaiseAll();
    }

    private string ReasonText(HotkeyReason reason) => _text.GetString(reason == HotkeyReason.NoModifier ? "HotkeyRecorder_NoModifier" : "HotkeyRecorder_NoKey");

    private void OnStateChanged() => _ui.Post(() =>
    {
        if (!_disposed)
        {
            RaiseAll();
        }
    });

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(ButtonAutomationName));
        OnPropertyChanged(nameof(HasShortcut));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(IsProblem));
        OnPropertyChanged(nameof(IsRecording));
    }

    private static bool IsFocusKey(int virtualKey, HotkeyModifiers modifiers) => virtualKey == VkTab && modifiers is HotkeyModifiers.None or HotkeyModifiers.Shift;

    private static bool IsModifierKey(int code) => code is VkShift or VkControl or VkMenu or VkLeftWindows or VkRightWindows or (>= VkLeftShift and <= VkRightMenu);

    private static string ModifierText(HotkeyModifiers modifiers)
    {
        var parts = new List<string>(4);
        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        return string.Join('+', parts);
    }
}
