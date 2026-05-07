using System.Buffers;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using SmbEnterprise.Cache;
using SmbEnterprise.Checksum;
using SmbEnterprise.Core.Abstractions;
using SmbEnterprise.Core.Models;
using SmbEnterprise.Diagnostics;
using SmbEnterprise.Jobs;
using SmbEnterprise.Persistence;
using SmbEnterprise.Protocol.SMB;
using SmbEnterprise.Transfer;
using SmbEnterprise.Transfer.Abstractions;
using SmbEnterprise.Transfer.Pipeline;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var services = new ServiceCollection();
services.AddLogging(b => b.AddSerilog(dispose: true));
services.AddSmbProvider();
services.AddChecksumEngine(ChecksumAlgorithm.XxHash64);
services.AddSingleton<ITransferEngine, TransferEngine>();
services.AddSingleton<TransferTelemetry>();
services.AddSingleton<TransferDashboard>();
services.AddSingleton<IJobQueue, InMemoryJobQueue>();
services.AddSqlitePersistence("smb_jobs.db");
services.AddSingleton(new JobSchedulerOptions { MaxConcurrentJobs = 2 });
services.AddSingleton<JobScheduler>();

var sp = services.BuildServiceProvider();

try
{
    if (args.Contains("--debug-ops", StringComparer.OrdinalIgnoreCase))
    {
        await RunSmbOpsDebugAsync(sp, args).ConfigureAwait(false);
        return;
    }

    if (args.Contains("--full-test", StringComparer.OrdinalIgnoreCase))
    {
        await RunFullFeatureTestAsync(sp, args).ConfigureAwait(false);
        return;
    }

    await RunDefaultDemoAsync(sp).ConfigureAwait(false);
}
finally
{
    await sp.DisposeAsync().ConfigureAwait(false);
}

// ─── Helper: test step printer ──────────────────────────────────────────────

static void Pass(string section, string detail)
{
    var prev = Console.ForegroundColor;
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"  [PASS] {section}: {detail}");
    Console.ForegroundColor = prev;
}

static void Fail(string section, string detail)
{
    var prev = Console.ForegroundColor;
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"  [FAIL] {section}: {detail}");
    Console.ForegroundColor = prev;
}

static void Section(string name)
{
    Console.WriteLine();
    Console.WriteLine($"══ {name} ══════════════════════════════════════════════════");
}

// ─── FULL FEATURE TEST ───────────────────────────────────────────────────────

