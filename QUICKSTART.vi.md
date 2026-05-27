# Quick Start Guide - Multi-Destination Transfer

## Cài đặt nhanh

### 1. Thêm vào project của bạn

```xml
<!-- File .csproj -->
<ItemGroup>
  <ProjectReference Include="..\SmbEnterprise.Core\SmbEnterprise.Core.csproj" />
  <ProjectReference Include="..\SmbEnterprise.Protocol.SMB\SmbEnterprise.Protocol.SMB.csproj" />
  <ProjectReference Include="..\SmbEnterprise.Transfer\SmbEnterprise.Transfer.csproj" />
</ItemGroup>
```

### 2. Setup DI Container (Program.cs)

```csharp
using Microsoft.Extensions.DependencyInjection;
using SmbEnterprise.Protocol.SMB;
using SmbEnterprise.Transfer;
using SmbEnterprise.Transfer.Abstractions;

var services = new ServiceCollection();
services.AddLogging();
services.AddSmbProvider();
services.AddSingleton<ITransferEngine, TransferEngine>();

var serviceProvider = services.BuildServiceProvider();
```

## Ví dụ 1: Transfer 1 file đến nhiều đích

```csharp
using SmbEnterprise.Core.Models;

var transferEngine = serviceProvider.GetRequiredService<ITransferEngine>();

// Danh sách destinations
var destinations = new[]
{
    @"\server1\backup\file.bin",
    @"\server2\backup\file.bin",
    @"\server3\backup\file.bin"
};

// Configure
var options = new TransferOptions 
{ 
    ChunkSize = 1 * 1024 * 1024,
    Resume = true 
};

var slowPolicy = new SlowConnectionPolicy
{
    EnableSlowConnectionHandling = true,
    Action = SlowConnectionAction.Throttle,
    ThrottleMaxBytesPerSecond = 5 * 1024 * 1024
};

// Progress callback
var progress = new Progress<AggregatedMultiDestinationProgress>(p =>
{
    Console.WriteLine($"Read: {p.SourceBytesRead}/{p.TotalBytes} bytes");
    Console.WriteLine($"Completed: {p.CompletedCount}/{p.DestinationProgresses.Count}");

    foreach (var dest in p.DestinationProgresses)
    {
        Console.WriteLine($"  [{dest.DestinationIndex}] {dest.PercentComplete:F1}% - " +
                         $"{dest.SpeedBytesPerSecond / (1024.0 * 1024.0):F2} MB/s" +
                         (dest.IsThrottled ? " [THROTTLED]" : ""));
    }
});

// Transfer
var result = await transferEngine.TransferMultiDestinationWithIndividualProgressAsync(
    sourcePath: @"\source\large-file.bin",
    destinationPaths: destinations,
    options: options,
    slowPolicy: slowPolicy,
    progress: progress
);

// Check results
if (result.Results.All(r => r.Result.Success))
    Console.WriteLine("✓ All destinations succeeded!");
else
    Console.WriteLine($"✗ Some failed: {result.Results.Count(r => !r.Result.Success)}");
```

## Ví dụ 2: Transfer cả folder đến nhiều đích

```csharp
var destDirs = new[]
{
    @"\server1\backup\documents",
    @"\server2\backup\documents",
    @"\server3\backup\documents"
};

var dirProgress = new Progress<DirectoryTransferProgress>(p =>
{
    Console.WriteLine($"Files: {p.ProcessedFiles}/{p.TotalFiles}");
    Console.WriteLine($"Success: {p.SuccessfulFiles} | Failed: {p.FailedFiles}");
    Console.WriteLine($"Current: {p.CurrentFile}");
});

var result = await transferEngine.TransferDirectoryMultiDestinationAsync(
    sourceDirectory: @"\source\documents",
    destinationDirectories: destDirs,
    options: options,
    slowPolicy: slowPolicy,
    progress: dirProgress
);

Console.WriteLine($"Completed: {result.SuccessfulFiles}/{result.TotalFiles} files");
```

## Các SlowConnectionAction

### 1. LogOnly - Chỉ log, không can thiệp

```csharp
Action = SlowConnectionAction.LogOnly
```

**Khi dùng:** Debug, monitor

### 2. Throttle - Giới hạn tốc độ destination chậm

