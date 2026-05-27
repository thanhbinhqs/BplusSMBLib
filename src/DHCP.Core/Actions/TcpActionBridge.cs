using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DHCP.Core.Actions;

/// <summary>
/// TCP-based action bridge implementation for remote client management
/// Allows server UI to push commands to DHCP clients
/// </summary>
public sealed class TcpActionBridge : IActionBridge, IDisposable
{
    private readonly ILogger<TcpActionBridge> _logger;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    // Connected clients: IP -> Connection info
    private readonly ConcurrentDictionary<string, ClientConnection> _connectedClients = new();

    // Active client sockets: IP -> Socket
    private readonly ConcurrentDictionary<string, Socket> _clientSockets = new();

    // Pending actions: IP -> Queue of actions
    private readonly ConcurrentDictionary<string, ConcurrentQueue<ClientActionPayload>> _pendingActions = new();

    public event EventHandler<ClientConnectionEventArgs>? ClientConnected;
    public event EventHandler<ClientConnectionEventArgs>? ClientDisconnected;
    public event EventHandler<ActionResultEventArgs>? ActionResultReceived;

    public TcpActionBridge(ILogger<TcpActionBridge> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(int port, CancellationToken cancellationToken = default)
    {
        if (_listener is not null)
        {
            _logger.LogWarning("Action bridge is already running");
            return;
        }

        try
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();

            _logger.LogInformation("Action bridge started on port {Port}", port);

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _acceptTask = AcceptClientsAsync(_cts.Token);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start action bridge");
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (_listener is null)
        {
            _logger.LogWarning("Action bridge is not running");
            return;
        }

        try
        {
            _logger.LogInformation("Stopping action bridge...");

            _cts?.Cancel();

            if (_acceptTask is not null)
            {
                await _acceptTask;
            }

            // Close all client connections
            foreach (var socket in _clientSockets.Values)
            {
                try
                {
                    socket.Close();
                    socket.Dispose();
                }
                catch { }
            }

            _clientSockets.Clear();
            _connectedClients.Clear();

            _listener?.Stop();
            _listener = null;

            _logger.LogInformation("Action bridge stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping action bridge");
        }
    }

    public async Task<bool> SendActionToClientAsync(IPAddress clientIp, ClientActionPayload action, CancellationToken cancellationToken = default)
    {
        var ipKey = clientIp.ToString();

        if (!_clientSockets.TryGetValue(ipKey, out var socket))
        {
            _logger.LogWarning("Client {IpAddress} is not connected - queuing action", clientIp);

            // Queue action for when client connects
            var queue = _pendingActions.GetOrAdd(ipKey, _ => new ConcurrentQueue<ClientActionPayload>());
            queue.Enqueue(action);

            return false;
        }

        try
        {
            var json = JsonSerializer.Serialize(action);
            var data = Encoding.UTF8.GetBytes(json + "\n");

            await socket.SendAsync(data, SocketFlags.None, cancellationToken);

            _logger.LogInformation("Sent action {ActionId} to {IpAddress}", action.ActionId, clientIp);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send action to {IpAddress}", clientIp);
            return false;
        }
    }

    public async Task<bool> SendActionToClientAsync(string macAddress, ClientActionPayload action, CancellationToken cancellationToken = default)
    {
        // Find client by MAC address
        var client = _connectedClients.Values.FirstOrDefault(c => 
            c.MacAddress?.Equals(macAddress, StringComparison.OrdinalIgnoreCase) == true);

        if (client is null)
        {
            _logger.LogWarning("Client with MAC {MacAddress} not found", macAddress);
            return false;
        }

        return await SendActionToClientAsync(client.IpAddress, action, cancellationToken);
    }

    public async Task BroadcastActionAsync(ClientActionPayload action, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Broadcasting action {ActionId} to {Count} clients", action.ActionId, _clientSockets.Count);

        var tasks = _connectedClients.Keys
            .Select(ipKey => SendActionToClientAsync(IPAddress.Parse(ipKey), action, cancellationToken));

        await Task.WhenAll(tasks);
    }

    public IReadOnlyCollection<ClientConnection> GetConnectedClients()
    {
        return _connectedClients.Values.ToList();
    }

    public bool IsClientConnected(IPAddress clientIp)
    {
        return _clientSockets.ContainsKey(clientIp.ToString());
    }

    public IReadOnlyCollection<ClientActionPayload> GetPendingActions(IPAddress clientIp)
    {
        var ipKey = clientIp.ToString();

        if (_pendingActions.TryGetValue(ipKey, out var queue))
        {
            return queue.ToList();
        }

        return Array.Empty<ClientActionPayload>();
    }

    private async Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Action bridge accept loop started");

        while (!cancellationToken.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var socket = await _listener.AcceptSocketAsync(cancellationToken);
                var remoteEndpoint = (IPEndPoint)socket.RemoteEndPoint!;

                _logger.LogInformation("Client connected from {IpAddress}", remoteEndpoint.Address);

                // Handle client on separate task
                _ = Task.Run(() => HandleClientAsync(socket, remoteEndpoint, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting client connection");
            }
        }

        _logger.LogInformation("Action bridge accept loop stopped");
    }

    private async Task HandleClientAsync(Socket socket, IPEndPoint remoteEndpoint, CancellationToken cancellationToken)
    {
        var ipKey = remoteEndpoint.Address.ToString();

        try
        {
            // Register client
            var client = new ClientConnection
            {
                IpAddress = remoteEndpoint.Address,
                ConnectedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow
            };

            _connectedClients[ipKey] = client;
            _clientSockets[ipKey] = socket;

            ClientConnected?.Invoke(this, new ClientConnectionEventArgs(client));

            // Send any pending actions
            if (_pendingActions.TryGetValue(ipKey, out var pendingQueue))
            {
                while (pendingQueue.TryDequeue(out var action))
                {
                    await SendActionToClientAsync(remoteEndpoint.Address, action, cancellationToken);
                }
            }

            // Read loop
            var buffer = new byte[8192];
            while (!cancellationToken.IsCancellationRequested)
            {
                var bytesRead = await socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);

                if (bytesRead == 0)
                {
                    _logger.LogInformation("Client {IpAddress} disconnected", remoteEndpoint.Address);
                    break;
                }

                // Update activity timestamp
                client.LastActivityAt = DateTime.UtcNow;

                // Process received data (action results)
                var data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                await ProcessClientDataAsync(remoteEndpoint.Address, data);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling client {IpAddress}", remoteEndpoint.Address);
        }
        finally
        {
            // Cleanup
            _clientSockets.TryRemove(ipKey, out _);

            if (_connectedClients.TryRemove(ipKey, out var client))
            {
                ClientDisconnected?.Invoke(this, new ClientConnectionEventArgs(client));
            }

            try
            {
                socket.Close();
                socket.Dispose();
            }
            catch { }
        }
    }

    private async Task ProcessClientDataAsync(IPAddress clientIp, string data)
    {
        try
        {
            // Parse action result
            var result = JsonSerializer.Deserialize<ActionResultEventArgs>(data);

            if (result is not null)
            {
                _logger.LogInformation(
                    "Received action result from {IpAddress}: ActionId={ActionId}, Success={Success}",
                    clientIp,
                    result.ActionId,
                    result.Success
                );

                ActionResultReceived?.Invoke(this, result);
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing client data from {IpAddress}", clientIp);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();

        foreach (var socket in _clientSockets.Values)
        {
            try
            {
                socket.Dispose();
            }
            catch { }
        }

        _listener?.Stop();
    }
}
