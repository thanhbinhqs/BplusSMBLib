# DHCP.Core - Production-Ready DHCPv4 Core Library

A high-performance, extensible **DHCPv4 Core Library** written in modern C# (.NET 8) that implements the complete DHCP protocol as specified in **RFC 2131** and **RFC 2132**.

## 🎯 Features

- ✅ **RFC 2131 Compliant**: Full implementation of DHCPv4 protocol state machine
- ✅ **High Performance**: Asynchronous, non-blocking I/O with minimal memory allocations
- ✅ **Dynamic IP Pool Management**: Flexible IP address allocation with automatic lease tracking
- ✅ **Static IP Bindings**: Reserved IPs for specific MAC addresses (perfect for industrial devices like CNCs, PLCs)
- ✅ **Event-Driven Architecture**: Real-time monitoring via C# events
- ✅ **Client Action Bridge**: Remote command execution infrastructure for managed clients
- ✅ **Production Ready**: Thread-safe, extensively logged, dependency injection support
- ✅ **Modern C#**: File-scoped namespaces, pattern matching, primary constructors, init-only properties

## 📦 Installation

Add the library to your project:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\DHCP.Core\DHCP.Core.csproj" />
</ItemGroup>
```

## 🚀 Quick Start

### 1. Basic DHCP Server Setup

```csharp
using System.Net;
using DHCP.Core;
using DHCP.Core.Engine;
using DHCP.Core.Network;
using Microsoft.Extensions.Logging;

// Create logger
using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
var logger = loggerFactory.CreateLogger<DhcpServerEngine>();
var listenerLogger = loggerFactory.CreateLogger<DhcpListener>();
var engineLogger = loggerFactory.CreateLogger<IpAllocationEngine>();

// Configure DHCP server
var config = new DhcpServerConfiguration
{
    ServerIpAddress = IPAddress.Parse("192.168.1.1"),
    SubnetMask = IPAddress.Parse("255.255.255.0"),
    Gateway = IPAddress.Parse("192.168.1.1"),
    DnsServers = new[] 
    { 
        IPAddress.Parse("8.8.8.8"), 
        IPAddress.Parse("8.8.4.4") 
    },
    DefaultLeaseTime = 86400, // 24 hours
    DomainName = "example.local"
};

// Create components
var listener = new DhcpListener(listenerLogger);
var allocationEngine = new IpAllocationEngine(
    engineLogger,
    config,
    IPAddress.Parse("192.168.1.100"), // Pool start
    IPAddress.Parse("192.168.1.200")  // Pool end
);

// Create and start server
var server = new DhcpServerEngine(logger, listener, allocationEngine, config);

// Subscribe to events
server.LeaseGranted += (sender, lease) => 
{
    Console.WriteLine($"Lease granted: {lease.IpAddress} -> {lease.MacAddress}");
};

server.PacketReceived += (sender, packet) =>
{
    Console.WriteLine($"Received {packet.GetMessageType()} from {packet.GetMacAddress()}");
};

// Start server
await server.StartAsync();
Console.WriteLine("DHCP Server is running. Press any key to stop...");
Console.ReadKey();

// Stop server
await server.StopAsync();
```

### 2. Using Dependency Injection

```csharp
using DHCP.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// Add DHCP server services
builder.Services.AddDhcpServer(options =>
{
    options.Configuration = new DhcpServerConfiguration
    {
        ServerIpAddress = IPAddress.Parse("192.168.1.1"),
        SubnetMask = IPAddress.Parse("255.255.255.0"),
        Gateway = IPAddress.Parse("192.168.1.1"),
        DnsServers = new[] { IPAddress.Parse("8.8.8.8") },
        DefaultLeaseTime = 86400
    };

    options.PoolStartIp = IPAddress.Parse("192.168.1.100");
    options.PoolEndIp = IPAddress.Parse("192.168.1.200");
    options.EnableActionBridge = true;
    options.ActionBridgePort = 8888;
});

var app = builder.Build();

// Get server instance
var server = app.Services.GetRequiredService<DhcpServerEngine>();
await server.StartAsync();

await app.RunAsync();
```

### 3. Static IP Bindings (For Industrial Machines)

```csharp
// Add static binding for a CNC machine
server.AddStaticBinding("00:11:22:33:44:55", IPAddress.Parse("192.168.1.50"));

// Add static binding for a PLC
server.AddStaticBinding("AA:BB:CC:DD:EE:FF", IPAddress.Parse("192.168.1.51"));

// Remove static binding
server.RemoveStaticBinding("00:11:22:33:44:55");
```

### 4. Client Action Bridge (Remote Command Execution)

```csharp
using DHCP.Core.Actions;

// Get action bridge instance
var actionBridge = app.Services.GetRequiredService<IActionBridge>();
await actionBridge.StartAsync(8888);

