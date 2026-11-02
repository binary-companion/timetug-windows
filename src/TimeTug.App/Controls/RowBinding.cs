using Microsoft.UI.Xaml;

namespace TimeTug.App.Controls;

/// <summary>Small functions the accounts list's row template binds to.</summary>
internal static class RowBinding
{
    /// <summary>Collapsed when <paramref name="value"/> is true (the inverse of the built-in conversion).</summary>
    public static Visibility Hidden(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
}
