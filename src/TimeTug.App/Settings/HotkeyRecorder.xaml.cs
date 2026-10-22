using System.ComponentModel;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TimeTug.Presentation.Settings;
using Windows.System;
using Windows.UI.Core;

namespace TimeTug.App.Settings;

/// <summary>
/// The popup shortcut recorder: a button that shows the shortcut (or "Record shortcut"), and a clear button. Clicking the button, or
/// pressing Enter or Space on it, starts recording; every key then goes to <see cref="HotkeyRecorderViewModel"/> (the page marks the event
/// handled so the key does not also press a button or move a selection) until a chord is committed, Escape cancels, Backspace clears, or
/// focus leaves the button. The view model suspends the registered shortcut while recording and always restores it.
/// </summary>
public sealed partial class HotkeyRecorder : UserControl
{
    private readonly HotkeyRecorderViewModel _model;

    public HotkeyRecorder(HotkeyRecorderViewModel model)
    {
        _model = model;
        InitializeComponent();
        ToolTipService.SetToolTip(ClearButton, model.ClearToolTip);
        AutomationProperties.SetName(ClearButton, model.ClearAutomationName);
        Loaded += (_, _) =>
        {
            model.PropertyChanged += OnModelChanged;
            Sync();
        };
        Unloaded += (_, _) =>
        {
            model.PropertyChanged -= OnModelChanged;
            // The pane was left (or the window closed) while recording: the shortcut must not stay released.
            _ = model.CancelRecordingAsync();
        };
        Sync();
    }

    /// <summary>The record button (for the self-test).</summary>
    internal Button Record => RecordButton;

    /// <summary>The clear (✕) button (for the self-test).</summary>
    internal Button Clear => ClearButton;

    internal HotkeyRecorderViewModel Model => _model;

    /// <summary>
    /// Passes a key press to the recorder as the key events do (for the self-test, which injects key events: a real <c>SendInput</c> would
    /// reach whatever has the keyboard focus on the machine). True when the recorder used the key.
    /// </summary>
    internal Task<bool> InjectKeyAsync(VirtualKey key, bool control = false, bool alt = false, bool shift = false, bool windows = false)
    {
        var modifiers = (control ? HotkeyModifiers.Control : 0) | (alt ? HotkeyModifiers.Alt : 0) | (shift ? HotkeyModifiers.Shift : 0) | (windows ? HotkeyModifiers.Windows : 0);
        return _model.HandleKeyDownAsync((int)key, modifiers);
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Sync();

    private void Sync()
    {
        RecordButton.Content = _model.DisplayText;
        AutomationProperties.SetName(RecordButton, _model.ButtonAutomationName);
        RecordButton.Style = _model.IsRecording ? (Style)Application.Current.Resources["AccentButtonStyle"] : (Style)Application.Current.Resources["DefaultButtonStyle"];
        ClearButton.IsEnabled = _model.HasShortcut;
    }

    private async void OnRecordClick(object sender, RoutedEventArgs e) => await _model.BeginRecordingAsync();

    private async void OnRecordLostFocus(object sender, RoutedEventArgs e)
    {
        // Focus moved to another control: recording is over. When the whole window lost the active state the button is still the focused
        // element; the window's own handler (SettingsWindow) decides what that means.
        if (XamlRoot is { } root && ReferenceEquals(FocusManager.GetFocusedElement(root), RecordButton))
        {
            return;
        }

        await _model.CancelRecordingAsync();
    }

    private async void OnClearClick(object sender, RoutedEventArgs e) => await _model.ClearAsync();

    private async void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var modifiers = ReadModifiers();
        var key = (int)e.Key;
        if (!_model.WillHandle(key, modifiers))
        {
            return;
        }

        // Handled at once: the awaited work below must not let the key reach the button (Space would press it) or move the focus.
        e.Handled = true;
        if (e.KeyStatus.WasKeyDown && !IsModifier(e.Key))
        {
            // A key held down repeats: only the first press counts.
            return;
        }

        await _model.HandleKeyDownAsync(key, modifiers);
    }

    private static bool IsModifier(VirtualKey key) =>
        key is VirtualKey.Control or VirtualKey.Menu or VirtualKey.Shift or VirtualKey.LeftWindows or VirtualKey.RightWindows
            or VirtualKey.LeftControl or VirtualKey.RightControl or VirtualKey.LeftMenu or VirtualKey.RightMenu or VirtualKey.LeftShift or VirtualKey.RightShift;

    /// <summary>The modifier keys held down right now.</summary>
    private static HotkeyModifiers ReadModifiers()
    {
        var modifiers = HotkeyModifiers.None;
        if (IsDown(VirtualKey.Control))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (IsDown(VirtualKey.Menu))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (IsDown(VirtualKey.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows))
        {
            modifiers |= HotkeyModifiers.Windows;
        }

        return modifiers;
    }

    private static bool IsDown(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
