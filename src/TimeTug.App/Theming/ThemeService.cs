using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Settings;

namespace TimeTug.App.Theming;

/// <summary>
/// Applies the person's theme (Use system, Light, Dark) to the app's own windows, the Settings window and the welcome window (the flyout takes
/// it from <c>FlyoutHost</c>'s look; the tug stays navy and is not registered). A window is attached once and is then themed when it is
/// attached and again whenever the choice, or the Windows theme under "Use system", changes, while it is open.
/// <para>
/// Each time this sets the root element's <c>RequestedTheme</c> (the controls, and the window's stock Mica, which follows the root's
/// <c>ActualTheme</c>) and the caption and caption button colours of the <c>AppWindow</c> explicitly: Windows draws the caption, so it ignores
/// the window's theme (measured on the VM, a window whose root was forced dark kept a light caption). Under a contrast theme the root is left to
/// Windows and the caption colours are cleared. docs/spikes/2026-10-appearance-theming.md records what was measured.
/// </para>
/// </summary>
public sealed class ThemeService : IDisposable
{
    private readonly IPresentationSettingsStore _store;
    private readonly IUiSettings _ui;
    private readonly IUiDispatcher _dispatcher;
    private readonly List<Themed> _windows = [];
    private (bool Dark, bool Contrast)? _applied;
    private bool _disposed;

    public ThemeService(IPresentationSettingsStore store, IUiSettings ui, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _store = store;
        _ui = ui;
        _dispatcher = dispatcher;
        store.Changed += OnChanged;
        ui.Changed += OnChanged;
    }

    /// <summary>True when the windows are dark now (the choice, or Windows' apps theme under "Use system").</summary>
    public bool IsDark => AppearanceResolver.IsDark(_store.Load().Theme, _ui);

    /// <summary>What the roots are given: the theme, or <see cref="ElementTheme.Default"/> (Windows decides) under a contrast theme.</summary>
    public ElementTheme Requested => _ui.HighContrast ? ElementTheme.Default : IsDark ? ElementTheme.Dark : ElementTheme.Light;

    /// <summary>How many windows are attached (for the self-test).</summary>
    public int Attached => _windows.Count;

    /// <summary>
    /// Themes <paramref name="window"/> now and whenever the theme changes until it closes (or the returned handle is disposed).
    /// <paramref name="root"/> is the window's content element.
    /// </summary>
    public IDisposable Attach(Window window, FrameworkElement root)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(root);
        var themed = new Themed(window, root);
        _windows.Add(themed);
        Apply(themed);
        window.Closed += OnWindowClosed;
        return new Detach(this, themed);
    }

    /// <summary>Gives a dialog the theme of the window it opens over (a <see cref="ContentDialog"/> does not inherit it).</summary>
    public static void Match(ContentDialog dialog, XamlRoot? root)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        if (root?.Content is FrameworkElement content)
        {
            dialog.RequestedTheme = content.ActualTheme;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _store.Changed -= OnChanged;
        _ui.Changed -= OnChanged;
        _windows.Clear();
    }

    private void OnChanged()
    {
        if (_dispatcher.HasThreadAccess)
        {
            Reapply();
        }
        else
        {
            _dispatcher.Post(Reapply);
        }
    }

    /// <summary>Re-themes every attached window when the theme that applies has changed (a saved window place raises the same event and changes nothing).</summary>
    private void Reapply()
    {
        if (_disposed || _applied == (IsDark, _ui.HighContrast))
        {
            return;
        }

        foreach (var themed in _windows.ToArray())
        {
            Apply(themed);
        }
    }

    private void Apply(Themed themed)
    {
        var dark = IsDark;
        var contrast = _ui.HighContrast;
        _applied = (dark, contrast);
        try
        {
            themed.Root.RequestedTheme = Requested;
            TitleBarColors.Apply(themed.Window.AppWindow.TitleBar, dark, contrast);
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // The window is closing; there is nothing left to theme.
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args) => _windows.RemoveAll(t => ReferenceEquals(t.Window, sender));

    private sealed class Themed(Window window, FrameworkElement root)
    {
        public Window Window { get; } = window;

        public FrameworkElement Root { get; } = root;
    }

    private sealed class Detach(ThemeService owner, Themed themed) : IDisposable
    {
        public void Dispose() => owner._windows.Remove(themed);
    }
}
