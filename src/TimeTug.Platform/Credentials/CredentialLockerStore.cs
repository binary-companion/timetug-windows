using System.Text.Json;
using TimeTug.Engine;
using TimeTug.Presentation.Abstractions;

namespace TimeTug.Platform.Credentials;

/// <summary>
/// Connection secrets in Windows Credential Locker. One vault resource (<c>TimeTug</c>); the user name is the connection
/// id; the password is a JSON object holding the secrets and a <c>revision</c> GUID. A Credential Locker entry holds
/// only about 2.5 KB, and a Microsoft refresh token can be longer, so the JSON is split into chunks:
/// the head entry (user name = connection id) reads <c>1|generation|count|first chunk</c> and the other chunks are
/// stored as <c>id#generation#index</c>. A write stores the new chunks, then replaces the head (the commit point), then
/// removes the old chunks, so a crash leaves either the old value or the new one.
/// Every operation runs behind one <see cref="SemaphoreSlim"/>, which makes the compare-and-set of the refresh token atomic.
/// </summary>
public sealed class CredentialLockerStore : ICredentialStore, IDisposable
{
    public const string DefaultResource = "TimeTug";
    private const int ChunkSize = 1000;
    private const string Format = "1";
    private const string RefreshTokenKey = "refresh_token";

    private readonly IPasswordVault _vault;
    private readonly IDiagnostics? _diagnostics;
    private readonly string _resource;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CredentialLockerStore(IPasswordVault vault, IDiagnostics? diagnostics = null, string resource = DefaultResource)
    {
        _vault = vault;
        _diagnostics = diagnostics;
        _resource = resource;
    }

    public async ValueTask<IReadOnlyDictionary<string, string>?> GetAsync(string connectionID, CancellationToken cancellationToken) =>
        (await SnapshotAsync(connectionID, cancellationToken))?.Secrets;

    public ValueTask SetAsync(string connectionID, IReadOnlyDictionary<string, string> secrets, CancellationToken cancellationToken) =>
        Locked("save", () =>
        {
            Write(connectionID, secrets, Guid.NewGuid().ToString("N"));
            return true;
        }, cancellationToken).AsValueTask();

    public ValueTask RemoveAsync(string connectionID, CancellationToken cancellationToken) =>
        Locked("remove", () =>
        {
            var head = _vault.Retrieve(_resource, connectionID);
            _vault.Remove(_resource, connectionID);
            if (TryParseHead(head, out var generation, out var count, out _))
            {
                RemoveChunks(connectionID, generation, count);
            }

            return true;
        }, cancellationToken).AsValueTask();

    public async ValueTask<CredentialSnapshot?> SnapshotAsync(string connectionID, CancellationToken cancellationToken) =>
        await Locked("read", () => Read(connectionID), cancellationToken);

    public async ValueTask<bool> UpdateRefreshTokenAsync(string connectionID, string token, string expectedRevision,
                                                         CancellationToken cancellationToken) =>
        await Locked("update the refresh token", () =>
        {
            var current = Read(connectionID);
            if (current is null || !string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal))
            {
                return false;
            }

            var secrets = new Dictionary<string, string>(current.Secrets) { [RefreshTokenKey] = token };
            Write(connectionID, secrets, Guid.NewGuid().ToString("N"));
            return true;
        }, cancellationToken);

    public void Dispose() => _gate.Dispose();

    private async ValueTask<T> Locked<T>(string step, Func<T> work, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return work();
        }
        catch (Exception ex) when (ex is not EngineServiceException and not OperationCanceledException)
        {
            // Name the step; never the secrets (the exception text of the vault does not contain them).
            _diagnostics?.Record("error", "credentials", $"Credential Locker could not {step}: {ex.GetType().Name} (0x{ex.HResult:X8})");
            var kind = ex is UnauthorizedAccessException ? ServiceErrorKind.Denied : ServiceErrorKind.Failed;
            throw new EngineServiceException(kind, $"Windows Credential Locker is unavailable ({step} failed). It may be blocked by policy.", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private CredentialSnapshot? Read(string connectionID)
    {
        var head = _vault.Retrieve(_resource, connectionID);
        if (!TryParseHead(head, out var generation, out var count, out var first))
        {
            return null;
        }

        var json = new System.Text.StringBuilder(first);
        for (var index = 1; index < count; index++)
        {
            var chunk = _vault.Retrieve(_resource, ChunkName(connectionID, generation, index));
            if (chunk is null)
            {
                return Damaged(connectionID);
            }

            json.Append(chunk);
        }

        try
        {
            var blob = JsonSerializer.Deserialize<Blob>(json.ToString());
            if (blob?.Revision is not { Length: > 0 } revision || blob.Secrets is null)
            {
                return Damaged(connectionID);
            }

            return new CredentialSnapshot(blob.Secrets, revision);
        }
        catch (JsonException)
        {
            return Damaged(connectionID);
        }
    }

    private CredentialSnapshot? Damaged(string connectionID)
    {
        _diagnostics?.Record("warning", "credentials", $"The stored credentials of connection {connectionID} are damaged and were ignored.");
        return null;
    }

    private void Write(string connectionID, IReadOnlyDictionary<string, string> secrets, string generation)
    {
        var previous = _vault.Retrieve(_resource, connectionID);
        var json = JsonSerializer.Serialize(new Blob { Revision = Guid.NewGuid().ToString("N"), Secrets = new Dictionary<string, string>(secrets) });
        var chunks = Split(json);
        for (var index = 1; index < chunks.Count; index++)
        {
            _vault.Add(_resource, ChunkName(connectionID, generation, index), chunks[index]);
        }

        _vault.Add(_resource, connectionID, $"{Format}|{generation}|{chunks.Count}|{chunks[0]}");
        if (TryParseHead(previous, out var oldGeneration, out var oldCount, out _))
        {
            RemoveChunks(connectionID, oldGeneration, oldCount);
        }
    }

    private void RemoveChunks(string connectionID, string generation, int count)
    {
        for (var index = 1; index < count; index++)
        {
            _vault.Remove(_resource, ChunkName(connectionID, generation, index));
        }
    }

    private static List<string> Split(string json)
    {
        var chunks = new List<string>();
        for (var start = 0; start < json.Length; start += ChunkSize)
        {
            chunks.Add(json.Substring(start, Math.Min(ChunkSize, json.Length - start)));
        }

        return chunks;
    }

    private static string ChunkName(string connectionID, string generation, int index) => $"{connectionID}#{generation}#{index}";

    private static bool TryParseHead(string? head, out string generation, out int count, out string first)
    {
        generation = "";
        count = 0;
        first = "";
        if (head is null)
        {
            return false;
        }

        var parts = head.Split('|', 4);
        if (parts.Length != 4 || parts[0] != Format || !int.TryParse(parts[2], out count) || count < 1)
        {
            return false;
        }

        generation = parts[1];
        first = parts[3];
        return true;
    }

    private sealed class Blob
    {
        [System.Text.Json.Serialization.JsonPropertyName("revision")]
        public string? Revision { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("secrets")]
        public Dictionary<string, string>? Secrets { get; set; }
    }
}

internal static class ValueTaskExtensions
{
    public static ValueTask AsValueTask(this ValueTask<bool> task) => new(task.AsTask());
}
