namespace TimeTug.Platform.Broker;

/// <summary>The window handle the Windows sign-in dialog is parented to (the broker requires one). Implemented by the app.</summary>
public interface IBrokerParentWindow
{
    /// <summary>A top-level window handle, never zero.</summary>
    nint Handle { get; }
}
