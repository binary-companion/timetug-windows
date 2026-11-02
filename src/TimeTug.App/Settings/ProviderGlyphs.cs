namespace TimeTug.App.Settings;

/// <summary>The Segoe Fluent glyph that stands for an account kind that has no vendor logo (see <see cref="ProviderLogos"/>; the Mac draws its own marks, <c>ProviderIcon</c>).</summary>
internal static class ProviderGlyphs
{
    public const string Calendar = "";
    public const string Unknown = "";

    public static string Of(string kindId) => kindId switch
    {
        "google" => "",
        "microsoft" => "",
        "icloud" => "",
        "caldav" => "",
        "icalsub" => "",
        _ => Calendar,
    };
}
