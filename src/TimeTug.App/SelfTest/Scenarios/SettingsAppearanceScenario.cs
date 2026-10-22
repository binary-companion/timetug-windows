#if SELFTEST
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using TimeTug.App.Settings;
using TimeTug.App.Theming;
using TimeTug.Platform.Tray;
using TimeTug.Presentation.Flyout;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Tray;
using Windows.Graphics;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>settings-appearance</c>: the real Settings window on the Appearance pane over scripted data (invented sample cards only),
/// with the real flyout host following the same presentation settings. Each theme (Use system, Light, Dark) is chosen through UI Automation
/// with the window open and must be applied at once to the root and the caption (the caption colours are read
/// from the window and from a screenshot of it, because Windows draws the caption); "Use system" follows a change of the fake system theme live. Each backdrop
/// (Acrylic, Solid) is chosen and the flyout is opened under Light and Dark, with reduced transparency and a contrast theme overriding the
/// choice (and the pane saying so). The tray icon style is chosen with its previews drawn by the real renderer. The Settings window's place
/// survives all of it, and the welcome window follows the theme too.
/// </summary>
internal static class SettingsAppearanceScenario
{
    public const string Name = "settings-appearance";

    // Wide enough for each row of tiles, tall enough to show all three rows (pixels).
    private static readonly (int Width, int Height) WindowSize = (1500, 1450);

    private static readonly string[] DictionaryKeys = ["TextFillColorPrimary", "CardBackgroundFillColorDefault", "SolidBackgroundFillColorBase"];

    private static readonly bool[] Themes = [false, true];

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        using var tug = TugRig.Create(options, cancellation);
        var run = tug.Run;
        using var settings = SettingsRig.Over(tug);
        using var flyout = FlyoutRig.Over(tug, settings.Store);
        tug.Engine.Push(SettingsRig.State(tug.Time.GetUtcNow()));
        tug.Settings.AppsUseDarkTheme = false;

        var window = await settings.OpenAsync(SettingsPane.Appearance, size: WindowSize);
        run.Check(window is not null, "the Settings window opens on Appearance");
        if (window?.CurrentPage is not AppearancePage page)
        {
            run.Check(false, "the Appearance pane is showing");
            return tug.Finish();
        }

