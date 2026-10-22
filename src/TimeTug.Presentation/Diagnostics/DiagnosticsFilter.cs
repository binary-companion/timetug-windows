using System.Text.RegularExpressions;

namespace TimeTug.Presentation.Diagnostics;

/// <summary>
/// The safety net between the log and the clipboard. The app's own logger never writes a link with a query string, a token or an event title
/// (it logs exception types and ids; a test scans the source for it), and the engine's diagnostics never carry calendar content. This drops
/// any line that looks like it does anyway, so a mistake elsewhere cannot leak into a report the person shares.
/// A dropped line is dropped whole: a half-hidden link is still a hint.
/// </summary>
public static partial class DiagnosticsFilter
{
    private const int ShortestTitle = 3;

    /// <summary>The lines that are safe to share, in their order.</summary>
    public static IEnumerable<string> Safe(IEnumerable<string> lines, IReadOnlyCollection<string>? eventTitles = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var titles = TitlePattern(eventTitles);
        return lines.Where(line => IsSafe(line, titles));
    }

    /// <summary>True when the line may be shared.</summary>
    public static bool IsSafe(string line, IReadOnlyCollection<string>? eventTitles = null) => IsSafe(line, TitlePattern(eventTitles));

    private static bool IsSafe(string line, Regex? titles)
    {
        if (LinkWithQuery().IsMatch(line) || QueryParameter().IsMatch(line) || Secret().IsMatch(line) || FeedLink().IsMatch(line))
        {
            return false;
        }

        if (titles is null)
        {
            return true;
        }

        // A title is looked for in the message only: a meeting called "Sync" must not blank every line of the sync category.
        var shape = LogLine().Match(line);
        return !titles.IsMatch(shape.Success ? shape.Groups["message"].Value : line);
    }

    private static Regex? TitlePattern(IReadOnlyCollection<string>? titles)
    {
        var usable = titles?.Select(t => t.Trim()).Where(t => t.Length >= ShortestTitle).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (usable is not { Count: > 0 })
        {
            return null;
        }

        // Whole words (or whole phrases): "Dentist" is not found inside "dentistry". A title that starts or ends with a symbol has no word edge there.
        var alternatives = usable.Select(t => $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(t)}(?![\p{{L}}\p{{N}}])");
        return new Regex(string.Join('|', alternatives), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    // scheme://anything?anything
    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9+.\-]*://\S*\?", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LinkWithQuery();

    // ?code=... or &access_token=... even without a scheme (a path in a message)
    [GeneratedRegex(@"[?&](code|state|token|access_token|refresh_token|id_token|client_secret|client_id|key|apikey|api_key|password|sig|signature)=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex QueryParameter();

    // access_token=..., refresh_token: ..., Authorization: ..., Bearer xxxxxxxx, Basic xxxxxxxx, a Google access token (ya29.), refresh token (1//) or a JWT
    [GeneratedRegex(
        @"\b(access_token|refresh_token|id_token|client_secret|api[_-]?key|password)\s*[=:]|\bauthorization\s*:|\bbearer\s+\S{8,}|\bbasic\s+[A-Za-z0-9+/=]{8,}|\bya29\.[\w\-]+|(?<![\w/])1//[\w\-]{20,}|\beyJ[\w\-]{8,}\.[\w\-]{8,}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Secret();

    // webcal://, webcals:// and anything that ends in .ics: a private feed link works for anyone who holds it
    [GeneratedRegex(@"\bwebcals?://|\.ics\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FeedLink();

    // 2026-10-21T10:15:30.000Z [level] category: message
    [GeneratedRegex(@"^\S+ \[[^\]]+\] [^:\s]+: (?<message>.*)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LogLine();
}
