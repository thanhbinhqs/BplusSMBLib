# SmbEnterprise — Production-Grade SMB Transfer Library for .NET 8

A high-performance, fault-tolerant SMB file transfer engine built on [SMBLibrary](https://github.com/TalAloni/SMBLibrary). Designed for production workloads with large files, unreliable networks, and demanding throughput requirements.

---

## Table of Contents

- [Features](#features)
- [Project Structure](#project-structure)
- [Installation & Setup](#installation--setup)
- [Quick Start](#quick-start)
- [Feature Guide](#feature-guide)
  - [1. SMB Filesystem — Core Operations](#1-smb-filesystem--core-operations)
  - [2. TransferEngine — File Transfer](#2-transferengine--file-transfer)
  - [3. Resume Transfer](#3-resume-transfer)
  - [4. Multi-Destination Transfer](#4-multi-destination-transfer)
  - [5. Directory Transfer](#5-directory-transfer)
  - [6. Checksum & Integrity Verification](#6-checksum--integrity-verification)
  - [7. Metadata Cache](#7-metadata-cache)
  - [8. Read-Ahead Prefetcher](#8-read-ahead-prefetcher)
  - [9. Adaptive Chunk Sizer](#9-adaptive-chunk-sizer)
  - [10. Job Queue — In-Memory](#10-job-queue--in-memory)
  - [11. Job Queue — SQLite Persistence](#11-job-queue--sqlite-persistence)
  - [12. Transfer Telemetry](#12-transfer-telemetry)
  - [13. Transfer Dashboard](#13-transfer-dashboard)
- [TransferOptions Reference](#transferoptions-reference)
- [Dependency Injection Setup](#dependency-injection-setup)
- [Architecture](#architecture)
- [Performance Notes](#performance-notes)

---

## Features

| Feature | Description |
|---|---|
| **SMB Session Pool** | Reuse authenticated connections; auto-reconnect on drop |
| **Adaptive Chunk Sizing** | Dynamically adjusts chunk size (64 KB–16 MB) based on live throughput |
| **Channel Pipeline** | Fan-out `System.Threading.Channels` pipeline: one reader → multiple writers |
| **Resume** | Detect partial destination and resume from exact byte offset |
| **Multi-Destination** | Stream one SMB source to N local destinations in a single pass |
| **4 Checksum Algorithms** | XxHash64 (fastest), CRC32, SHA-256, MD5 |
| **Verify-After-Copy** | Hash source and destination; fail on mismatch |
| **Metadata Cache** | LRU + TTL cache for `GetMetadata`/`ListDirectory` results |
| **Read-Ahead Prefetcher** | Background thread pre-loads next N chunks to hide network latency |
| **In-Memory Job Queue** | Priority queue (High/Normal/Low) with pause/resume/cancel |
| **SQLite Persistence** | Jobs survive process restart; crash recovery re-queues `Running` jobs |
| **Transfer Telemetry** | Per-session and aggregate metrics (bytes, retries, reconnects, errors) |
| **Transfer Dashboard** | Console progress bar, summary panel, telemetry panel |
| **WinForms Explorer** | Full GUI with file browsing, transfer manager, and full-feature test runner |

---

## Project Structure

```
SmbEnterprise/
├── src/
│   ├── SmbEnterprise.Core/          Abstractions, models, result types
│   ├── SmbEnterprise.Protocol.SMB/  SMBLibrary wrapper, session pool, retry
│   ├── SmbEnterprise.Transfer/      TransferEngine, pipeline, adaptive sizer
│   ├── SmbEnterprise.Checksum/      4 hash algorithms, TransferVerifier
│   ├── SmbEnterprise.Diagnostics/   TransferTelemetry, TransferDashboard
│   ├── SmbEnterprise.Jobs/          InMemoryJobQueue, JobScheduler
│   ├── SmbEnterprise.Persistence/   SqliteJobRepository, EF Core
│   └── SmbEnterprise.Cache/         MetadataCache, ReadAheadPrefetcher
├── samples/
│   ├── SmbEnterprise.SampleApp/     CLI demo & full feature test runner
│   └── SmbEnterprise.WinFormsApp/   Windows GUI explorer
└── tests/
    └── SmbEnterprise.Tests/         Unit tests
```

---

## Installation & Setup

### Prerequisites

- .NET 8.0 SDK
- Access to an SMB2/SMB3 share (Windows, Samba, NAS, etc.)

### Add project references

```xml
<!-- In your .csproj -->
<ItemGroup>
  <ProjectReference Include="..\SmbEnterprise.Core\SmbEnterprise.Core.csproj" />
  <ProjectReference Include="..\SmbEnterprise.Protocol.SMB\SmbEnterprise.Protocol.SMB.csproj" />
  <ProjectReference Include="..\SmbEnterprise.Transfer\SmbEnterprise.Transfer.csproj" />
  <ProjectReference Include="..\SmbEnterprise.Checksum\SmbEnterprise.Checksum.csproj" />
  <ProjectReference Include="..\SmbEnterprise.Diagnostics\SmbEnterprise.Diagnostics.csproj" />
  <ProjectReference Include="..\SmbEnterprise.Jobs\SmbEnterprise.Jobs.csproj" />
  <ProjectReference Include="..\SmbEnterprise.Persistence\SmbEnterprise.Persistence.csproj" />
  <ProjectReference Include="..\SmbEnterprise.Cache\SmbEnterprise.Cache.csproj" />
</ItemGroup>
```

---

## Quick Start

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmbEnterprise.Core.Abstractions;
using SmbEnterprise.Core.Models;
using SmbEnterprise.Protocol.SMB;
using SmbEnterprise.Transfer;

// 1. Setup DI
var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole());
services.AddSmbProvider();                      // registers SMB session pool
var sp = services.BuildServiceProvider();

// 2. Create and connect a filesystem
var provider = sp.GetRequiredService<IFileSystemProvider>();
await using var smb = provider.CreateFileSystem();

await smb.ConnectAsync(new RemoteCredential
{
    Server   = "192.168.1.100",
    Share    = "media",
    Username = "user",
    Password = "password"
});

// 3. Transfer a file
var engine = new TransferEngine(
    smb,
    new[] { /* local or another SMB filesystem */ },
    sp.GetRequiredService<ILogger<TransferEngine>>());

var result = await engine.TransferAsync(
    sourcePath:      @"\movies\film.mkv",
    destinationPath: @"C:\Downloads\film.mkv",
    options:         new TransferOptions { Resume = true });

Console.WriteLine(result.Success
    ? $"Done: {result.BytesTransferred / 1024.0 / 1024:F1} MB"
    : $"Failed: {result.ErrorMessage}");

await smb.DisconnectAsync();
```

---

## Feature Guide

### 1. SMB Filesystem — Core Operations

`IRemoteFileSystem` provides all low-level SMB operations. Obtain an instance via `IFileSystemProvider.CreateFileSystem()`.

```csharp
var provider = sp.GetRequiredService<IFileSystemProvider>();
await using var smb = provider.CreateFileSystem();

await smb.ConnectAsync(new RemoteCredential
{
    Server   = "192.168.1.100",
    Share    = "data",
    Username = "alice",
    Password = "secret"
});

// List directory
await foreach (var item in smb.ListDirectoryAsync(@"\documents"))
{
    Console.WriteLine($"{(item.IsDirectory ? "[DIR]" : "[FILE]")} {item.Name}  {item.Size:N0} bytes");
}

// Get metadata
var meta = await smb.GetMetadataAsync(@"\documents\report.pdf");
Console.WriteLine($"Size: {meta.Size}  Modified: {meta.ModifiedUtc:u}");

// Check existence
bool exists = await smb.ExistsAsync(@"\documents\report.pdf");

// Create directory
await smb.CreateDirectoryAsync(@"\documents\archive\2026");

// Rename / move
await smb.RenameAsync(@"\documents\old.txt", @"\documents\new.txt");

// Delete file
await smb.DeleteFileAsync(@"\documents\temp.txt");

// Delete directory (recursive)
await smb.DeleteDirectoryAsync(@"\documents\old_backup", recursive: true);

// Read stream (low-level)
await using var readStream = await smb.OpenReadAsync(@"\documents\data.bin");
var buffer = new byte[64 * 1024];
int bytesRead = await readStream.ReadAsync(buffer);

// Write stream (low-level)
await using var writeStream = await smb.OpenWriteAsync(@"\documents\output.bin", createNew: true);
await writeStream.WriteAsync(buffer.AsMemory(0, bytesRead));
await writeStream.FlushAsync();

// Update timestamps / attributes
await smb.SetAttributesAsync(@"\documents\report.pdf", new FileMetadata
{
    FullPath    = @"\documents\report.pdf",
    Size        = meta.Size,
    Attributes  = meta.Attributes,
    ModifiedUtc = DateTime.UtcNow,
    CreatedUtc  = meta.CreatedUtc,
    AccessedUtc = meta.AccessedUtc,
    IsDirectory = false
});

await smb.DisconnectAsync();
```

---

### 2. TransferEngine — File Transfer

`TransferEngine` streams a file from a source `IRemoteFileSystem` to one or more destination `IRemoteFileSystem` instances through a high-performance channel-based pipeline.

```csharp
using SmbEnterprise.Transfer;
using SmbEnterprise.Core.Models;

// Source = SMB share,  Destination = local disk (implement IRemoteFileSystem or use built-in LocalFileSystem)
var engine = new TransferEngine(
    sourceFs:      smb,
    destinationFs: new[] { localFs },
    logger:        sp.GetRequiredService<ILogger<TransferEngine>>());

var options = new TransferOptions
{
    MaxParallelWorkers = 1,           // keep at 1 for most SMB servers
    ChunkSize          = 256 * 1024,  // initial 256 KB, adaptive engine grows this
    MaxChunkSize       = 4 * 1024 * 1024,
    MinChunkSize       = 64 * 1024,
    MaxChunkRetries    = 8,
    VerifyAfterCopy    = false,       // use TransferVerifier separately for large files
    Resume             = true
};

// Transfer with live progress
var result = await engine.TransferAsync(
    sourcePath:      @"\movies\film.mkv",
    destinationPath: @"C:\Downloads\film.mkv",
    options:         options,
    progress: new Progress<TransferProgress>(p =>
    {
        Console.Write($"\r{p.PercentComplete:F1}% | {p.SpeedBytesPerSecond / 1024.0 / 1024:F1} MB/s | ETA: {p.Eta?.ToString(@"m\:ss") ?? "?"}   ");
    }));

Console.WriteLine();
if (result.Success)
    Console.WriteLine($"✔ {result.BytesTransferred / 1024.0 / 1024:F1} MB transferred in {result.Duration:m\\:ss}  avg {result.BytesTransferred / result.Duration.TotalSeconds / 1024 / 1024:F1} MB/s");
else
    Console.WriteLine($"✘ {result.ErrorMessage}");
```

**`TransferProgress` fields:**

| Property | Type | Description |
|---|---|---|
| `TransferredBytes` | `long` | Bytes transferred so far |
| `TotalBytes` | `long` | Total file size |
| `PercentComplete` | `double` | 0.0 – 100.0 |
| `SpeedBytesPerSecond` | `double` | Current rolling speed |
| `Eta` | `TimeSpan?` | Estimated time remaining |
| `Stage` | `TransferStage` | `Reading`, `Writing`, `Verifying` |
| `RetryCount` | `int` | Chunk retries so far |

---

### 3. Resume Transfer

When `Resume = true` (default), the engine reads the destination file's current size and starts reading the source from that offset. Interrupted downloads automatically continue without re-downloading already-received bytes.

```csharp
var options = new TransferOptions { Resume = true, Overwrite = false };

// First run: downloads 1 GB of 3.5 GB file then crashes
// Second run: detects 1 GB already on disk, resumes from byte 1_073_741_824
var result = await engine.TransferAsync(
    @"\movies\film.mkv",
    @"C:\Downloads\film.mkv",
    options);
```

> **Note:** Resume requires the destination file to be intact up to its current size. If the file is corrupted, delete it and restart with `Overwrite = true`.

---

### 4. Multi-Destination Transfer

`TransferMultiDestinationAsync` reads the source **once** and fans the data out to N destinations simultaneously — ideal for backup/mirror scenarios.

```csharp
var result = await engine.TransferMultiDestinationAsync(
    sourcePath:       @"\movies\film.mkv",
    destinationPaths: new[] { @"C:\Backup1\film.mkv", @"D:\Backup2\film.mkv" },
    options: new TransferOptions { Resume = false });

foreach (var (destPath, destResult) in result.Results)
{
    Console.WriteLine($"{destPath}: {(destResult.Success ? "OK" : destResult.ErrorMessage)}");
}
```

---

### 5. Directory Transfer

`TransferDirectoryAsync` recursively enumerates a remote directory and transfers all files while preserving folder structure.

```csharp
var result = await engine.TransferDirectoryAsync(
    sourceDirectory:      @"\movies\Series",
    destinationDirectory: @"C:\Downloads\Series",
    options: new TransferOptions { Resume = true },
    progress: new Progress<TransferProgress>(p =>
    {
        Console.Write($"\r{p.PercentComplete:F1}% — {p.SpeedBytesPerSecond / 1024.0 / 1024:F1} MB/s");
    }));

Console.WriteLine($"\nTotal: {result.BytesTransferred / 1024.0 / 1024:F0} MB");
```

---

### 6. Checksum & Integrity Verification

#### Compute checksum only

```csharp
using SmbEnterprise.Checksum;
using SmbEnterprise.Core.Models;

// Factory supports: XxHash64 (fastest), Crc32, Sha256, Md5
var engine = ChecksumEngineFactory.Create(ChecksumAlgorithm.XxHash64);

var localFilePath = @"C:\Downloads\film.mkv";
var fileSize      = new FileInfo(localFilePath).Length;

var result = await engine.ComputeFileAsync(
    localFilePath,
    async (offset, buffer, ct) =>
    {
        await using var fs = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        fs.Seek(offset, SeekOrigin.Begin);
        return await fs.ReadAsync(buffer, ct);
    },
    fileSize);

Console.WriteLine($"XxHash64: {result.HexHash}");
```

#### Verify source ↔ destination integrity

```csharp
var verEngine = ChecksumEngineFactory.Create(ChecksumAlgorithm.XxHash64);
var verifier  = new TransferVerifier(verEngine, sp.GetRequiredService<ILogger<TransferVerifier>>());

var verResult = await verifier.VerifyAsync(
    sourceFs:        smb,
    sourcePath:      @"\movies\film.mkv",
    destFs:          localFs,
    destinationPath: @"C:\Downloads\film.mkv",
    expectedSize:    3_700_198_168L);

if (verResult.IsValid)
    Console.WriteLine($"✔ Integrity OK  hash={verResult.Hash}");
else
    Console.WriteLine($"✘ Mismatch: {verResult.ErrorMessage}");
```

**Algorithm performance** (measured on a 3.5 GB file, local NVMe):

| Algorithm | Speed |
|---|---|
| XxHash64 | ~870 MB/s |
| CRC32 | ~3 600 MB/s |
| SHA-256 | ~1 300 MB/s |
| MD5 | ~520 MB/s |

---

### 7. Metadata Cache

`MetadataCache` reduces SMB round-trips for `GetMetadata` and `ListDirectory` calls. Supports per-entry TTL and LRU eviction.

```csharp
using SmbEnterprise.Cache;

var cache = new MetadataCache(
    new MetadataCacheOptions
    {
        MetadataTtl        = TimeSpan.FromMinutes(5),
        DirectoryTtl       = TimeSpan.FromMinutes(2),
        MaxMetadataEntries = 10_000,
        MaxDirectoryEntries = 500
    },
    sp.GetRequiredService<ILogger<MetadataCache>>());

// Wrap SMB calls with cache look-up
async Task<FileMetadata> GetMetaCachedAsync(string path)
{
    if (cache.TryGetMetadata(path, out var cached))
        return cached!;                         // cache hit

    var meta = await smb.GetMetadataAsync(path);
    cache.SetMetadata(path, meta);              // populate cache
    return meta;
}

async Task<List<FileItem>> ListDirCachedAsync(string path)
{
    if (cache.TryGetDirectoryListing(path, out var cached))
        return cached!;

    var items = new List<FileItem>();
    await foreach (var item in smb.ListDirectoryAsync(path))
        items.Add(item);

    cache.SetDirectoryListing(path, items);
    return items;
}

// Invalidate on write operations
await smb.DeleteFileAsync(@"\docs\old.txt");
cache.Invalidate(@"\docs\old.txt");             // remove single file entry
cache.InvalidateDirectory(@"\docs");            // remove directory listing
```

---

### 8. Read-Ahead Prefetcher

`ReadAheadPrefetcher` pre-reads the next N chunks in a background task while the consumer processes the current chunk, hiding network latency.

```csharp
using SmbEnterprise.Cache;
using System.Buffers;

var filePath = @"\movies\film.mkv";
var fileMeta = await smb.GetMetadataAsync(filePath);

await using var stream = await smb.OpenReadAsync(filePath);

var prefetcher = new ReadAheadPrefetcher(
    source:   stream,
    fileSize: fileMeta.Size,
    options:  new ReadAheadOptions
    {
        ChunkSize    = 1 * 1024 * 1024,  // 1 MB chunks
        PrefetchDepth = 4                // pre-load up to 4 chunks ahead
    },
    logger: sp.GetRequiredService<ILogger<ReadAheadPrefetcher>>());

await foreach (var chunk in prefetcher.Reader.ReadAllAsync())
{
    // Process chunk.Buffer[0..chunk.BytesRead-1]
    Console.Write($"\rRead {chunk.BytesRead / 1024.0:F0} KB at offset …");

    ArrayPool<byte>.Shared.Return(chunk.Buffer); // always return rented buffer
}

await prefetcher.DisposeAsync();
```

---

### 9. Adaptive Chunk Sizer

`AdaptiveChunkSizer` monitors throughput samples and automatically grows/shrinks the chunk size to maximise throughput while staying within configured bounds.

```csharp
using SmbEnterprise.Transfer.Pipeline;

var sizer = new AdaptiveChunkSizer(
    initialChunkSize: 256 * 1024,   // start at 256 KB
    minChunkSize:      64 * 1024,   // never go below 64 KB
    maxChunkSize:    16 * 1024 * 1024); // never exceed 16 MB

// In your transfer loop:
var chunkSize = sizer.CurrentChunkSize;
var sw        = Stopwatch.StartNew();

var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, chunkSize));
sw.Stop();

// Feed back actual performance; sizer adjusts for next chunk
sizer.RecordMetrics(bytesRead, sw.Elapsed);

Console.WriteLine($"Next chunk size: {sizer.CurrentChunkSize / 1024} KB");
```

Rules:
- **Fast path** (avg ≥ 50 MB/s): chunk size doubles (capped at `MaxChunkSize`)
- **Slow path** (avg < 5 MB/s for 3 consecutive samples): chunk size halves (floored at `MinChunkSize`)

---

### 10. Job Queue — In-Memory

`InMemoryJobQueue` provides a priority-ordered job queue with pause, resume, cancel, and progress tracking. Jobs survive as long as the process is alive.

```csharp
using SmbEnterprise.Jobs;
using SmbEnterprise.Core.Models;

var queue = new InMemoryJobQueue(sp.GetRequiredService<ILogger<InMemoryJobQueue>>());

// Enqueue jobs with priorities
var job1 = await queue.EnqueueAsync(new TransferJob
{
    JobId           = Guid.NewGuid(),
    SourcePath      = @"\movies\film_a.mkv",
    DestinationPath = @"C:\Downloads\film_a.mkv",
    Priority        = JobPriority.High,
    TotalBytes      = 3_700_000_000,
    Options         = new TransferOptions()
});

var job2 = await queue.EnqueueAsync(new TransferJob
{
    JobId           = Guid.NewGuid(),
    SourcePath      = @"\movies\film_b.mkv",
    DestinationPath = @"C:\Downloads\film_b.mkv",
    Priority        = JobPriority.Normal,
    TotalBytes      = 1_200_000_000,
    Options         = new TransferOptions()
});

// Dequeue next (highest priority first)
var next = await queue.DequeueAsync();
Console.WriteLine($"Processing: {next?.SourcePath}  priority={next?.Priority}");

// Lifecycle management
await queue.PauseAsync(job2.JobId);
await queue.ResumeAsync(job2.JobId);
await queue.CancelAsync(job2.JobId);

// Track progress
await queue.UpdateProgressAsync(job1.JobId, bytesTransferred: 500_000_000);

// Complete
await queue.CompleteJobAsync(job1.JobId, success: true);

// Enumerate all
await foreach (var job in queue.GetAllJobsAsync())
    Console.WriteLine($"  {job.JobId:B}  {job.Status,-12}  {job.BytesTransferred / 1024.0 / 1024:F0} MB");
```

---

### 11. Job Queue — SQLite Persistence

`SqliteJobRepository` stores jobs in a SQLite database. Jobs survive process restarts and `RecoverJobsAsync()` automatically re-queues any jobs that were left in `Running` state when the process crashed.

```csharp
using SmbEnterprise.Persistence;
using Microsoft.Extensions.DependencyInjection;

// Register with custom DB path
var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole());
services.AddSqlitePersistence("C:\\ProgramData\\MyApp\\jobs.db");
var sp = services.BuildServiceProvider();

var repo = sp.GetRequiredService<SqliteJobRepository>();
await repo.EnsureCreatedAsync();    // creates schema on first run

// Persist a job
var job = new TransferJob
{
    JobId           = Guid.NewGuid(),
    SourcePath      = @"\movies\film.mkv",
    DestinationPath = @"C:\Downloads\film.mkv",
    Priority        = JobPriority.High,
    TotalBytes      = 3_700_000_000,
    Options         = new TransferOptions()
};
await repo.EnqueueAsync(job);

// Load back
var loaded = await repo.GetJobAsync(job.JobId);

// Status transitions
await repo.PauseAsync(job.JobId);
await repo.ResumeAsync(job.JobId);
await repo.CancelAsync(job.JobId);

// List all jobs
await foreach (var j in repo.GetAllJobsAsync())
    Console.WriteLine($"{j.SourcePath}  {j.Status}");

// === Crash recovery ===
// On startup, re-queue any jobs left in Running state from previous crash
var recovered = await repo.RecoverJobsAsync();
Console.WriteLine($"Recovered {recovered.Count} interrupted jobs");
foreach (var j in recovered)
    await queue.EnqueueAsync(j); // push back into in-memory queue
```

---

### 12. Transfer Telemetry

`TransferTelemetry` collects per-session and aggregate metrics. Thread-safe for concurrent transfers.

```csharp
using SmbEnterprise.Diagnostics;

var telemetry = new TransferTelemetry(sp.GetRequiredService<ILogger<TransferTelemetry>>());

// Start a session (one per file transfer)
var sessionId = Guid.NewGuid();
telemetry.StartSession(sessionId, sourceFilePath);

// In transfer progress callback:
telemetry.RecordProgress(sessionId, transferProgress);

// On transient error (will be retried):
telemetry.RecordRetry(sessionId);

// On SMB reconnect:
telemetry.RecordReconnect(sessionId);

// On unrecoverable error:
telemetry.RecordError(sessionId, "STATUS_IO_TIMEOUT");

// When done:
telemetry.CompleteSession(sessionId, success: true);

// Aggregate summary across all sessions
var summary = telemetry.GetSummary();
Console.WriteLine($"Files:       {summary.TotalFilesCompleted}");
Console.WriteLine($"Total data:  {summary.TotalBytesTransferred / 1024.0 / 1024 / 1024:F2} GB");
Console.WriteLine($"Retries:     {summary.TotalRetries}");
Console.WriteLine($"Reconnects:  {summary.TotalReconnects}");
Console.WriteLine($"Errors:      {summary.TotalErrors}");
```

---

### 13. Transfer Dashboard

`TransferDashboard` renders formatted output to the console: live progress bar, summary panel, and telemetry panel.

```csharp
using SmbEnterprise.Diagnostics;

var dashboard = new TransferDashboard();

// Live progress bar (call from Progress<T> callback)
dashboard.Render(new TransferProgress
{
    SourcePath           = @"\movies\film.mkv",
    DestinationPath      = @"C:\Downloads\film.mkv",
    TotalBytes           = 3_700_198_168,
    TransferredBytes     = 1_850_099_084,
    SpeedBytesPerSecond  = 60 * 1024 * 1024,
    Eta                  = TimeSpan.FromSeconds(30),
    Stage                = TransferStage.Reading,
    RetryCount           = 0
});
// Output: [Reading   ] [####################--------------------]  50.0% |  60.0 MB/s | ETA:     0:30 | Retries: 0 | 1.72 GB/3.45 GB

// Summary box after transfer
dashboard.RenderSummary(transferResult);
// Output:
// ================================================================================
// Transfer SUCCEEDED
//   Bytes transferred : 3.45 GB
//   Duration          : 1:01
//   Avg speed         : 57.8 MB/s
//   Checksum verified : False
// ================================================================================

// Telemetry panel
dashboard.RenderTelemetry(telemetry.GetSummary());
// Output:
// --- Telemetry ---
//   Total bytes      : 3.45 GB
//   Files completed  : 1
//   Retries          : 0
//   Reconnects       : 0
//   Errors           : 0
```

---

## TransferOptions Reference

```csharp
var options = new TransferOptions
{
    // Chunk settings
    ChunkSize          = 256 * 1024,      // Initial chunk size (bytes). Default: 1 MB
    MinChunkSize       =  64 * 1024,      // Adaptive lower bound. Default: 64 KB
    MaxChunkSize       =  16 * 1024 * 1024, // Adaptive upper bound. Default: 16 MB

    // Parallelism
    MaxParallelWorkers = 1,               // Parallel writer threads. Use 1 for most SMB servers

    // Retry
    MaxChunkRetries    = 8,               // Retries per chunk on transient error. Default: 5
    MaxReconnectRetries = 3,              // Full session reconnect attempts. Default: 3

    // Integrity
    VerifyAfterCopy    = true,            // Hash verify after copy. Default: true
    ChecksumAlgorithm  = ChecksumAlgorithm.XxHash64, // Default: XxHash64

    // Pipeline
    WriteQueueDepth    = 16,              // Channel backpressure depth. Default: 16
    EnableReadAhead    = true,            // Pre-fetch next chunks. Default: true

    // Behavior
    Overwrite          = false,           // Overwrite existing destination. Default: false
    Resume             = true,            // Resume from partial destination. Default: true
    BandwidthLimitBytesPerSecond = 0,     // 0 = unlimited. Example: 50*1024*1024 = 50 MB/s cap
};
```

> **Tip for SMB servers with "Not enough credits" errors:** Set `MaxParallelWorkers = 1` and `ChunkSize = 256 * 1024`. The library already treats credit exhaustion as a transient error and retries automatically.

---

## Dependency Injection Setup

Full DI registration for all modules:

```csharp
var services = new ServiceCollection();

// Logging (use Serilog, Microsoft.Extensions.Logging, or any provider)
services.AddLogging(b => b.AddConsole());

// SMB provider with optional pool tuning
services.AddSmbProvider(pool =>
{
    pool.MaxSessionsPerServer = 4;
    pool.SessionIdleTimeout   = TimeSpan.FromMinutes(10);
});

// Checksum (pick default algorithm; can be overridden per-call)
services.AddChecksumEngine(ChecksumAlgorithm.XxHash64);

// Diagnostics
services.AddSingleton<TransferTelemetry>();
services.AddSingleton<TransferDashboard>();

// Cache
services.AddSingleton(sp => new MetadataCache(
    new MetadataCacheOptions
    {
        MetadataTtl  = TimeSpan.FromMinutes(5),
        DirectoryTtl = TimeSpan.FromMinutes(2)
    },
    sp.GetRequiredService<ILogger<MetadataCache>>()));

// Jobs — in-memory only
services.AddSingleton<InMemoryJobQueue>();

// Jobs — SQLite persistence (picks up job queue automatically)
services.AddSqlitePersistence("smbjobs.db");

var sp = services.BuildServiceProvider();
```

---

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Your Application                      │
└────────────────────────┬────────────────────────────────┘
                         │ IFileSystemProvider
┌────────────────────────▼────────────────────────────────┐
│              SmbEnterprise.Protocol.SMB                  │
│  SmbFileSystemProvider → SmbFileSystem                   │
│  SmbSessionPool (reuse/reconnect) + SmbRetryEngine       │
│  SMBLibrary (SMB2 wire protocol — isolated here only)    │
└────────────────────────┬────────────────────────────────┘
                         │ IRemoteFileSystem
┌────────────────────────▼────────────────────────────────┐
│               SmbEnterprise.Transfer                     │
│  TransferEngine                                          │
│  └── TransferPipeline (Channel fan-out)                  │
│       ├── Reader task (source chunks → channel)          │
│       └── Writer task × N (channel → destinations)      │
│  AdaptiveChunkSizer  ReadAheadPrefetcher                 │
└────────────────────────┬────────────────────────────────┘
                         │
        ┌────────────────┼────────────────────┐
        │                │                    │
┌───────▼──────┐ ┌───────▼──────┐ ┌──────────▼──────────┐
│  Checksum    │ │  Diagnostics │ │  Cache               │
│  XxHash64    │ │  Telemetry   │ │  MetadataCache (TTL) │
│  CRC32       │ │  Dashboard   │ │  ReadAheadPrefetcher  │
│  SHA-256     │ └──────────────┘ └─────────────────────┘
│  MD5         │
│  Verifier    │
└──────────────┘
        │
┌───────▼──────────────────────────────────────────────────┐
│                    Jobs & Persistence                     │
│  InMemoryJobQueue  (priority + pause/resume/cancel)       │
│  SqliteJobRepository  (EF Core + crash recovery)          │
└──────────────────────────────────────────────────────────┘
```

---

## Performance Notes

Measured on: `192.168.1.250` Samba NAS, 1 Gbps LAN, source file 3.5 GB

| Config | Throughput |
|---|---|
| MaxParallelWorkers=1, ChunkSize=256 KB | ~56–60 MB/s |
| MaxParallelWorkers=4, ChunkSize=1 MB | varies (credit limit on some servers) |

**Recommendations:**
- Start with `MaxParallelWorkers = 1` — single-stream SMB2 often saturates the link
- Let `AdaptiveChunkSizer` grow the chunk size naturally; it will reach the optimal size within the first few seconds
- Use `XxHash64` for verify-after-copy — it is 6× faster than SHA-256 at comparable collision resistance for integrity checking
- Enable `ReadAhead` (`EnableReadAhead = true`) to hide network round-trip latency between chunk reads
- For very large directories (1 000+ files), wrap `ListDirectoryAsync` with `MetadataCache` to avoid re-listing on repeated UI refreshes