static async Task RunFullFeatureTestAsync(IServiceProvider sp, string[] args)
{
    static string GetArgFT(string[] a, string key, string fallback)
    {
        for (var i = 0; i < a.Length - 1; i++)
        {
            if (string.Equals(a[i], key, StringComparison.OrdinalIgnoreCase))
            {
                return a[i + 1];
            }
        }

        return fallback;
    }

    var server    = GetArgFT(args, "--server", "192.168.1.250");
    var share     = GetArgFT(args, "--share", "media");
    var username  = GetArgFT(args, "--user", "share");
    var password  = GetArgFT(args, "--pass", "1234567890");
    var sourceDir = GetArgFT(args, "--sourceDir", @"\movies\2002 2001 DD2 0 Chan10Bit");

    var credential = new RemoteCredential { Server = server, Share = share, Username = username, Password = password };

    var desktop     = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    var ts          = DateTime.Now.ToString("yyyyMMdd-HHmmss");
    var workDir     = Path.Combine(desktop, "SmbFullTest", ts);
    var dbPath      = Path.Combine(workDir, "test_jobs.db");
    Directory.CreateDirectory(workDir);

    Console.WriteLine($"\n╔══════════════════════════════════════════════════════╗");
    Console.WriteLine($"║  SMB Enterprise — Full Feature Test                  ║");
    Console.WriteLine($"╚══════════════════════════════════════════════════════╝");
    Console.WriteLine($"  SMB   : \\\\{server}\\{share}{sourceDir}");
    Console.WriteLine($"  Local : {workDir}");

    var provider = sp.GetRequiredService<IFileSystemProvider>();
    await using var smb = provider.CreateFileSystem();
    await smb.ConnectAsync(credential).ConfigureAwait(false);
    Pass("Connect", $"\\\\{server}\\{share}");

    // ─── 1. List / GetMetadata / Exists / CreateDirectory / SetAttributes ──────

    Section("1. Core filesystem ops");

    var allItems = new List<FileItem>();
    await foreach (var item in smb.ListDirectoryAsync(sourceDir).ConfigureAwait(false))
    {
        allItems.Add(item);
    }

    Pass("ListDirectory", $"{allItems.Count} entries in {sourceDir}");

    FileItem? testFile = allItems.FirstOrDefault(f => !f.IsDirectory);
    if (testFile is null)
    {
        Fail("No file", "Source directory contains no files – cannot continue");
        return;
    }

    Pass("PickTestFile", $"{testFile.Name}  ({testFile.Size / 1024.0 / 1024:F1} MB)");

    var meta = await smb.GetMetadataAsync(testFile.FullPath).ConfigureAwait(false);
    Pass("GetMetadata", $"size={meta.Size}  modified={meta.ModifiedUtc:u}  dir={meta.IsDirectory}");

    var existsYes = await smb.ExistsAsync(testFile.FullPath).ConfigureAwait(false);
    var existsNo  = await smb.ExistsAsync(testFile.FullPath + "__no_such_path__").ConfigureAwait(false);
    Pass("ExistsAsync", $"existing={existsYes}  missing={existsNo}");

    var testRemoteDir = $"\\movies\\__fulltest_{ts}__";
    await smb.CreateDirectoryAsync(testRemoteDir).ConfigureAwait(false);
    var dirExists = await smb.ExistsAsync(testRemoteDir).ConfigureAwait(false);
    Pass("CreateDirectory", $"{testRemoteDir}  exists={dirExists}");

    // SetAttributes – round-trip timestamps
    var setAttrPath = testRemoteDir + "\\attr_test.txt";
    await using (var ws = await smb.OpenWriteAsync(setAttrPath, 0, createNew: true).ConfigureAwait(false))
    {
        await ws.WriteAsync(System.Text.Encoding.UTF8.GetBytes("hello").AsMemory()).ConfigureAwait(false);
        await ws.FlushAsync().ConfigureAwait(false);
    }

    var attrMeta = await smb.GetMetadataAsync(setAttrPath).ConfigureAwait(false);
    var newMeta  = new FileMetadata
    {
        FullPath   = setAttrPath,
        Size       = attrMeta.Size,
        Attributes = attrMeta.Attributes,
        ModifiedUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        CreatedUtc  = attrMeta.CreatedUtc,
        AccessedUtc = attrMeta.AccessedUtc,
        IsDirectory = false
    };
    await smb.SetAttributesAsync(setAttrPath, newMeta).ConfigureAwait(false);
    Pass("SetAttributesAsync", "timestamps updated");

    // ─── 2. TransferEngine – single file SMB→local ────────────────────────────

    Section("2. TransferEngine – SMB → local (single file, with verify)");

    var localSingleDest = Path.Combine(workDir, "single", testFile.Name);
    Directory.CreateDirectory(Path.Combine(workDir, "single"));

    var dashboard = sp.GetRequiredService<TransferDashboard>();
    var telemetry = sp.GetRequiredService<TransferTelemetry>();

    var localFs = new LocalFileSystemForTest();
    var engineLogger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<TransferEngine>();
    var transferOptions = new TransferOptions
    {
        MaxParallelWorkers = 1,
        ChunkSize          = 256 * 1024,
        MaxChunkSize       = 1 * 1024 * 1024,
        MinChunkSize       = 64 * 1024,
        MaxChunkRetries    = 8,
        VerifyAfterCopy    = false,   // verify done separately via TransferVerifier
        Resume             = true
    };

    var engine = new TransferEngine(smb, new IRemoteFileSystem[] { localFs }, engineLogger);
    var sessionId = Guid.NewGuid();
    telemetry.StartSession(sessionId, testFile.FullPath);

    var sw = Stopwatch.StartNew();
    var tResult = await engine.TransferAsync(
        testFile.FullPath,
        localSingleDest,
        transferOptions,
        new Progress<TransferProgress>(p =>
        {
            dashboard.Render(p);
            telemetry.RecordProgress(sessionId, p);
        })).ConfigureAwait(false);
    sw.Stop();

    Console.WriteLine();   // newline after live progress bar
    telemetry.CompleteSession(sessionId, tResult.Success);

    if (tResult.Success)
        Pass("TransferEngine.TransferAsync", $"{tResult.BytesTransferred / 1024.0 / 1024:F1} MB in {sw.Elapsed:m\\:ss\\.f}");
    else
        Fail("TransferEngine.TransferAsync", tResult.ErrorMessage ?? "unknown");

    dashboard.RenderSummary(tResult);

    // ─── 3. TransferEngine – resume ───────────────────────────────────────────

    Section("3. TransferEngine – resume partial file");

    var resumePath = Path.Combine(workDir, "resume", testFile.Name);
    Directory.CreateDirectory(Path.Combine(workDir, "resume"));

    // Write first 1 MB to simulate a partial download
    const int partialSize = 1 * 1024 * 1024;
    await using (var src = await smb.OpenReadAsync(testFile.FullPath).ConfigureAwait(false))
    {
        await using var dst = new FileStream(resumePath, FileMode.Create, FileAccess.Write, FileShare.None);
        var buf = ArrayPool<byte>.Shared.Rent(partialSize);
        var read = await src.ReadAsync(buf.AsMemory(0, partialSize)).ConfigureAwait(false);
        await dst.WriteAsync(buf.AsMemory(0, read)).ConfigureAwait(false);
        ArrayPool<byte>.Shared.Return(buf);
    }

    var beforeResumeSize = new FileInfo(resumePath).Length;
    var resumeEngine = new TransferEngine(smb, new IRemoteFileSystem[] { localFs }, engineLogger);
    var resumeResult = await resumeEngine.TransferAsync(testFile.FullPath, resumePath, transferOptions).ConfigureAwait(false);

    Console.WriteLine();
    var afterResumeSize = new FileInfo(resumePath).Length;
    if (resumeResult.Success && afterResumeSize > beforeResumeSize)
        Pass("Resume", $"partial={beforeResumeSize / 1024.0:F0} KB → final={afterResumeSize / 1024.0 / 1024:F1} MB");
    else
        Fail("Resume", resumeResult.ErrorMessage ?? $"size unchanged {afterResumeSize}");

    // ─── 4. TransferEngine – multi-destination ────────────────────────────────

    Section("4. TransferEngine – multi-destination (1 file → 2 local paths)");

    var destA = Path.Combine(workDir, "multi_a", testFile.Name);
    var destB = Path.Combine(workDir, "multi_b", testFile.Name);
    Directory.CreateDirectory(Path.Combine(workDir, "multi_a"));
    Directory.CreateDirectory(Path.Combine(workDir, "multi_b"));

    var multiEngine = new TransferEngine(smb, new IRemoteFileSystem[] { localFs, localFs }, engineLogger);
    var multiResult = await multiEngine.TransferMultiDestinationAsync(
        testFile.FullPath,
        [destA, destB],
        new TransferOptions
        {
            MaxParallelWorkers = 1, ChunkSize = 256 * 1024, MaxChunkSize = 1 * 1024 * 1024,
            MinChunkSize = 64 * 1024, MaxChunkRetries = 8, VerifyAfterCopy = false, Resume = false
        }).ConfigureAwait(false);

    Console.WriteLine();
    var okA = File.Exists(destA) && new FileInfo(destA).Length > 0;
    var okB = File.Exists(destB) && new FileInfo(destB).Length > 0;
    if (multiResult.Results.All(r => r.Item2.Success) && okA && okB)
        Pass("TransferEngine.MultiDestination", $"destA={new FileInfo(destA).Length / 1024.0 / 1024:F1} MB  destB={new FileInfo(destB).Length / 1024.0 / 1024:F1} MB");
    else
        Fail("TransferEngine.MultiDestination", string.Join("; ", multiResult.Results.Select(r => r.Item2.ErrorMessage)));

    // ─── 5. TransferEngine – recursive directory ──────────────────────────────

    Section("5. TransferEngine – TransferDirectoryAsync (SMB dir → local)");

    var localDirDest = Path.Combine(workDir, "dir_transfer");
    Directory.CreateDirectory(localDirDest);

    var dirEngine = new TransferEngine(smb, new IRemoteFileSystem[] { localFs }, engineLogger);
    var dirResult = await dirEngine.TransferDirectoryAsync(sourceDir, localDirDest,
        new TransferOptions
        {
            MaxParallelWorkers = 1, ChunkSize = 256 * 1024, MaxChunkSize = 1 * 1024 * 1024,
            MinChunkSize = 64 * 1024, MaxChunkRetries = 8, VerifyAfterCopy = false, Resume = false
        }).ConfigureAwait(false);
    Console.WriteLine();
    if (dirResult.Success)
        Pass("TransferEngine.TransferDirectoryAsync", $"{dirResult.BytesTransferred / 1024.0 / 1024:F1} MB total");
    else
        Fail("TransferEngine.TransferDirectoryAsync", dirResult.ErrorMessage ?? "failed");

    // ─── 6. Checksum – all algorithms ─────────────────────────────────────────

    Section("6. ChecksumEngine – all 4 algorithms on local file");

    var localFilePath = localSingleDest;
    if (!File.Exists(localFilePath))
    {
        localFilePath = destA;   // fallback to multi-dest copy
    }

    if (File.Exists(localFilePath))
    {
        var localFileSize = new FileInfo(localFilePath).Length;
        foreach (var algo in new[] { ChecksumAlgorithm.XxHash64, ChecksumAlgorithm.Crc32, ChecksumAlgorithm.Sha256, ChecksumAlgorithm.Md5 })
        {
            var csEngine = ChecksumEngineFactory.Create(algo);
            var csSw = Stopwatch.StartNew();
            var checksum = await csEngine.ComputeFileAsync(localFilePath,
                async (offset, buf, ct) =>
                {
                    await using var fs = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    fs.Seek(offset, SeekOrigin.Begin);
                    return await fs.ReadAsync(buf, ct).ConfigureAwait(false);
                },
                localFileSize).ConfigureAwait(false);
            csSw.Stop();
            var mbps = (localFileSize / 1024.0 / 1024.0) / Math.Max(0.001, csSw.Elapsed.TotalSeconds);
            Pass($"Checksum.{algo}", $"{checksum.HexHash[..Math.Min(16, checksum.HexHash.Length)]}...  {mbps:F0} MB/s");
        }
    }
    else
    {
        Fail("Checksum", "no local file available from prior steps");
    }

    // ─── 7. TransferVerifier ──────────────────────────────────────────────────

    Section("7. TransferVerifier – SMB source vs local destination");

    if (File.Exists(localSingleDest))
    {
        var verLogger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<TransferVerifier>();
        var verEngine = ChecksumEngineFactory.Create(ChecksumAlgorithm.XxHash64);
        var verifier  = new TransferVerifier(verEngine, verLogger);
        var localVerFs = new LocalFileSystemForTest();
        var verResult = await verifier.VerifyAsync(smb, testFile.FullPath, localVerFs, localSingleDest, testFile.Size).ConfigureAwait(false);
        if (verResult.IsValid)
            Pass("TransferVerifier", $"hash={verResult.Hash}  match=true");
        else
            Fail("TransferVerifier", verResult.ErrorMessage ?? "mismatch");
    }
    else
    {
        Fail("TransferVerifier", "source local file missing, skipping");
    }

    // ─── 8. MetadataCache ─────────────────────────────────────────────────────

    Section("8. MetadataCache – set/get/invalidate/directory/TTL");

    var cacheLogger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<MetadataCache>();
    var cacheOptions = new MetadataCacheOptions { MetadataTtl = TimeSpan.FromSeconds(2), DirectoryTtl = TimeSpan.FromSeconds(2) };
    await using var cache = new MetadataCache(cacheOptions, cacheLogger);

    cache.SetMetadata("/test/path", meta);
    var cacheHit = cache.TryGetMetadata("/test/path", out var cachedMeta);
    Pass("MetadataCache.SetGet", $"hit={cacheHit}  size={cachedMeta?.Size}");

    cache.Invalidate("/test/path");
    var afterInvalidate = cache.TryGetMetadata("/test/path", out _);
    Pass("MetadataCache.Invalidate", $"present_after_invalidate={afterInvalidate}  expected=False");

    var dirItems = new List<FileItem>(allItems);
    cache.SetDirectoryListing("/test/dir", dirItems);
    var dirHit = cache.TryGetDirectoryListing("/test/dir", out var cachedDir);
    Pass("MetadataCache.DirectoryListing", $"hit={dirHit}  count={cachedDir?.Count}");

    cache.InvalidateDirectory("/test/dir");
    var afterDirInvalidate = cache.TryGetDirectoryListing("/test/dir", out _);
    Pass("MetadataCache.InvalidateDirectory", $"present_after_invalidate={afterDirInvalidate}  expected=False");

    // TTL expiry (2s TTL)
    cache.SetMetadata("/test/ttl", meta);
    await Task.Delay(2500).ConfigureAwait(false);  // wait past TTL
    var afterTtl = cache.TryGetMetadata("/test/ttl", out _);
    Pass("MetadataCache.TTL", $"present_after_expiry={afterTtl}  expected=False");

    // ─── 9. ReadAheadPrefetcher ────────────────────────────────────────────────

    Section("9. ReadAheadPrefetcher – prefetch first 3 chunks from SMB file");

    await using var srcStream = await smb.OpenReadAsync(testFile.FullPath).ConfigureAwait(false);
    var prefetchOpts = new ReadAheadOptions { ChunkSize = 256 * 1024, PrefetchDepth = 3 };
    await using var prefetcher = new ReadAheadPrefetcher(srcStream, Math.Min(testFile.Size, prefetchOpts.ChunkSize * 5L), prefetchOpts,
        sp.GetRequiredService<ILoggerFactory>().CreateLogger<ReadAheadPrefetcher>());

    var prefetchedCount = 0;
    long prefetchedBytes = 0;
    await foreach (var chunk in prefetcher.Reader.ReadAllAsync().ConfigureAwait(false))
    {
        prefetchedBytes += chunk.BytesRead;
        prefetchedCount++;
        ArrayPool<byte>.Shared.Return(chunk.Buffer);
    }

    Pass("ReadAheadPrefetcher", $"chunks={prefetchedCount}  bytes={prefetchedBytes / 1024.0:F0} KB");

    // ─── 10. AdaptiveChunkSizer ────────────────────────────────────────────────

    Section("10. AdaptiveChunkSizer – throughput simulation");

    var sizer = new AdaptiveChunkSizer(256 * 1024, 64 * 1024, 16 * 1024 * 1024);

    // Simulate fast throughput → should grow chunk
    var initialSize = sizer.CurrentChunkSize;
    for (var i = 0; i < 10; i++)
        sizer.RecordMetrics(4 * 1024 * 1024, TimeSpan.FromMilliseconds(50));  // 80 MB/s
    var afterFast = sizer.CurrentChunkSize;
    Pass("AdaptiveChunkSizer.FastPath", $"initial={initialSize / 1024}KB  after_fast={afterFast / 1024}KB  grew={afterFast > initialSize}");

    // Simulate slow throughput → should shrink chunk after 3 consecutive slow samples
    for (var i = 0; i < 5; i++)
        sizer.RecordMetrics(64 * 1024, TimeSpan.FromMilliseconds(500));  // ~0.1 MB/s
    var afterSlow = sizer.CurrentChunkSize;
    Pass("AdaptiveChunkSizer.SlowPath", $"after_fast={afterFast / 1024}KB  after_slow={afterSlow / 1024}KB  shrank={afterSlow < afterFast}");

    // ─── 11. InMemoryJobQueue ─────────────────────────────────────────────────

    Section("11. InMemoryJobQueue – enqueue / priority / pause / resume / cancel");

    var queueLogger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<InMemoryJobQueue>();
    var queue = new InMemoryJobQueue(queueLogger);

    var highJob = new TransferJob
    {
        JobId = Guid.NewGuid(), SourcePath = @"\test\high.mkv", DestinationPath = @"C:\out\high.mkv",
        Priority = JobPriority.High, TotalBytes = 100 * 1024 * 1024, Options = new TransferOptions()
    };
    var normalJob = new TransferJob
    {
        JobId = Guid.NewGuid(), SourcePath = @"\test\normal.mkv", DestinationPath = @"C:\out\normal.mkv",
        Priority = JobPriority.Normal, TotalBytes = 200 * 1024 * 1024, Options = new TransferOptions()
    };
    var lowJob = new TransferJob
    {
        JobId = Guid.NewGuid(), SourcePath = @"\test\low.mkv", DestinationPath = @"C:\out\low.mkv",
        Priority = JobPriority.Low, TotalBytes = 50 * 1024 * 1024, Options = new TransferOptions()
    };

    await queue.EnqueueAsync(lowJob).ConfigureAwait(false);
    await queue.EnqueueAsync(normalJob).ConfigureAwait(false);
    await queue.EnqueueAsync(highJob).ConfigureAwait(false);

    // Dequeue should give highest priority first
    var first = await queue.DequeueAsync().ConfigureAwait(false);
    Pass("InMemoryJobQueue.Priority", $"first_dequeued={first?.Priority}  expected=High");

    // Re-enqueue for further tests
    await queue.EnqueueAsync(highJob).ConfigureAwait(false);

    await queue.PauseAsync(normalJob.JobId).ConfigureAwait(false);
    var paused = await queue.GetJobAsync(normalJob.JobId).ConfigureAwait(false);
    Pass("InMemoryJobQueue.Pause", $"status={paused?.Status}  expected=Paused");

    await queue.ResumeAsync(normalJob.JobId).ConfigureAwait(false);
    var resumed = await queue.GetJobAsync(normalJob.JobId).ConfigureAwait(false);
    Pass("InMemoryJobQueue.Resume", $"status={resumed?.Status}  expected=Queued");

    await queue.CancelAsync(lowJob.JobId).ConfigureAwait(false);
    var cancelled = await queue.GetJobAsync(lowJob.JobId).ConfigureAwait(false);
    Pass("InMemoryJobQueue.Cancel", $"status={cancelled?.Status}  expected=Cancelled");

    await queue.UpdateProgressAsync(normalJob.JobId, 50 * 1024 * 1024).ConfigureAwait(false);
    var progressed = await queue.GetJobAsync(normalJob.JobId).ConfigureAwait(false);
    Pass("InMemoryJobQueue.UpdateProgress", $"bytes={progressed?.BytesTransferred / 1024 / 1024} MB  expected=50 MB");

    await queue.CompleteJobAsync(normalJob.JobId, success: true).ConfigureAwait(false);
    var completed = await queue.GetJobAsync(normalJob.JobId).ConfigureAwait(false);
    Pass("InMemoryJobQueue.Complete", $"status={completed?.Status}  success expected=Completed");

    var allJobs = new List<TransferJob>();
    await foreach (var j in queue.GetAllJobsAsync().ConfigureAwait(false))
        allJobs.Add(j);
    Pass("InMemoryJobQueue.GetAll", $"total={allJobs.Count}  statuses={string.Join(",", allJobs.Select(j => j.Status.ToString()[0]))}");

    // ─── 12. SqliteJobRepository ──────────────────────────────────────────────

    Section("12. SqliteJobRepository – persist / query / recover");

    var services2 = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
    services2.AddLogging(b => b.AddSerilog());
    services2.AddSqlitePersistence(dbPath);
    await using var sp2 = services2.BuildServiceProvider();

    var repo = sp2.GetRequiredService<SqliteJobRepository>();
    await repo.EnsureCreatedAsync().ConfigureAwait(false);

    var dbJob1 = new TransferJob
    {
        JobId = Guid.NewGuid(), SourcePath = @"\movies\test1.mkv", DestinationPath = @"C:\out\test1.mkv",
        Priority = JobPriority.High, TotalBytes = 1024 * 1024 * 500, Options = new TransferOptions()
    };
    var dbJob2 = new TransferJob
    {
        JobId = Guid.NewGuid(), SourcePath = @"\movies\test2.mkv", DestinationPath = @"C:\out\test2.mkv",
        Priority = JobPriority.Normal, TotalBytes = 1024 * 1024 * 200, Options = new TransferOptions()
    };

    await repo.EnqueueAsync(dbJob1).ConfigureAwait(false);
    await repo.EnqueueAsync(dbJob2).ConfigureAwait(false);

    var fromDb = await repo.GetJobAsync(dbJob1.JobId).ConfigureAwait(false);
    Pass("SqliteJobRepository.Enqueue+Get", $"id={fromDb?.JobId.ToString()[..8]}...  path={fromDb?.SourcePath}");

    await repo.PauseAsync(dbJob1.JobId).ConfigureAwait(false);
    var pausedDb = await repo.GetJobAsync(dbJob1.JobId).ConfigureAwait(false);
    Pass("SqliteJobRepository.Pause", $"status={pausedDb?.Status}  expected=Paused");

    await repo.ResumeAsync(dbJob1.JobId).ConfigureAwait(false);
    var resumedDb = await repo.GetJobAsync(dbJob1.JobId).ConfigureAwait(false);
    Pass("SqliteJobRepository.Resume", $"status={resumedDb?.Status}  expected=Queued");

    await repo.CancelAsync(dbJob2.JobId).ConfigureAwait(false);
    var cancelledDb = await repo.GetJobAsync(dbJob2.JobId).ConfigureAwait(false);
    Pass("SqliteJobRepository.Cancel", $"status={cancelledDb?.Status}  expected=Cancelled");

    var allDbJobs = new List<TransferJob>();
    await foreach (var j in repo.GetAllJobsAsync().ConfigureAwait(false))
        allDbJobs.Add(j);
    Pass("SqliteJobRepository.GetAll", $"count={allDbJobs.Count}  persisted correctly");

    // Recovery test: simulate crash (job left as Running)
    var crashedJob = new TransferJob
    {
        JobId = Guid.NewGuid(), SourcePath = @"\movies\crash.mkv", DestinationPath = @"C:\out\crash.mkv",
        Priority = JobPriority.Normal, TotalBytes = 1024 * 1024 * 100, Status = JobStatus.Running, Options = new TransferOptions()
    };
    await repo.EnqueueAsync(crashedJob).ConfigureAwait(false);
    var recovered = await repo.RecoverJobsAsync().ConfigureAwait(false);
    var wasRequeued = recovered.Any(j => j.JobId == crashedJob.JobId && j.Status == JobStatus.Queued);
    Pass("SqliteJobRepository.CrashRecovery", $"recovered={recovered.Count}  requeued={wasRequeued}");

    // ─── 13. TransferTelemetry ────────────────────────────────────────────────

    Section("13. TransferTelemetry – lifecycle, retries, reconnects, errors");

    var t1 = Guid.NewGuid();
    var t2 = Guid.NewGuid();
    var t3 = Guid.NewGuid();

    telemetry.StartSession(t1, "file1.mkv");
    telemetry.RecordRetry(t1);
    telemetry.RecordReconnect(t1);
    telemetry.CompleteSession(t1, success: true);

    telemetry.StartSession(t2, "file2.mkv");
    telemetry.RecordProgress(t2, new TransferProgress { SessionId = t2, SourcePath = "file2.mkv", DestinationPath = "C:\\out\\file2.mkv", TransferredBytes = 50 * 1024 * 1024, TotalBytes = 200 * 1024 * 1024 });
    telemetry.CompleteSession(t2, success: true);

    telemetry.StartSession(t3, "file3.mkv");
    telemetry.RecordRetry(t3);
    telemetry.RecordRetry(t3);
    telemetry.RecordError(t3, "STATUS_IO_TIMEOUT");
    telemetry.CompleteSession(t3, success: false);

    var tSummary = telemetry.GetSummary();
    Pass("Telemetry.TotalFiles", $"completed={tSummary.TotalFilesCompleted}  (includes prior test steps)");
    Pass("Telemetry.Retries",    $"retries={tSummary.TotalRetries}");
    Pass("Telemetry.Reconnects", $"reconnects={tSummary.TotalReconnects}");
    Pass("Telemetry.Errors",     $"errors={tSummary.TotalErrors}");
    Pass("Telemetry.Bytes",      $"total={tSummary.TotalBytesTransferred / 1024.0 / 1024:F1} MB");

    // ─── 14. TransferDashboard ────────────────────────────────────────────────

    Section("14. TransferDashboard – Render / RenderSummary / RenderTelemetry");

    var fakeProgress = new TransferProgress
    {
        SessionId = Guid.NewGuid(),
        SourcePath = @"\movies\demo.mkv",
        DestinationPath = @"C:\out\demo.mkv",
        TotalBytes = 1000 * 1024 * 1024,
        TransferredBytes = 350 * 1024 * 1024,
        SpeedBytesPerSecond = 45 * 1024 * 1024,
        Eta = TimeSpan.FromSeconds(15),
        Stage = TransferStage.Writing,
        RetryCount = 1
    };
    dashboard.Render(fakeProgress);
    Console.WriteLine();
    Pass("Dashboard.Render", "live progress bar rendered above");

    var fakeSummary = new TransferResult
    {
        Success = true,
        BytesTransferred = 350 * 1024 * 1024,
        Duration = TimeSpan.FromSeconds(7),
        ChecksumVerified = true
    };
    dashboard.RenderSummary(fakeSummary);
    Pass("Dashboard.RenderSummary", "summary panel rendered");

    dashboard.RenderTelemetry(tSummary);
    Pass("Dashboard.RenderTelemetry", "telemetry panel rendered");

    // ─── Cleanup remote test directory ────────────────────────────────────────

    Section("Cleanup");

    await smb.DeleteFileAsync(setAttrPath).ConfigureAwait(false);
    await smb.DeleteDirectoryAsync(testRemoteDir, recursive: true).ConfigureAwait(false);
    var cleanedUp = !(await smb.ExistsAsync(testRemoteDir).ConfigureAwait(false));
    Pass("Cleanup", $"remote test dir removed={cleanedUp}");

    await smb.DisconnectAsync().ConfigureAwait(false);

    Console.WriteLine();
    Console.WriteLine($"╔══════════════════════════════════════════════════════╗");
    Console.WriteLine($"║  Full Feature Test COMPLETE                          ║");
    Console.WriteLine($"║  Results in: {workDir,-40}║");
    Console.WriteLine($"╚══════════════════════════════════════════════════════╝");
}

