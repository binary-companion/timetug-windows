using System.Text.Json;
using TimeTug.Presentation.Abstractions;
using Pkg = global::TimeTug.Engine;

namespace TimeTug.Presentation.Startup;

/// <summary>
/// Reads the developer-only OAuth client registrations from <c>dev-oauth.json</c> (development and CI builds; the Windows
/// path is <c>%LOCALAPPDATA%\TimeTug\dev-oauth.json</c>). The file is git-ignored and copied onto a machine by hand. The ids and
/// secrets are never logged or echoed: diagnostics name the families that were found and nothing else. When the file is
/// absent the result is empty and the Google and Microsoft kinds simply stay unconfigured.
/// <code>{ "google": { "clientId": "...", "clientSecret": "..." }, "microsoft": { "clientId": "..." } }</code>
/// </summary>
public static class DevOAuthFile
{
    public const string FileName = "dev-oauth.json";

    private const string Category = "oauth";
    private static readonly string[] Families = ["google", "microsoft"];

    /// <summary>The configured clients keyed by connector family (<c>google</c>, <c>microsoft</c>); empty when the file is missing or unreadable.</summary>
    public static IReadOnlyDictionary<string, Pkg.OAuthClient> Load(string path, IDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (!File.Exists(path))
        {
            return Empty();
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            diagnostics.Record("warning", Category, $"{FileName} could not be read ({e.GetType().Name}); Google and Microsoft sign-in stay unconfigured");
            return Empty();
        }

        var clients = Parse(text, diagnostics);
        if (clients.Count > 0)
        {
            diagnostics.Record("info", Category, $"{FileName} configures: {string.Join(", ", clients.Keys.Order(StringComparer.Ordinal))}");
        }

        return clients;
    }

    /// <summary>Parses the file's text. A problem is recorded once, without any of the file's contents.</summary>
    public static IReadOnlyDictionary<string, Pkg.OAuthClient> Parse(string json, IDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        var clients = new Dictionary<string, Pkg.OAuthClient>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Record("warning", Category, $"{FileName} must be a JSON object with a \"google\" and/or a \"microsoft\" entry");
                return Empty();
            }

            foreach (var family in document.RootElement.EnumerateObject())
            {
                var key = Families.FirstOrDefault(f => string.Equals(f, family.Name, StringComparison.OrdinalIgnoreCase));
                if (key is null)
                {
                    continue;
                }

                if (family.Value.ValueKind != JsonValueKind.Object)
                {
                    diagnostics.Record("warning", Category, $"the \"{key}\" entry in {FileName} must be an object with a clientId");
                    return Empty();
                }

                var id = Text(family.Value, "clientId");
                if (id is not null)
                {
                    clients[key] = new Pkg.OAuthClient(id, Text(family.Value, "clientSecret"));
                }
            }
        }
        catch (JsonException)
        {
            diagnostics.Record("warning", Category, $"{FileName} is not valid JSON; Google and Microsoft sign-in stay unconfigured");
            return Empty();
        }

        return clients;
    }

    private static string? Text(JsonElement entry, string name)
    {
        foreach (var property in entry.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
            {
                var value = property.Value.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }
        }

        return null;
    }

    private static Dictionary<string, Pkg.OAuthClient> Empty() => new(StringComparer.Ordinal);
}
