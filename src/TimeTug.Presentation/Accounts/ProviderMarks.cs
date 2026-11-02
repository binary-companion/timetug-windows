namespace TimeTug.Presentation.Accounts;

/// <summary>The vendor logo an account row or menu entry shows. Kinds without a vendor logo keep a generic symbol the app picks.</summary>
public enum ProviderMark
{
    /// <summary>No vendor logo: the app shows its own symbol (iCloud, CalDAV, iCal link, anything new).</summary>
    None,

    /// <summary>The Microsoft four-square logo, for either way of signing in to a Microsoft account.</summary>
    Microsoft,

    /// <summary>The Google "G".</summary>
    Google,
}

public static class ProviderMarks
{
    /// <summary>The mark for an engine account kind (or the menu entry id of the Windows account picker).</summary>
    public static ProviderMark Of(string kindId) => kindId switch
    {
        BrokeredKinds.Browser or BrokeredKinds.Brokered or BrokeredKinds.PickItem => ProviderMark.Microsoft,
        "google" => ProviderMark.Google,
        _ => ProviderMark.None,
    };
}
