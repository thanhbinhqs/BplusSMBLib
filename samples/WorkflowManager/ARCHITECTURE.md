# Kiến trúc và Design Decisions

## Tổng quan kiến trúc

```
WorkflowManager (WinForms App)
├── Models/
│   ├── PackageInfo.cs           - Data model cho package
│   └── SmbConnectionConfig.cs   - Configuration model
├── Services/
│   ├── PackageScannerService.cs - Business logic quét packages
│   └── PackageCopyService.cs    - Business logic copy files
├── MainForm.cs                  - UI layer
└── Program.cs                   - Entry point & DI setup
```

## Design Patterns

### 1. Dependency Injection

**Quyết định**: Sử dụng Microsoft.Extensions.DependencyInjection

**Lý do**:
- Loose coupling giữa các components
- Dễ dàng test với mock dependencies
- Consistent với kiến trúc của solution hiện có
- Lifetime management tự động (singleton, transient, scoped)

**Implementation**:
```csharp
services.AddSingleton<PackageScannerService>();
services.AddSingleton<PackageCopyService>();
services.AddTransient<MainForm>();
```

### 2. Factory Pattern

**Quyết định**: Sử dụng `Func<IRemoteFileSystem>` factory

**Lý do**:
- Mỗi operation cần một instance mới của `IRemoteFileSystem`
- Không thể share connection giữa các operations
- Factory cho phép tạo instance on-demand
- Giữ được DI container quản lý dependencies

**Implementation**:
```csharp
private readonly Func<IRemoteFileSystem> _fileSystemFactory;

var fileSystem = _fileSystemFactory();
await fileSystem.ConnectAsync(...);
// Use fileSystem
await fileSystem.DisconnectAsync();
```

### 3. Repository/Service Pattern

**Quyết định**: Tách business logic ra Services

**Lý do**:
- Separation of concerns (UI vs business logic)
- Reusability (có thể dùng services trong console app, API, etc.)
- Testability (test services độc lập với UI)
- Single Responsibility Principle

### 4. Observer Pattern (Progress Reporting)

**Quyết định**: Sử dụng `IProgress<T>`

**Lý do**:
- Thread-safe progress reporting
- Decoupling giữa worker và UI
- Built-in support trong .NET
- Tự động marshal về UI thread

**Implementation**:
```csharp
var progress = new Progress<CopyProgress>(p =>
{
    progressBar.Value = (int)p.TotalPercentage;
    lblStatus.Text = $"Copying {p.CurrentFile}...";
});

await _copyService.CopyPackageAsync(..., progress);
```

## Layer Architecture

### Models Layer

**Responsibility**: Data structures và validation rules

**Classes**:
- `PackageInfo`: Immutable record với computed properties
- `SmbConnectionConfig`: Configuration với default values
- `CopyProgress`: Progress reporting data

**Design decisions**:
- Sử dụng `record` cho immutability
- `required` properties để đảm bảo initialization
- Computed properties cho derived values

### Services Layer

**Responsibility**: Business logic và SMB operations

**Classes**:
- `PackageScannerService`: Scan và validate packages
- `PackageCopyService`: Copy operations với progress

**Design decisions**:
- Stateless services (có thể cache kết quả nếu cần)
- Async/await cho I/O operations
- Cancellation token support
- Comprehensive logging
- Error handling và retries (có thể thêm)

### UI Layer

**Responsibility**: User interaction và presentation

**Classes**:
- `MainForm`: WinForms form với event handlers

**Design decisions**:
- Code-first UI generation (không dùng designer)
- Explicit control naming cho testability
- `async void` event handlers (WinForms limitation)
- Progress reporting trên UI thread

## Threading Model

### UI Thread
- Form initialization
- Event handlers
- Progress updates (marshaled từ worker thread)

### Worker Thread (ThreadPool)
- SMB operations (connect, scan, copy)
- File I/O
- Progress computation

### Thread Safety
- `IProgress<T>` tự động marshal về UI thread
- Services không share state giữa operations
- Mỗi operation có isolated `IRemoteFileSystem` instance

## Error Handling Strategy

### Layers of error handling

1. **Service Layer**:
   - Catch và log exceptions
   - Rethrow với context
   - Cleanup resources (disconnect)

2. **UI Layer**:
   - Catch exceptions từ services
   - Show user-friendly messages
   - Update UI state (re-enable buttons)

3. **Application Layer**:
   - Global exception handler
   - Fatal error logging
   - Graceful shutdown

### Exception types

```csharp
try
{
    await service.ScanAsync(...);
}
catch (UnauthorizedAccessException)
{
    // Wrong credentials
}
catch (TimeoutException)
{
    // Network timeout
}
catch (IOException)
{
    // Disk space, permissions, etc.
}
catch (Exception ex)
{
    // Unexpected errors
    _logger.LogError(ex, "Unexpected error");
}
```

## Validation Strategy

### Input Validation

