using System.Runtime.InteropServices;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Accounts;

namespace TimeTug.Platform.Broker;

/// <summary>
/// <see cref="IBrokerClient"/> over MSAL.NET and the Windows Web Account Manager. The only class that sees MSAL. Settings come from the spike
/// (docs/spikes/2026-11-wam-broker.md): the personal-plus-work authority (<c>common</c>; an organizations-only authority fails for a personal Windows
/// account), <c>ListOperatingSystemAccounts</c> (without it the list is empty), no token-cache file and no MSAL log callback, which could carry account data.
/// </summary>
public sealed class MsalBrokerClient : IBrokerClient
{
    private readonly IPublicClientApplication _app;
    private readonly IDiagnostics _diagnostics;

    private MsalBrokerClient(IPublicClientApplication app, IDiagnostics diagnostics)
    {
        _app = app;
        _diagnostics = diagnostics;
    }

    /// <summary>Builds the client, or returns null when MSAL or its native runtime cannot be loaded here (the browser sign-in then stands alone).</summary>
    public static MsalBrokerClient? TryCreate(string clientId, IBrokerParentWindow parent, IDiagnostics diagnostics)
    {
        try
        {
            var app = PublicClientApplicationBuilder.Create(clientId)
                .WithAuthority(AzureCloudInstance.AzurePublic, AadAuthorityAudience.AzureAdAndPersonalMicrosoftAccount)
                .WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows) { ListOperatingSystemAccounts = true })
                .WithParentActivityOrWindow(() => parent.Handle)
                .Build();
            return new MsalBrokerClient(app, diagnostics);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            diagnostics.Record("warning", "broker", $"create: unavailable ({exception.GetType().Name})");
            return null;
        }
    }

    /// <summary>
    /// Whether the broker's native runtime for this process's architecture (<c>msalruntime.dll</c> on x64, <c>msalruntime_arm64.dll</c> on ARM64) can be
    /// loaded from the package. The self-test's <c>broker-probe</c> reads this: a package that lost the file fails there, not on a person's PC.
    /// </summary>
    public static bool NativeRuntimeLoads(out string fileName)
    {
        fileName = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "msalruntime_arm64.dll" : "msalruntime.dll";
        if (!NativeLibrary.TryLoad(fileName, typeof(MsalBrokerClient).Assembly, DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories, out var handle))
        {
            return false;
        }

        NativeLibrary.Free(handle);
        return true;
    }

    public async ValueTask<IReadOnlyList<BrokerAccount>> ListAccountsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var accounts = await _app.GetAccountsAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            return [.. accounts.Select(a => new BrokerAccount(a.HomeAccountId.Identifier, a.Username))];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Listing is only a hint (the welcome card names the account); an unavailable broker lists nothing.
            MsalErrorMapper.Map(exception, "list", _diagnostics);
            return [];
        }
    }

    public async ValueTask<BrokerOutcome> AcquireSilentAsync(string accountId, string? email, IReadOnlyList<string> scopes, bool forceRefresh, CancellationToken cancellationToken)
    {
        try
        {
            var account = await FindAccountAsync(accountId, email, cancellationToken).ConfigureAwait(false);
            if (account is null)
            {
                _diagnostics.Record("info", "broker", "silent: account not listed");
                return new BrokerOutcome.UiRequired(BrokerReason.AccountMissing);
            }

            var result = await _app.AcquireTokenSilent(scopes, account).WithForceRefresh(forceRefresh).ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return Ok(result);
        }
        catch (Exception exception)
        {
            return MsalErrorMapper.Map(exception, "silent", _diagnostics);
        }
    }

    public async ValueTask<BrokerOutcome> AcquireInteractiveAsync(BrokerIntent intent, string? accountId, string? email, IReadOnlyList<string> scopes, CancellationToken cancellationToken)
    {
        try
        {
            var request = _app.AcquireTokenInteractive(scopes);
            var known = accountId is null ? null : await FindAccountAsync(accountId, email, cancellationToken).ConfigureAwait(false);
            if (known is not null)
            {
                // A reconnect: the dialog is for that account.
                request = request.WithAccount(known);
            }
            else if (accountId is not null)
            {
                // The account is gone from Windows: let the person pick, with the old email as a hint.
                request = request.WithPrompt(Prompt.SelectAccount);
                if (!string.IsNullOrEmpty(email))
                {
                    request = request.WithLoginHint(email);
                }
            }
            else if (intent == BrokerIntent.PickAccount)
            {
                request = request.WithPrompt(Prompt.SelectAccount);
            }
            else
            {
                // With consent in place this returns a token with no window; without it Windows asks.
                request = request.WithAccount(PublicClientApplication.OperatingSystemAccount);
            }

            var result = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return Ok(result);
        }
        catch (Exception exception)
        {
            return MsalErrorMapper.Map(exception, "interactive", _diagnostics);
        }
    }

    // The stored id first (stable across restarts in the spike); the email only when the id no longer finds the account (S4 fallback).
    private async Task<IAccount?> FindAccountAsync(string accountId, string? email, CancellationToken cancellationToken)
    {
        var byId = await _app.GetAccountAsync(accountId).WaitAsync(cancellationToken).ConfigureAwait(false);
        if (byId is not null || string.IsNullOrEmpty(email))
        {
            return byId;
        }

        var all = await _app.GetAccountsAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        return all.FirstOrDefault(a => string.Equals(a.Username, email, StringComparison.OrdinalIgnoreCase));
    }

    private static BrokerOutcome Ok(AuthenticationResult result) =>
        result.Account?.HomeAccountId?.Identifier is { Length: > 0 } id && !string.IsNullOrEmpty(result.AccessToken)
            ? new BrokerOutcome.Ok(id, result.AccessToken, result.ExpiresOn)
            : new BrokerOutcome.Failed();
}
