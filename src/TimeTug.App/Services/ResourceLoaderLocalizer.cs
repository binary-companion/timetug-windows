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
        var value = _loader.GetString(key);
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
