using System.Globalization;
using System.Xml.Linq;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;

namespace TimeTug.Platform.OsState;

/// <summary>One button press (or selection) on a takeover toast.</summary>
/// <param name="Kind"><c>join</c>, <c>snooze</c> or <c>dismiss</c>.</param>
/// <param name="EventId">The request the toast was made for.</param>
/// <param name="Snooze">The chosen duration for <c>snooze</c>; null otherwise.</param>
public sealed record ToastAction(string Kind, string EventId, TimeSpan? Snooze);

/// <summary>Builds the toast payload for a takeover and reads the activation back. Pure: no OS calls.</summary>
public static class ToastXml
{
    public const string JoinKind = "join";
    public const string SnoozeKind = "snooze";
    public const string DismissKind = "dismiss";
    public const string SnoozeInputId = "snoozeSeconds";
    public const string Group = "takeover";

    /// <summary>
    /// The toast: title, details, a snooze choice (the request's own options, from the engine), and Join (only with
    /// a link), Snooze and Dismiss buttons that act without opening a window. The reminder scenario keeps it on
    /// screen until the person answers.
    /// </summary>
    public static string Build(TakeoverRequest request, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(localizer);

        var actions = new XElement("actions");
        if (request.SnoozeOptions.Count > 0)
        {
            var input = new XElement(
                "input",
                new XAttribute("id", SnoozeInputId),
                new XAttribute("type", "selection"),
                new XAttribute("defaultInput", Seconds(request.SnoozeOptions[0])));
            foreach (var option in request.SnoozeOptions)
            {
                input.Add(new XElement("selection", new XAttribute("id", Seconds(option)), new XAttribute("content", OptionText(option, localizer))));
            }

            actions.Add(input);
        }

        if (!string.IsNullOrEmpty(request.JoinUrl))
        {
            actions.Add(Button(localizer.GetString("Toast_Join"), Arguments(JoinKind, request.EventId, null)));
        }

        if (request.SnoozeOptions.Count > 0)
        {
            actions.Add(Button(localizer.GetString("Toast_Snooze"), Arguments(SnoozeKind, request.EventId, request.SnoozeOptions[0])));
        }

        actions.Add(Button(localizer.GetString("Toast_Dismiss"), Arguments(DismissKind, request.EventId, null)));

        var toast = new XElement(
            "toast",
            new XAttribute("scenario", "reminder"),
            new XAttribute("launch", Arguments("open", request.EventId, null)),
            new XElement(
                "visual",
                new XElement(
                    "binding",
                    new XAttribute("template", "ToastGeneric"),
                    new XElement("text", request.Title),
                    new XElement("text", Details(request, localizer)))),
            actions);
        return toast.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Reads an activation. Returns null for a click on the toast body or anything unrecognised. A snooze takes the
    /// selected duration, falling back to the one carried by the button.
    /// </summary>
    public static ToastAction? Parse(IReadOnlyDictionary<string, string> arguments, IReadOnlyDictionary<string, string> userInput)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(userInput);
        if (!arguments.TryGetValue("action", out var kind) || !arguments.TryGetValue("eventId", out var encoded))
        {
            return null;
        }

        var eventId = Uri.UnescapeDataString(encoded);
        switch (kind)
        {
            case JoinKind:
            case DismissKind:
                return new ToastAction(kind, eventId, null);
            case SnoozeKind:
                var text = userInput.TryGetValue(SnoozeInputId, out var chosen) ? chosen
                    : arguments.TryGetValue("seconds", out var carried) ? carried : null;
                return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
                    ? new ToastAction(kind, eventId, TimeSpan.FromSeconds(seconds))
                    : null;
            default:
                return null;
        }
    }

    /// <summary>The <c>arguments</c> string of a toast element: <c>key=value;key=value</c> with values percent-encoded.</summary>
    private static string Arguments(string action, string eventId, TimeSpan? snooze)
    {
        var text = "action=" + action + ";eventId=" + Uri.EscapeDataString(eventId);
        return snooze is { } duration ? text + ";seconds=" + Seconds(duration) : text;
    }

    private static XElement Button(string content, string arguments) => new(
        "action",
        new XAttribute("content", content),
        new XAttribute("arguments", arguments),
        new XAttribute("activationType", "background"));

    private static string Seconds(TimeSpan duration) => ((long)duration.TotalSeconds).ToString(CultureInfo.InvariantCulture);

    private static string OptionText(TimeSpan duration, ILocalizer localizer) => duration.TotalSeconds >= 60 && duration.TotalSeconds % 60 == 0
        ? localizer.Format("Toast_SnoozeMinutes", (int)duration.TotalMinutes)
        : localizer.Format("Toast_SnoozeSeconds", (int)duration.TotalSeconds);

    private static string Details(TakeoverRequest request, ILocalizer localizer)
    {
        if (request.IsAllDay)
        {
            return localizer.GetString("Toast_AllDay");
        }

        var culture = CultureInfo.CurrentCulture;
        var start = request.Start.ToLocalTime().ToString("t", culture);
        var end = request.End.ToLocalTime().ToString("t", culture);
        return string.IsNullOrWhiteSpace(request.CalendarTitle)
            ? localizer.Format("Toast_Details", start, end)
            : localizer.Format("Toast_DetailsCalendar", start, end, request.CalendarTitle);
    }
}
