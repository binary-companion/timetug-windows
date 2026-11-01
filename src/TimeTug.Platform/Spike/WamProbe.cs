#pragma warning disable
// THROWAWAY spike probe for docs/spikes/2026-11-wam-broker.md. Not merged. Records outcomes only: hashes instead of ids, no tokens.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;

namespace TimeTug.Platform.Spike;

public sealed class WamProbe
{
    public static readonly string[] ShortScopes = ["User.Read", "MailboxSettings.Read", "Calendars.Read", "Calendars.Read.Shared"];
    public static readonly string[] GraphScopes = ShortScopes.Select(s => "https://graph.microsoft.com/" + s).ToArray();

    private readonly Action<string> _note;
    private readonly string _clientId;
    private readonly string _stateFile;
    private IntPtr _visibleWindow;
    private IntPtr _hiddenOwner;
    private readonly List<string> _msalLog = [];

    public WamProbe(string clientId, string stateFile, IntPtr visibleWindow, Action<string> note)
    {
        _clientId = clientId; _stateFile = stateFile; _visibleWindow = visibleWindow; _note = note;
    }

    public static string Redact(string? text, int max = 500)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var s = Regex.Replace(text, @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", "<email>");
        s = Regex.Replace(s, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", "<guid>");
        s = Regex.Replace(s, @"[A-Za-z0-9_\-\.]{60,}", "<long>");
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length > max ? s[..max] + "..." : s;
    }

    public static string H(string? s) => s is null ? "null" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..8];

    private static string DomainClass(string? username)
    {
        if (string.IsNullOrEmpty(username)) return "none";
        var at = username.LastIndexOf('@');
        if (at < 0) return "no-at";
        var d = username[(at + 1)..].ToLowerInvariant();
        return d is "outlook.com" or "hotmail.com" or "live.com" or "msn.com" or "gmail.com" or "outlook.fr" ? "consumer-domain" : "custom-domain";
    }

