using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Tug;

namespace TimeTug.Presentation.Tests.Tug;

/// <summary>
/// A model of the engine side of the host contract: every takeoverDue gets exactly one terminal command
/// (takeoverClosed, join, snooze or dismiss) and none after it. Anything else is recorded as a violation.
/// </summary>
internal sealed class ContractModel
{
    public string? Outstanding { get; set; }

    /// <summary>Requests the engine replaced with a test tug; the host still owes each one a closing command.</summary>
    public HashSet<string> Replaced { get; } = [];

    public List<string> Violations { get; } = [];

    public void OnCommand(string command)
    {
        var colon = command.IndexOf(':', StringComparison.Ordinal);
        var kind = command[..colon];
        var id = command[(colon + 1)..];
        if (kind == "presented")
        {
            if (Outstanding != id)
            {
                Violations.Add($"presented for {id} while outstanding is {Outstanding ?? "nothing"}");
            }

            return;
        }

        if (Outstanding == id)
        {
            Outstanding = null;
        }
        else if (!Replaced.Remove(id))
        {
            Violations.Add($"second or stray terminal command {command} (outstanding {Outstanding ?? "nothing"})");
        }
    }
}

public class TugControllerPropertyTests
{
    private static readonly string[] Ids = ["e1", "e2", "e3"];

    [Fact]
    public async Task Over_500_random_event_sequences_every_request_gets_at_most_one_terminal_command_and_exactly_one_once_it_leaves_shown_toast_and_held()
    {
        for (var seed = 0; seed < 500; seed++)
        {
            await RunSequenceAsync(seed);
        }
    }

