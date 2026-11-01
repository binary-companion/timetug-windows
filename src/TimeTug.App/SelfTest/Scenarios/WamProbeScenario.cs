#if SELFTEST
#pragma warning disable
using System.Text.Json;
using TimeTug.App.Views;
using TimeTug.Platform.Spike;
using Windows.Storage;
using WinRT.Interop;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>THROWAWAY spike scenario <c>wam-probe</c>: --probe "step|step" with step = mode;key=value;key=value. Not merged.</summary>
internal static class WamProbeScenario
{
    public const string Name = "wam-probe";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        var run = new ScenarioRun(options);
        var note = (string s) => { lock (run) { run.Info(s); } };
        string clientId = "";
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeTug", "dev-oauth.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            clientId = doc.RootElement.GetProperty("microsoft").GetProperty("clientId").GetString() ?? "";
        }
        catch (Exception ex) { note("clientId: not read " + ex.GetType().Name); }
        note($"clientId: present={(clientId.Length > 0)}");

        var window = new PlaceholderWindow { Title = "TimeTug spike parent" };
        window.Activate();
        await SelfTestRunner.WaitForFrameAsync(window, cancellation, 800);
        var hwnd = WindowNative.GetWindowHandle(window);

        string stateFile;
        try { stateFile = Path.Combine(ApplicationData.Current.LocalFolder.Path, "wam-probe-state.json"); } catch { stateFile = Path.Combine(Path.GetTempPath(), "wam-probe-state.json"); }
        if (clientId.Length == 0) clientId = "00000000-0000-0000-0000-000000000000";
        var probe = new WamProbe(clientId, stateFile, hwnd, note);
        var spec = string.IsNullOrEmpty(options.Probe) ? "env|list;os=1|silent;kind=os" : options.Probe;

        foreach (var step in spec.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = step.Split(';', StringSplitOptions.RemoveEmptyEntries);
            var mode = parts[0].Trim();
            var kv = parts.Skip(1).Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0].Trim(), p => p[1].Trim());
            string G(string k, string d) => kv.TryGetValue(k, out var v) ? v : d;
            note($"=== step {mode} {string.Join(" ", kv.Select(p => p.Key + "=" + (p.Key == "login" ? "<hint>" : p.Value)))}");
            try
            {
                switch (mode)
                {
                    case "env": probe.Env(); break;
                    case "list": await probe.ListAsync(G("os", "1") == "1", G("aud", "common")); break;
                    case "silent": await probe.SilentAsync(G("kind", "os"), G("scopes", "short"), G("login", "nobody@example.invalid"), G("force", "0") == "1", G("aud", "common"), G("save", "0") == "1"); break;
                    case "interactive": await probe.InteractiveAsync(G("parent", "visible"), G("scopes", "short"), int.Parse(G("cancelAfter", "0")), G("prompt", "default"), G("account", "none"), G("save", "0") == "1", G("aud", "common"), int.Parse(G("survey", "4"))); break;
                    case "forget": await probe.RemoveSavedAsync(); break;
                    case "store": await CalendarStoreProbe.RunAsync(note, G("fidelity", "0") == "1"); break;
                    case "sleep": await Task.Delay(int.Parse(G("s", "5")) * 1000, cancellation); break;
                    case "minimize": window.AppWindow.Hide(); break;
                    case "show": window.Activate(); break;
                    default: note("unknown step " + mode); break;
                }
            }
            catch (Exception ex) { note($"step {mode} threw {ex.GetType().FullName}: {WamProbe.Redact(ex.Message)}"); }
        }

        return run.Finish();
    }
}
#endif
