using System.Runtime.InteropServices;
using Microsoft.Windows.ApplicationModel.Resources;
using TimeTug.Presentation.Abstractions;

namespace TimeTug.App;

/// <summary>The app's strings from <c>Strings/&lt;language&gt;/Resources.resw</c>.</summary>
public sealed class ResourceLoaderLocalizer : ILocalizer
{
    private readonly ResourceLoader _loader = new();

    public string GetString(string key) => TryGetString(key) ?? key;

    public string? TryGetString(string key)
    {
        try
        {
            var value = _loader.GetString(key);
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (Exception e) when (e is COMException or ArgumentException or InvalidOperationException)
        {
            // A missing key may throw rather than return empty; callers fall back to the engine's English text.
            return null;
        }
    }
}