    public void Env()
    {
        var asm = typeof(IPublicClientApplication).Assembly;
        var broker = typeof(BrokerOptions).Assembly;
        _note($"env: msal={asm.GetName().Version} file={FileVersionInfo.GetVersionInfo(asm.Location).ProductVersion} broker={FileVersionInfo.GetVersionInfo(broker.Location).ProductVersion}");
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name!.Contains("NativeInterop")))
            _note($"env: nativeinterop-managed={a.GetName().Version} file={FileVersionInfo.GetVersionInfo(a.Location).ProductVersion}");
        _note($"env: arch={RuntimeInformation.ProcessArchitecture} os={RuntimeInformation.OSDescription} fw={RuntimeInformation.FrameworkDescription}");
        var len = 0; var sb = new StringBuilder(256); len = 256;
        var rc = GetCurrentPackageFullName(ref len, sb);
        _note($"env: packaged={(rc == 0)} package={(rc == 0 ? sb.ToString() : "none")}");
        var baseDir = AppContext.BaseDirectory;
        _note($"env: baseDir={baseDir}");
        foreach (var f in Directory.EnumerateFiles(baseDir, "*msal*", SearchOption.AllDirectories))
            _note($"env: file {Path.GetRelativePath(baseDir, f)} bytes={new FileInfo(f).Length}");
        foreach (var f in Directory.EnumerateFiles(baseDir, "*Identity.Client*", SearchOption.AllDirectories))
            _note($"env: file {Path.GetRelativePath(baseDir, f)} bytes={new FileInfo(f).Length}");
        foreach (var name in new[] { "msalruntime.dll", "msalruntime_arm64.dll", "msalruntime_x86.dll" })
        {
            var ok = NativeLibrary.TryLoad(name, typeof(WamProbe).Assembly, DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories, out var h);
            _note($"env: load {name} -> {ok}");
            if (ok) NativeLibrary.Free(h);
        }
        foreach (var m in Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Where(m => m.ModuleName.Contains("msalruntime", StringComparison.OrdinalIgnoreCase)))
            _note($"env: loadedModule {m.ModuleName} path-under-package={m.FileName.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase)}");
    }

    public IPublicClientApplication Build(bool listOs = true, string audience = "common", bool log = true)
    {
        var aud = audience switch { "organizations" => AadAuthorityAudience.AzureAdMyOrg, "consumers" => AadAuthorityAudience.PersonalMicrosoftAccount, _ => AadAuthorityAudience.AzureAdAndPersonalMicrosoftAccount };
        var b = PublicClientApplicationBuilder.Create(_clientId);
        b = audience == "common" ? b.WithAuthority(AzureCloudInstance.AzurePublic, AadAuthorityAudience.AzureAdAndPersonalMicrosoftAccount)
          : audience == "organizations" ? b.WithAuthority("https://login.microsoftonline.com/organizations")
          : b.WithAuthority("https://login.microsoftonline.com/consumers");
        b = b.WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows) { ListOperatingSystemAccounts = listOs })
             .WithParentActivityOrWindow(() => ParentFor(_currentParent));
        if (log)
            b = b.WithLogging((level, message, pii) => { lock (_msalLog) { _msalLog.Add($"{level}: {Redact(message, 300)}"); } }, LogLevel.Info, enablePiiLogging: false, enableDefaultPlatformLogging: false);
        return b.Build();
    }

    private string _currentParent = "visible";

    private IntPtr ParentFor(string parent) => parent switch
    {
        "visible" => _visibleWindow,
        "hidden" => EnsureHidden(),
        "desktop" => GetDesktopWindow(),
        "none" => IntPtr.Zero,
        _ => _visibleWindow,
    };

    private IntPtr EnsureHidden()
    {
        if (_hiddenOwner == IntPtr.Zero)
            _hiddenOwner = CreateWindowEx(0, "STATIC", "TimeTugSpikeOwner", 0x80000000 /*WS_POPUP, not visible*/, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        return _hiddenOwner;
    }

    private void FlushMsalLog(string label)
    {
        string[] lines;
        lock (_msalLog) { lines = _msalLog.ToArray(); _msalLog.Clear(); }
        var keep = lines.Where(l => !l.Contains("LogTelemetryData") || Regex.IsMatch(l, "Key: (api_name|auth_flow|is_successful|broker_app_used|all_error_tags|msa_wam_[a-z_]+|error_[a-z_]+|[a-z_]*error[a-z_]*|msalruntime_version|request_eligible_for_broker|is_cached|account_type|ui_flow[a-z_]*|[a-z_]*status[a-z_]*|[a-z_]*tag[a-z_]*), Value")).Where(l => !l.Contains("Region discovery") && !l.Contains("Internal cache")).Select(l => Regex.Replace(l, @"^(\w+): False MSAL [0-9.]+ MSAL.NetCore \.NET [0-9.]+ Microsoft Windows [0-9.]+ ", "$1: ")).ToArray();
        foreach (var l in keep.Take(80)) _note($"{label}: msal-log {l}");
        if (keep.Length > 80) _note($"{label}: msal-log ... {keep.Length - 80} more");
    }

    public static string DescribeException(Exception ex)
    {
        var sb = new StringBuilder();
        sb.Append($"exception={ex.GetType().FullName}");
        if (ex is MsalException me) sb.Append($" errorCode={me.ErrorCode}");
        if (ex is MsalUiRequiredException ui) sb.Append($" classification={ui.Classification}");
        if (ex is MsalServiceException svc) sb.Append($" statusCode={svc.StatusCode} responseHeaders={(svc.Headers == null ? "none" : "some")}");
        if (ex is MsalClientException) sb.Append(" client=true");
        sb.Append($" hresult=0x{ex.HResult:X8}");
        sb.Append($" message=\"{Redact(ex.Message)}\"");
        if (ex.InnerException != null) sb.Append($" inner={ex.InnerException.GetType().FullName}:\"{Redact(ex.InnerException.Message, 200)}\"");
        return sb.ToString();
    }

    public static string DescribeResult(AuthenticationResult r)
    {
        var mins = (r.ExpiresOn - DateTimeOffset.UtcNow).TotalMinutes;
        var tenantMsa = string.Equals(r.TenantId, "9188040d-6c67-4c5b-b112-36a304b66dad", StringComparison.OrdinalIgnoreCase);
        return $"OK tokenLen={r.AccessToken?.Length} tokenHash={H(r.AccessToken)} expiresInMin={mins:0.0} source={r.AuthenticationResultMetadata?.TokenSource} scopes=[{string.Join(",", r.Scopes ?? [])}] idToken={(r.IdToken != null)} account={(r.Account == null ? "none" : H(r.Account.HomeAccountId?.Identifier))} accountEnv={r.Account?.Environment} domainClass={DomainClass(r.Account?.Username)} msaTenant={tenantMsa} tenantHash={H(r.TenantId)}";
    }

    public async Task ListAsync(bool listOs, string audience)
    {
        var label = $"list(os={listOs},aud={audience})";
        try
        {
            var pca = Build(listOs, audience);
            var sw = Stopwatch.StartNew();
            var accounts = (await pca.GetAccountsAsync()).ToList();
            _note($"{label}: count={accounts.Count} ms={sw.ElapsedMilliseconds}");
            foreach (var a in accounts)
                _note($"{label}: account idHash={H(a.HomeAccountId?.Identifier)} usernameHash={H(a.Username)} domainClass={DomainClass(a.Username)} env={a.Environment} idLen={a.HomeAccountId?.Identifier?.Length} tenantHash={H(a.HomeAccountId?.TenantId)}");
            var none = await pca.GetAccountAsync("not-a-real-id.not-a-real-tenant");
            _note($"{label}: GetAccountAsync(bogus)={(none == null ? "null" : "found")}");
            FlushMsalLog(label);
        }
        catch (Exception ex) { _note($"{label}: {DescribeException(ex)}"); FlushMsalLog(label); }
    }

    public async Task SilentAsync(string kind, string scopes, string loginHint, bool forceRefresh, string audience, bool save = false)
    {
        var label = $"silent({kind},scopes={scopes},force={forceRefresh})";
        try
        {
            var pca = Build(true, audience);
            var s = scopes == "graph" ? GraphScopes : scopes.Contains('.') && scopes != "short" ? scopes.Split(',') : ShortScopes;
            var sw = Stopwatch.StartNew();
            AuthenticationResult r;
            if (kind == "os")
                r = await pca.AcquireTokenSilent(s, PublicClientApplication.OperatingSystemAccount).WithForceRefresh(forceRefresh).ExecuteAsync();
            else if (kind == "hint")
                r = await pca.AcquireTokenSilent(s, loginHint).WithForceRefresh(forceRefresh).ExecuteAsync();
            else if (kind == "saved")
            {
                var st = LoadState();
                if (st == null) { _note($"{label}: no saved state"); return; }
                var acc = await pca.GetAccountAsync(st.AccountId);
                _note($"{label}: GetAccountAsync(savedId)={(acc == null ? "null" : "found")} savedIdHash={H(st.AccountId)}");
                if (acc == null)
                {
                    var all = (await pca.GetAccountsAsync()).ToList();
                    var byName = all.FirstOrDefault(a => string.Equals(a.Username, st.Username, StringComparison.OrdinalIgnoreCase));
                    _note($"{label}: fallback GetAccountsAsync count={all.Count} matchByUsername={(byName != null)}");
                    acc = byName;
                    if (acc == null) { FlushMsalLog(label); return; }
                }
                r = await pca.AcquireTokenSilent(s, acc).WithForceRefresh(forceRefresh).ExecuteAsync();
            }
            else if (kind == "saved-os")
            {
                r = await pca.AcquireTokenSilent(s, PublicClientApplication.OperatingSystemAccount).WithForceRefresh(forceRefresh).ExecuteAsync();
            }
            else { _note($"{label}: unknown kind"); return; }
            _note($"{label}: {DescribeResult(r)} ms={sw.ElapsedMilliseconds}");
            if (save && r.Account != null) SaveState(r.Account.HomeAccountId.Identifier, r.Account.Username);
            FlushMsalLog(label);
        }
        catch (Exception ex) { _note($"{label}: {DescribeException(ex)}"); FlushMsalLog(label); }
    }

    public async Task InteractiveAsync(string parent, string scopes, int cancelAfter, string prompt, string account, bool save, string audience, int survey)
    {
        var label = $"interactive(parent={parent},scopes={scopes},cancelAfter={cancelAfter},prompt={prompt},account={account},aud={audience})";
        _currentParent = parent;
        using var cts = new CancellationTokenSource();
        var started = Stopwatch.StartNew();
        string? cancelNote = null;
        try
        {
            var pca = Build(true, audience);
            var s = scopes == "graph" ? GraphScopes : ShortScopes;
            var b = pca.AcquireTokenInteractive(s);
            if (prompt == "selectaccount") b = b.WithPrompt(Prompt.SelectAccount);
            else if (prompt == "login") b = b.WithPrompt(Prompt.ForceLogin);
            else if (prompt == "consent") b = b.WithPrompt(Prompt.Consent);
            if (account == "os") b = b.WithAccount(PublicClientApplication.OperatingSystemAccount);
            _note($"{label}: starting; foreground before = {DescribeForeground()}");
            _note($"{label}: windows before:\n" + SurveyWindows());
            var call = b.ExecuteAsync(cts.Token);
            var surveyTask = Task.Run(async () =>
            {
                await Task.Delay(survey * 1000);
                _note($"{label}: windows at +{survey}s:\n" + SurveyWindows());
            });
            if (cancelAfter > 0)
            {
                await Task.WhenAny(call, Task.Delay(cancelAfter * 1000));
                if (!call.IsCompleted)
                {
                    await surveyTask;
                    _note($"{label}: cancelling the token at +{started.Elapsed.TotalSeconds:0.0}s");
                    var t0 = Stopwatch.StartNew();
                    cts.Cancel();
                    var done = await Task.WhenAny(call, Task.Delay(15000));
                    cancelNote = $"returned-after-cancel={(done == call)} ms={t0.ElapsedMilliseconds}";
                    _note($"{label}: {cancelNote}");
                    await Task.Delay(1500);
                    _note($"{label}: windows 1.5s after cancel:\n" + SurveyWindows());
                }
            }
            var r = await call;
            _note($"{label}: {DescribeResult(r)} ms={started.ElapsedMilliseconds}");
            if (save && r.Account != null) SaveState(r.Account.HomeAccountId.Identifier, r.Account.Username);
            FlushMsalLog(label);
        }
        catch (Exception ex) { _note($"{label}: {DescribeException(ex)} ms={started.ElapsedMilliseconds}"); FlushMsalLog(label); }
    }

    public async Task RemoveSavedAsync()
    {
        try { File.Delete(_stateFile); _note("state: deleted"); } catch (Exception ex) { _note("state: " + DescribeException(ex)); }
        await Task.CompletedTask;
    }

    private sealed record State(string AccountId, string Username);
    private State? LoadState() { try { return JsonSerializer.Deserialize<State>(File.ReadAllText(_stateFile)); } catch { return null; } }
    private void SaveState(string id, string user) { File.WriteAllText(_stateFile, JsonSerializer.Serialize(new State(id, user))); _note($"state: saved idHash={H(id)}"); }

    // ---- window survey (Win32) ----
    private string DescribeForeground() => DescribeWindow(GetForegroundWindow());

    private string DescribeWindow(IntPtr h)
    {
        if (h == IntPtr.Zero) return "none";
        GetWindowThreadProcessId(h, out var pid);
        string proc; try { proc = Process.GetProcessById((int)pid).ProcessName; } catch { proc = "?"; }
        var cls = new StringBuilder(128); GetClassName(h, cls, 128);
        var title = new StringBuilder(256); GetWindowText(h, title, 256);
        GetWindowRect(h, out var rc);
        var owner = GetWindow(h, 4);
        DwmGetWindowAttribute(h, 14, out int cloaked, 4);
        return $"[{proc}] class={cls} title=\"{Redact(title.ToString(), 80)}\" visible={IsWindowVisible(h)} cloaked={cloaked != 0} size={rc.R - rc.L}x{rc.B - rc.T} ownedBy={(owner == IntPtr.Zero ? "none" : (owner == _hiddenOwner ? "our-hidden-owner" : owner == _visibleWindow ? "our-visible-window" : "other"))} foreground={(h == GetForegroundWindow())}";
    }

    private string SurveyWindows()
    {
        var lines = new List<string>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            GetWindowRect(h, out var rc);
            if (rc.R - rc.L < 20 || rc.B - rc.T < 20) return true;
            var d = DescribeWindow(h);
            if (d.Contains("[explorer]") && d.Contains("Shell_") ) return true;
            lines.Add("   " + d);
            return true;
        }, IntPtr.Zero);
        return string.Join("\n", lines.Take(40));
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT rc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(int ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetCurrentPackageFullName(ref int len, StringBuilder name);
}