        await PaneAsync(tug, settings, window, page);
        await ThemesAsync(tug, settings, window, page);
        await TrayStyleAsync(tug, settings, window, page);
        await BackdropsAsync(tug, settings, flyout, window, page);
        await WindowPlaceAsync(tug, settings, window);
        await WelcomeAsync(tug, settings);
        return tug.Finish();
    }

    // ---- what the pane offers and shows at the start -----------------------------------------------------------------------------
    private static async Task PaneAsync(TugRig tug, SettingsRig settings, SettingsWindow window, AppearancePage page)
    {
        var run = tug.Run;
        run.Check(page.TrayStyleTiles.Count == 2 && page.BackdropChoiceTiles.Count == 2 && page.ThemeChoiceTiles.Count == 3, "two tray styles, two backdrops and three themes are offered");
        run.Check(Names(page.TrayStyleTiles).SequenceEqual(["Icon only tray icon", "Icon with minute digit tray icon"]), "the tray tiles are named: " + string.Join(" | ", Names(page.TrayStyleTiles)));
        run.Check(Names(page.BackdropChoiceTiles).SequenceEqual(["Acrylic flyout backdrop", "Solid flyout backdrop"]), "the backdrop tiles are named: " + string.Join(" | ", Names(page.BackdropChoiceTiles)));
        run.Check(Names(page.ThemeChoiceTiles).SequenceEqual(["Use system theme", "Light theme", "Dark theme"]), "the theme tiles are named: " + string.Join(" | ", Names(page.ThemeChoiceTiles)));
        run.Check(Checked(page.TrayStyleTiles) == 1 && Checked(page.BackdropChoiceTiles) == 0 && Checked(page.ThemeChoiceTiles) == 0, "the defaults are chosen: the minute digit, acrylic and Use system");
        run.Check(page.BackdropNoteText.Text.Contains("transparency", StringComparison.Ordinal) && page.BackdropNoteText.Text.Contains("contrast", StringComparison.Ordinal), "the note says reduced transparency and contrast override the backdrop: " + page.BackdropNoteText.Text);
        run.Check(!page.OverrideNotice.IsOpen, "nothing overrides the backdrop yet, so the override notice is closed");
        run.Check(page.TrayPreviewPictures.All(p => p is not null), "each tray tile shows the icon drawn by the tray's own renderer");
        run.Check(ScenarioUi.Unnamed(window).Count == 0, "no control is without a name: " + string.Join(", ", ScenarioUi.Unnamed(window)));
        run.Check(window.RootElement.RequestedTheme == ElementTheme.Light && window.RootElement.ActualTheme == ElementTheme.Light, "with Windows on the light theme, the window is light");
        run.Check(window.SystemBackdrop is Microsoft.UI.Xaml.Media.MicaBackdrop, "Mica is behind it, which follows the root's theme");
        run.Info("theme colours from WinUI's dictionaries: " + string.Join(", ", DictionaryKeys.SelectMany(k => Themes.Select(d => $"{k}/{(d ? "dark" : "light")}={ThemeColors.InDictionaries(k, d)}"))));
        await tug.Settle(1800);
        await PaintedShotAsync(run, window, "appearance-light");
    }

    // ---- the theme: applied at once to the open window ------------------------------------------------------------------------------
    private static async Task ThemesAsync(TugRig tug, SettingsRig settings, SettingsWindow window, AppearancePage page)
    {
        var run = tug.Run;
        var theme = settings.Theme;
        var lightBar = ThemeColors.Get("SolidBackgroundFillColorBase", dark: false);
        var darkBar = ThemeColors.Get("SolidBackgroundFillColorBase", dark: true);
        run.Check(Same(window.AppWindow.TitleBar.BackgroundColor, lightBar), "under the light theme the caption is the light base colour: " + window.AppWindow.TitleBar.BackgroundColor);

        // Dark, chosen while the window is open.
        var changes = 0;
        settings.Store.Changed += () => changes++;
        run.Check(Select(page.ThemeChoiceTiles[2]), "the Dark tile is chosen through UI Automation");
        await tug.Settle(900);
        run.Check(settings.Store.Load().Theme == AppTheme.Dark && changes == 1, "Dark is stored and the store raised Changed once: " + changes);
        run.Check(Checked(page.ThemeChoiceTiles) == 2, "the Dark tile is the chosen one");
        run.Check(window.RootElement.ActualTheme == ElementTheme.Dark, "the open window is dark at once: " + window.RootElement.ActualTheme);
        run.Check(Same(window.AppWindow.TitleBar.BackgroundColor, darkBar) && Same(window.AppWindow.TitleBar.ButtonBackgroundColor, darkBar), "the caption and its buttons were given the dark colours: " + window.AppWindow.TitleBar.BackgroundColor);
        await PaintedShotAsync(run, window, "appearance-dark");
        var (captionDark, bodyDark) = Sample(run, window);
        run.Check(captionDark < 90, $"in the screenshot the caption is dark (luminance {captionDark})");
        run.Check(bodyDark < 90, $"and so is the pane (luminance {bodyDark})");

        // Light, chosen while Windows is on the dark theme: the choice wins.
        tug.Settings.AppsUseDarkTheme = true;
        tug.Settings.Raise();
        run.Check(Select(page.ThemeChoiceTiles[1]), "the Light tile is chosen");
        await tug.Settle(900);
        run.Check(settings.Store.Load().Theme == AppTheme.Light && window.RootElement.ActualTheme == ElementTheme.Light, "Light wins over a dark Windows theme");
        run.Check(Same(window.AppWindow.TitleBar.BackgroundColor, lightBar), "the caption is light again");
        await PaintedShotAsync(run, window, "appearance-light-chosen");
        var (captionLight, bodyLight) = Sample(run, window);
        run.Check(captionLight > 200 && bodyLight > 200, $"in the screenshot the caption and the pane are light (luminance {captionLight} and {bodyLight})");

        // Use system follows Windows while the window is open.
        run.Check(Select(page.ThemeChoiceTiles[0]), "the Use system tile is chosen (Windows is dark)");
        await tug.Settle(900);
        run.Check(window.RootElement.ActualTheme == ElementTheme.Dark && Same(window.AppWindow.TitleBar.BackgroundColor, darkBar), "Use system follows the dark Windows theme");
        await PaintedShotAsync(run, window, "appearance-system-dark");
        tug.Settings.AppsUseDarkTheme = false;
        tug.Settings.Raise();
        await tug.Settle(700);
        run.Check(window.RootElement.ActualTheme == ElementTheme.Light && Same(window.AppWindow.TitleBar.BackgroundColor, lightBar), "and follows Windows back to light while the window stays open");

        // A contrast theme leaves the colours to Windows.
        tug.Settings.HighContrast = true;
        tug.Settings.Raise();
        await tug.Settle(700);
        run.Check(window.RootElement.RequestedTheme == ElementTheme.Default && window.AppWindow.TitleBar.BackgroundColor is null, "under a contrast theme the root is left to Windows and the caption colours are cleared");
        tug.Settings.HighContrast = false;
        tug.Settings.Raise();
        await tug.Settle(700);
        run.Check(window.RootElement.RequestedTheme == ElementTheme.Light && Same(window.AppWindow.TitleBar.BackgroundColor, lightBar), "and are applied again when it ends");
        run.Check(theme.Attached == 1, "the service holds just this window: " + theme.Attached);
    }

    // ---- the tray icon style --------------------------------------------------------------------------------------------------------
    private static async Task TrayStyleAsync(TugRig tug, SettingsRig settings, SettingsWindow window, AppearancePage page)
    {
        var run = tug.Run;
        run.Check(Select(page.TrayStyleTiles[0]), "the Icon only tile is chosen");
        await tug.Settle(500);
        run.Check(settings.Store.Load().TrayStyle == TrayIconStyle.IconOnly && Checked(page.TrayStyleTiles) == 0, "icon only is stored and shown as chosen");
        await PaintedShotAsync(run, window, "appearance-tray-icon-only");
        run.Check(Select(page.TrayStyleTiles[1]), "the minute digit tile is chosen again");
        await tug.Settle(500);
        run.Check(settings.Store.Load().TrayStyle == TrayIconStyle.IconWithMinuteDigit && Checked(page.TrayStyleTiles) == 1, "the minute digit is stored and shown as chosen");
    }

    // ---- the flyout backdrop and the flyout under each theme -------------------------------------------------------------------------
    private static async Task BackdropsAsync(TugRig tug, SettingsRig settings, FlyoutRig flyout, SettingsWindow window, AppearancePage page)
    {
        var run = tug.Run;
        var acrylicWorks = DesktopAcrylicController.IsSupported();
        tug.Engine.Push(flyout.State(flyout.Day()));

        foreach (var (theme, themeTile) in new[] { (AppTheme.Light, 1), (AppTheme.Dark, 2) })
        {
            run.Check(Select(page.ThemeChoiceTiles[themeTile]), $"the {theme} tile is chosen");
            foreach (var (backdrop, backdropTile) in new[] { (FlyoutBackdrop.Material, 0), (FlyoutBackdrop.Solid, 1) })
            {
                run.Check(Select(page.BackdropChoiceTiles[backdropTile]), $"the {backdrop} backdrop tile is chosen");
                await tug.Settle(500);
                run.Check(settings.Store.Load().Backdrop == backdrop && Checked(page.BackdropChoiceTiles) == backdropTile, $"{backdrop} is stored and shown as chosen");
                await PaintedShotAsync(run, window, $"appearance-{theme}-{backdrop}-pane".ToLowerInvariant());

                var open = await flyout.OpenAsync();
                run.Check(open is not null, $"the flyout opens under {theme} with the {backdrop} backdrop");
                if (open is null)
                {
                    continue;
                }

                var expected = backdrop == FlyoutBackdrop.Material && acrylicWorks ? FlyoutBackdrop.Material : FlyoutBackdrop.Solid;
                run.Check(open.Backdrop == expected, $"its backdrop is {expected}: {open.Backdrop}");
                run.Check(open.IsDarkTheme == (theme == AppTheme.Dark) && ((FrameworkElement)open.Content).ActualTheme == (theme == AppTheme.Dark ? ElementTheme.Dark : ElementTheme.Light), $"it is in the {theme} theme");
                await tug.Settle(900);
                await PaintedShotAsync(run, open, $"flyout-{theme}-{backdrop}".ToLowerInvariant());
                flyout.Host.Close();
                await tug.Settle(FlyoutHost.ClickGrace.Milliseconds + 100);
            }
        }

        // A choice made while the flyout is open restyles it.
        run.Check(Select(page.ThemeChoiceTiles[1]) && Select(page.BackdropChoiceTiles[0]), "Light and acrylic are chosen");
        await tug.Settle(500);
        var live = await flyout.OpenAsync();
        run.Check(live is not null && !live.IsDarkTheme, "the flyout opens light");
        if (live is not null)
        {
            settings.Store.Save(settings.Store.Load() with { Theme = AppTheme.Dark });
            await tug.Settle(900);
            run.Check(live.IsDarkTheme && ((FrameworkElement)live.Content).ActualTheme == ElementTheme.Dark, "a theme chosen while it is open turns it dark without reopening");
            await PaintedShotAsync(run, live, "flyout-live-dark");

            // Reduced transparency overrides the choice, and the pane says so.
            tug.Settings.TransparencyEffectsEnabled = false;
            tug.Settings.Raise();
            await tug.Settle(900);
            run.Check(live.Backdrop == FlyoutBackdrop.Solid, "with transparency effects off the open flyout is solid whatever was chosen");
            run.Check(page.OverrideNotice.IsOpen && page.OverrideNotice.Message.Contains("transparency", StringComparison.Ordinal), "the pane says transparency effects are off: " + page.OverrideNotice.Message);
            run.Check(Checked(page.BackdropChoiceTiles) == 0, "the person's choice (acrylic) is still the chosen tile");
            tug.Settings.HighContrast = true;
            tug.Settings.Raise();
            await tug.Settle(900);
            run.Check(live.Backdrop == FlyoutBackdrop.Solid && page.OverrideNotice.Message.Contains("contrast", StringComparison.Ordinal), "a contrast theme also gives a solid flyout and the pane says so: " + page.OverrideNotice.Message);
            await PaintedShotAsync(run, window, "appearance-override-contrast");
            tug.Settings.HighContrast = false;
            tug.Settings.TransparencyEffectsEnabled = true;
            tug.Settings.Raise();
            await tug.Settle(900);
            run.Check(!page.OverrideNotice.IsOpen && live.Backdrop == (acrylicWorks ? FlyoutBackdrop.Material : FlyoutBackdrop.Solid), "when Windows allows it again the choice applies and the notice closes");
            flyout.Host.Close();
        }

        run.Check(Select(page.ThemeChoiceTiles[0]), "Use system is chosen again");
        await tug.Settle(500);
    }

    // ---- the Settings window keeps its place through all of this ---------------------------------------------------------------------------
    private static async Task WindowPlaceAsync(TugRig tug, SettingsRig settings, SettingsWindow window)
    {
        var run = tug.Run;
        var area = Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;
        var wanted = new RectInt32(area.X + 60, area.Y + 70, 1180, 860);
        window.AppWindow.MoveAndResize(wanted);
        await tug.Settle(1500);
        var saved = settings.Store.Load().SettingsWindow;
        run.Check(saved is { } s && s.X == window.AppWindow.Position.X && s.Y == window.AppWindow.Position.Y && s.Width == window.AppWindow.Size.Width, "the moved window's place was stored: " + saved);

        settings.Store.Save(settings.Store.Load() with { Theme = AppTheme.Dark });
        await tug.Settle(700);
        run.Check(settings.Store.Load().SettingsWindow == saved, "choosing a theme leaves the stored place alone");
        run.Check(window.RootElement.ActualTheme == ElementTheme.Dark, "and the theme was applied");

        settings.Host.Close();
        await tug.Settle(700);
        var reopened = await settings.OpenAsync(SettingsPane.Appearance);
        run.Check(reopened is not null, "the Settings window opens again");
        if (reopened is not null)
        {
            run.Check(reopened.AppWindow.Position.X == saved?.X && reopened.AppWindow.Position.Y == saved?.Y, $"at the stored place: {reopened.AppWindow.Position.X},{reopened.AppWindow.Position.Y}");
            run.Check(reopened.RootElement.ActualTheme == ElementTheme.Dark && Same(reopened.AppWindow.TitleBar.BackgroundColor, ThemeColors.Get("SolidBackgroundFillColorBase", dark: true)), "and is dark from the start (the theme is applied when the window opens)");
            run.Check(reopened.CurrentPage is AppearancePage { ThemeChoiceTiles: var tiles } && Checked(tiles) == 2, "the pane shows Dark as chosen");
            await tug.Settle(1800);
            await PaintedShotAsync(run, reopened, "appearance-reopened-dark");
        }

        settings.Store.Save(PresentationSettings.Defaults with { SettingsWindow = settings.Store.Load().SettingsWindow });
    }

    // ---- the welcome window follows the theme too -----------------------------------------------------------------------------------------------
    private static async Task WelcomeAsync(TugRig tug, SettingsRig settings)
    {
        var run = tug.Run;
        tug.Engine.Push(Scenes.Empty());
        settings.Store.Save(settings.Store.Load() with { Theme = AppTheme.Dark });
        var welcome = WelcomeWindowFactory.Create(tug.Engine, tug.Forms, tug.Text, tug.Time, theme: settings.Theme);
        welcome.Activate();
        await SelfTestRunner.WaitForFrameAsync(welcome, CancellationToken.None, 900);
        var root = (FrameworkElement)welcome.Content;
        var darkBar = ThemeColors.Get("SolidBackgroundFillColorBase", dark: true);
        run.Check(root.ActualTheme == ElementTheme.Dark && Same(welcome.AppWindow.TitleBar.BackgroundColor, darkBar), "the welcome window opens in the dark theme with a dark caption");
        await tug.Settle(700);
        await PaintedShotAsync(run, welcome, "welcome-dark");
        settings.Store.Save(settings.Store.Load() with { Theme = AppTheme.Light });
        await tug.Settle(900);
        run.Check(root.ActualTheme == ElementTheme.Light && Same(welcome.AppWindow.TitleBar.BackgroundColor, ThemeColors.Get("SolidBackgroundFillColorBase", dark: false)), "and turns light while it is open");
        await PaintedShotAsync(run, welcome, "welcome-light");
        welcome.Close();
        await tug.Settle(300);
        run.Check(settings.Theme.Attached == 1, "closing the welcome window detaches it: " + settings.Theme.Attached);
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A screenshot taken once the window has really painted: the first PrintWindow of a window that has only just appeared can come back as a
    /// blank page with a caption, which the blank check does not catch. A throwaway capture is repeated until it shows many colours.
    /// </summary>
    private static async Task PaintedShotAsync(ScenarioRun run, Window window, string label)
    {
        var scratch = Path.Combine(run.Folder, ".painted");
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var (best, _) = await SelfTestRunner.CaptureAsync(window, scratch, "probe");
            var file = Path.Combine(scratch, "probe.png");
            var painted = best is not null && File.Exists(file) && DistinctColours(TrayIconImage.FromPng(File.ReadAllBytes(file))) > 40;
            if (File.Exists(file))
            {
                File.Delete(file);
            }

            if (painted)
            {
                break;
            }

            await Task.Delay(900);
        }

        if (Directory.Exists(scratch) && !Directory.EnumerateFileSystemEntries(scratch).Any())
        {
            Directory.Delete(scratch);
        }

        await run.ShotAsync(window, label);
    }

    /// <summary>How many different colours a coarse grid of the picture's pixels holds.</summary>
    private static int DistinctColours(TrayIconImage image)
    {
        var seen = new HashSet<(byte, byte, byte)>();
        for (var y = 0; y < image.Height; y += 7)
        {
            for (var x = 0; x < image.Width; x += 7)
            {
                var p = image.PixelAt(x, y);
                seen.Add((p.R, p.G, p.B));
            }
        }

        return seen.Count;
    }

    private static IEnumerable<string> Names(IEnumerable<RadioButton> tiles) => tiles.Select(AutomationProperties.GetName);

    /// <summary>The index of the chosen tile, or -1.</summary>
    private static int Checked(IReadOnlyList<RadioButton> tiles)
    {
        var chosen = tiles.Select((t, i) => (t, i)).Where(x => x.t.IsChecked == true).Select(x => x.i).ToList();
        return chosen.Count == 1 ? chosen[0] : -1;
    }

    /// <summary>Chooses a tile the way a screen reader does: the Selection Item pattern of its UI Automation peer.</summary>
    private static bool Select(RadioButton tile)
    {
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(tile);
        if (peer?.GetPattern(PatternInterface.SelectionItem) is not ISelectionItemProvider selection)
        {
            return false;
        }

        selection.Select();
        return true;
    }

    private static bool Same(Windows.UI.Color? actual, Windows.UI.Color wanted) => actual is { } c && c.A == wanted.A && c.R == wanted.R && c.G == wanted.G && c.B == wanted.B;

    /// <summary>The luminance (0 to 255) of the caption and of the pane in the screenshot taken last.</summary>
    private static (int Caption, int Body) Sample(ScenarioRun run, SettingsWindow window)
    {
        if (run.LastShotPath is not { } path || !File.Exists(path))
        {
            return (-1, -1);
        }

        var image = TrayIconImage.FromPng(File.ReadAllBytes(path));
        int Luminance(int x, int y)
        {
            var p = image.PixelAt(Math.Clamp(x, 0, image.Width - 1), Math.Clamp(y, 0, image.Height - 1));
            return ((p.R * 299) + (p.G * 587) + (p.B * 114)) / 1000;
        }

        // The caption's middle (clear of its title and buttons), and the navigation pane under the search box.
        return (Luminance(image.Width / 2, 8), Luminance(40, image.Height - 120));
    }
}
#endif
