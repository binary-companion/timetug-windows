using Microsoft.UI.Xaml;
using TimeTug.Presentation.Accounts;

namespace TimeTug.App.Controls;

/// <summary>Small functions the accounts list's row template binds to.</summary>
internal static class RowBinding
{
    /// <summary>Collapsed when <paramref name="value"/> is true (the inverse of the built-in conversion).</summary>
    public static Visibility Hidden(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Visible when the kind has a vendor logo.</summary>
    public static Visibility ShowMark(ProviderMark mark) => mark == ProviderMark.None ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Visible when the kind has no vendor logo (the generic glyph shows instead).</summary>
    public static Visibility HideMark(ProviderMark mark) => mark == ProviderMark.None ? Visibility.Visible : Visibility.Collapsed;
}
