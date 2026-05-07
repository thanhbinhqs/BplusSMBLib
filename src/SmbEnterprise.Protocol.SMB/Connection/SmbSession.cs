using SMBLibrary;
using SMBLibrary.Client;
using SmbEnterprise.Core.Models;
using Microsoft.Extensions.Logging;

namespace SmbEnterprise.Protocol.SMB.Connection;

/// <summary>
/// Holds an active SMB2Client session connected to a specific server/share.
/// SMBLibrary is isolated within this layer only.
/// </summary>
internal sealed class SmbSession : IAsyncDisposable
{
    private readonly SMB2Client _client;
    private readonly ISMBFileStore? _fileStore;
    private readonly ILogger _logger;
    private volatile bool _isConnected;
    private int _disposeState;
    private DateTime _lastActivity = DateTime.UtcNow;

    public string Server { get; }
    public string Share { get; }
    public string Username { get; }
    public bool IsConnected => _isConnected;
    public DateTime LastActivity => _lastActivity;

    /// <summary>SMB capabilities negotiated during connect.</summary>
    public SmbCapabilities Capabilities { get; private set; } = new();

    internal SMB2Client Client => _client;
    internal ISMBFileStore? FileStore => _fileStore;

    private SmbSession(SMB2Client client, ISMBFileStore fileStore,
        string server, string share, string username, SmbCapabilities capabilities, ILogger logger)
    {
        _client = client;
        _fileStore = fileStore;
        Server = server;
        Share = share;
        Username = username;
        Capabilities = capabilities;
        _isConnected = true;
        _logger = logger;
    }

    public static async Task<SmbSession> ConnectAsync(
        RemoteCredential credential,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var opId = Guid.NewGuid().ToString("N")[..8];
        var client = new SMB2Client();

        logger.LogInformation("[{OpId}] Connecting to {Server}:{Port} share={Share} user={User}",
            opId, credential.Server, credential.Port, credential.Share, credential.Username);

        var connected = await Task.Run(() =>
            client.Connect(credential.Server, SMBTransportType.DirectTCPTransport), cancellationToken)
            .ConfigureAwait(false);

        if (!connected)
            throw new InvalidOperationException($"Failed to connect to {credential.Server}:{credential.Port}");

        logger.LogDebug("[{OpId}] TCP connect success server={Server}", opId, credential.Server);

        NTStatus loginStatus;
        if (credential.IsAnonymous)
        {
            loginStatus = await Task.Run(() =>
                client.Login(string.Empty, string.Empty, string.Empty), cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            loginStatus = await Task.Run(() =>
                client.Login(credential.Domain ?? string.Empty,
                             credential.Username ?? string.Empty,
                             credential.Password ?? string.Empty), cancellationToken)
                .ConfigureAwait(false);
        }

        if (loginStatus != NTStatus.STATUS_SUCCESS)
            throw new UnauthorizedAccessException($"SMB login failed: {loginStatus}");

        logger.LogDebug("[{OpId}] Login success user={User}", opId, credential.Username);

        var store = client.TreeConnect(credential.Share, out var storeResult);
        if (storeResult != NTStatus.STATUS_SUCCESS || store is null)
            throw new InvalidOperationException($"Failed to connect to share '{credential.Share}': {storeResult}");

        var capabilities = NegotiateCapabilities(client);
        logger.LogInformation("[{OpId}] Connected to \\\\{Server}\\{Share} dialect={Dialect} maxRead={MaxRead} maxWrite={MaxWrite}",
            opId, credential.Server, credential.Share, capabilities.Dialect, capabilities.MaxReadSize, capabilities.MaxWriteSize);

        return new SmbSession(client, store,
            credential.Server, credential.Share, credential.Username ?? "anonymous",
            capabilities, logger);
    }

    private static SmbCapabilities NegotiateCapabilities(SMB2Client client)
    {
        return new SmbCapabilities
        {
            Dialect = "SMB 2.x",
            MaxReadSize = (int)client.MaxReadSize,
            MaxWriteSize = (int)client.MaxWriteSize
        };
    }

    public void Touch() => _lastActivity = DateTime.UtcNow;

    internal void MarkDisconnected(string reason)
    {
        if (!_isConnected)
        {
            return;
        }

        _isConnected = false;
        _logger.LogWarning("Mark SMB session disconnected server={Server} share={Share} reason={Reason}", Server, Share, reason);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) == 1)
        {
            return;
        }

        _isConnected = false;

        try
        {
            _logger.LogDebug("Disposing SMB session server={Server} share={Share} user={User}", Server, Share, Username);
            _fileStore?.Disconnect();
            await Task.Run(() => _client.Logoff()).ConfigureAwait(false);
            _client.Disconnect();
            _logger.LogDebug("Disposed SMB session server={Server} share={Share}", Server, Share);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during SMB session disposal");
        }
    }
}

public sealed class SmbCapabilities
{
    public string Dialect { get; init; } = "Unknown";
    public int MaxReadSize { get; init; } = 65536;
    public int MaxWriteSize { get; init; } = 65536;
    public bool SupportsEncryption { get; init; }
    public bool SupportsDurableHandles { get; init; }
}
