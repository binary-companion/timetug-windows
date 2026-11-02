namespace TimeTug.Presentation.Welcome;

public enum FocusKind
{
    /// <summary>A row of the accounts list.</summary>
    Row,

    /// <summary>The "+" button (nothing is left to select).</summary>
    AddButton,
}

/// <summary>Where keyboard focus should go. Raised only after a confirmed removal: nothing the engine publishes moves focus.</summary>
public sealed record FocusTarget(FocusKind Kind, string? AccountId);
