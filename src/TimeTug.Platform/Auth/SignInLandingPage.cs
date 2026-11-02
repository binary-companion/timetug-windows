using System.Globalization;
using System.Net;
using System.Text;
using TimeTug.Presentation.Abstractions;

namespace TimeTug.Platform.Auth;

/// <summary>What a request to the loopback address turned out to be.</summary>
public enum LandingOutcome
{
    /// <summary>The redirect carried a <c>code</c> and no <c>error</c>.</summary>
    Connected,

    /// <summary>The redirect carried an <c>error</c>.</summary>
    DidNotFinish,

    /// <summary>Anything else: a bare request, the favicon, a port scan. It neither ends nor burns the sign-in.</summary>
    NotARedirect,
}

/// <summary>The words of the landing page, from <c>Resources.resw</c> (<c>Landing_*</c>), plus the language and direction of the page.</summary>
public sealed record LandingText(string PageTitle, string ConnectedTitle, string ConnectedBody, string FailedTitle, string FailedBody, string LanguageTag, bool RightToLeft)
{
    /// <summary>The English words, used when no localizer is given. They must match <c>Resources.resw</c>.</summary>
    public static LandingText English { get; } = new(
        "TimeTug", "You're connected", "Return to TimeTug.", "Sign-in didn't finish", "Return to TimeTug and try again.", "en", false);

    /// <summary>Reads the five <c>Landing_*</c> keys; <paramref name="culture"/> gives the page's <c>lang</c> and <c>dir</c>.</summary>
    public static LandingText From(ILocalizer text, CultureInfo culture) => new(
        text.GetString("Landing_PageTitle"),
        text.GetString("Landing_ConnectedTitle"),
        text.GetString("Landing_ConnectedBody"),
        text.GetString("Landing_FailedTitle"),
        text.GetString("Landing_FailedBody"),
        culture.Name.Length > 0 ? culture.Name : "en",
        culture.TextInfo.IsRightToLeft);
}

/// <summary>
/// The page the browser shows after a sign-in redirects back to TimeTug, and the rule for which requests count as that redirect. Nothing from the request
/// is ever placed in the page: <see cref="Classify"/> reads only whether <c>code</c> or <c>error</c> has a value, and returns neither.
/// </summary>
public static class SignInLandingPage
{
    private static readonly IReadOnlyDictionary<string, string> SecurityHeaders = new Dictionary<string, string>
    {
        ["Cache-Control"] = "no-store",
        ["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'",
        ["X-Content-Type-Options"] = "nosniff",
        ["Referrer-Policy"] = "no-referrer",
    };

    /// <summary>The response headers of every answer the loopback listener gives (the page and the 404).</summary>
    public static IReadOnlyDictionary<string, string> Headers => SecurityHeaders;

    /// <summary>
    /// Decides what <paramref name="rawUrl"/> (path and query, as <c>HttpListenerRequest.RawUrl</c> gives it) is. Only the root path counts, because that is
    /// the redirect address handed to the provider; there a non-empty <c>error</c> wins, else a non-empty <c>code</c> is a connected redirect.
    /// </summary>
    public static LandingOutcome Classify(string rawUrl)
    {
        var queryStart = rawUrl.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0)
        {
            return LandingOutcome.NotARedirect;
        }

        var path = rawUrl[..queryStart];
        if (path.Length != 0 && path != "/")
        {
            return LandingOutcome.NotARedirect;
        }

        var hasCode = false;
        var hasError = false;
        foreach (var pair in rawUrl[(queryStart + 1)..].Split('&'))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0 || equals == pair.Length - 1)
            {
                continue;   // no name, or no value
            }

            switch (pair[..equals])
            {
                case "code":
                    hasCode = true;
                    break;
                case "error":
                    hasError = true;
                    break;
            }
        }

        return hasError ? LandingOutcome.DidNotFinish : hasCode ? LandingOutcome.Connected : LandingOutcome.NotARedirect;
    }

    /// <summary>The page for a redirect. <paramref name="outcome"/> must be <see cref="LandingOutcome.Connected"/> or <see cref="LandingOutcome.DidNotFinish"/>.</summary>
    public static string Render(LandingOutcome outcome, LandingText text)
    {
        var (title, body) = outcome switch
        {
            LandingOutcome.Connected => (text.ConnectedTitle, text.ConnectedBody),
            LandingOutcome.DidNotFinish => (text.FailedTitle, text.FailedBody),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Only a redirect has a page."),
        };

        var page = new StringBuilder(2048);
        page.Append("<!doctype html>")
            .Append("<html lang=\"").Append(Encode(text.LanguageTag)).Append("\" dir=\"").Append(text.RightToLeft ? "rtl" : "ltr").Append("\">")
            .Append("<head><meta charset=\"utf-8\">")
            .Append("<title>").Append(Encode(text.PageTitle)).Append("</title>")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            .Append("<meta name=\"color-scheme\" content=\"light dark\">")
            .Append("<style>").Append(Style).Append("</style></head>")
            .Append("<body><main>")
            .Append(Mark)
            .Append("<h1>").Append(Encode(title)).Append("</h1>")
            .Append("<p>").Append(Encode(body)).Append("</p>")
            .Append("</main></body></html>");
        return page.ToString();
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    // The TimeTug puppy as one inline element, so the page makes no request at all.
    private const string Mark =
        "<svg class=\"mark\" xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 64 64\" role=\"img\" aria-label=\"TimeTug\">" +
        "<path d=\"M16 20C8 18 4 24 5 35c1 9 6 16 12 17l7-11-2-17Z\"/>" +
        "<path d=\"M48 20c8-2 12 4 11 15-1 9-6 16-12 17l-7-11 2-17Z\"/>" +
        "<path d=\"M12 32C12 19 20 11 32 11s20 8 20 21v8c0 11-9 18-20 18s-20-7-20-18Z\"/></svg>";

    // Colours are custom properties so each mode changes five values. Text is at least 4.5:1 in every mode (a test computes it); forced colours use system colours.
    private const string Style =
        ":root{color-scheme:light dark;--bg:#ffffff;--fg:#1b1b1f;--muted:#4b4b55;--accent:#2f5bd8}" +
        "@media (prefers-color-scheme:dark){:root{--bg:#1b1b1f;--fg:#f4f4f6;--muted:#c9c9d1;--accent:#8ab0ff}}" +
        "@media (prefers-contrast:more){:root{--bg:#ffffff;--fg:#000000;--muted:#000000;--accent:#0030a0}}" +
        "@media (prefers-contrast:more) and (prefers-color-scheme:dark){:root{--bg:#000000;--fg:#ffffff;--muted:#ffffff;--accent:#a8c4ff}}" +
        "@media (forced-colors:active){:root{--bg:Canvas;--fg:CanvasText;--muted:CanvasText;--accent:CanvasText}}" +
        "html,body{margin:0}" +
        "body{min-height:100vh;display:grid;place-items:center;background:var(--bg);color:var(--fg);font:16px/1.5 'Segoe UI',system-ui,sans-serif}" +
        "main{box-sizing:border-box;max-width:32rem;padding:2rem 1rem;text-align:center;overflow-wrap:anywhere}" +
        ".mark{width:4rem;height:4rem;fill:var(--accent)}" +
        "h1{margin:1rem 0 .5rem;font-size:1.75rem;line-height:1.25}" +
        "p{margin:0;font-size:1.125rem;color:var(--muted)}";
}