// Subscribe to client events
actionBridge.ClientConnected += (sender, e) =>
{
    Console.WriteLine($"Client connected: {e.Client.IpAddress}");
};

actionBridge.ActionResultReceived += (sender, e) =>
{
    Console.WriteLine($"Action {e.ActionId} result: {e.Success}");
};

// Send command to client
var action = new ClientActionPayload
{
    CommandType = "INSTALL_SOFTWARE",
    Payload = JsonSerializer.Serialize(new { PackageName = "MyApp", Version = "1.0" }),
    TargetIpAddress = "192.168.1.150"
};

await actionBridge.SendActionToClientAsync(IPAddress.Parse("192.168.1.150"), action);

// Broadcast to all clients
await actionBridge.BroadcastActionAsync(action);
```

## 🏗️ Architecture

The library is organized into logical layers:

```
DHCP.Core/
├── Models/                      # Data models and enums
│   ├── DhcpMessageType.cs      # DHCP message types (Discover, Offer, etc.)
│   ├── DhcpOptionCode.cs       # DHCP option codes (RFC 2132)
│   ├── DhcpOption.cs           # DHCP option with helper methods
│   └── DhcpPacket.cs           # DHCPv4 packet structure
├── Network/                     # Network layer
│   ├── DhcpParser.cs           # Binary packet parser/serializer
│   └── DhcpListener.cs         # Asynchronous UDP listener
├── Engine/                      # Core logic
│   ├── DhcpLease.cs            # Lease data structure
│   ├── DhcpServerConfiguration.cs  # Server configuration
│   └── IpAllocationEngine.cs   # IP allocation and state machine
├── Actions/                     # Client action bridge
│   ├── ClientActionPayload.cs  # Action payload model
│   ├── IActionBridge.cs        # Action bridge interface
│   └── TcpActionBridge.cs      # TCP-based implementation
├── Extensions/                  # Dependency injection
│   └── ServiceCollectionExtensions.cs
└── DhcpServerEngine.cs         # Main orchestrator
```

## 📋 DHCP State Machine

The library implements the complete DHCP state machine:

1. **DISCOVER** → Server finds available IP (static or dynamic pool) → **OFFER**
2. **REQUEST** → Server validates and commits lease → **ACK** (or **NAK**)
3. **RELEASE** → Client releases IP → Lease freed
4. **DECLINE** → Client reports IP conflict → IP marked as problematic
5. **INFORM** → Client requests network parameters only

## 🎯 Use Cases

### Windows Service Integration
Perfect for hosting inside a Windows Service to provide DHCP functionality.

### Server UI Monitoring
Real-time events allow building a monitoring dashboard:
- Track active leases
- View DHCP packet traffic
- Monitor IP pool utilization
- Manage static bindings

### Industrial Automation
Static bindings ensure CNCs, PLCs, and other industrial machines always get the same IP.

### Remote Management
Action Bridge enables pushing commands to clients in Windows Audit Mode for:
- Software deployment
- Configuration updates
- Remote diagnostics
- System provisioning

## ⚡ Performance Characteristics

- **Asynchronous I/O**: Non-blocking socket operations
- **Zero-copy parsing**: Uses `Span<byte>` and `ReadOnlyMemory<byte>`
- **Thread-safe**: Concurrent dictionaries for lease management
- **Minimal allocations**: Reusable buffers where possible
- **Event-driven**: Efficient callback-based architecture

## 🔒 Thread Safety

All public APIs are thread-safe. The library uses:
- `ConcurrentDictionary` for lease tracking
- Proper lock-free synchronization
- Async/await for I/O operations

## 📝 Logging

The library integrates with `Microsoft.Extensions.Logging`:
- Structured logging with Serilog support
- Configurable log levels
- Comprehensive error logging
- Performance metrics

## 🧪 Testing

```bash
# Build the library
dotnet build src/DHCP.Core/DHCP.Core.csproj

# Run tests (if available)
dotnet test tests/DHCP.Core.Tests/DHCP.Core.Tests.csproj
```

## 📚 Additional Resources

- [RFC 2131 - Dynamic Host Configuration Protocol](https://tools.ietf.org/html/rfc2131)
- [RFC 2132 - DHCP Options and BOOTP Vendor Extensions](https://tools.ietf.org/html/rfc2132)

## 🤝 Contributing

This is a production-ready library designed for enterprise use. Contributions should:
- Follow RFC specifications strictly
- Maintain backward compatibility
- Include comprehensive logging
- Be fully asynchronous
- Pass all tests

## 📄 License

This library is part of the BplusSMBLib project.

## 🏆 Credits

Designed and implemented following software engineering best practices:
- Clean architecture
- SOLID principles
- Asynchronous patterns
- Modern C# features

---

**Built with ❤️ for professional DHCP server implementations**