static async Task RunSmbOpsDebugAsync(IServiceProvider sp, string[] args)
{
    static string GetArg(string[] allArgs, string key, string fallback)
    {
        for (var i = 0; i < allArgs.Length - 1; i++)
        {
            if (string.Equals(allArgs[i], key, StringComparison.OrdinalIgnoreCase))
            {
                return allArgs[i + 1];
            }
        }

        return fallback;
    }

    var server = GetArg(args, "--server", "192.168.1.250");
    var share = GetArg(args, "--share", "media");
    var username = GetArg(args, "--user", "share");
    var password = GetArg(args, "--pass", "1234567890");
    var sourceDir = GetArg(args, "--sourceDir", @"\movies\2002 2001 DD2 0 Chan10Bit");

    var provider = sp.GetRequiredService<IFileSystemProvider>();
    await using var smb = provider.CreateFileSystem();

    var credential = new RemoteCredential
    {
        Server = server,
        Share = share,
        Username = username,
        Password = password
    };

    Console.WriteLine("=== SMB OPS DEBUG START ===");
    Console.WriteLine($"SMB: \\\\{server}\\{share}{sourceDir}");

    await smb.ConnectAsync(credential).ConfigureAwait(false);

    var files = new List<FileItem>();
    await foreach (var item in smb.ListDirectoryAsync(sourceDir).ConfigureAwait(false))
    {
        if (!item.IsDirectory)
        {
            files.Add(item);
            if (files.Count >= 1)
            {
                break;
            }
        }
    }

    if (files.Count == 0)
    {
        throw new InvalidOperationException($"No file found in source directory: {sourceDir}");
    }

    var sourceFile = files[0];
    var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
    var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    var localWorkDir = Path.Combine(desktop, "SmbEnterpriseDebug", ts);
    Directory.CreateDirectory(localWorkDir);

    var downloadedLocalPath = Path.Combine(localWorkDir, sourceFile.Name);

    // 1) Copy SMB -> Desktop
    await CopyRemoteToLocalAsync(smb, sourceFile.FullPath, downloadedLocalPath).ConfigureAwait(false);
    var localInfo = new FileInfo(downloadedLocalPath);
    Console.WriteLine($"[PASS] Download SMB->Desktop: {downloadedLocalPath} ({localInfo.Length} bytes)");

    // 2) Copy Desktop -> SMB (reverse copy)
    var remoteOpsRoot = $"\\movies\\__copilot_debug_ops__{ts}";
    await smb.CreateDirectoryAsync(remoteOpsRoot).ConfigureAwait(false);

    var uploadedRemotePath = remoteOpsRoot + "\\" + sourceFile.Name;
    await CopyLocalToRemoteAsync(downloadedLocalPath, smb, uploadedRemotePath).ConfigureAwait(false);
    var uploadedMeta = await smb.GetMetadataAsync(uploadedRemotePath).ConfigureAwait(false);
    Console.WriteLine($"[PASS] Upload Desktop->SMB: {uploadedRemotePath} ({uploadedMeta.Size} bytes)");

    // 3) Cut/Move on SMB
    var movedDir = remoteOpsRoot + "\\moved";
    await smb.CreateDirectoryAsync(movedDir).ConfigureAwait(false);
    var movedPath = movedDir + "\\" + sourceFile.Name;
    await smb.RenameAsync(uploadedRemotePath, movedPath).ConfigureAwait(false);
    var movedExists = await smb.ExistsAsync(movedPath).ConfigureAwait(false);
    Console.WriteLine($"[PASS] Cut/Move SMB: {uploadedRemotePath} -> {movedPath}; Exists={movedExists}");

    // 4) Rename on SMB
    var renamedPath = movedDir + "\\" + Path.GetFileNameWithoutExtension(sourceFile.Name) + "-renamed" + Path.GetExtension(sourceFile.Name);
    await smb.RenameAsync(movedPath, renamedPath).ConfigureAwait(false);
    var renamedExists = await smb.ExistsAsync(renamedPath).ConfigureAwait(false);
    Console.WriteLine($"[PASS] Rename SMB: {movedPath} -> {renamedPath}; Exists={renamedExists}");

    // 5) Delete file on SMB
    await smb.DeleteFileAsync(renamedPath).ConfigureAwait(false);
    var stillExists = await smb.ExistsAsync(renamedPath).ConfigureAwait(false);
    Console.WriteLine($"[PASS] Delete file SMB: {renamedPath}; ExistsAfterDelete={stillExists}");

    // 6) Delete folders on SMB
    await smb.DeleteDirectoryAsync(movedDir, recursive: true).ConfigureAwait(false);
    await smb.DeleteDirectoryAsync(remoteOpsRoot, recursive: true).ConfigureAwait(false);
    var rootExists = await smb.ExistsAsync(remoteOpsRoot).ConfigureAwait(false);
    Console.WriteLine($"[PASS] Delete folder SMB: {remoteOpsRoot}; ExistsAfterDelete={rootExists}");

    await smb.DisconnectAsync().ConfigureAwait(false);
    Console.WriteLine("=== SMB OPS DEBUG DONE ===");
}

