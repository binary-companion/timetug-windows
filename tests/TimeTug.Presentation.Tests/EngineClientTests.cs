using TimeTug.Presentation.Engine;
using Pkg = TimeTug.Engine;

namespace TimeTug.Presentation.Tests;

public class EngineClientTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private static Pkg.EngineState StateWith(bool enabled = true) => new()
    {
        Revision = 1,
        Settings = new Pkg.EngineSettings { Takeover = new Pkg.TakeoverSettings { Enabled = enabled } },
    };

    private static async Task<(FakeEngine Engine, EngineClient Client)> StartedAsync()
    {
        var engine = new FakeEngine();
        var client = new EngineClient(engine);
        await client.StartAsync();
        return (engine, client);
    }

    [Fact]
    public async Task A_state_message_updates_State()
    {
        var (engine, client) = await StartedAsync();
        await using (client)
        {
            using var states = new Collector<AppState>(client.State);
            engine.Push(StateWith(enabled: false));
            var seen = await states.WaitForAsync(1);
            seen[0].Enabled.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Disposing_twice_is_harmless_and_disposes_the_engine_once()
    {
        var (engine, client) = await StartedAsync();

        // A dependency-injection container that registers the client under two types disposes it twice.
        await client.DisposeAsync();
        var second = async () => await client.DisposeAsync();

        await second.Should().NotThrowAsync();
        engine.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Start_sends_the_start_command_once()
    {
        var (engine, client) = await StartedAsync();
        await using (client)
        {
            await client.StartAsync();
            engine.Commands.OfType<Pkg.EngineCommand.Start>().Should().ContainSingle();
        }
    }

    [Fact]
    public async Task Adding_an_account_sends_account_add_with_that_kind()
    {
        var (engine, client) = await StartedAsync();
        await using (client)
        {
            await client.AddAccountAsync("google");
            engine.Commands.Should().Contain(new Pkg.EngineCommand.AddAccount("google"));
        }
    }

    [Fact]
    public async Task Takeover_presented_off_screen_sends_onScreen_false()
    {
        var (engine, client) = await StartedAsync();
        await using (client)
        {
            await client.TakeoverPresentedAsync("evt-1", onScreen: false);
            engine.Commands.Should().Contain(new Pkg.EngineCommand.TakeoverPresented("evt-1", false));
        }
    }

    [Fact]
    public async Task A_second_subscriber_gets_the_latest_state_immediately()
    {
        var (engine, client) = await StartedAsync();
        await using (client)
        {
            using var first = new Collector<AppState>(client.State);
            engine.Push(StateWith(enabled: true));
            engine.Push(StateWith(enabled: false));
            await first.WaitForAsync(1);
            await Task.Delay(50);
            using var second = new Collector<AppState>(client.State);
            second.Items.Should().ContainSingle().Which.Enabled.Should().Be(first.Items[^1].Enabled);
        }
    }

    [Fact]
    public async Task Nothing_is_replayed_before_the_first_state()
    {
        var (_, client) = await StartedAsync();
        await using (client)
        {
            using var states = new Collector<AppState>(client.State);
            states.Items.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Set_enabled_false_sends_a_settings_patch_with_only_the_enabled_field()
    {
        var (engine, client) = await StartedAsync();
        await using (client)
        {
            await client.SetEnabledAsync(false);
            var patch = engine.Commands.OfType<Pkg.EngineCommand.ApplySettings>().Should().ContainSingle().Subject;
            patch.Should().Be(new Pkg.EngineCommand.ApplySettings { Enabled = false });
        }
    }

    [Fact]
    public async Task A_settings_patch_goes_out_as_one_settings_apply_with_its_command_id()
    {
        var (engine, client) = await StartedAsync();
        await using (client)
        {
            await client.ApplySettingsAsync(new TimeTug.Presentation.Settings.EngineSettingsPatch { LeadTime = TimeSpan.FromMinutes(2), SkipAllDayEvents = false }, "settings-7");

            engine.Commands.OfType<Pkg.EngineCommand.ApplySettings>().Should().ContainSingle()
                .Which.Should().Be(new Pkg.EngineCommand.ApplySettings { LeadTime = 120, SkipAllDayEvents = false });
            engine.Ids[^1].Should().Be("settings-7");
        }
    }

    [Fact]
    public async Task Forgetting_corrections_sends_dedup_forget()
    {
        var (engine, client) = await StartedAsync();
        await using (client)
        {
            await client.ForgetCorrectionsAsync();

            engine.Commands[^1].Should().Be(new Pkg.EngineCommand.ForgetCorrections());
        }
    }

    [Fact]
    public async Task Duplicate_management_sends_dedup_commands_with_ids_only_and_the_command_id()
    {
        var (engine, client) = await StartedAsync();
        await using (client)
        {
            await client.MergeEventsAsync("evt-1", "evt-2", "flyout-merge-1");
            await client.UnmergeEventAsync("evt-3", "flyout-merge-2");
            await client.SeparateMembersAsync("evt-4", ["design review|1|2", "design review|1|3"], "flyout-merge-3");

            var sent = engine.Commands.Skip(1).ToList();
            sent.Should().HaveCount(3);
            sent[0].Should().BeOfType<Pkg.EngineCommand.Merge>().Which.EventIDs.Should().Equal("evt-1", "evt-2");
            sent[1].Should().Be(new Pkg.EngineCommand.Unmerge("evt-3"));
            var separate = sent[2].Should().BeOfType<Pkg.EngineCommand.Separate>().Subject;
            separate.EventID.Should().Be("evt-4");
            separate.MemberIDs.Should().Equal("design review|1|2", "design review|1|3");
            engine.Ids.Skip(1).Should().Equal("flyout-merge-1", "flyout-merge-2", "flyout-merge-3");
        }
    }

    [Fact]
    public async Task A_diagnostic_carries_the_id_of_the_command_it_answers()
    {
        var (engine, client) = await StartedAsync();
        await using (client)
        {
            using var diagnostics = new Collector<EngineDiagnosticInfo>(client.Diagnostics);
            engine.Push(new Pkg.EngineEvent.Diagnostic(new Pkg.EngineDiagnostic { Level = "warning", Category = "command", Message = "no", CommandId = "settings-7" }));

            (await diagnostics.WaitForAsync(1))[0].CommandId.Should().Be("settings-7");
        }
    }

    [Fact]
    public async Task Snooze_sends_the_duration_in_seconds()
    {
        var (engine, client) = await StartedAsync();
        await using (client)
        {
            await client.SnoozeAsync("evt-1", TimeSpan.FromMinutes(5));
            engine.Commands.Should().Contain(new Pkg.EngineCommand.Snooze("evt-1", 300));
        }
    }

    [Fact]
    public async Task The_other_commands_map_one_to_one()
    {
        var (engine, client) = await StartedAsync();
        await using (client)
        {
            await client.RemoveAccountAsync("c1");
            await client.ReconnectAccountAsync("c2");
            await client.CancelAuthorizationAsync();
            await client.TakeoverClosedAsync("e1");
            await client.JoinAsync("e2");
            await client.DismissAsync("e3");
            await client.TestTugAsync("Standup", "Work");
            await client.SystemEventAsync(Pkg.SystemEventKind.TimeZoneChanged, "Europe/Berlin");
            engine.Commands.Skip(1).Should().Equal(
                new Pkg.EngineCommand.RemoveAccount("c1"),
                new Pkg.EngineCommand.ReconnectAccount("c2"),
                new Pkg.EngineCommand.CancelAuthorization(),
                new Pkg.EngineCommand.TakeoverClosed("e1"),
                new Pkg.EngineCommand.Join("e2"),
                new Pkg.EngineCommand.Dismiss("e3"),
                new Pkg.EngineCommand.TestTakeover("Standup", "Work"),
                new Pkg.EngineCommand.SystemEvent("timeZoneChanged", "Europe/Berlin"));
        }
    }

    [Fact]
    public async Task A_takeover_due_event_reaches_TakeoversDue_as_a_request()
    {
        var (engine, client) = await StartedAsync();
        await using (client)
        {
            using var due = new Collector<TakeoverRequest>(client.TakeoversDue);
            engine.Push(new Pkg.EngineEvent.TakeoverDue(new Pkg.TakeoverRequest
            {
                Event = new Pkg.CalendarEvent { Id = "e1", Title = "Standup", Start = Noon, End = Noon.AddHours(1), OtherAttendeeCount = 3 },
                JoinURL = "https://meet.example/x",
                SnoozeOptions = [60, 300],
                CalendarTitle = "Work",
            }));
            var request = (await due.WaitForAsync(1))[0];
            request.EventId.Should().Be("e1");
            request.JoinUrl.Should().Be("https://meet.example/x");
            request.SnoozeOptions.Should().Equal(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(300));
            request.CalendarTitle.Should().Be("Work");
            request.OtherAttendeeCount.Should().Be(3);
        }
    }

    [Fact]
    public async Task A_withdrawn_event_reaches_TakeoversWithdrawn_and_a_diagnostic_reaches_Diagnostics()
    {
        var (engine, client) = await StartedAsync();
        await using (client)
        {
            using var withdrawn = new Collector<string>(client.TakeoversWithdrawn);
            using var diagnostics = new Collector<EngineDiagnosticInfo>(client.Diagnostics);
            engine.Push(new Pkg.EngineEvent.TakeoverWithdrawn("e9"));
            engine.Push(new Pkg.EngineEvent.Diagnostic(new Pkg.EngineDiagnostic { Level = "warning", Category = "sync", Message = "slow" }));
            (await withdrawn.WaitForAsync(1)).Should().Equal("e9");
            (await diagnostics.WaitForAsync(1))[0].Should().Be(new EngineDiagnosticInfo("warning", "sync", "slow"));
        }
    }

    [Fact]
    public async Task Disposing_the_client_disposes_the_engine()
    {
        var (engine, client) = await StartedAsync();
        await client.DisposeAsync();
        engine.Disposed.Should().BeTrue();
    }
}
