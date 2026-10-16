namespace TimeTug.Presentation.Tug;

/// <summary>Where the tug for the current takeover request is.</summary>
public enum TugState
{
    /// <summary>No request is outstanding.</summary>
    Hidden,

    /// <summary>The full tug is on the monitors and the primary window has keyboard focus.</summary>
    Shown,

    /// <summary>The full tug is on the monitors but Windows did not give it the keyboard; the mouse still works ("Click anywhere to respond").</summary>
    ShownWithoutFocus,

    /// <summary>Windows accepted a toast; the request stays outstanding until a toast action or a return.</summary>
    Toast,

    /// <summary>The request could not be shown (locked session, covered by a full-screen app, toast refused) and waits for the person to return.</summary>
    HeldUntilReturn,
}
