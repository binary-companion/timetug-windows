#if SELFTEST
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using TimeTug.App.Settings;
using TimeTug.App.Theming;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Theming;
using Windows.Storage.Streams;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>settings-calendars</c>: the real Settings window on the Calendars pane over the real app and the real native engine (testing
/// mode, one recorded Google account that lists eight invented calendars, two of them a holiday feed and birthdays, and three whose colour is
/// as dark or as light as the card they sit on). Each control is
/// changed the way a person changes it and the engine's own published state is checked to echo it: a calendar's Tug switch, its visibility
/// checkbox (hiding a tugging calendar also takes it out of the tug list), the group's "All" switch, skip all-day. The inference switch
/// stays disabled and off and the engine's stored value is left alone. It also asks the engine what it does with a patch that contradicts
/// itself (a calendar both hidden and in the tug list), which the app never sends, and writes the answer to the result.
/// <para>
/// The check boxes are drawn in the calendar's colour; the scenario renders each one and measures it, in the Light and the Dark theme: the
/// check mark must read on the fill at 4.5 to 1 and the box's edge on the card at 3 to 1, whatever the calendar's colour, checked or not.
/// Under a (fake) contrast theme the calendar's colour is ignored. Two screenshots show the result.
/// </para>
/// </summary>
internal static class SettingsCalendarsScenario
{
    public const string Name = "settings-calendars";

    private const string Source = "google-conn-1/";
    private const string Me = Source + "me@example.com";
    private const string Team = Source + "team@example.com";
    private const string Cafe = Source + "cafe@example.com";
    private const string Holidays = Source + "en.usa#holiday@group.v.calendar.google.com";
    private const string Birthdays = Source + "addressbook#contacts@group.v.calendar.google.com";
    private const string Night = Source + "night@example.com";
    private const string Graphite = Source + "graphite@example.com";
    private const string Paper = Source + "paper@example.com";

    /// <summary>The calendars of the main list: the first three have ordinary colours, the last three are #0B0F19, #2B2B2B and #FBFBFB (the dark card is #2B2B2B, the light one #FBFBFB).</summary>
    private static readonly string[] Ordinary = [Cafe, Me, Team, Night, Graphite, Paper];

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        await using var rig = await EngineRig.StartAsync(options, cancellation, "google-calendar-list-swatches.json");
        var run = rig.Run;
        if (!rig.Ready)
        {
            return rig.Finish();
        }

        run.Check(await rig.WaitFor(() => rig.Latest is { CalendarGroups.Count: > 0 }), "the engine listed the account's calendars");
        var state = rig.Latest!;
        var main = state.CalendarGroups.SelectMany(g => g.Calendars).Select(c => c.Key).Order().ToList();
        run.Check(main.SequenceEqual(Ordinary.Order()), "six ordinary calendars are listed: " + string.Join(", ", main));
        run.Check(state.SystemCalendars.Select(c => c.Key).Order().SequenceEqual(new[] { Birthdays, Holidays }.Order()), "the holiday feed and the birthdays are set apart: " + string.Join(", ", state.SystemCalendars.Select(c => c.Key)));
        run.Info($"inference before any change: {state.Inference} (stored {state.Settings.InferenceEnabled}), reason '{state.InferenceReason}'");

        var window = await rig.OpenSettingsAsync(SettingsPane.Calendars);
        run.Check(window is not null, "the Settings window opens on Calendars");
        if (window is null)
        {
            return rig.Finish();
        }

        run.Check(await rig.WaitFor(() => window.CurrentPage is CalendarsPage { EmptyState.Visibility: Visibility.Collapsed } page && page.ControlsOf(Me) is not null), "the Calendars page lists the calendars");
        if (window.CurrentPage is not CalendarsPage page)
        {
            return rig.Finish();
        }

        // ---- what the engine says now ------------------------------------------------------------------------------------------
        run.Check(page.ControlsOf(Me)!.Value.Tug.IsOn && !page.ControlsOf(Team)!.Value.Tug.IsOn, "Tug is on for the calendar the engine's settings opt in, and off for the others");
        run.Check(page.ControlsOf(Me)!.Value.Show.IsChecked == true && page.ControlsOf(Cafe)!.Value.Show.IsChecked == true, "every calendar is shown");
        run.Check(page.SkipToggle.IsOn, "skip all-day events is on, as the engine has it");
        run.Check(!page.InferenceToggle.IsEnabled && !page.InferenceToggle.IsOn, "the inference switch is disabled and off");
        run.Check(page.InferenceStatusText.Text == "No on-device model is available on this PC. Using rules only.", "and says why: " + page.InferenceStatusText.Text);
        await rig.Settle(1200);
        await run.ShotAsync(window, "calendars-default");

        // ---- the colour swatches stay legible: light, dark and a contrast theme ---------------------------------------------------------
        await SwatchesAsync(rig, window, page);

        // ---- a calendar's Tug switch --------------------------------------------------------------------------------------------
        page.ControlsOf(Team)!.Value.Tug.IsOn = true;
        run.Check(await rig.WaitFor(() => rig.Latest is { } s && s.Settings.TakeoverCalendarKeys.Contains(Team)), "turning Tug on for Team Sync reaches the engine and comes back in its state");
        run.Check(rig.Latest!.Settings.TakeoverCalendarKeys.Contains(Me) && rig.Latest.Settings.HiddenCalendarKeys.Count == 0, "the other opted-in calendar stays, and nothing was hidden");

        // ---- visibility: hiding a calendar ------------------------------------------------------------------------------------------
        page.ControlsOf(Cafe)!.Value.Show.IsChecked = false;
        run.Check(await rig.WaitFor(() => rig.Latest is { } s && s.Settings.HiddenCalendarKeys.Contains(Cafe)), "hiding Café Sync reaches the engine");
        run.Check(await rig.WaitFor(() => !page.ControlsOf(Cafe)!.Value.Tug.IsEnabled), "its Tug switch is disabled while it is hidden");
        page.ControlsOf(Team)!.Value.Show.IsChecked = false;
        run.Check(await rig.WaitFor(() => rig.Latest is { } s && s.Settings.HiddenCalendarKeys.Contains(Team)), "hiding Team Sync (which tugs) reaches the engine");
        run.Check(!rig.Latest!.Settings.TakeoverCalendarKeys.Contains(Team), "and the same patch took it out of the tug list: " + string.Join(", ", rig.Latest.Settings.TakeoverCalendarKeys));
        run.Check(await rig.WaitFor(() => page.ControlsOf(Team) is { Tug.IsOn: false, Tug.IsEnabled: false }), "its Tug switch shows off and disabled");
        await rig.Settle(1200);
        await run.ShotAsync(window, "calendars-hidden");

        // ---- what the engine does with a patch that contradicts itself --------------------------------------------------------------
        await ProbeNormalisationAsync(rig);

        // ---- "All" for the group -----------------------------------------------------------------------------------------------------
        var group = rig.Latest!.CalendarGroups[0];
        var all = page.AllSwitchOf(group.SourceId, group.AccountName);
        run.Check(all is not null && !all.IsOn, "the group's All switch is off while some calendars do not tug");
        all!.IsOn = true;
        run.Check(await rig.WaitFor(() => rig.Latest is { } s && Ordinary.All(s.Settings.TakeoverCalendarKeys.Contains)), "All turns Tug on for every calendar of the group in the engine");
        run.Check(rig.Latest!.Settings.HiddenCalendarKeys.Count == 0, "and shows the hidden ones, so the lists agree: hidden = " + string.Join(", ", rig.Latest.Settings.HiddenCalendarKeys));
        run.Check(await rig.WaitFor(() => page.ControlsOf(Cafe) is { Show.IsChecked: true, Tug.IsOn: true, Tug.IsEnabled: true }), "the rows show it");
        await rig.Settle(1200);
        await run.ShotAsync(window, "calendars-all");
        all = page.AllSwitchOf(group.SourceId, group.AccountName)!;
        all.IsOn = false;
        run.Check(await rig.WaitFor(() => rig.Latest is { } s && !Ordinary.Any(s.Settings.TakeoverCalendarKeys.Contains)), "All off clears them again");

        // ---- the subscribed and system calendars ---------------------------------------------------------------------------------------
        run.Check(page.Disclosure is { IsExpanded: false }, "the subscribed and system calendars are closed");
        window.Shell.Open(SettingsPane.Calendars, "calendar:" + Holidays);
        run.Check(await rig.WaitFor(() => page.Disclosure is { IsExpanded: true }), "a search result for a calendar in it opens the list");
        // The tint lasts 1.5 s from the request: look for it at once, then take the picture while it is still there.
        run.Check(await rig.WaitFor(() => SettingsHighlight.Hosts(page).Select(SettingsHighlight.OverlayOf).Any(o => o is { Opacity: > 0 }), 1500), "and that calendar is tinted");
        await run.ShotAsync(window, "calendars-system");

        // ---- options ---------------------------------------------------------------------------------------------------------------------
        page.SkipToggle.IsOn = false;
        run.Check(await rig.WaitFor(() => rig.Latest is { Settings.SkipAllDayEvents: false }), "skip all-day events off reaches the engine");
        page.SkipToggle.IsOn = true;
        run.Check(await rig.WaitFor(() => rig.Latest is { Settings.SkipAllDayEvents: true }), "and back on");
        run.Check(rig.Latest is { Settings.InferenceEnabled: false }, "the engine's stored inference value is untouched");
        page.ScrollArea.ChangeView(null, page.ScrollArea.ScrollableHeight, null, disableAnimation: true);
        await rig.Settle(1200);
        await run.ShotAsync(window, "calendars-options");

        // What the engine reports once the switch is on (the app never turns it on: the switch is disabled), then back off.
        await rig.Client.ApplySettingsAsync(new EngineSettingsPatch { InferenceEnabled = true });
        await rig.WaitFor(() => rig.Latest is { Settings.InferenceEnabled: true });
        await rig.Settle(500);
        run.Info($"inference with the stored switch on: {rig.Latest?.Inference}, reason '{rig.Latest?.InferenceReason}'");
        await rig.Client.ApplySettingsAsync(new EngineSettingsPatch { InferenceEnabled = false });
        await rig.WaitFor(() => rig.Latest is { Settings.InferenceEnabled: false });

        // ---- every control is named ------------------------------------------------------------------------------------------------------
        run.Check(ScenarioUi.Unnamed(window).Count == 0, "every control has a name: " + string.Join(", ", ScenarioUi.Unnamed(window)));
        run.Check(AutomationProperties.GetName(page.ControlsOf(Me)!.Value.Tug) == "Tug for Me" && AutomationProperties.GetName(page.ControlsOf(Me)!.Value.Show) == "Show Me in list", "a calendar's switch and checkbox are named after it");
        return rig.Finish();
    }

    /// <summary>
    /// Sends the real engine two patches the app never sends (a calendar in both lists, once through the tug list and once through the hidden
    /// list) and records what it publishes, so the notes say whether the engine normalises an inconsistent patch.
    /// </summary>
    private static async Task ProbeNormalisationAsync(EngineRig rig)
    {
        var run = rig.Run;
        var before = rig.Latest!.Settings;
        await rig.Client.ApplySettingsAsync(new EngineSettingsPatch { TakeoverCalendarKeys = new HashSet<string> { Me, Cafe }, HiddenCalendarKeys = new HashSet<string> { Cafe, Team } });
        await rig.WaitFor(() => rig.Latest is { } s && s.Settings.TakeoverCalendarKeys.Contains(Me));
        await rig.Settle(600);
        var after = rig.Latest!.Settings;
        run.Info("engine probe: sent tug {Me, Cafe} and hidden {Cafe, Team}; the engine now holds tug {" + string.Join(", ", after.TakeoverCalendarKeys.Order()) + "} and hidden {" + string.Join(", ", after.HiddenCalendarKeys.Order()) + "}");
        var normalised = !after.TakeoverCalendarKeys.Contains(Cafe) && after.HiddenCalendarKeys.Contains(Cafe);
        run.Info("engine probe: Café in both lists was " + (normalised ? "changed by the engine (hidden wins: it left the tug list)" : "stored as sent (the engine does not normalise)"));
        run.Check(normalised && after.TakeoverCalendarKeys.Contains(Me), "the engine itself drops a hidden calendar from the tug list when a patch lists it in both, as the Mac's settings do on load");
        // Put it back the way the pane would have left it.
        await rig.Client.ApplySettingsAsync(new EngineSettingsPatch { TakeoverCalendarKeys = new HashSet<string>(before.TakeoverCalendarKeys), HiddenCalendarKeys = new HashSet<string>(before.HiddenCalendarKeys) });
        await rig.WaitFor(() => rig.Latest is { } s && s.Settings.TakeoverCalendarKeys.SetEquals(before.TakeoverCalendarKeys) && s.Settings.HiddenCalendarKeys.SetEquals(before.HiddenCalendarKeys));
    }

    // ---- colour swatches ----------------------------------------------------------------------------------------------------------------

    private static async Task SwatchesAsync(EngineRig rig, SettingsWindow window, CalendarsPage page)
    {
        var run = rig.Run;
        var store = rig.Shell.Services.GetRequiredService<IPresentationSettingsStore>();
        var light = await MeasureThemeAsync(rig, page, dark: false);
        run.Check(light, "in the Light theme every check box shows its mark and its edge, checked and unchecked");
        await run.ShotAsync(window, "calendars-swatches-light");

        store.Save(store.Load() with { Theme = AppTheme.Dark });
        run.Check(await rig.WaitFor(() => page.SwatchLook == "dark"), "choosing Dark redraws the swatches in the dark look while the pane is open: " + page.SwatchLook);
        var dark = await MeasureThemeAsync(rig, page, dark: true);
        run.Check(dark, "in the Dark theme every check box shows its mark and its edge, checked and unchecked");
        await run.ShotAsync(window, "calendars-swatches-dark");

        // A contrast theme ignores the calendar's colour: nothing is set on the check box, so Windows' own colours apply.
        var drawn = page.ControlsOf(Me)!.Value.Show;
        rig.Settings.HighContrast = true;
        rig.Settings.Raise();
        run.Check(await rig.WaitFor(() => page.SwatchLook == "contrast"), "a contrast theme is noticed by the open pane: " + page.SwatchLook);
        run.Check(await rig.WaitFor(() => !ReferenceEquals(page.ControlsOf(Me)!.Value.Show, drawn), 5000), "and the check boxes are built again");
        // (Count, not ContainsKey: a lookup also finds the key in the app's own resources.)
        run.Check(Ordinary.All(k => page.ControlsOf(k) is { } c && c.Show.Resources.Count == 0), "under a contrast theme no check box takes the calendar's colour: nothing is set on any of them");
        rig.Settings.HighContrast = false;
        rig.Settings.Raise();
        store.Save(store.Load() with { Theme = AppTheme.Light });
        run.Check(await rig.WaitFor(() => page.SwatchLook == "light"), "and when it ends the Light look is back: " + page.SwatchLook);
        await rig.Settle(900);
    }

    /// <summary>Measures every ordinary calendar's check box in the theme the pane is in; true when all are legible.</summary>
    private static async Task<bool> MeasureThemeAsync(EngineRig rig, CalendarsPage page, bool dark)
    {
        var run = rig.Run;
        await rig.Settle(1200);
        var surface = SwatchColors.Surface(dark);
        var theme = dark ? "dark" : "light";
        var allGood = true;
        foreach (var key in Ordinary)
        {
            var box = page.ControlsOf(key)!.Value.Show;
            var wanted = SwatchColors.For(Hex(key), dark);
            var filled = await MeasureAsync(box, surface);
            run.Info($"{theme} {NameOf(key)} checked: fill {filled.Fill.ToHex()}, {filled.MarkPixels} mark pixels (best {filled.MarkContrast:0.0} to 1), edge {filled.EdgeContrast:0.0} to 1");
            var ok = box.IsChecked == true && filled.MarkPixels >= 6 && filled.EdgeContrast >= ColorContrast.GraphicMinimum && Near(filled.Fill, wanted.Fill);
            allGood &= run.Check(ok, $"{theme}: the checked box of {NameOf(key)} is filled with its colour, shows a check mark ({filled.MarkPixels} pixels at 4.5 to 1 or better) and an edge at {filled.EdgeContrast:0.0} to 1");
        }

        // The unchecked state: hide the three odd ones, measure the outline, show them again.
        foreach (var key in new[] { Night, Graphite, Paper })
        {
            var box = page.ControlsOf(key)!.Value.Show;
            box.IsChecked = false;
            run.Check(await rig.WaitFor(() => rig.Latest is { } s && s.Settings.HiddenCalendarKeys.Contains(key)), $"{NameOf(key)} is hidden for the unchecked measurement");
            await rig.Settle(500);
            var outline = await MeasureAsync(box, surface);
            run.Info($"{theme} {NameOf(key)} unchecked: edge {outline.EdgeContrast:0.0} to 1");
            allGood &= run.Check(outline.EdgeContrast >= ColorContrast.GraphicMinimum, $"{theme}: the unchecked box of {NameOf(key)} has an outline at {outline.EdgeContrast:0.0} to 1 against the card");
            box.IsChecked = true;
            run.Check(await rig.WaitFor(() => rig.Latest is { } s && !s.Settings.HiddenCalendarKeys.Contains(key)), $"{NameOf(key)} is shown again");
        }

        await rig.Settle(900);
        return allGood;
    }

    private static string Hex(string key) => key switch
    {
        Night => "#0b0f19",
        Graphite => "#2b2b2b",
        Paper => "#fbfbfb",
        Me => "#4285f4",
        Team => "#0b8043",
        _ => "#e67c73",
    };

    private static string NameOf(string key) => key switch
    {
        Night => "Night Owls",
        Graphite => "Graphite",
        Paper => "Paper Trail",
        Me => "Me",
        Team => "Team Sync",
        _ => "Café Sync",
    };

    private static bool Near(Rgb a, Rgb b) => Math.Abs(a.R - b.R) <= 6 && Math.Abs(a.G - b.G) <= 6 && Math.Abs(a.B - b.B) <= 6;

    private readonly record struct BoxLook(Rgb Fill, int MarkPixels, double MarkContrast, double EdgeContrast);

    /// <summary>
    /// Renders the check box as WinUI draws it and measures it: the colour just inside its edge (the fill), how many interior pixels read at
    /// 4.5 to 1 or better against that fill (the check mark), and the contrast of the outermost lines on each side against the card.
    /// </summary>
    private static async Task<BoxLook> MeasureAsync(FrameworkElement box, Rgb surface)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(box);
        var buffer = await bitmap.GetPixelsAsync();
        var bytes = new byte[buffer.Length];
        using (var reader = DataReader.FromBuffer(buffer))
        {
            reader.ReadBytes(bytes);
        }

        int w = bitmap.PixelWidth, h = bitmap.PixelHeight;

        // Premultiplied BGRA, composited over the card.
        Rgb At(int x, int y)
        {
            var i = ((y * w) + x) * 4;
            var rest = 255 - bytes[i + 3];
            return new Rgb(Clamp(bytes[i + 2] + (surface.R * rest / 255)), Clamp(bytes[i + 1] + (surface.G * rest / 255)), Clamp(bytes[i] + (surface.B * rest / 255)));
        }

        int Alpha(int x, int y) => bytes[(((y * w) + x) * 4) + 3];

        int left = w, right = -1, top = h, bottom = -1;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (Alpha(x, y) >= 128)
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        if (right < left || bottom < top)
        {
            return new BoxLook(default, 0, 0, 0);
        }

        // The edge: on each side the better of the two outermost lines (the stroke can straddle a pixel boundary), over the middle half of that side;
        // the box is as visible as its weakest side.
        double Best(IEnumerable<(int X, int Y)> points) => points.Max(p => ColorContrast.Ratio(At(p.X, p.Y), surface));
        var xs = Enumerable.Range(left + ((right - left) / 4), Math.Max(1, (right - left) / 2)).ToList();
        var ys = Enumerable.Range(top + ((bottom - top) / 4), Math.Max(1, (bottom - top) / 2)).ToList();
        var edge = new[]
        {
            Best(xs.SelectMany(x => new[] { (x, top), (x, top + 1) })),
            Best(xs.SelectMany(x => new[] { (x, bottom), (x, bottom - 1) })),
            Best(ys.SelectMany(y => new[] { (left, y), (left + 1, y) })),
            Best(ys.SelectMany(y => new[] { (right, y), (right - 1, y) })),
        }.Min();

        // The fill: just inside the left edge at mid height. The mark: interior pixels that read against it.
        var fill = At(Math.Min(right, left + 3), (top + bottom) / 2);
        var marks = 0;
        var best = 1.0;
        for (var y = top + 3; y <= bottom - 3; y++)
        {
            for (var x = left + 3; x <= right - 3; x++)
            {
                var ratio = ColorContrast.Ratio(At(x, y), fill);
                best = Math.Max(best, ratio);
                if (ratio >= ColorContrast.TextMinimum)
                {
                    marks++;
                }
            }
        }

        return new BoxLook(fill, marks, best, edge);
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);
}
#endif