```csharp
Action = SlowConnectionAction.Throttle,
ThrottleMaxBytesPerSecond = 5 * 1024 * 1024  // 5 MB/s
```

**Khi dùng:** Muốn tất cả destinations hoàn thành nhưng không để destination chậm làm nghẽn reader

### 3. Pause - Tạm dừng destination chậm

```csharp
Action = SlowConnectionAction.Pause
```

**Khi dùng:** Destination chậm có thể recover, muốn đợi nó

### 4. Skip - Bỏ qua destination chậm

```csharp
Action = SlowConnectionAction.Skip,
ContinueOnSlowConnectionFailure = true
```

**Khi dùng:** Ưu tiên destinations nhanh, destination chậm không quan trọng

## Tùy chỉnh ngưỡng slow connection

```csharp
var slowPolicy = new SlowConnectionPolicy
{
    EnableSlowConnectionHandling = true,

    // Coi là slow nếu < 30% tốc độ trung bình
    SlowSpeedThresholdPercent = 30.0,

    // Chỉ đánh giá sau 10 giây (tránh false positive)
    MinimumEvaluationDurationSeconds = 10,

    // Action
    Action = SlowConnectionAction.Throttle,
    ThrottleMaxBytesPerSecond = 5 * 1024 * 1024,

    // Tiếp tục nếu có destination fail
    ContinueOnSlowConnectionFailure = true
};
```

## Tips

### 1. Hiệu năng tối ưu

```csharp
var options = new TransferOptions
{
    ChunkSize = 1 * 1024 * 1024,      // Bắt đầu 1 MB
    MaxChunkSize = 16 * 1024 * 1024,  // Tối đa 16 MB
    MaxParallelWorkers = 1,            // Single stream (khuyến nghị)
    EnableReadAhead = true             // Giảm latency
};
```

### 2. Xử lý slow connection aggressive

```csharp
var slowPolicy = new SlowConnectionPolicy
{
    EnableSlowConnectionHandling = true,
    SlowSpeedThresholdPercent = 40.0,  // Nghiêm ngặt hơn
    MinimumEvaluationDurationSeconds = 5,
    Action = SlowConnectionAction.Skip
};
```

### 3. Monitor chi tiết từng destination

```csharp
var progress = new Progress<AggregatedMultiDestinationProgress>(aggProgress =>
{
    foreach (var dest in aggProgress.DestinationProgresses)
    {
        Console.WriteLine($"[{dest.DestinationIndex}] {dest.DestinationPath}");
        Console.WriteLine($"  Stage: {dest.Stage}");
        Console.WriteLine($"  Progress: {dest.PercentComplete:F1}%");
        Console.WriteLine($"  Speed: {dest.SpeedBytesPerSecond / (1024.0 * 1024.0):F2} MB/s");
        Console.WriteLine($"  ETA: {dest.Eta}");
        Console.WriteLine($"  Throttled: {dest.IsThrottled}");
        Console.WriteLine($"  Retries: {dest.RetryCount}");

        if (!string.IsNullOrEmpty(dest.ErrorMessage))
            Console.WriteLine($"  Error: {dest.ErrorMessage}");
    }
});
```

## Chạy demo

```bash
# Demo đầy đủ
cd samples/SmbEnterprise.MultiDestinationDemo
dotnet run

# Hoặc sample app có sẵn
cd samples/SmbEnterprise.SampleApp
dotnet run --full-test
```

## Troubleshooting

**Q: Transfer chậm hơn mong đợi?**
```csharp
// Thử tăng chunk size
ChunkSize = 4 * 1024 * 1024  // 4 MB
```

**Q: Memory usage cao?**
```csharp
// Giảm chunk size và số destinations
ChunkSize = 512 * 1024,  // 512 KB
// Hoặc transfer từng đích một
```

**Q: Slow connection không được phát hiện?**
```csharp
// Tăng evaluation duration và threshold
MinimumEvaluationDurationSeconds = 15,
SlowSpeedThresholdPercent = 40.0
```

## Xem thêm

- [README.vi.md](README.vi.md) - Tài liệu đầy đủ
- [samples/](samples/) - Các ví dụ code
- [Architecture.md](docs/Architecture.md) - Kiến trúc hệ thống