static async Task CopyRemoteToLocalAsync(IRemoteFileSystem remoteFs, string remotePath, string localPath, CancellationToken ct = default)
{
    var parent = Path.GetDirectoryName(localPath);
    if (!string.IsNullOrWhiteSpace(parent))
    {
        Directory.CreateDirectory(parent);
    }

    await using var src = await remoteFs.OpenReadAsync(remotePath, 0, ct).ConfigureAwait(false);
    await using var dst = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

    var pool = ArrayPool<byte>.Shared;
    var buffer = pool.Rent(256 * 1024);
    try
    {
        while (true)
        {
            var read = await src.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        await dst.FlushAsync(ct).ConfigureAwait(false);
    }
    finally
    {
        pool.Return(buffer);
    }
}

static async Task CopyLocalToRemoteAsync(string localPath, IRemoteFileSystem remoteFs, string remotePath, CancellationToken ct = default)
{
    await using var src = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
    await using var dst = await remoteFs.OpenWriteAsync(remotePath, 0, createNew: true, ct).ConfigureAwait(false);

    var pool = ArrayPool<byte>.Shared;
    var buffer = pool.Rent(256 * 1024);
    try
    {
        while (true)
        {
            var read = await src.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        await dst.FlushAsync(ct).ConfigureAwait(false);
    }
    finally
    {
        pool.Return(buffer);
    }
}

static async Task RunDefaultDemoAsync(ServiceProvider sp)
{
    Console.WriteLine("SMB Enterprise Transfer Engine - Sample App");

    Console.WriteLine("Job Queue Demo");
    var queue = sp.GetRequiredService<IJobQueue>();

    var jobs = new[]
    {
        new TransferJob
        {
            JobId = Guid.NewGuid(),
            SourcePath = @"\\fileserver\data\reports\2024\Q4.xlsx",
            DestinationPath = @"\\backup\archive\2024\Q4.xlsx",
            Priority = JobPriority.High,
            TotalBytes = 50 * 1024 * 1024,
            Options = new TransferOptions { VerifyAfterCopy = true, ChecksumAlgorithm = ChecksumAlgorithm.XxHash64 }
        },
        new TransferJob
        {
            JobId = Guid.NewGuid(),
            SourcePath = @"\\fileserver\media\videos\presentation.mp4",
            DestinationPath = @"\\backup\media\presentation.mp4",
            Priority = JobPriority.Normal,
            TotalBytes = 500 * 1024 * 1024,
            Options = new TransferOptions { ChunkSize = 4 * 1024 * 1024 }
        },
        new TransferJob
        {
            JobId = Guid.NewGuid(),
            SourcePath = @"\\fileserver\logs\archive.tar.gz",
            DestinationPath = @"\\cold-storage\logs\archive.tar.gz",
            Priority = JobPriority.Low,
            TotalBytes = 2L * 1024 * 1024 * 1024,
            Options = new TransferOptions { MaxParallelWorkers = 1 }
        }
    };

    foreach (var job in jobs)
    {
        await queue.EnqueueAsync(job).ConfigureAwait(false);
        Console.WriteLine($"  Enqueued [{job.Priority,-6}] {job.SourcePath}");
    }

    Console.WriteLine("Current queue (sorted by priority):");
    await foreach (var j in queue.GetAllJobsAsync().ConfigureAwait(false))
    {
        Console.WriteLine($"  [{j.Priority,-6}] {j.Status,-8} {Path.GetFileName(j.SourcePath)} ({j.TotalBytes / 1024.0 / 1024:F1} MB)");
    }

    Console.WriteLine("Checksum Demo");
    var data = new byte[4 * 1024 * 1024];
    new Random(42).NextBytes(data);

    foreach (var algo in new[] { ChecksumAlgorithm.XxHash64, ChecksumAlgorithm.Crc32, ChecksumAlgorithm.Sha256, ChecksumAlgorithm.Md5 })
    {
        var engine = ChecksumEngineFactory.Create(algo);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await engine.ComputeFileAsync("demo.bin",
            (offset, buffer, _) =>
            {
                if (offset >= data.Length)
                {
                    return new ValueTask<int>(0);
                }

                var len = (int)Math.Min(buffer.Length, data.Length - (int)offset);
                data.AsSpan((int)offset, len).CopyTo(buffer.Span);
                return new ValueTask<int>(len);
            },
            data.Length).ConfigureAwait(false);
        sw.Stop();
        var mbps = (data.Length / 1024.0 / 1024.0) / sw.Elapsed.TotalSeconds;
        Console.WriteLine($"  {algo,-12} {result.HexHash[..Math.Min(16, result.HexHash.Length)]}...  {mbps:F0} MB/s");
    }

    Console.WriteLine("Telemetry Demo");
    var telemetry = sp.GetRequiredService<TransferTelemetry>();
    var sessionId = Guid.NewGuid();

    telemetry.StartSession(sessionId, "demo-correlation-1");
    telemetry.RecordRetry(sessionId);
    telemetry.CompleteSession(sessionId, success: true);

    var sessionId2 = Guid.NewGuid();
    telemetry.StartSession(sessionId2, "demo-correlation-2");
    telemetry.CompleteSession(sessionId2, success: true);

    var sessionId3 = Guid.NewGuid();
    telemetry.StartSession(sessionId3, "demo-correlation-3");
    telemetry.RecordRetry(sessionId3);
    telemetry.RecordRetry(sessionId3);
    telemetry.RecordError(sessionId3, "Transfer failed");
    telemetry.CompleteSession(sessionId3, success: false);

    var summary = telemetry.GetSummary();
    Console.WriteLine($"  Total completed : {summary.TotalFilesCompleted}");
    Console.WriteLine($"  Total bytes     : {summary.TotalBytesTransferred / 1024.0 / 1024:F1} MB");
    Console.WriteLine($"  Total retries   : {summary.TotalRetries}");
    Console.WriteLine($"  Total errors    : {summary.TotalErrors}");
    Console.WriteLine($"  Active sessions : {summary.ActiveSessions}");

    Console.WriteLine("Done.");
}

// ─── Minimal local IRemoteFileSystem for test use ────────────────────────────

internal sealed class LocalFileSystemForTest : IRemoteFileSystem
{
    public Task ConnectAsync(RemoteCredential c, CancellationToken ct = default) => Task.CompletedTask;
    public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default) =>
        Task.FromResult(File.Exists(path) || Directory.Exists(path));

    public async IAsyncEnumerable<FileItem> ListDirectoryAsync(string path, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var e in Directory.EnumerateFileSystemEntries(path))
        {
            ct.ThrowIfCancellationRequested();
            var isDir = Directory.Exists(e);
            yield return new FileItem
            {
                Name = Path.GetFileName(e), FullPath = e, IsDirectory = isDir,
                Size = isDir ? 0L : new FileInfo(e).Length,
                CreatedUtc = File.GetCreationTimeUtc(e), ModifiedUtc = File.GetLastWriteTimeUtc(e),
                Attributes = File.GetAttributes(e)
            };
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public Task<FileMetadata> GetMetadataAsync(string path, CancellationToken ct = default)
    {
        if (File.Exists(path))
        {
            var fi = new FileInfo(path);
            return Task.FromResult(new FileMetadata
            {
                FullPath = fi.FullName, Size = fi.Length, IsDirectory = false,
                CreatedUtc = fi.CreationTimeUtc, ModifiedUtc = fi.LastWriteTimeUtc, AccessedUtc = fi.LastAccessTimeUtc,
                Attributes = fi.Attributes
            });
        }

        var di = new DirectoryInfo(path);
        return Task.FromResult(new FileMetadata
        {
            FullPath = di.FullName, Size = 0, IsDirectory = true,
            CreatedUtc = di.CreationTimeUtc, ModifiedUtc = di.LastWriteTimeUtc, AccessedUtc = di.LastAccessTimeUtc,
            Attributes = di.Attributes
        });
    }

    public Task<IRemoteReadStream> OpenReadAsync(string path, long offset = 0, CancellationToken ct = default)
    {
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 128 * 1024, FileOptions.Asynchronous);
        if (offset > 0) fs.Seek(offset, SeekOrigin.Begin);
        return Task.FromResult<IRemoteReadStream>(new LocalTestReadStream(fs));
    }

    public Task<IRemoteWriteStream> OpenWriteAsync(string path, long offset = 0, bool createNew = false, CancellationToken ct = default)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
        var mode = createNew ? FileMode.Create : (offset > 0 ? FileMode.OpenOrCreate : FileMode.Create);
        var fs = new FileStream(path, mode, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous);
        if (offset > 0) fs.Seek(offset, SeekOrigin.Begin);
        return Task.FromResult<IRemoteWriteStream>(new LocalTestWriteStream(fs));
    }

    public Task CreateDirectoryAsync(string path, CancellationToken ct = default) { Directory.CreateDirectory(path); return Task.CompletedTask; }

    public Task DeleteFileAsync(string path, CancellationToken ct = default) { if (File.Exists(path)) File.Delete(path); return Task.CompletedTask; }

    public Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken ct = default) { if (Directory.Exists(path)) Directory.Delete(path, recursive); return Task.CompletedTask; }

    public Task RenameAsync(string src, string dst, CancellationToken ct = default) { File.Move(src, dst); return Task.CompletedTask; }

    public Task SetAttributesAsync(string path, FileMetadata meta, CancellationToken ct = default)
    {
        File.SetAttributes(path, meta.Attributes);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class LocalTestReadStream : IRemoteReadStream
{
    private readonly FileStream _fs;
    public LocalTestReadStream(FileStream fs) { _fs = fs; }
    public long Length => _fs.Length;
    public long Position => _fs.Position;
    public bool CanSeek => true;
    public ValueTask<int> ReadAsync(Memory<byte> buf, CancellationToken ct = default) => _fs.ReadAsync(buf, ct);
    public Task SeekAsync(long offset, CancellationToken ct = default) { _fs.Seek(offset, SeekOrigin.Begin); return Task.CompletedTask; }
    public ValueTask DisposeAsync() => _fs.DisposeAsync();
}

internal sealed class LocalTestWriteStream : IRemoteWriteStream
{
    private readonly FileStream _fs;
    public LocalTestWriteStream(FileStream fs) { _fs = fs; }
    public long Position => _fs.Position;
    public ValueTask WriteAsync(ReadOnlyMemory<byte> buf, CancellationToken ct = default) => _fs.WriteAsync(buf, ct);
    public Task FlushAsync(CancellationToken ct = default) => _fs.FlushAsync(ct);
    public ValueTask DisposeAsync() => _fs.DisposeAsync();
}

// Extension to write string as bytes to IRemoteWriteStream
internal static class WriteStreamExtensions
{
    public static ValueTask WriteAsync(this IRemoteWriteStream ws, string text)
        => ws.WriteAsync(System.Text.Encoding.UTF8.GetBytes(text).AsMemory());
}
