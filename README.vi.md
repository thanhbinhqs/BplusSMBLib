# SMB Enterprise Transfer Engine - Thư viện C# cho SMB/CIFS

[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

## Giới thiệu

**SMB Enterprise Transfer Engine** là thư viện C# hiệu năng cao để truyền tải file qua giao thức SMB2/SMB3. Thư viện được thiết kế cho các ứng dụng doanh nghiệp cần tốc độ transfer lớn, độ tin cậy cao và khả năng mở rộng tốt.

### Điểm nổi bật

- ⚡ **Hiệu năng cao**: Đạt 56-60 MB/s trên kết nối 1 Gbps
- 🔄 **Multi-destination**: Copy 1 file nguồn đến nhiều đích cùng lúc
- 📊 **Progress tracking riêng**: Theo dõi tiến trình từng destination độc lập
- 🐌 **Xử lý slow connection**: Tự động phát hiện và xử lý các kết nối chậm
- 🔁 **Resume support**: Tiếp tục transfer từ vị trí bị ngắt
- ✅ **Verify integrity**: Hỗ trợ 4 thuật toán checksum (XxHash64, CRC32, SHA-256, MD5)
- 🎯 **Adaptive chunk sizing**: Tự động điều chỉnh kích thước chunk dựa trên throughput
- 🔌 **Connection pooling**: Tái sử dụng SMB session, tự động reconnect
- 📁 **Directory transfer**: Copy toàn bộ thư mục đệ quy
- 🎨 **Clean architecture**: Dependency Injection, interface-based design

## Mục lục

- [Cấu trúc dự án](#cấu-trúc-dự-án)
- [Yêu cầu hệ thống](#yêu-cầu-hệ-thống)
- [Cài đặt](#cài-đặt)
- [Bắt đầu nhanh](#bắt-đầu-nhanh)
- [Tính năng chính](#tính-năng-chính)
- [Transfer đến nhiều đích](#transfer-đến-nhiều-đích)
- [Xử lý slow connection](#xử-lý-slow-connection)
- [Transfer directory](#transfer-directory)
- [Cấu hình nâng cao](#cấu-hình-nâng-cao)
- [Kiến trúc](#kiến-trúc)
- [Hiệu năng](#hiệu-năng)
- [Ví dụ](#ví-dụ)
- [Troubleshooting](#troubleshooting)

## Cấu trúc dự án

```
SmbEnterprise/
├── src/
│   ├── SmbEnterprise.Core/              # Abstractions, models, result types
│   ├── SmbEnterprise.Protocol.SMB/      # SMB wrapper, session pool, retry logic
│   ├── SmbEnterprise.Transfer/          # Transfer engine, pipeline, adaptive sizer
│   ├── SmbEnterprise.Checksum/          # 4 thuật toán hash, TransferVerifier
│   ├── SmbEnterprise.Diagnostics/       # Telemetry, Dashboard
│   ├── SmbEnterprise.Jobs/              # Job queue (in-memory), JobScheduler
│   ├── SmbEnterprise.Persistence/       # SQLite persistence, EF Core
│   └── SmbEnterprise.Cache/             # Metadata cache, Read-ahead prefetcher
├── samples/
│   ├── SmbEnterprise.SampleApp/         # Console demo app
│   └── SmbEnterprise.WinFormsApp/       # Windows GUI explorer
├── tests/
│   └── SmbEnterprise.Tests/             # Unit tests
└── benchmarks/
    └── SmbEnterprise.Benchmarks/        # Performance benchmarks
```

## Yêu cầu hệ thống

- **.NET 8.0 SDK** trở lên
- **Windows** (hỗ trợ SMBLibrary)
- **Quyền truy cập** vào SMB2/SMB3 share (Windows, Samba, NAS, v.v.)
- **RAM**: Tối thiểu 512 MB (khuyến nghị 1 GB+ cho transfer lớn)
- **Network**: 100 Mbps trở lên cho hiệu năng tối ưu

## Cài đặt

### Option 1: Thêm Project Reference (khuyến nghị cho development)

Trong file `.csproj` của bạn, thêm:

```xml
<ItemGroup>
  <!-- Core libraries -->
  <ProjectReference Include="..\SmbEnterprise.Core\SmbEnterprise.Core.csproj" />
  <ProjectReference Include="..\SmbEnterprise.Protocol.SMB\SmbEnterprise.Protocol.SMB.csproj" />
  <ProjectReference Include="..\SmbEnterprise.Transfer\SmbEnterprise.Transfer.csproj" />

  <!-- Optional: Checksum verification -->
  <ProjectReference Include="..\SmbEnterprise.Checksum\SmbEnterprise.Checksum.csproj" />

  <!-- Optional: Diagnostics & telemetry -->
  <ProjectReference Include="..\SmbEnterprise.Diagnostics\SmbEnterprise.Diagnostics.csproj" />

  <!-- Optional: Job queue & persistence -->
  <ProjectReference Include="..\SmbEnterprise.Jobs\SmbEnterprise.Jobs.csproj" />
  <ProjectReference Include="..\SmbEnterprise.Persistence\SmbEnterprise.Persistence.csproj" />

  <!-- Optional: Metadata caching -->
  <ProjectReference Include="..\SmbEnterprise.Cache\SmbEnterprise.Cache.csproj" />
</ItemGroup>
```

### Option 2: NuGet Package (khi publish)

```bash
dotnet add package SmbEnterprise.Core
dotnet add package SmbEnterprise.Transfer
dotnet add package SmbEnterprise.Protocol.SMB
```

## Bắt đầu nhanh

### 1. Setup Dependency Injection

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using SmbEnterprise.Core.Abstractions;
using SmbEnterprise.Core.Models;
using SmbEnterprise.Checksum;
using SmbEnterprise.Protocol.SMB;
using SmbEnterprise.Transfer;
using SmbEnterprise.Transfer.Abstractions;

// Setup logging
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File("logs/smb-transfer-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

// Configure DI container
var services = new ServiceCollection();
services.AddLogging(b => b.AddSerilog(dispose: true));

// Đăng ký SMB provider
services.AddSmbProvider();

// Đăng ký Transfer Engine
services.AddSingleton<ITransferEngine, TransferEngine>();

// Optional: Checksum engine
services.AddChecksumEngine(ChecksumAlgorithm.XxHash64);

var serviceProvider = services.BuildServiceProvider();
```

### 2. Transfer một file đơn giản

```csharp
// Lấy dependencies từ DI container
var provider = serviceProvider.GetRequiredService<IFileSystemProvider>();
var transferEngine = serviceProvider.GetRequiredService<ITransferEngine>();

// Connect tới SMB share
var credential = new RemoteCredential
{
    ServerName = "192.168.1.100",
    ShareName = "data",
    Domain = "WORKGROUP",
    Username = "user",
    Password = "password"
};

var sourceFs = await provider.CreateAsync(credential);
await sourceFs.ConnectAsync(credential);

var destFs = await provider.CreateAsync(credential);
await destFs.ConnectAsync(credential);

// Configure transfer options
var options = new TransferOptions
{
    ChunkSize = 1 * 1024 * 1024,      // 1 MB initial chunk
    MaxChunkSize = 16 * 1024 * 1024,   // Max 16 MB
    MinChunkSize = 64 * 1024,          // Min 64 KB
    MaxParallelWorkers = 1,            // Số luồng write song song
    Resume = true,                     // Bật resume
    Overwrite = false,                 // Không ghi đè nếu file tồn tại
    EnableReadAhead = true             // Bật read-ahead prefetching
};

// Transfer với progress callback
var progress = new Progress<TransferProgress>(p =>
{
    Console.WriteLine($"Progress: {p.PercentComplete:F1}% - {p.SpeedBytesPerSecond / (1024.0 * 1024.0):F2} MB/s");
});

var result = await transferEngine.TransferAsync(
    sourcePath: @"\source\file.bin",
    destinationPath: @"\destination\file.bin",
    options: options,
    progress: progress,
    cancellationToken: CancellationToken.None
);

if (result.Success)
{
    Console.WriteLine($"✓ Transfer thành công! {result.BytesTransferred:N0} bytes in {result.Duration:g}");
}
else
{
    Console.WriteLine($"✗ Transfer thất bại: {result.ErrorMessage}");
}
```

## Tính năng chính

### 1. Single File Transfer

Transfer một file từ nguồn đến đích với progress tracking và resume support.

```csharp
var result = await transferEngine.TransferAsync(
    sourcePath: @"\source\large-file.iso",
    destinationPath: @"\backup\large-file.iso",
    options: new TransferOptions 
    { 
        Resume = true,
        Overwrite = false 
    },
    progress: new Progress<TransferProgress>(p => 
        Console.WriteLine($"{p.PercentComplete:F1}% | ETA: {p.Eta}")
    )
);
```

### 2. Multi-Destination Transfer (Basic)

Copy một file đến nhiều destinations, nhưng progress chung cho tất cả.

```csharp
var destinations = new[]
{
    @"\backup1\file.bin",
    @"\backup2\file.bin",
    @"\backup3\file.bin"
};

var result = await transferEngine.TransferMultiDestinationAsync(
    sourcePath: @"\source\file.bin",
    destinationPaths: destinations,
    options: options,
    progress: progress
);

Console.WriteLine($"Bytes read: {result.BytesRead:N0}");
foreach (var (dest, transferResult) in result.Results)
{
    Console.WriteLine($"  {dest}: {(transferResult.Success ? "✓" : "✗")}");
}
```

### 3. Directory Transfer

Transfer toàn bộ thư mục (bao gồm subdirectories) từ nguồn đến đích.

```csharp
var result = await transferEngine.TransferDirectoryAsync(
    sourceDirectory: @"\source\documents",
    destinationDirectory: @"\backup\documents",
    options: options,
    progress: progress
);

Console.WriteLine($"Transferred {result.BytesTransferred:N0} bytes in {result.Duration:g}");
```

## Transfer đến nhiều đích

### Transfer file với progress riêng cho từng destination

Đây là tính năng **MỚI** và **nâng cao** - cho phép theo dõi progress riêng biệt của từng destination và xử lý slow connection.

```csharp
using SmbEnterprise.Core.Models;

var destinations = new[]
{
    @"\fast-server\backup\file.bin",      // Server nhanh
    @"\slow-server\backup\file.bin",      // Server chậm
    @"\medium-server\backup\file.bin"     // Server trung bình
};

// Configure slow connection policy
var slowPolicy = new SlowConnectionPolicy
{
    EnableSlowConnectionHandling = true,        // Bật xử lý slow connection
    SlowSpeedThresholdPercent = 30.0,          // Coi là slow nếu < 30% tốc độ trung bình
    MinimumEvaluationDurationSeconds = 10,     // Đánh giá sau 10 giây
    Action = SlowConnectionAction.Throttle,    // Throttle destination chậm
    ThrottleMaxBytesPerSecond = 5 * 1024 * 1024, // Giới hạn 5 MB/s
    ContinueOnSlowConnectionFailure = true     // Tiếp tục nếu có destination fail
};

// Progress callback với thông tin chi tiết từng destination
var progressReporter = new Progress<AggregatedMultiDestinationProgress>(aggProgress =>
{
    Console.Clear();
    Console.WriteLine($"=== Multi-Destination Transfer ===");
    Console.WriteLine($"Source: {aggProgress.SourcePath}");
    Console.WriteLine($"Read: {aggProgress.SourceBytesRead:N0} / {aggProgress.TotalBytes:N0} bytes");
    Console.WriteLine($"Speed: {aggProgress.SourceReadSpeedBytesPerSecond / (1024.0 * 1024.0):F2} MB/s");
    Console.WriteLine($"Elapsed: {aggProgress.Elapsed:g}");
    Console.WriteLine($"Completed: {aggProgress.CompletedCount}/{aggProgress.DestinationProgresses.Count}");
    Console.WriteLine($"Throttled: {aggProgress.ThrottledCount}");
    Console.WriteLine($"Failed: {aggProgress.FailedCount}");
    Console.WriteLine();

    // Progress từng destination
    foreach (var destProgress in aggProgress.DestinationProgresses)
    {
        var status = destProgress.Stage switch
        {
            TransferStage.Completed => "✓",
            TransferStage.Failed => "✗",
            TransferStage.Writing => "→",
            _ => "⋯"
        };

        var throttled = destProgress.IsThrottled ? " [THROTTLED]" : "";

        Console.WriteLine($"{status} Dest[{destProgress.DestinationIndex}]: {destProgress.DestinationPath}");
        Console.WriteLine($"   Progress: {destProgress.PercentComplete:F1}% | " +
                         $"Speed: {destProgress.SpeedBytesPerSecond / (1024.0 * 1024.0):F2} MB/s | " +
                         $"ETA: {destProgress.Eta?.ToString(@"mm\:ss") ?? "N/A"}{throttled}");

        if (!string.IsNullOrEmpty(destProgress.ErrorMessage))
        {
            Console.WriteLine($"   Error: {destProgress.ErrorMessage}");
        }
    }
});

// Transfer với progress tracking riêng
var result = await transferEngine.TransferMultiDestinationWithIndividualProgressAsync(
    sourcePath: @"\source\large-file.bin",
    destinationPaths: destinations,
    options: options,
    slowPolicy: slowPolicy,
    progress: progressReporter
);

// Kết quả
Console.WriteLine($"\n✓ Transfer completed in {result.Duration:g}");
Console.WriteLine($"Total bytes transferred: {result.BytesRead:N0}");

foreach (var (dest, transferResult) in result.Results)
{
    Console.WriteLine($"  {dest}: {(transferResult.Success ? "SUCCESS" : "FAILED")}");
    if (!transferResult.Success)
        Console.WriteLine($"    Error: {transferResult.ErrorMessage}");
}
```

### Output mẫu:

```
=== Multi-Destination Transfer ===
Source: \source\large-file.bin
Read: 524,288,000 / 1,073,741,824 bytes
Speed: 58.32 MB/s
Elapsed: 00:00:09
Completed: 0/3
Throttled: 1
Failed: 0

✓ Dest[0]: \fast-server\backup\file.bin
   Progress: 52.3% | Speed: 62.15 MB/s | ETA: 00:08

→ Dest[1]: \slow-server\backup\file.bin [THROTTLED]
   Progress: 35.7% | Speed: 12.45 MB/s | ETA: 00:52

→ Dest[2]: \medium-server\backup\file.bin
   Progress: 48.1% | Speed: 45.30 MB/s | ETA: 00:11
```

## Xử lý Slow Connection

### Các chiến lược xử lý connection chậm

```csharp
// 1. LogOnly - Chỉ log warning, không làm gì
var logOnlyPolicy = new SlowConnectionPolicy
{
    EnableSlowConnectionHandling = true,
    Action = SlowConnectionAction.LogOnly
};

// 2. Throttle - Giới hạn tốc độ destination chậm
var throttlePolicy = new SlowConnectionPolicy
{
    EnableSlowConnectionHandling = true,
    Action = SlowConnectionAction.Throttle,
    ThrottleMaxBytesPerSecond = 5 * 1024 * 1024,  // 5 MB/s max
    SlowSpeedThresholdPercent = 30.0               // < 30% avg = slow
};

// 3. Pause - Tạm dừng destination chậm
var pausePolicy = new SlowConnectionPolicy
{
    EnableSlowConnectionHandling = true,
    Action = SlowConnectionAction.Pause,
    SlowSpeedThresholdPercent = 20.0
};

// 4. Skip - Bỏ qua destination chậm, đánh dấu failed
var skipPolicy = new SlowConnectionPolicy
{
    EnableSlowConnectionHandling = true,
    Action = SlowConnectionAction.Skip,
    SlowSpeedThresholdPercent = 25.0,
    ContinueOnSlowConnectionFailure = true  // Tiếp tục với các dest khác
};

// 5. Retry - Thử lại connection
var retryPolicy = new SlowConnectionPolicy
{
    EnableSlowConnectionHandling = true,
    Action = SlowConnectionAction.Retry,
    MaxRetries = 3
};
```

### Tùy chỉnh ngưỡng phát hiện slow connection

```csharp
var customPolicy = new SlowConnectionPolicy
{
    // Bật xử lý
    EnableSlowConnectionHandling = true,

    // Coi là slow nếu tốc độ < 40% tốc độ trung bình
    SlowSpeedThresholdPercent = 40.0,

    // Chỉ đánh giá sau 15 giây (tránh false positive do spike)
    MinimumEvaluationDurationSeconds = 15,

    // Action: throttle ở 10 MB/s
    Action = SlowConnectionAction.Throttle,
    ThrottleMaxBytesPerSecond = 10 * 1024 * 1024,

    // Retry 2 lần trước khi từ bỏ
    MaxRetries = 2,

    // Tiếp tục transfer nếu một destination fail
    ContinueOnSlowConnectionFailure = true
};
```

## Transfer Directory đến nhiều đích

Transfer toàn bộ directory (bao gồm subdirectories) đến nhiều destinations với progress tracking chi tiết.

```csharp
var destDirectories = new[]
{
    @"\backup-server-1\data",
    @"\backup-server-2\data",
    @"\backup-server-3\data"
};

var slowPolicy = new SlowConnectionPolicy
{
    EnableSlowConnectionHandling = true,
    Action = SlowConnectionAction.Throttle,
    SlowSpeedThresholdPercent = 30.0
};

// Directory progress callback
var dirProgress = new Progress<DirectoryTransferProgress>(p =>
{
    Console.Clear();
    Console.WriteLine($"=== Directory Multi-Destination Transfer ===");
    Console.WriteLine($"Source: {p.SourceDirectory}");
    Console.WriteLine($"Files: {p.ProcessedFiles}/{p.TotalFiles} | " +
                     $"Success: {p.SuccessfulFiles} | Failed: {p.FailedFiles}");
    Console.WriteLine($"Overall: {p.OverallPercentComplete:F1}% | " +
                     $"{p.TransferredBytes:N0}/{p.TotalBytes:N0} bytes");
    Console.WriteLine($"Elapsed: {p.Elapsed:g}");

    if (!string.IsNullOrEmpty(p.CurrentFile))
    {
        Console.WriteLine($"\nCurrent file: {p.CurrentFile}");

        // Progress của file hiện tại
        if (p.CurrentFileProgress != null)
        {
            var fp = p.CurrentFileProgress;
            Console.WriteLine($"  Read: {fp.SourceBytesRead:N0}/{fp.TotalBytes:N0} bytes");

            foreach (var dest in fp.DestinationProgresses)
            {
                Console.WriteLine($"  [{dest.DestinationIndex}] {dest.PercentComplete:F1}% | " +
                                 $"{dest.SpeedBytesPerSecond / (1024.0 * 1024.0):F2} MB/s");
            }
        }
    }

    // Completed files
    Console.WriteLine($"\nCompleted files: {p.CompletedFiles.Count}");
    foreach (var file in p.CompletedFiles.TakeLast(5))
    {
        var status = file.AllDestinationsSucceeded ? "✓" : "✗";
        Console.WriteLine($"  {status} {Path.GetFileName(file.SourcePath)} " +
                         $"({file.SuccessfulDestinations}/{file.DestinationResults.Count})");
    }
});

var result = await transferEngine.TransferDirectoryMultiDestinationAsync(
    sourceDirectory: @"\source\documents",
    destinationDirectories: destDirectories,
    options: options,
    slowPolicy: slowPolicy,
    progress: dirProgress
);

// Kết quả tổng hợp
Console.WriteLine($"\n=== Transfer Complete ===");
Console.WriteLine($"Total files: {result.TotalFiles}");
Console.WriteLine($"Successful: {result.SuccessfulFiles}");
Console.WriteLine($"Failed: {result.FailedFiles}");
Console.WriteLine($"Total bytes: {result.TotalBytesTransferred:N0}");
Console.WriteLine($"Duration: {result.Duration:g}");
Console.WriteLine($"Destinations: {result.DestinationCount}");

// Chi tiết từng file
foreach (var file in result.FileResults)
{
    Console.WriteLine($"\n{Path.GetFileName(file.SourcePath)}:");
    foreach (var dest in file.DestinationResults)
    {
        var status = dest.Success ? "✓" : "✗";
        var throttled = dest.WasThrottled ? " [THROTTLED]" : "";
        Console.WriteLine($"  {status} [{dest.DestinationIndex}] " +
                         $"{dest.AverageSpeedBytesPerSecond / (1024.0 * 1024.0):F2} MB/s{throttled}");
        if (!dest.Success)
            Console.WriteLine($"     Error: {dest.ErrorMessage}");
    }
}
```

## Cấu hình nâng cao

### TransferOptions

```csharp
var options = new TransferOptions
{
    // Chunk sizing
    ChunkSize = 1 * 1024 * 1024,           // Initial chunk: 1 MB
    MinChunkSize = 64 * 1024,              // Min: 64 KB
    MaxChunkSize = 16 * 1024 * 1024,       // Max: 16 MB

    // Parallel workers
    MaxParallelWorkers = 1,                 // 1 = single stream (khuyến nghị)
    WriteQueueDepth = 4,                    // Độ sâu queue cho write channel

    // Resume & overwrite
    Resume = true,                          // Bật resume từ offset
    Overwrite = false,                      // Không ghi đè nếu file tồn tại

    // Read-ahead
    EnableReadAhead = true,                 // Bật read-ahead prefetching
    ReadAheadChunkCount = 2,               // Pre-load 2 chunks ahead

    // Retry
    MaxRetries = 3,                         // Retry 3 lần khi gặp lỗi
    MaxChunkRetries = 5,                    // Retry từng chunk 5 lần
    RetryDelayMs = 500,                     // Delay 500ms giữa các retry

    // Checksum
    EnableChecksum = true,                  // Bật verify checksum
    ChecksumAlgorithm = ChecksumAlgorithm.XxHash64  // XxHash64 (nhanh nhất)
};
```

### Checksum Algorithms

```csharp
// XxHash64 - Nhanh nhất, khuyến nghị cho production
services.AddChecksumEngine(ChecksumAlgorithm.XxHash64);

// CRC32 - Nhanh, phát hiện lỗi cơ bản
services.AddChecksumEngine(ChecksumAlgorithm.CRC32);

// SHA-256 - Chậm hơn nhưng an toàn hơn
services.AddChecksumEngine(ChecksumAlgorithm.SHA256);

// MD5 - Legacy, không khuyến nghị
services.AddChecksumEngine(ChecksumAlgorithm.MD5);
```

### Verify sau khi transfer

```csharp
var verifier = serviceProvider.GetRequiredService<TransferVerifier>();

var verifyResult = await verifier.VerifyTransferAsync(
    sourcePath: @"\source\file.bin",
    destinationPath: @"\backup\file.bin",
    algorithm: ChecksumAlgorithm.XxHash64,
    sourceFs: sourceFs,
    destFs: destFs
);

if (verifyResult.Success && verifyResult.IsMatch)
{
    Console.WriteLine("✓ Checksum matched!");
}
else
{
    Console.WriteLine($"✗ Checksum mismatch! Source: {verifyResult.SourceHash}, Dest: {verifyResult.DestHash}");
}
```

## Kiến trúc

### Layered Architecture

```
┌──────────────────────────────────────────────────────┐
│              Application Layer                        │
│  (Console App, WinForms, Web API, Background Service) │
└────────────────────┬─────────────────────────────────┘
                     │ ITransferEngine
┌────────────────────▼─────────────────────────────────┐
│           SmbEnterprise.Transfer                      │
│  TransferEngine → TransferPipeline                    │
│  MultiDestinationTransferPipeline                     │
│  AdaptiveChunkSizer  ReadAheadPrefetcher              │
└────────────────────┬─────────────────────────────────┘
                     │ IRemoteFileSystem
┌────────────────────▼─────────────────────────────────┐
│          SmbEnterprise.Protocol.SMB                   │
│  SmbFileSystemProvider → SmbFileSystem                │
│  SmbSessionPool + SmbRetryEngine                      │
│  SMBLibrary wrapper (isolated)                        │
└────────────────────┬─────────────────────────────────┘
                     │
        ┌────────────┼────────────┐
        │            │            │
┌───────▼──────┐ ┌──▼────────┐ ┌─▼──────────────┐
│  Checksum    │ │Diagnostics│ │  Cache         │
│  XxHash64    │ │ Telemetry │ │ MetadataCache  │
│  CRC32       │ │ Dashboard │ │ ReadAhead      │
│  SHA-256     │ └───────────┘ └────────────────┘
│  MD5         │
└──────────────┘
        │
┌───────▼─────────────────────────────┐
│      Jobs & Persistence              │
│  InMemoryJobQueue                    │
│  SqliteJobRepository (EF Core)       │
└──────────────────────────────────────┘
```

### Multi-Destination Pipeline Architecture

```
                         Source File
                             │
                             ▼
                    ┌────────────────┐
                    │  Read Worker   │
                    │  (Single)      │
                    └────────┬───────┘
                             │
                 ┌───────────┼───────────┐
                 │           │           │
            ┌────▼───┐  ┌────▼───┐  ┌────▼───┐
            │Channel1│  │Channel2│  │Channel3│
            │(Dest 1)│  │(Dest 2)│  │(Dest 3)│
            └────┬───┘  └────┬───┘  └────┬───┘
                 │           │           │
            ┌────▼───┐  ┌────▼───┐  ┌────▼───┐
            │Writer 1│  │Writer 2│  │Writer 3│
            │+Progress│  │+Progress│ │+Progress│
            └────┬───┘  └────┬───┘  └────┬───┘
                 │           │           │
                 │    ┌──────▼──────┐    │
                 │    │SlowMonitor  │    │
                 │    │(Background) │    │
                 │    └─────────────┘    │
                 │                       │
            ┌────▼───┐  ┌────────┐  ┌────▼───┐
            │ Dest 1 │  │ Dest 2 │  │ Dest 3 │
            │(Fast)  │  │(Slow   │  │(Normal)│
            │        │  │Throttled) │        │
            └────────┘  └────────┘  └────────┘
```

## Hiệu năng

### Benchmark Results

**Test Environment:**
- **Network**: 1 Gbps LAN
- **SMB Server**: Samba NAS (192.168.1.250)
- **File Size**: 3.5 GB
- **OS**: Windows 11

**Results:**

| Configuration | Throughput | Notes |
|---------------|------------|-------|
| MaxParallelWorkers=1, ChunkSize=256KB | 56-60 MB/s | Khuyến nghị - single stream |
| MaxParallelWorkers=4, ChunkSize=1MB | 45-52 MB/s | Multi-stream (credit limit) |
| + ReadAhead=true | +5-8% | Giảm latency |
| + XxHash64 verify | -2% | Overhead nhỏ |
| + SHA-256 verify | -15% | Overhead lớn |

### Performance Tips

1. **Single stream thường nhanh hơn multi-stream** trên SMB2/3
   ```csharp
   MaxParallelWorkers = 1
   ```

2. **Để AdaptiveChunkSizer tự điều chỉnh**
   - Bắt đầu với 256 KB hoặc 1 MB
   - Sẽ tăng lên 16 MB trong vài giây đầu

3. **Sử dụng XxHash64 cho verify**
   - Nhanh gấp 6× so với SHA-256
   - Đủ tin cậy cho integrity checking

4. **Bật ReadAhead để giảm latency**
   ```csharp
   EnableReadAhead = true,
   ReadAheadChunkCount = 2
   ```

5. **Cache metadata cho thư mục lớn**
   ```csharp
   services.AddSingleton<IMetadataCache, MetadataCache>();
   ```

## Ví dụ

### Example 1: Console Progress Bar

```csharp
using Spectre.Console;

var progress = new Progress<TransferProgress>(p =>
{
    AnsiConsole.Progress()
        .Start(ctx =>
        {
            var task = ctx.AddTask($"[green]Transferring {Path.GetFileName(p.SourcePath)}[/]");
            task.MaxValue = p.TotalBytes;
            task.Value = p.TransferredBytes;
            task.Description = $"[green]{p.SpeedBytesPerSecond / (1024.0 * 1024.0):F2} MB/s[/] | ETA: {p.Eta}";
        });
});
```

### Example 2: WinForms ProgressBar

```csharp
var progress = new Progress<AggregatedMultiDestinationProgress>(aggProgress =>
{
    this.Invoke(() =>
    {
        progressBar1.Maximum = (int)(aggProgress.TotalBytes / 1024);
        progressBar1.Value = (int)(aggProgress.SourceBytesRead / 1024);

        labelSpeed.Text = $"{aggProgress.SourceReadSpeedBytesPerSecond / (1024.0 * 1024.0):F2} MB/s";
        labelCompleted.Text = $"{aggProgress.CompletedCount}/{aggProgress.DestinationProgresses.Count}";

        // Update ListVew cho từng destination
        foreach (var dest in aggProgress.DestinationProgresses)
        {
            var item = listViewDests.Items[dest.DestinationIndex];
            item.SubItems[1].Text = $"{dest.PercentComplete:F1}%";
            item.SubItems[2].Text = $"{dest.SpeedBytesPerSecond / (1024.0 * 1024.0):F2} MB/s";
            item.SubItems[3].Text = dest.Stage.ToString();

            if (dest.IsThrottled)
                item.BackColor = Color.Yellow;
        }
    });
});
```

### Example 3: Background Transfer với Job Queue

```csharp
using SmbEnterprise.Jobs;
using SmbEnterprise.Persistence;

// Setup
services.AddSingleton<IJobQueue, InMemoryJobQueue>();
services.AddSqlitePersistence("smb_jobs.db");
services.AddSingleton<JobScheduler>();

// Queue transfer job
var jobQueue = serviceProvider.GetRequiredService<IJobQueue>();
var job = new TransferJob
{
    Id = Guid.NewGuid(),
    SourcePath = @"\source\file.bin",
    DestinationPaths = new[] { @"\backup1\file.bin", @"\backup2\file.bin" },
    Priority = JobPriority.High,
    State = JobState.Pending
};

await jobQueue.EnqueueAsync(job);

// Start scheduler
var scheduler = serviceProvider.GetRequiredService<JobScheduler>();
await scheduler.StartAsync(CancellationToken.None);
```

## Troubleshooting

### 1. "Access Denied" khi connect

**Nguyên nhân**: Credential không đúng hoặc không có quyền truy cập share.

**Giải pháp**:
```csharp
// Kiểm tra credential
var credential = new RemoteCredential
{
    ServerName = "192.168.1.100",
    ShareName = "data",
    Domain = "WORKGROUP",      // hoặc "DOMAIN" nếu domain-joined
    Username = "username",
    Password = "password"
};

// Test connect
try
{
    await sourceFs.ConnectAsync(credential);
    Console.WriteLine("✓ Connected successfully");
}
catch (Exception ex)
{
    Console.WriteLine($"✗ Connection failed: {ex.Message}");
}
```

### 2. Transfer chậm hơn mong đợi

**Nguyên nhân**: Chunk size không tối ưu hoặc network congestion.

**Giải pháp**:
```csharp
// Thử tăng initial chunk size
var options = new TransferOptions
{
    ChunkSize = 4 * 1024 * 1024,      // 4 MB thay vì 1 MB
    MaxChunkSize = 16 * 1024 * 1024,
    EnableReadAhead = true
};

// Kiểm tra network bandwidth
// Dùng iperf hoặc công cụ tương tự để test network throughput
```

### 3. "Channel full" errors

**Nguyên nhân**: Writer chậm hơn reader, queue đầy.

**Giải pháp**:
```csharp
// Tăng write queue depth
var options = new TransferOptions
{
    WriteQueueDepth = 8,  // Default là 4
    MaxParallelWorkers = 1
};
```

### 4. Memory usage cao

**Nguyên nhân**: Chunk size quá lớn hoặc quá nhiều destinations.

**Giải pháp**:
```csharp
// Giảm chunk size
var options = new TransferOptions
{
    ChunkSize = 512 * 1024,        // 512 KB
    MaxChunkSize = 4 * 1024 * 1024  // 4 MB
};

// Hoặc transfer từng đích một thay vì tất cả cùng lúc
foreach (var dest in destinations)
{
    await transferEngine.TransferAsync(source, dest, options);
}
```

### 5. Slow connection không được phát hiện

**Nguyên nhân**: Evaluation duration quá ngắn hoặc threshold không phù hợp.

**Giải pháp**:
```csharp
var slowPolicy = new SlowConnectionPolicy
{
    EnableSlowConnectionHandling = true,
    MinimumEvaluationDurationSeconds = 15,  // Tăng lên 15s
    SlowSpeedThresholdPercent = 40.0,        // Tăng threshold lên 40%
    Action = SlowConnectionAction.Throttle
};
```

## Sample Applications

### Console App

Xem `samples/SmbEnterprise.SampleApp/Program.cs` để có ví dụ đầy đủ về:
- Basic file transfer
- Multi-destination transfer
- Directory transfer
- Checksum verification
- Job queue & persistence

Chạy:
```bash
cd samples/SmbEnterprise.SampleApp
dotnet run --full-test
```

### WinForms App

Xem `samples/SmbEnterprise.WinFormsApp/` để có GUI đầy đủ tính năng:
- File explorer với lazy-loading tree
- Transfer manager với queue
- Progress tracking cho từng transfer
- Pause/Resume/Retry/Cancel
- Real-time logging
- Light/Dark theme

Chạy:
```bash
cd samples/SmbEnterprise.WinFormsApp
dotnet run
```

## Contributing

Contributions are welcome! Please:
1. Fork repository
2. Tạo feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit changes (`git commit -m 'Add AmazingFeature'`)
4. Push to branch (`git push origin feature/AmazingFeature`)
5. Open Pull Request

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- [SMBLibrary](https://github.com/TalAloni/SMBLibrary) - SMB2/SMB3 implementation
- [Serilog](https://serilog.net/) - Structured logging
- [XxHash](https://github.com/uranium62/xxHash) - Fast hashing algorithm

## Contact

- **Author**: [Your Name]
- **Email**: your.email@example.com
- **GitHub**: https://github.com/thanhbinhqs/BplusSMBLib

---

**Made with ❤️ for high-performance SMB file transfers**
