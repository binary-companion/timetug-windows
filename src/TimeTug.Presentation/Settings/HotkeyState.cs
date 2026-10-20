using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TimeTug.Presentation.Settings;

/// <summary>What the last attempt to use the shortcut came to.</summary>
public enum HotkeyStatus
{
    /// <summary>No shortcut is set (the Mac default), or it was cleared.</summary>
    Cleared,

    /// <summary>Windows accepted the shortcut.</summary>
    Registered,

    /// <summary>Another program holds the shortcut.</summary>
    Conflict,

    /// <summary>The shortcut was refused before asking Windows (no modifier, no key). Never persisted.</summary>
    Invalid,
}

/// <summary>The outcome of setting or clearing the shortcut.</summary>
public readonly record struct HotkeyResult(HotkeyStatus Status, HotkeyReason Reason = HotkeyReason.None)
{
    public static HotkeyResult Registered { get; } = new(HotkeyStatus.Registered);

    public static HotkeyResult Cleared { get; } = new(HotkeyStatus.Cleared);

    public static HotkeyResult Conflict { get; } = new(HotkeyStatus.Conflict, HotkeyReason.OtherAppHoldsIt);

    public static HotkeyResult Invalid(HotkeyReason reason) => new(HotkeyStatus.Invalid, reason);
}

/// <summary>
/// The shortcut as the app keeps it between runs, so the Settings page can say after a restart that "this shortcut is inactive
/// because another program uses it".
/// </summary>
/// <param name="Binding">The shortcut in force: registered with Windows, or stored but refused (see <paramref name="Refused"/>). Null: none.</param>
/// <param name="LastResult">What the last attempt came to.</param>
/// <param name="At">When that was (UTC); null before any attempt.</param>
/// <param name="Refused">The shortcut another program held when it was last tried. When a new shortcut is refused, <paramref name="Binding"/> stays
/// the earlier one and this names the new one; when a stored shortcut cannot be registered at startup it equals <paramref name="Binding"/>.</param>
public sealed record HotkeyState(HotkeyBinding? Binding, HotkeyStatus LastResult, DateTimeOffset? At, HotkeyBinding? Refused = null)
{
    /// <summary>No shortcut, nothing tried: the default, as on the Mac.</summary>
    public static HotkeyState None { get; } = new(null, HotkeyStatus.Cleared, null);

    /// <summary>The last attempt found the shortcut held by another program.</summary>
    public bool HasConflict => Refused is not null;

    /// <summary>The shortcut in <see cref="Binding"/> is registered and working.</summary>
    public bool BindingIsActive => Binding is not null && Binding != Refused;

    /// <summary>The text kept in the app's local settings: a small JSON object with the readable shortcut ("Ctrl+Alt+T").</summary>
    public string ToStorage() => JsonSerializer.Serialize(new Stored(
        Binding?.ToText(), LastResult.ToString(), At?.ToString("o", CultureInfo.InvariantCulture), Refused?.ToText()), Options);

    /// <summary>Reads <see cref="ToStorage"/> back. Anything unreadable, or a shortcut that could not be registered (no modifier), gives <see cref="None"/>.</summary>
    public static HotkeyState FromStorage(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return None;
        }

        Stored? value;
        try
        {
            value = JsonSerializer.Deserialize<Stored>(stored, Options);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            return None;
        }

        if (value is null)
        {
            return None;
        }

        HotkeyBinding? binding = null;
        if (value.Binding is not null)
        {
            if (!HotkeyBinding.TryParse(value.Binding, out var parsed) || parsed.Validate() != HotkeyReason.None)
            {
                return None;
            }

            binding = parsed;
        }

        HotkeyBinding? refused = null;
        if (HotkeyBinding.TryParse(value.Refused, out var parsedRefused) && parsedRefused.Validate() == HotkeyReason.None)
        {
            refused = parsedRefused;
        }

        var result = Enum.TryParse<HotkeyStatus>(value.LastResult, ignoreCase: true, out var status) && status != HotkeyStatus.Invalid ? status : HotkeyStatus.Cleared;
        DateTimeOffset? at = DateTimeOffset.TryParse(value.At, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when) ? when : null;
        return new HotkeyState(binding, result, at, refused);
    }

    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private sealed record Stored(string? Binding, string? LastResult, string? At, string? Refused);
}
