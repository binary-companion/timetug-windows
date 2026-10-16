using System.Net.NetworkInformation;

namespace TimeTug.Platform.OsState;

/// <summary>Raises an event when the machine gains or loses network access.</summary>
public interface INetworkAvailability
{
    /// <summary>The argument is true when a network became available.</summary>
    event Action<bool>? AvailabilityChanged;
}

/// <summary><see cref="NetworkChange.NetworkAvailabilityChanged"/> as an <see cref="INetworkAvailability"/>.</summary>
public sealed class NetworkAvailability : INetworkAvailability, IDisposable
{
    public NetworkAvailability() => NetworkChange.NetworkAvailabilityChanged += OnChanged;

    public event Action<bool>? AvailabilityChanged;

    public void Dispose() => NetworkChange.NetworkAvailabilityChanged -= OnChanged;

    private void OnChanged(object? sender, NetworkAvailabilityEventArgs e) => AvailabilityChanged?.Invoke(e.IsAvailable);
}
