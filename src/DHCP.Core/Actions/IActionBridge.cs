using System.Net;

namespace DHCP.Core.Actions;

/// <summary>
/// Interface for client action bridge
/// Enables remote command execution on DHCP clients
/// </summary>
public interface IActionBridge
{
    /// <summary>
    /// Event fired when a client connects to the action channel
    /// </summary>
    event EventHandler<ClientConnectionEventArgs>? ClientConnected;

    /// <summary>
    /// Event fired when a client disconnects from the action channel
    /// </summary>
    event EventHandler<ClientConnectionEventArgs>? ClientDisconnected;

    /// <summary>
    /// Event fired when an action result is received from a client
    /// </summary>
    event EventHandler<ActionResultEventArgs>? ActionResultReceived;

    /// <summary>
    /// Start the action bridge server
    /// </summary>
    Task StartAsync(int port, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop the action bridge server
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Send an action to a specific client by IP address
    /// </summary>
    Task<bool> SendActionToClientAsync(IPAddress clientIp, ClientActionPayload action, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send an action to a specific client by MAC address
    /// </summary>
    Task<bool> SendActionToClientAsync(string macAddress, ClientActionPayload action, CancellationToken cancellationToken = default);

    /// <summary>
    /// Broadcast an action to all connected clients
    /// </summary>
    Task BroadcastActionAsync(ClientActionPayload action, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get list of currently connected clients
    /// </summary>
    IReadOnlyCollection<ClientConnection> GetConnectedClients();

    /// <summary>
    /// Check if a client is connected
    /// </summary>
    bool IsClientConnected(IPAddress clientIp);

    /// <summary>
    /// Get pending actions for a specific client
    /// </summary>
    IReadOnlyCollection<ClientActionPayload> GetPendingActions(IPAddress clientIp);
}

/// <summary>
/// Represents a connected client
/// </summary>
public sealed class ClientConnection
{
    public IPAddress IpAddress { get; init; } = IPAddress.Any;
    public string? MacAddress { get; init; }
    public DateTime ConnectedAt { get; init; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
    public string? ClientInfo { get; init; }
}

/// <summary>
/// Event args for client connection events
/// </summary>
public sealed class ClientConnectionEventArgs : EventArgs
{
    public ClientConnection Client { get; init; }

    public ClientConnectionEventArgs(ClientConnection client)
    {
        Client = client;
    }
}

/// <summary>
/// Event args for action result events
/// </summary>
public sealed class ActionResultEventArgs : EventArgs
{
    public Guid ActionId { get; init; }
    public IPAddress ClientIp { get; init; } = IPAddress.Any;
    public bool Success { get; init; }
    public string? ResultData { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime ReceivedAt { get; init; } = DateTime.UtcNow;
}
