using Microsoft.Extensions.Time.Testing;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Tests.Welcome;

namespace TimeTug.Presentation.Tests.Settings;

public class SettingsClientTests
{
    /// <summary>A scripted engine: it keeps settings, applies the patches it is told to, and publishes states with a rising revision.</summary>
    private sealed class Rig : IDisposable
    {
        private EngineSettingsView _settings = EngineSettingsView.Defaults;
        private ulong _revision = 1;

        public Rig(bool answerAtOnce = true)
        {
            Engine = new FakeEngineClient();
            Time = new FakeTimeProvider();
            Client = new SettingsClient(Engine, Time, TimeSpan.FromSeconds(5));
            Publish();
            if (answerAtOnce)
            {
                Engine.OnApplySettings = (patch, _) => Accept(patch);
            }
            else
            {
                Engine.OnApplySettings = (patch, id) => Pending.Enqueue((patch, id));
            }
        }

        public FakeEngineClient Engine { get; }

        public FakeTimeProvider Time { get; }

        public SettingsClient Client { get; }

        public System.Collections.Concurrent.ConcurrentQueue<(EngineSettingsPatch Patch, string? Id)> Pending { get; } = new();

        public EngineSettingsView Settings => _settings;

        /// <summary>The engine takes the patch and publishes its new state.</summary>
        public void Accept(EngineSettingsPatch patch)
        {
            _settings = _settings with
            {
                Enabled = patch.Enabled ?? _settings.Enabled,
                LeadTime = patch.LeadTime ?? _settings.LeadTime,
                RequireConferenceLink = patch.RequireConferenceLink ?? _settings.RequireConferenceLink,
                RequireOtherAttendees = patch.RequireOtherAttendees ?? _settings.RequireOtherAttendees,
                SkipAllDayEvents = patch.SkipAllDayEvents ?? _settings.SkipAllDayEvents,
                InferenceEnabled = patch.InferenceEnabled ?? _settings.InferenceEnabled,
                HiddenCalendarKeys = patch.HiddenCalendarKeys ?? _settings.HiddenCalendarKeys,
                TakeoverCalendarKeys = patch.TakeoverCalendarKeys ?? _settings.TakeoverCalendarKeys,
            };
            _revision++;
            Publish();
        }

        /// <summary>Waits for the next patch the client sends, then has the engine take it.</summary>
        public async Task AcceptNextAsync()
        {
            var (patch, _) = await NextPendingAsync();
            Accept(patch);
        }

        public async Task<(EngineSettingsPatch Patch, string? Id)> NextPendingAsync()
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                if (Pending.TryDequeue(out var next))
                {
                    return next;
                }

                await Task.Delay(2);
            }