**Folder name format**: `^[A-Z0-9-]+$`
- Uppercase letters, digits, hyphens
- No spaces, no special characters

**Base name format**: Same as folder name

**Package structure**:
- Must have `.hash`, `.whd`, `.wcl` files
- Must have 5-12 `.wxx` files
- All files must share same base name

### Implementation

```csharp
[GeneratedRegex(@"^[A-Z0-9-]+$", RegexOptions.Compiled)]
private static partial Regex ValidFolderNameRegex();
```

**Benefits**:
- Compile-time regex generation (.NET 7+)
- Better performance than runtime regex
- Source generation cho debugging

## Performance Considerations

### Scan Performance

**Approach**: Parallel enumeration với bounded concurrency

**Considerations**:
- Network bandwidth limits
- SMB connection pool
- Memory usage cho file listings

**Current**: Sequential scan
**Future**: Parallel scan với semaphore

### Copy Performance

**Buffer size**: 81920 bytes (80KB)

**Rationale**:
- Balance giữa memory và throughput
- Optimal cho network I/O
- Large Object Heap avoidance (< 85KB)

**Progress reporting**: Every buffer write

**Rationale**:
- Smooth progress bar
- Reasonable overhead
- Could throttle nếu cần (mỗi 1MB chẳng hạn)

### Memory Management

**Stream disposal**: `await using` statements

**Benefits**:
- Automatic cleanup
- Exception-safe
- Async-friendly

**File enumeration**: `IAsyncEnumerable<T>`

**Benefits**:
- Streaming (không load tất cả vào memory)
- Cancellation support
- Async iteration

## Logging Strategy

### Log Levels

- **Debug**: Chi tiết implementation (path normalization, etc.)
- **Information**: Major operations (connect, scan start, copy complete)
- **Warning**: Recoverable issues (invalid folder, skip file)
- **Error**: Operation failures (connection failed, copy error)
- **Fatal**: Application crashes

### Structured Logging

```csharp
_logger.LogInformation(
    "Copying file {FileName} ({FileIndex}/{TotalFiles})", 
    file.Name, i + 1, fileList.Count);
```

**Benefits**:
- Searchable logs
- Aggregation-friendly
- Better tooling support

### Log Sinks

- **Console**: Development debugging
- **File**: Production logging (rolling daily)

## Configuration Management

### Current Approach

Hard-coded defaults trong code:
```csharp
public const string DefaultSharePath = @"\\192.168.1.250\share\image";
public const string DefaultUsername = "share";
public const string DefaultPassword = "1234567890";
```

### Future Improvements

**Option 1: appsettings.json**
```json
{
  "Smb": {
    "SharePath": "\\\\192.168.1.250\\share\\image",
    "Username": "share",
    "Password": "encrypted_value"
  }
}
```

**Option 2: User settings (Windows Registry)**
- Persist user's last configuration
- Encrypted password storage

**Option 3: Environment variables**
- CI/CD friendly
- Container-ready

## Testability

### Unit Testing

**Services**: Easy to mock dependencies

```csharp
var mockFileSystem = new Mock<IRemoteFileSystem>();
var service = new PackageScannerService(logger, () => mockFileSystem.Object);
```

### Integration Testing

**Approach**: Test với real SMB share (test environment)

**Setup**:
- Local SMB server với test data
- Docker container với Samba

### UI Testing

**Approach**: 
- Extract logic ra services (done)
- UI tests focus on interaction
- Could use FlaUI, WinAppDriver

## Extension Points

### Future Features

1. **Parallel scan**: Scan multiple folders concurrently
2. **Resume copy**: Continue interrupted transfers
3. **Verify checksums**: Validate copied files against `.hash`
4. **Batch operations**: Copy multiple packages
5. **Background service**: Monitor folder và auto-copy
6. **Notification**: Toast notifications khi hoàn thành
7. **Statistics**: Track throughput, success rate
8. **Settings UI**: Configure defaults via UI

### Pluggability

**Storage abstraction**: Easy to add other protocols
- FTP
- SFTP  
- Azure Blob Storage
- AWS S3

**Validation rules**: Easy to customize
- Different naming conventions
- Different file structure
- Custom validators

## Security Considerations

### Current

- Plain text password trong memory
- No encryption in transit (SMB handles this)
- No audit logging

### Improvements

- **SecureString**: Store password securely
- **Windows Credential Manager**: System-level storage
- **Audit trail**: Log all operations với user context
- **Access control**: Role-based permissions

## Deployment

### Current Approach

Self-contained executable:
```bash
dotnet publish -c Release -r win-x64 --self-contained
```

### Distribution Options

1. **XCOPY deployment**: Copy executable và dependencies
2. **MSI installer**: Windows Installer package
3. **ClickOnce**: Auto-update support
4. **Microsoft Store**: Modern deployment

### Prerequisites

- .NET 8 Runtime (hoặc self-contained)
- Windows 10/11
- Network access đến SMB share
- Write permissions to destination folder