    private static async Task RunSequenceAsync(int seed)
    {
        var random = new Random(seed);
        using var rig = new Rig();
        var model = new ContractModel();
        rig.Engine.OnCommand = model.OnCommand;
        var known = new Dictionary<string, TakeoverRequest>();
        var log = new List<string>();

        async Task Step(string name, Func<Task> action)
        {
            log.Add(name);
            await action();
            Check(rig, model, seed, log);
        }

        for (var i = 0; i < 60; i++)
        {
            switch (random.Next(19))
            {
                case 0 or 1 or 2:
                    if (model.Outstanding is null)
                    {
                        var id = Ids[random.Next(Ids.Length)];
                        var start = rig.Time.GetUtcNow().AddMinutes(random.Next(0, 3));
                        var request = Requests.Meeting(id, random.Next(4) == 0 ? null : "https://meet.example.test/" + id) with
                        {
                            Start = start,
                            End = start.AddMinutes(random.Next(5, 45)),
                        };
                        known[id] = request;
                        model.Outstanding = id;
                        await Step("due " + id, async () => await rig.Controller.OnTakeoverDueAsync(request));
                    }

                    break;
                case 3:
                    {
                        var test = Requests.Meeting("test") with { Start = rig.Time.GetUtcNow().AddMinutes(1), End = rig.Time.GetUtcNow().AddMinutes(31) };
                        if (model.Outstanding is { } old && old != "test")
                        {
                            model.Replaced.Add(old);
                        }

                        model.Outstanding = "test";
                        await Step("test tug", async () => await rig.Controller.OnTakeoverDueAsync(test));
                        break;
                    }

                case 4:
                    {
                        var id = Ids[random.Next(Ids.Length)];
                        await Step("withdrawn " + id, async () => await rig.Controller.OnTakeoverWithdrawnAsync(id));
                        break;
                    }

                case 5:
                    {
                        var present = known.Values.Where(_ => random.Next(2) == 0).ToArray();
                        await Step("state " + present.Length, async () => await rig.Controller.OnEngineStateAsync(Requests.StateWith(present)));
                        break;
                    }

                case 6:
                    await Step("lock", () => { rig.Session.Lock(); return Task.CompletedTask; });
                    break;
                case 7:
                    await Step("unlock", async () =>
                    {
                        rig.Session.Unlock();
                        await rig.Controller.OnSessionUnlockedAsync();
                    });
                    break;
                case 8:
                    await Step("fullscreen on", () => { rig.Notifications.CanShowFullScreen = false; return Task.CompletedTask; });
                    break;
                case 9:
                    await Step("fullscreen ended", async () =>
                    {
                        rig.Notifications.EndFullScreen();
                        await rig.Controller.OnFullScreenEndedAsync();
                    });
                    break;
                case 10:
                    await Step("toast policy", () =>
                    {
                        rig.Toasts.Accept = random.Next(2) == 0;
                        rig.Notifications.ToastsAllowed = random.Next(4) != 0;
                        rig.Toasts.Throw = random.Next(8) == 0;
                        return Task.CompletedTask;
                    });
                    break;
                case 11:
                    await Step("monitors", async () =>
                    {
                        var count = random.Next(0, 4);
                        var primary = random.Next(Math.Max(count, 1));
                        rig.Monitors.Set(Enumerable.Range(0, count)
                            .Select(n => new MonitorInfo("m" + n, n * 1920, 0, 1920, 1080, n == primary)).ToArray());
                        await rig.Controller.OnMonitorsChangedAsync();
                    });
                    break;
                case 12:
                    await Step("join", async () => await rig.Controller.JoinAsync());
                    break;
                case 13:
                    await Step("snooze", async () => await rig.Controller.SnoozeAsync(TimeSpan.FromMinutes(5)));
                    break;
                case 14:
                    await Step("dismiss", async () => await rig.Controller.DismissAsync());
                    break;
                case 15:
                    await Step("launcher", () => { rig.Launcher.Succeeds = random.Next(2) == 0; return Task.CompletedTask; });
                    break;
                case 16:
                    await Step("focus", async () => await rig.Controller.ReportFocusAsync(random.Next(2) == 0));
                    break;
                case 17:
                    await Step("time", () => { rig.Time.Advance(TimeSpan.FromMinutes(random.Next(0, 40))); return Task.CompletedTask; });
                    break;
                default:
                    {
                        // A toast action, possibly for a request that has since gone.
                        var id = Ids[random.Next(Ids.Length)];
                        var which = random.Next(3);
                        await Step("toast action " + which + " " + id, async () =>
                        {
                            switch (which)
                            {
                                case 0: await rig.Controller.JoinAsync(id); break;
                                case 1: await rig.Controller.SnoozeAsync(id, TimeSpan.FromMinutes(1)); break;
                                default: await rig.Controller.DismissAsync(id); break;
                            }
                        });
                        break;
                    }
            }
        }

        await Step("quit", async () => await rig.Controller.OnApplicationQuittingAsync());
        model.Outstanding.Should().BeNull($"quitting must leave no request outstanding (seed {seed}: {string.Join(", ", log)})");
        model.Replaced.Should().BeEmpty($"every replaced request was closed (seed {seed})");
    }

    private static void Check(Rig rig, ContractModel model, int seed, List<string> log)
    {
        var context = $"seed {seed}: {string.Join(", ", log)}";
        model.Violations.Should().BeEmpty(context);
        var controller = rig.Controller;

        if (model.Outstanding is null)
        {
            controller.State.Should().Be(TugState.Hidden, context);
            controller.Current.Should().BeNull(context);
        }
        else
        {
            controller.State.Should().BeOneOf([TugState.Shown, TugState.ShownWithoutFocus, TugState.Toast, TugState.HeldUntilReturn], context);
            controller.Current!.EventId.Should().Be(model.Outstanding, context);
        }

        var plan = controller.Plan;
        var expectedMode = controller.State switch
        {
            TugState.Shown or TugState.ShownWithoutFocus => TugMode.FullScreen,
            TugState.Toast => TugMode.Toast,
            _ => TugMode.None,
        };
        plan.Mode.Should().Be(expectedMode, context);
        if (plan.Mode == TugMode.FullScreen)
        {
            plan.Monitors.Count(m => m.Role == TugMonitorRole.Primary).Should().Be(rig.Monitors.Monitors.Count == 0 ? 0 : 1, context);
            plan.Monitors.Should().HaveCount(rig.Monitors.Monitors.Count, context);
        }

        controller.NeedsAttention.Should().Be(controller.NeedsAttention && controller.State == TugState.HeldUntilReturn, context);
    }
}
