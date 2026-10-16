using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using TimeTug.Platform.OsState;
using Xunit.Abstractions;

namespace TimeTug.Platform.Tests.OsState;

/// <summary>Runs only when <c>TIMETUG_OS_SMOKE=1</c>: the checks need an interactive user session with a desktop.</summary>
public sealed class OsSmokeFactAttribute : FactAttribute
{
    public OsSmokeFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TIMETUG_OS_SMOKE") != "1")
        {
            Skip = "Set TIMETUG_OS_SMOKE=1 and run in an interactive user session (prlctl exec --current-user on the VM).";
        }
    }
}

/// <summary>Runs only when <c>TIMETUG_OS_TIMECHANGE=1</c>: waits for someone else to change the clock and the time zone while it runs.</summary>
public sealed class OsTimeChangeFactAttribute : FactAttribute
{
    public OsTimeChangeFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TIMETUG_OS_TIMECHANGE") != "1")
        {
            Skip = "Set TIMETUG_OS_TIMECHANGE=1, then change the clock (Set-Date) and the time zone (tzutil /s) from an elevated session within a minute.";
        }
    }
}

/// <summary>Thin checks of the real Windows APIs behind the OS-state services. The logic around them is tested with fakes.</summary>
public sealed class RealApiSmokeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task The_message_window_is_created_receives_a_message_and_closes()
    {
        var received = new List<WindowMessage>();
        var window = new MessageWindow();
        try
        {
            IWindowMessageSource source = window;
            source.Handle.Should().NotBe(0);
            source.MessageReceived += received.Add;

            PInvoke.SendMessage(new HWND(source.Handle), WindowMessage.DisplayChange, 0, 0);
            PInvoke.SendMessage(new HWND(source.Handle), 0x0400, 0, 0);    // WM_USER: not forwarded

            await Wait.UntilAsync(() => received.Count == 1, "the forwarded message");
            received.Should().ContainSingle().Which.Id.Should().Be(WindowMessage.DisplayChange);
        }
        finally
        {
            window.Dispose();
        }

        window.Dispose();   // twice is fine
    }

    [Fact]
    public async Task Two_message_windows_each_get_their_own_messages()
    {
        using var first = new MessageWindow();
        using var second = new MessageWindow();
        var a = 0;
        var b = 0;
        first.MessageReceived += _ => Interlocked.Increment(ref a);
        second.MessageReceived += _ => Interlocked.Increment(ref b);

        PInvoke.SendMessage(new HWND(((IWindowMessageSource)first).Handle), WindowMessage.TimeChange, 0, 0);
        PInvoke.SendMessage(new HWND(((IWindowMessageSource)second).Handle), WindowMessage.TimeChange, 0, 0);
        PInvoke.SendMessage(new HWND(((IWindowMessageSource)second).Handle), WindowMessage.TimeChange, 0, 0);

        await Wait.UntilAsync(() => Volatile.Read(ref a) == 1 && Volatile.Read(ref b) == 2, "per-window delivery");
    }

    [OsSmokeFact]
    public async Task A_system_wide_broadcast_reaches_the_hidden_window()
    {
        using var window = new MessageWindow();
        var got = 0;
        window.MessageReceived += message =>
        {
            if (message.Id == WindowMessage.SettingChange)
            {
                Interlocked.Increment(ref got);
            }
        };

        // A harmless WM_SETTINGCHANGE broadcast (apps just re-read their settings). Every top-level window gets it;
        // message-only windows do not, which is why the window is a hidden top-level window. (Windows refuses a
        // WM_TIMECHANGE broadcast from a user-mode app, so that message cannot be exercised this way.)
        output.WriteLine("SendMessageTimeout returned " + Broadcast(WindowMessage.SettingChange));

        await Wait.UntilAsync(() => Volatile.Read(ref got) >= 1, "the broadcast");
        output.WriteLine("broadcast WM_SETTINGCHANGE reached the hidden top-level window");
    }

    [OsTimeChangeFact]
    public async Task A_real_clock_and_time_zone_change_reach_the_bridge()
    {
        using var window = new MessageWindow();
        using var network = new NetworkAvailability();
        var engine = new RecordingEngineClient();
        using var bridge = new SystemEventsBridge(engine, window, network, TimeProvider.System, new RecordingDiagnostics());
        output.WriteLine("listening; zone now " + SystemEventsBridge.ReadWindowsTimeZoneAsIana());
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "timetug-listening.txt"), "ready");

        await Wait.UntilAsync(() => engine.Events.Contains("clockChanged") && engine.Events.Any(e => e.StartsWith("timeZoneChanged:", StringComparison.Ordinal)), "a clock change and a time zone change", timeoutMs: 90000);

        output.WriteLine("events: " + string.Join(", ", engine.Events));
    }

    [OsSmokeFact]
    public void The_session_is_unlocked_and_lock_notifications_register_on_the_hidden_window()
    {
        SessionState.QueryIsLocked().Should().BeFalse("the smoke tests run in a signed-in, unlocked session");
        using var window = new MessageWindow();
        var diagnostics = new RecordingDiagnostics();

        using var session = new SessionState(window, diagnostics: diagnostics);

        session.IsLocked.Should().BeFalse();
        diagnostics.Entries.Should().BeEmpty("registration succeeded");
    }

    [OsSmokeFact]
    public void The_notification_state_can_be_read()
    {
        using var state = new NotificationState(TimeProvider.System);
        output.WriteLine($"CanShowFullScreen={state.CanShowFullScreen} ToastsAllowed={state.ToastsAllowed}");
        state.CanShowFullScreen.Should().BeTrue("no exclusive full-screen app is running during the smoke test");
    }

    [OsSmokeFact]
    public void The_real_monitors_are_enumerated_with_one_primary()
    {
        var monitors = MonitorService.Enumerate();
        foreach (var monitor in monitors)
        {
            output.WriteLine(monitor.ToString());
        }

        monitors.Should().NotBeEmpty();
        monitors.Count(m => m.IsPrimary).Should().Be(1);
        monitors.Should().OnlyContain(m => m.Width > 0 && m.Height > 0 && m.Id.Length > 0);
    }

    [OsSmokeFact]
    public void The_ui_settings_can_be_read()
    {
        using var settings = new UiSettings();
        output.WriteLine($"Animations={settings.AnimationsEnabled} Transparency={settings.TransparencyEffectsEnabled} HighContrast={settings.HighContrast} LightTaskbar={settings.TaskbarUsesLightTheme}");
        settings.HighContrast.Should().BeFalse();
    }

    [OsSmokeFact]
    public void The_time_zone_converts_to_an_iana_id()
    {
        var zone = SystemEventsBridge.ReadWindowsTimeZoneAsIana();
        output.WriteLine($"Windows zone {TimeZoneInfo.Local.Id} -> {zone}");
        zone.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void The_toast_payload_loads_in_windows_xml_and_makes_a_toast_notification()
    {
        var request = new Presentation.Engine.TakeoverRequest(
            "e1", "Design review & Q&A", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(30), false, false,
            "https://meet.example.test/x", [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5)], "Work", 2);
        var document = new Windows.Data.Xml.Dom.XmlDocument();

        document.LoadXml(ToastXml.Build(request, new KeyLocalizer()));
        var toast = new Windows.UI.Notifications.ToastNotification(document);

        toast.Content.GetXml().Should().Contain("ToastGeneric");
    }

    [OsSmokeFact]
    public void The_app_notification_manager_is_reported_for_a_process_without_package_identity()
    {
        try
        {
            using var notifier = new WindowsAppNotifier();
            output.WriteLine("AppNotificationManager.Setting enabled = " + notifier.IsEnabled);
        }
        catch (Exception e)
        {
            output.WriteLine("AppNotificationManager unavailable without package identity: " + e.GetType().Name + " 0x" + e.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public async Task The_real_launcher_refuses_a_file_path_without_calling_windows()
    {
        (await new UrlLauncher().LaunchAsync("file:///C:/Windows/System32/calc.exe")).Should().BeFalse();
        (await new UrlLauncher().LaunchAsync("ms-settings:")).Should().BeFalse();
    }

    private static unsafe string Broadcast(uint message)
    {
        nuint result;
        var returned = PInvoke.SendMessageTimeout(new HWND(0xFFFF), message, 0, 0, SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_NORMAL, 10000, &result);
        return returned.Value + " (error " + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ")";
    }
}