            throw new TimeoutException("The client sent no patch.");
        }

        /// <summary>A state the engine publishes for some other reason: the same settings, a higher revision.</summary>
        public void PublishUnrelated()
        {
            _revision++;
            Publish();
        }

        public void RepublishSameRevision() => Publish();

        public void Reject(string? id, string message) =>
            Engine.Push(new EngineDiagnosticInfo("warning", "command", message) { CommandId = id });

        private void Publish() =>
            Engine.Push(new AppState([], [], [], null, null, false, _settings.Enabled) { Revision = _revision, Settings = _settings });

        public void Dispose() => Client.Dispose();
    }

    [Fact]
    public async Task One_edit_sends_exactly_one_patch_with_only_the_edited_field()
    {
        using var rig = new Rig();

        var result = await rig.Client.ApplyAsync(p => p with { LeadTime = TimeSpan.FromMinutes(5) });

        result.Should().Be(SettingsResult.Applied);
        var sent = rig.Engine.Patches.Should().ContainSingle().Subject;
        sent.Patch.LeadTime.Should().Be(TimeSpan.FromMinutes(5));
        sent.Patch.Enabled.Should().BeNull();
        sent.Patch.RequireConferenceLink.Should().BeNull();
        sent.Patch.RequireOtherAttendees.Should().BeNull();
        sent.Patch.SkipAllDayEvents.Should().BeNull();
        sent.Patch.InferenceEnabled.Should().BeNull();
        sent.Patch.HiddenCalendarKeys.Should().BeNull();
        sent.Patch.TakeoverCalendarKeys.Should().BeNull();
        rig.Engine.Sent.Should().Equal("settings");
        sent.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task An_edit_sees_what_the_engine_last_published_and_starts_from_an_empty_patch()
    {
        using var rig = new Rig();
        await rig.Client.ApplyAsync(p => p with { SkipAllDayEvents = false });
        EngineSettingsPatch? seen = null;

        await rig.Client.ApplyAsync(p =>
        {
            seen = p;
            return p with { LeadTime = TimeSpan.FromMinutes(2) };
        });

        seen!.IsEmpty.Should().BeTrue();
        seen.Current.SkipAllDayEvents.Should().BeFalse("Current is the engine's published settings, not the defaults");
    }

    [Fact]
    public async Task Ids_are_different_for_each_patch()
    {
        using var rig = new Rig();

        await rig.Client.ApplyAsync(p => p with { Enabled = false });
        await rig.Client.ApplyAsync(p => p with { Enabled = true });

        rig.Engine.Patches.Select(p => p.Id).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task Two_rapid_edits_of_the_tug_list_do_not_lose_the_first()
    {
        using var rig = new Rig(answerAtOnce: false);
        var first = rig.Client.ApplyAsync(p => p with { TakeoverCalendarKeys = new HashSet<string>(p.Current.TakeoverCalendarKeys) { "a/1" } }).AsTask();
        var second = rig.Client.ApplyAsync(p => p with { TakeoverCalendarKeys = new HashSet<string>(p.Current.TakeoverCalendarKeys) { "b/2" } }).AsTask();

        rig.Engine.Patches.Should().ContainSingle("the second edit waits until the engine has answered the first");
        first.IsCompleted.Should().BeFalse();

        await rig.AcceptNextAsync();
        (await first).Should().Be(SettingsResult.Applied);
        await rig.AcceptNextAsync();
        rig.Engine.Patches.Should().HaveCount(2);
        rig.Engine.Patches[1].Patch.TakeoverCalendarKeys.Should().BeEquivalentTo("a/1", "b/2");
        (await second).Should().Be(SettingsResult.Applied);
        rig.Settings.TakeoverCalendarKeys.Should().BeEquivalentTo("a/1", "b/2");
    }

    [Fact]
    public async Task Edits_of_different_fields_are_separate_patches_that_cannot_overwrite_each_other()
    {
        using var rig = new Rig(answerAtOnce: false);
        var lead = rig.Client.ApplyAsync(p => p with { LeadTime = TimeSpan.FromMinutes(3) }).AsTask();
        var link = rig.Client.ApplyAsync(p => p with { RequireConferenceLink = true }).AsTask();

        await rig.AcceptNextAsync();
        await lead;
        await rig.AcceptNextAsync();
        await link;

        rig.Engine.Patches[0].Patch.RequireConferenceLink.Should().BeNull();
        rig.Engine.Patches[1].Patch.LeadTime.Should().BeNull();
        rig.Settings.LeadTime.Should().Be(TimeSpan.FromMinutes(3));
        rig.Settings.RequireConferenceLink.Should().BeTrue();
    }

    [Fact]
    public async Task A_patch_the_engine_refuses_comes_back_rejected_with_its_message_and_sends_nothing_else()
    {
        using var rig = new Rig();
        rig.Engine.OnApplySettings = (_, id) => rig.Reject(id, "lead time out of range");

        var result = await rig.Client.ApplyAsync(p => p with { LeadTime = TimeSpan.FromDays(9) });

        result.IsApplied.Should().BeFalse();
        result.Reason.Should().Be("lead time out of range");
        rig.Engine.Sent.Should().Equal("settings");
        rig.Settings.LeadTime.Should().Be(TimeSpan.FromSeconds(60), "the engine's last published value is untouched");
    }

    [Fact]
    public async Task A_rejection_does_not_block_the_next_edit()
    {
        using var rig = new Rig();
        rig.Engine.OnApplySettings = (_, id) => rig.Reject(id, "no");
        await rig.Client.ApplyAsync(p => p with { LeadTime = TimeSpan.FromDays(9) });
        rig.Engine.OnApplySettings = (patch, _) => rig.Accept(patch);

        var result = await rig.Client.ApplyAsync(p => p with { LeadTime = TimeSpan.FromMinutes(2) });

        result.Should().Be(SettingsResult.Applied);
        rig.Settings.LeadTime.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task A_diagnostic_for_some_other_command_does_not_reject_this_one()
    {
        using var rig = new Rig(answerAtOnce: false);
        var task = rig.Client.ApplyAsync(p => p with { Enabled = false }).AsTask();

        rig.Reject("some-other-command", "unknown event id");
        rig.Reject(null, "a diagnostic with no command");
        task.IsCompleted.Should().BeFalse();
        await rig.AcceptNextAsync();

        (await task).Should().Be(SettingsResult.Applied);
    }

    [Fact]
    public async Task An_engine_that_never_answers_is_rejected_after_the_timeout_and_the_next_edit_still_goes_out()
    {
        using var rig = new Rig(answerAtOnce: false);
        var task = rig.Client.ApplyAsync(p => p with { Enabled = false }).AsTask();
        task.IsCompleted.Should().BeFalse();

        rig.Time.Advance(TimeSpan.FromSeconds(6));
        var result = await task;

        result.IsApplied.Should().BeFalse();
        result.Reason.Should().NotBeNullOrWhiteSpace();
        var next = rig.Client.ApplyAsync(p => p with { LeadTime = TimeSpan.FromMinutes(9) }).AsTask();
        rig.Engine.Patches.Should().HaveCount(2);
        await rig.NextPendingAsync();
        await rig.AcceptNextAsync();
        (await next).Should().Be(SettingsResult.Applied);
    }

    [Fact]
    public async Task A_state_that_is_not_newer_does_not_count_as_the_answer()
    {
        using var rig = new Rig(answerAtOnce: false);
        var task = rig.Client.ApplyAsync(p => p with { Enabled = false }).AsTask();

        rig.RepublishSameRevision();
        task.IsCompleted.Should().BeFalse();
        await rig.AcceptNextAsync();

        (await task).Should().Be(SettingsResult.Applied);
    }

    [Fact]
    public async Task A_state_that_arrives_before_the_send_returns_still_answers()
    {
        using var rig = new Rig();
        rig.Engine.OnApplySettings = (patch, _) =>
        {
            rig.Accept(patch);
            rig.PublishUnrelated();
        };

        var result = await rig.Client.ApplyAsync(p => p with { RequireOtherAttendees = true });

        result.Should().Be(SettingsResult.Applied);
    }

    // ---- a state answers a patch only when it contains the change ------------------------------------------------

    [Fact]
    public async Task An_unrelated_newer_state_does_not_answer_and_a_later_rejection_still_counts()
    {
        using var rig = new Rig(answerAtOnce: false);
        var task = rig.Client.ApplyAsync(p => p with { LeadTime = TimeSpan.FromMinutes(5) }).AsTask();
        var (_, id) = await rig.NextPendingAsync();

        rig.PublishUnrelated();
        task.IsCompleted.Should().BeFalse("a state without the patched value is not the engine's answer to the patch");
        rig.Reject(id, "lead time out of range");
        var result = await task;

        result.IsApplied.Should().BeFalse();
        result.Reason.Should().Be("lead time out of range");
    }

    [Fact]
    public async Task An_unrelated_newer_state_then_the_state_with_the_change_is_applied()
    {
        using var rig = new Rig(answerAtOnce: false);
        var task = rig.Client.ApplyAsync(p => p with { RequireConferenceLink = true }).AsTask();

        rig.PublishUnrelated();
        rig.PublishUnrelated();
        task.IsCompleted.Should().BeFalse();
        await rig.AcceptNextAsync();

        (await task).Should().Be(SettingsResult.Applied);
        rig.Settings.RequireConferenceLink.Should().BeTrue();
    }

    [Fact]
    public async Task Two_rapid_edits_with_unrelated_states_between_them_are_each_answered_by_their_own_state()
    {
        using var rig = new Rig(answerAtOnce: false);
        var first = rig.Client.ApplyAsync(p => p with { LeadTime = TimeSpan.FromMinutes(3) }).AsTask();
        var second = rig.Client.ApplyAsync(p => p with { RequireOtherAttendees = true }).AsTask();

        rig.PublishUnrelated();
        first.IsCompleted.Should().BeFalse();
        await rig.AcceptNextAsync();
        (await first).Should().Be(SettingsResult.Applied);

        var (_, secondId) = await rig.NextPendingAsync();
        rig.PublishUnrelated();
        second.IsCompleted.Should().BeFalse("the first edit's state does not answer the second edit");
        rig.Reject(secondId, "no");

        (await second).IsApplied.Should().BeFalse();
        rig.Settings.LeadTime.Should().Be(TimeSpan.FromMinutes(3));
        rig.Settings.RequireOtherAttendees.Should().BeFalse();
    }

    [Fact]
    public async Task A_newer_state_without_the_change_is_rejected_at_the_timeout()
    {
        using var rig = new Rig(answerAtOnce: false);
        var task = rig.Client.ApplyAsync(p => p with { Enabled = false }).AsTask();
        rig.PublishUnrelated();

        rig.Time.Advance(TimeSpan.FromSeconds(6));
        var result = await task;

        result.IsApplied.Should().BeFalse();
        result.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_key_list_the_engine_normalised_still_counts_as_taken()
    {
        using var rig = new Rig(answerAtOnce: false);
        var task = rig.Client.ApplyAsync(p => p with
        {
            TakeoverCalendarKeys = new HashSet<string> { "me", "cafe" },
            HiddenCalendarKeys = new HashSet<string> { "cafe", "team" },
        }).AsTask();
        var (patch, _) = await rig.NextPendingAsync();

        // The engine lets hidden win: the tug list it stores loses the hidden key.
        rig.Accept(patch with { TakeoverCalendarKeys = new HashSet<string> { "me" } });

        (await task).Should().Be(SettingsResult.Applied);
    }

    [Fact]
    public async Task A_patch_the_engine_took_is_applied_whatever_else_changed_in_the_same_state()
    {
        using var rig = new Rig(answerAtOnce: false);
        var task = rig.Client.ApplyAsync(p => p with { SkipAllDayEvents = false }).AsTask();
        var (patch, _) = await rig.NextPendingAsync();

        rig.Accept(patch with { LeadTime = TimeSpan.FromMinutes(7) });

        (await task).Should().Be(SettingsResult.Applied);
    }

    // ---- the engine cannot be reached ----------------------------------------------------------------------------

    [Fact]
    public async Task A_send_that_throws_comes_back_rejected_and_does_not_block_the_next_edit()
    {
        using var rig = new Rig();
        rig.Engine.OnApplySettings = (_, _) => throw new InvalidOperationException("the engine process has exited");

        var result = await rig.Client.ApplyAsync(p => p with { Enabled = false });

        result.IsApplied.Should().BeFalse();
        result.Reason.Should().NotBeNullOrWhiteSpace();
        result.Reason.Should().NotContain("exited", "the message is for the person, not the exception text");
        rig.Engine.OnApplySettings = (patch, _) => rig.Accept(patch);
        (await rig.Client.ApplyAsync(p => p with { Enabled = false })).Should().Be(SettingsResult.Applied);
    }

    [Fact]
    public async Task A_send_that_fails_asynchronously_is_rejected_too()
    {
        using var rig = new Rig();
        rig.Engine.ApplySettingsFailure = new IOException("pipe closed");

        var result = await rig.Client.ApplyAsync(p => p with { LeadTime = TimeSpan.FromMinutes(2) });

        result.IsApplied.Should().BeFalse();
    }

    [Fact]
    public async Task A_patch_that_changes_nothing_is_not_sent()
    {
        using var rig = new Rig();

        var result = await rig.Client.ApplyAsync(p => p with { LeadTime = TimeSpan.FromSeconds(60), SkipAllDayEvents = true });

        result.Should().Be(SettingsResult.Applied);
        rig.Engine.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task An_edit_that_sets_nothing_sends_nothing()
    {
        using var rig = new Rig();

        var result = await rig.Client.ApplyAsync(p => p);

        result.Should().Be(SettingsResult.Applied);
        rig.Engine.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task An_edit_that_throws_propagates_and_does_not_block_the_next_edit()
    {
        using var rig = new Rig();

        var failing = async () => await rig.Client.ApplyAsync(_ => throw new InvalidOperationException("bad edit"));
        await failing.Should().ThrowAsync<InvalidOperationException>();
        var result = await rig.Client.ApplyAsync(p => p with { Enabled = false });

        result.Should().Be(SettingsResult.Applied);
        rig.Engine.Patches.Should().ContainSingle();
    }

    [Fact]
    public async Task Before_the_first_state_the_edit_starts_from_the_engines_defaults()
    {
        var engine = new FakeEngineClient();
        var time = new FakeTimeProvider();
        using var client = new SettingsClient(engine, time, TimeSpan.FromSeconds(5));
        EngineSettingsPatch? seen = null;
        engine.OnApplySettings = (_, _) => engine.Push(new AppState([], [], [], null, null, false, false) { Revision = 1, Settings = EngineSettingsView.Defaults with { Enabled = false } });

        var result = await client.ApplyAsync(p =>
        {
            seen = p;
            return p with { Enabled = false };
        });

        result.Should().Be(SettingsResult.Applied);
        seen!.Current.Should().Be(EngineSettingsView.Defaults);
    }

    [Fact]
    public async Task Forgetting_learned_corrections_sends_one_forget_command()
    {
        using var rig = new Rig();

        await rig.Client.ForgetLearnedCorrectionsAsync();

        rig.Engine.Sent.Should().Equal("forget");
    }

    [Fact]
    public async Task After_dispose_an_edit_is_refused()
    {
        var rig = new Rig();
        rig.Dispose();

        var late = async () => await rig.Client.ApplyAsync(p => p with { Enabled = false });

        await late.Should().ThrowAsync<ObjectDisposedException>();
    }
}
