# DHCP Management Application

A professional Windows WPF application for managing DHCP servers built with DHCP.Core library.

## 🎯 Features

### Dashboard
- **Quick Start/Stop** server controls
- **Real-time statistics**: Active leases count, server status
- **Recent activity logs** display
- Server configuration overview

### Active Leases Management
- View all active DHCP leases in real-time
- Display lease information:
  - IP Address
  - MAC Address
  - Hostname
  - Lease start time
  - Remaining lease time
  - Lease status (Active/Static/Expired)
- Refresh leases on demand

### Static IP Bindings
- **Add static bindings** for specific devices (CNCs, PLCs, industrial machines)
- **Remove static bindings**
- Manage reserved IP addresses
- Add descriptions for each binding

### Server Settings
- **Network Configuration**:
  - Server IP Address
  - Subnet Mask
  - Gateway
  - Primary & Secondary DNS
  - Domain Name

- **IP Pool Configuration**:
  - Pool Start IP
  - Pool End IP

- **Lease Configuration**:
  - Default Lease Time (seconds)
  - Maximum Lease Time (seconds)

- **Additional Options**:
  - Enable/Disable Action Bridge
  - Configure Action Bridge Port
  - Auto-start server option

### Logs Viewer
- Real-time server logs
- Timestamp for each log entry
- Clear logs functionality
- Scrollable log history (up to 500 entries)

## 🚀 Getting Started

### Prerequisites
- Windows 10/11
- .NET 8 Runtime
- Administrator privileges (required for DHCP server on port 67)

### Running the Application

```powershell
# Navigate to the application directory
cd samples\DHCP.ManagementApp

# Run the application
dotnet run
```

Or build and run the executable:

```powershell
# Build
dotnet build

# Run
.\bin\Debug\net8.0-windows\DHCP.ManagementApp.exe
```

### First-Time Setup

1. **Launch the application**
2. **Go to Settings** tab
3. **Configure your network settings**:
   - Set your server IP address
   - Configure subnet mask and gateway
   - Set DNS servers
   - Define IP pool range
   - Set lease times
4. **Click "Save Settings"**
5. **Go to Dashboard** and click "Start Server"

## 📋 Usage

### Starting the DHCP Server

1. Navigate to **Dashboard**
2. Click **"▶️ Start Server"**
3. Monitor the status indicator (green = running, red = stopped)
4. Watch real-time activity in the logs

### Adding Static Bindings

1. Navigate to **Static Bindings** tab
2. Enter:
   - MAC Address (format: AA:BB:CC:DD:EE:FF)
   - IP Address
   - Description (optional)
3. Click **"➕ Add"**
4. The binding will be active immediately

### Viewing Active Leases

1. Navigate to **Active Leases** tab
2. View all current leases in the table
3. Click **"🔄 Refresh"** to update the list
4. Information includes:
   - IP and MAC addresses
   - Hostname
   - Lease timing
   - Status

### Monitoring Server Activity

1. Navigate to **Logs** tab
2. View real-time server events:
   - DHCP packet reception (DISCOVER, REQUEST, etc.)
   - Lease grants and releases
   - Server start/stop events
   - Errors and warnings
3. Use **"🗑️ Clear Logs"** to clear the display

## ⚙️ Configuration

Settings are stored in:
```
%APPDATA%\DHCPManagement\settings.json
```

You can manually edit this file or use the Settings UI.

### Example Configuration

```json
{
  "ServerIpAddress": "192.168.1.1",
  "SubnetMask": "255.255.255.0",
  "Gateway": "192.168.1.1",
  "PrimaryDns": "8.8.8.8",
  "SecondaryDns": "8.8.4.4",
  "DomainName": "local",
  "PoolStartIp": "192.168.1.100",
  "PoolEndIp": "192.168.1.200",
  "DefaultLeaseTime": 86400,
  "MaxLeaseTime": 604800,
  "EnableActionBridge": true,
  "ActionBridgePort": 8888,
  "AutoStart": false
}
```

## 🏗️ Architecture

### MVVM Pattern
The application uses the **Model-View-ViewModel** pattern:
- **Models**: Data structures (DhcpSettings, LeaseDisplayModel, etc.)
- **ViewModels**: Business logic and state management
- **Views**: XAML UI definitions

### Dependency Injection
Built with Microsoft.Extensions.DependencyInjection:
- Services registered in `App.xaml.cs`
- ViewModels injected into Views
- Loose coupling for testability

### Key Components

```
DHCP.ManagementApp/
├── Models/              # Data models
├── ViewModels/          # MVVM ViewModels
├── Views/               # WPF Views (XAML)
├── Services/            # Business services
│   ├── DhcpServerService    # DHCP server wrapper
│   └── SettingsService      # Configuration management
├── Converters/          # Value converters for binding
└── Styles/              # UI styling resources
```

## 🎨 UI Components

### Color Scheme
- **Primary**: Blue (#2563EB) - Main actions, highlights
- **Secondary**: Green (#10B981) - Success states
- **Danger**: Red (#EF4444) - Stop, delete actions
- **Background**: Light gray (#F9FAFB)
- **Surface**: White (#FFFFFF)

### Navigation
- Sidebar navigation for main sections
- Responsive layout
- Modern, clean design

## 🔧 Troubleshooting

### Server Won't Start
- **Check Administrator Privileges**: DHCP requires admin rights
- **Check Port 67**: Ensure no other DHCP server is running
- **Verify Network Settings**: Check that IP addresses are valid
- **Review Logs**: Check the logs tab for error messages

### No Leases Appearing
- **Ensure Server is Running**: Check dashboard status
- **Verify Network Configuration**: Clients must be on the same network
- **Check IP Pool**: Ensure pool has available addresses
- **Refresh**: Click the refresh button in Leases tab

### Settings Not Saved
- **Check File Permissions**: Ensure write access to %APPDATA%
- **Validate Input**: All IP addresses must be valid
- **Restart Required**: Changes require server restart

## 📝 Logs Location

Application logs are stored in:
```
.\logs\dhcp-management-YYYYMMDD.log
```

## 🤝 Integration

### With Windows Service
The Management App can control a DHCP server running as a Windows Service by connecting to the same DHCP.Core instance.

### With Monitoring Tools
Export logs for analysis in external tools:
- Serilog file sink format
- Structured logging support

## 🛡️ Security Considerations

- **Administrator Rights**: Required for binding to port 67
- **Network Exposure**: DHCP server will respond to all network requests
- **Static Bindings**: Use for critical infrastructure devices
- **Action Bridge**: Secure the TCP port if enabled

## 📚 Related Documentation

- [DHCP.Core Library Documentation](../../src/DHCP.Core/README.md)
- [RFC 2131 - DHCP Protocol](https://tools.ietf.org/html/rfc2131)
- [RFC 2132 - DHCP Options](https://tools.ietf.org/html/rfc2132)

## 💡 Tips

1. **Start Small**: Begin with a small IP pool for testing
2. **Monitor Logs**: Keep an eye on the activity log for issues
3. **Static Bindings**: Use for servers, printers, and critical devices
4. **Regular Backups**: Export your configuration periodically
5. **Test Changes**: Stop the server before making major configuration changes

## 🆘 Support

For issues, questions, or feature requests:
- Check the logs for error details
- Review the DHCP.Core documentation
- Consult RFC 2131 for protocol specifications

---

**Built with ❤️ using WPF, MVVM, and DHCP.Core**
