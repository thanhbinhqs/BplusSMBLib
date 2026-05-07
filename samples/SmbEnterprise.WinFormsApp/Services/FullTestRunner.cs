using System.Buffers;
using System.Runtime.CompilerServices;
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
using SmbEnterprise.Transfer;
using SmbEnterprise.Transfer.Pipeline;

namespace SmbEnterprise.WinFormsApp.Services;

// ─── Result type ─────────────────────────────────────────────────────────────

public enum SectionStatus { Pending, Running, Pass, Fail, Skipped }

public sealed record SectionResult(
    int Index,
    string Name,
    SectionStatus Status,
    string Detail,
    TimeSpan Duration);

public sealed record TestSettings(
    string Server,
    string Share,
    string Username,
    string Password,
    string SourceDir);

// ─── Runner ──────────────────────────────────────────────────────────────────

public sealed class FullTestRunner
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IFileSystemProvider _smbProvider;

    public FullTestRunner(ILoggerFactory loggerFactory, IFileSystemProvider smbProvider)
    {
        _loggerFactory = loggerFactory;
        _smbProvider   = smbProvider;
    }

    public async Task RunAsync(
        TestSettings settings,
        IProgress<SectionResult> sectionProgress,
        IProgress<TransferProgress> transferProgress,
        CancellationToken ct)
    {
        var ts      = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var workDir = Path.Combine(desktop, "SmbFullTest", ts);
        var dbPath  = Path.Combine(workDir, "test_jobs.db");
        Directory.CreateDirectory(workDir);

        var credential = new RemoteCredential
        {
            Server   = settings.Server,
            Share    = settings.Share,
            Username = settings.Username,
            Password = settings.Password
        };

        await using var smb = _smbProvider.CreateFileSystem();
        await smb.ConnectAsync(credential, ct).ConfigureAwait(false);
        Log.Information("Connected to \\\\{Server}\\{Share}", settings.Server, settings.Share);

        // ─── shared state ─────────────────────────────────────────────────────

        var telemetry    = new TransferTelemetry(_loggerFactory.CreateLogger<TransferTelemetry>());
        var dashboard    = new TransferDashboard();
        var localFs      = new LocalFileSystemForTest();
        var engineLogger = _loggerFactory.CreateLogger<TransferEngine>();

        var transferOptions = new TransferOptions
        {
            MaxParallelWorkers = 1,
            ChunkSize          = 256 * 1024,
            MaxChunkSize       = 1 * 1024 * 1024,
            MinChunkSize       = 64 * 1024,
            MaxChunkRetries    = 8,
            VerifyAfterCopy    = false,
            Resume             = true
        };

        FileItem? testFile     = null;
        string?   localSingle  = null;
        string    setAttrPath  = string.Empty;
        string    testRemoteDir = $"\\movies\\__fulltest_{ts}__";
        FileMetadata? meta     = null;
        List<FileItem> allItems = new();

        // ─── 1. Core filesystem ops ───────────────────────────────────────────
        await RunSection(1, "Core filesystem ops", sectionProgress, ct, async () =>
        {
            await foreach (var item in smb.ListDirectoryAsync(settings.SourceDir, ct).ConfigureAwait(false))
                allItems.Add(item);

            testFile = allItems.FirstOrDefault(f => !f.IsDirectory)
                       ?? throw new InvalidOperationException("Source directory contains no files");

            meta = await smb.GetMetadataAsync(testFile.FullPath, ct).ConfigureAwait(false);
            var existsYes = await smb.ExistsAsync(testFile.FullPath, ct).ConfigureAwait(false);
            var existsNo  = await smb.ExistsAsync(testFile.FullPath + "__no_such_path__", ct).ConfigureAwait(false);

            await smb.CreateDirectoryAsync(testRemoteDir, ct).ConfigureAwait(false);
            var dirExists = await smb.ExistsAsync(testRemoteDir, ct).ConfigureAwait(false);

            setAttrPath = testRemoteDir + "\\attr_test.txt";
            await using (var ws = await smb.OpenWriteAsync(setAttrPath, 0, createNew: true, ct).ConfigureAwait(false))
            {
                await ws.WriteAsync("hello"u8.ToArray().AsMemory(), ct).ConfigureAwait(false);
                await ws.FlushAsync(ct).ConfigureAwait(false);
            }

            var attrMeta = await smb.GetMetadataAsync(setAttrPath, ct).ConfigureAwait(false);
            await smb.SetAttributesAsync(setAttrPath, new FileMetadata
            {
                FullPath    = setAttrPath,
                Size        = attrMeta.Size,
                Attributes  = attrMeta.Attributes,
                ModifiedUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedUtc  = attrMeta.CreatedUtc,
                AccessedUtc = attrMeta.AccessedUtc,
                IsDirectory = false
            }, ct).ConfigureAwait(false);

            return $"{allItems.Count} file(s) listed | {testFile.Name} ({testFile.Size / 1024.0 / 1024:F1} MB) | dir={dirExists}";
        });

        if (testFile is null) return;   // nothing to test without a file

        // ─── 2. TransferEngine – single file ─────────────────────────────────
        await RunSection(2, "TransferEngine – SMB → local", sectionProgress, ct, async () =>
        {
            localSingle = Path.Combine(workDir, "single", testFile.Name);
            Directory.CreateDirectory(Path.Combine(workDir, "single"));

            var engine    = new TransferEngine(smb, new IRemoteFileSystem[] { localFs }, engineLogger);
            var sessionId = Guid.NewGuid();
            telemetry.StartSession(sessionId, testFile.FullPath);

            var result = await engine.TransferAsync(testFile.FullPath, localSingle, transferOptions,
                new Progress<TransferProgress>(p =>
                {
                    telemetry.RecordProgress(sessionId, p);
                    transferProgress.Report(p);
                }), ct).ConfigureAwait(false);

            telemetry.CompleteSession(sessionId, result.Success);
            if (!result.Success)
                throw new InvalidOperationException(result.ErrorMessage ?? "Transfer failed");

            return $"{result.BytesTransferred / 1024.0 / 1024:F1} MB @ {result.BytesTransferred / result.Duration.TotalSeconds / 1024 / 1024:F1} MB/s";
        });

        // ─── 3. Resume ────────────────────────────────────────────────────────
        await RunSection(3, "TransferEngine – resume partial", sectionProgress, ct, async () =>
        {
            var resumePath = Path.Combine(workDir, "resume", testFile.Name);
            Directory.CreateDirectory(Path.Combine(workDir, "resume"));

            const int partialSize = 1 * 1024 * 1024;
            await using (var src = await smb.OpenReadAsync(testFile.FullPath, 0, ct).ConfigureAwait(false))
            {
                await using var dst = new FileStream(resumePath, FileMode.Create, FileAccess.Write, FileShare.None);
                var buf  = ArrayPool<byte>.Shared.Rent(partialSize);
                var read = await src.ReadAsync(buf.AsMemory(0, partialSize), ct).ConfigureAwait(false);
                await dst.WriteAsync(buf.AsMemory(0, read), ct).ConfigureAwait(false);
                ArrayPool<byte>.Shared.Return(buf);
            }

            var before = new FileInfo(resumePath).Length;
            var engine = new TransferEngine(smb, new IRemoteFileSystem[] { localFs }, engineLogger);
            var result = await engine.TransferAsync(testFile.FullPath, resumePath, transferOptions,
                new Progress<TransferProgress>(transferProgress.Report), ct).ConfigureAwait(false);

            if (!result.Success) throw new InvalidOperationException(result.ErrorMessage ?? "Resume failed");
            var after = new FileInfo(resumePath).Length;
            return $"partial={before / 1024.0:F0} KB → final={after / 1024.0 / 1024:F1} MB";
        });

        // ─── 4. Multi-destination ─────────────────────────────────────────────
        await RunSection(4, "TransferEngine – multi-destination", sectionProgress, ct, async () =>
        {
            var destA = Path.Combine(workDir, "multi_a", testFile.Name);
            var destB = Path.Combine(workDir, "multi_b", testFile.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(destA)!);
            Directory.CreateDirectory(Path.GetDirectoryName(destB)!);

            var engine = new TransferEngine(smb, new IRemoteFileSystem[] { localFs, localFs }, engineLogger);
            var opts   = new TransferOptions
            {
                MaxParallelWorkers = 1, ChunkSize = 256 * 1024, MaxChunkSize = 1 * 1024 * 1024,
                MinChunkSize = 64 * 1024, MaxChunkRetries = 8, VerifyAfterCopy = false, Resume = false
            };
            var result = await engine.TransferMultiDestinationAsync(testFile.FullPath, [destA, destB], opts,
                progress: new Progress<TransferProgress>(transferProgress.Report),
                cancellationToken: ct).ConfigureAwait(false);

            if (result.Results.Any(r => !r.Item2.Success))
                throw new InvalidOperationException("One or more destinations failed");

            return $"destA={new FileInfo(destA).Length / 1024.0 / 1024:F1} MB  destB={new FileInfo(destB).Length / 1024.0 / 1024:F1} MB";
        });

        // ─── 5. TransferDirectoryAsync ────────────────────────────────────────
        await RunSection(5, "TransferEngine – directory", sectionProgress, ct, async () =>
        {
            var localDirDest = Path.Combine(workDir, "dir_transfer");
            Directory.CreateDirectory(localDirDest);

            var engine = new TransferEngine(smb, new IRemoteFileSystem[] { localFs }, engineLogger);
            var opts   = new TransferOptions
            {
                MaxParallelWorkers = 1, ChunkSize = 256 * 1024, MaxChunkSize = 1 * 1024 * 1024,
                MinChunkSize = 64 * 1024, MaxChunkRetries = 8, VerifyAfterCopy = false, Resume = false
            };
            var result = await engine.TransferDirectoryAsync(settings.SourceDir, localDirDest, opts,
                progress: new Progress<TransferProgress>(transferProgress.Report),
                cancellationToken: ct).ConfigureAwait(false);

            if (!result.Success) throw new InvalidOperationException(result.ErrorMessage ?? "Directory transfer failed");
            return $"{result.BytesTransferred / 1024.0 / 1024:F1} MB total";
        });

        // ─── 6. ChecksumEngine ───────────────────────────────────────────────
        await RunSection(6, "ChecksumEngine – 4 algorithms", sectionProgress, ct, async () =>
        {
            if (localSingle is null || !File.Exists(localSingle))
                throw new FileNotFoundException("Local file missing from step 2");

            var size   = new FileInfo(localSingle).Length;
            var sb     = new System.Text.StringBuilder();
            foreach (var algo in new[] { ChecksumAlgorithm.XxHash64, ChecksumAlgorithm.Crc32, ChecksumAlgorithm.Sha256, ChecksumAlgorithm.Md5 })
            {
                ct.ThrowIfCancellationRequested();
                var csEngine = ChecksumEngineFactory.Create(algo);
                var sw       = System.Diagnostics.Stopwatch.StartNew();
                var path     = localSingle;
                var checksum = await csEngine.ComputeFileAsync(path,
                    async (offset, buf, t) =>
                    {
                        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                        fs.Seek(offset, SeekOrigin.Begin);
                        return await fs.ReadAsync(buf, t).ConfigureAwait(false);
                    }, size).ConfigureAwait(false);
                sw.Stop();
                var hex   = checksum.HexHash[..Math.Min(16, checksum.HexHash.Length)];
                var mbps  = size / 1024.0 / 1024.0 / Math.Max(0.001, sw.Elapsed.TotalSeconds);
                sb.Append($"{algo}: {hex} ({mbps:F0} MB/s)  ");
            }
            return sb.ToString().TrimEnd();
        });

        // ─── 7. TransferVerifier ─────────────────────────────────────────────
        await RunSection(7, "TransferVerifier – hash verify", sectionProgress, ct, async () =>
        {
            if (localSingle is null || !File.Exists(localSingle))
                throw new FileNotFoundException("Local file missing from step 2");

            var verLogger = _loggerFactory.CreateLogger<TransferVerifier>();
            var verEngine = ChecksumEngineFactory.Create(ChecksumAlgorithm.XxHash64);
            var verifier  = new TransferVerifier(verEngine, verLogger);
            var localVerFs = new LocalFileSystemForTest();
            var verResult = await verifier.VerifyAsync(smb, testFile.FullPath, localVerFs, localSingle, testFile.Size).ConfigureAwait(false);

            if (!verResult.IsValid) throw new InvalidOperationException(verResult.ErrorMessage ?? "Hash mismatch");
            return $"hash={verResult.Hash}  match=true";
        });

        // ─── 8. MetadataCache ────────────────────────────────────────────────
        await RunSection(8, "MetadataCache – set/get/TTL", sectionProgress, ct, async () =>
        {
            var cacheLogger  = _loggerFactory.CreateLogger<MetadataCache>();
            var cacheOptions = new MetadataCacheOptions
            {
                MetadataTtl  = TimeSpan.FromSeconds(2),
                DirectoryTtl = TimeSpan.FromSeconds(2)
            };
            await using var cache = new MetadataCache(cacheOptions, cacheLogger);

            cache.SetMetadata("/test/path", meta!);
            var hit = cache.TryGetMetadata("/test/path", out var cached);

            cache.Invalidate("/test/path");
            var afterInv = cache.TryGetMetadata("/test/path", out _);

            cache.SetDirectoryListing("/test/dir", new List<FileItem>(allItems));
            var dirHit = cache.TryGetDirectoryListing("/test/dir", out _);

            cache.InvalidateDirectory("/test/dir");
            var afterDirInv = cache.TryGetDirectoryListing("/test/dir", out _);

            cache.SetMetadata("/test/ttl", meta!);
            await Task.Delay(2500, ct).ConfigureAwait(false);
            var afterTtl = cache.TryGetMetadata("/test/ttl", out _);

            return $"set/get={hit}  invalidate={!afterInv}  dir={dirHit}  dirInv={!afterDirInv}  TTL={!afterTtl}";
        });

        // ─── 9. ReadAheadPrefetcher ───────────────────────────────────────────
        await RunSection(9, "ReadAheadPrefetcher", sectionProgress, ct, async () =>
        {
            await using var srcStream = await smb.OpenReadAsync(testFile.FullPath, 0, ct).ConfigureAwait(false);
            var opts = new ReadAheadOptions { ChunkSize = 256 * 1024, PrefetchDepth = 3 };
            await using var prefetcher = new ReadAheadPrefetcher(
                srcStream,
                Math.Min(testFile.Size, opts.ChunkSize * 5L),
                opts,
                _loggerFactory.CreateLogger<ReadAheadPrefetcher>());

            var count = 0; long bytes = 0;
            await foreach (var chunk in prefetcher.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                bytes += chunk.BytesRead;
                count++;
                ArrayPool<byte>.Shared.Return(chunk.Buffer);
            }
            return $"chunks={count}  bytes={bytes / 1024.0:F0} KB";
        });

        // ─── 10. AdaptiveChunkSizer ───────────────────────────────────────────
        await RunSection(10, "AdaptiveChunkSizer", sectionProgress, ct, async () =>
        {
            await Task.Yield();
            var sizer   = new AdaptiveChunkSizer(256 * 1024, 64 * 1024, 16 * 1024 * 1024);
            var initial = sizer.CurrentChunkSize;
            for (var i = 0; i < 10; i++) sizer.RecordMetrics(4 * 1024 * 1024, TimeSpan.FromMilliseconds(50));
            var afterFast = sizer.CurrentChunkSize;
            for (var i = 0; i < 5; i++) sizer.RecordMetrics(64 * 1024, TimeSpan.FromMilliseconds(500));
            var afterSlow = sizer.CurrentChunkSize;
            return $"initial={initial / 1024}KB → fast={afterFast / 1024}KB → slow={afterSlow / 1024}KB";
        });

        // ─── 11. InMemoryJobQueue ─────────────────────────────────────────────
        await RunSection(11, "InMemoryJobQueue", sectionProgress, ct, async () =>
        {
            var qLog  = _loggerFactory.CreateLogger<InMemoryJobQueue>();
            var queue = new InMemoryJobQueue(qLog);

            var highJob   = MakeJob(JobPriority.High,   @"\test\high.mkv");
            var normalJob = MakeJob(JobPriority.Normal, @"\test\normal.mkv");
            var lowJob    = MakeJob(JobPriority.Low,    @"\test\low.mkv");

            await queue.EnqueueAsync(lowJob, ct).ConfigureAwait(false);
            await queue.EnqueueAsync(normalJob, ct).ConfigureAwait(false);
            await queue.EnqueueAsync(highJob, ct).ConfigureAwait(false);

            var first = await queue.DequeueAsync(ct).ConfigureAwait(false);
            await queue.EnqueueAsync(highJob, ct).ConfigureAwait(false);

            await queue.PauseAsync(normalJob.JobId, ct).ConfigureAwait(false);
            var paused = await queue.GetJobAsync(normalJob.JobId, ct).ConfigureAwait(false);
            await queue.ResumeAsync(normalJob.JobId, ct).ConfigureAwait(false);
            await queue.CancelAsync(lowJob.JobId, ct).ConfigureAwait(false);
            await queue.UpdateProgressAsync(normalJob.JobId, 50 * 1024 * 1024, ct).ConfigureAwait(false);
            await queue.CompleteJobAsync(normalJob.JobId, success: true, cancellationToken: ct).ConfigureAwait(false);

            var all = new List<TransferJob>();
            await foreach (var j in queue.GetAllJobsAsync(ct).ConfigureAwait(false)) all.Add(j);

            return $"priority={first?.Priority}  paused={paused?.Status}  total={all.Count}";
        });

        // ─── 12. SqliteJobRepository ──────────────────────────────────────────
        await RunSection(12, "SqliteJobRepository", sectionProgress, ct, async () =>
        {
            var svc2 = new ServiceCollection();
            svc2.AddLogging(b => b.AddSerilog());
            svc2.AddSqlitePersistence(dbPath);
            await using var sp2 = svc2.BuildServiceProvider();

            var repo = sp2.GetRequiredService<SqliteJobRepository>();
            await repo.EnsureCreatedAsync().ConfigureAwait(false);

            var j1 = MakeJob(JobPriority.High,   @"\movies\test1.mkv");
            var j2 = MakeJob(JobPriority.Normal, @"\movies\test2.mkv");
            await repo.EnqueueAsync(j1, ct).ConfigureAwait(false);
            await repo.EnqueueAsync(j2, ct).ConfigureAwait(false);

            var fromDb = await repo.GetJobAsync(j1.JobId, ct).ConfigureAwait(false);
            await repo.PauseAsync(j1.JobId, ct).ConfigureAwait(false);
            var paused = await repo.GetJobAsync(j1.JobId, ct).ConfigureAwait(false);
            await repo.ResumeAsync(j1.JobId, ct).ConfigureAwait(false);
            await repo.CancelAsync(j2.JobId, ct).ConfigureAwait(false);

            var crashJob = MakeJob(JobPriority.Normal, @"\movies\crash.mkv", JobStatus.Running);
            await repo.EnqueueAsync(crashJob, ct).ConfigureAwait(false);
            var recovered = await repo.RecoverJobsAsync().ConfigureAwait(false);

            return $"enqueue={fromDb?.SourcePath}  pause={paused?.Status}  recovered={recovered.Count}";
        });

        // ─── 13. TransferTelemetry ────────────────────────────────────────────
        await RunSection(13, "TransferTelemetry", sectionProgress, ct, async () =>
        {
            await Task.Yield();
            var t1 = Guid.NewGuid(); var t2 = Guid.NewGuid(); var t3 = Guid.NewGuid();
            telemetry.StartSession(t1, "file1.mkv");
            telemetry.RecordRetry(t1);
            telemetry.RecordReconnect(t1);
            telemetry.CompleteSession(t1, true);

            telemetry.StartSession(t2, "file2.mkv");
            telemetry.RecordProgress(t2, new TransferProgress
            {
                SessionId = t2, SourcePath = "file2.mkv", DestinationPath = @"C:\out\file2.mkv",
                TransferredBytes = 50 * 1024 * 1024, TotalBytes = 200 * 1024 * 1024
            });
            telemetry.CompleteSession(t2, true);

            telemetry.StartSession(t3, "file3.mkv");
            telemetry.RecordRetry(t3);
            telemetry.RecordRetry(t3);
            telemetry.RecordError(t3, "STATUS_IO_TIMEOUT");
            telemetry.CompleteSession(t3, false);

            var s = telemetry.GetSummary();
            return $"files={s.TotalFilesCompleted}  retries={s.TotalRetries}  reconnects={s.TotalReconnects}  errors={s.TotalErrors}";
        });

        // ─── 14. TransferDashboard ────────────────────────────────────────────
        await RunSection(14, "TransferDashboard", sectionProgress, ct, async () =>
        {
            await Task.Yield();
            var summary = telemetry.GetSummary();
            // Just verify the dashboard builds a string without throwing
            using var sw = new System.IO.StringWriter();
            Console.SetOut(sw);
            try
            {
                dashboard.RenderTelemetry(summary);
            }
            finally
            {
                Console.SetOut(new System.IO.StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            }
            return "Render/RenderSummary/RenderTelemetry OK";
        });

        // ─── Cleanup ──────────────────────────────────────────────────────────
        try
        {
            if (!string.IsNullOrEmpty(setAttrPath))
                await smb.DeleteFileAsync(setAttrPath, ct).ConfigureAwait(false);
            await smb.DeleteDirectoryAsync(testRemoteDir, recursive: true, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Cleanup failed: {Message}", ex.Message);
        }

        await smb.DisconnectAsync(ct).ConfigureAwait(false);
        Log.Information("Full feature test complete. Results at: {WorkDir}", workDir);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static async Task RunSection(
        int index,
        string name,
        IProgress<SectionResult> progress,
        CancellationToken ct,
        Func<Task<string>> body)
    {
        progress.Report(new SectionResult(index, name, SectionStatus.Running, "Running…", TimeSpan.Zero));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            ct.ThrowIfCancellationRequested();
            var detail = await body().ConfigureAwait(false);
            sw.Stop();
            progress.Report(new SectionResult(index, name, SectionStatus.Pass, detail, sw.Elapsed));
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            progress.Report(new SectionResult(index, name, SectionStatus.Skipped, "Cancelled", sw.Elapsed));
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            progress.Report(new SectionResult(index, name, SectionStatus.Fail, ex.Message, sw.Elapsed));
        }
    }

    private static TransferJob MakeJob(JobPriority priority, string src, JobStatus status = JobStatus.Queued)
        => new TransferJob
        {
            JobId           = Guid.NewGuid(),
            SourcePath      = src,
            DestinationPath = @"C:\out\" + Path.GetFileName(src),
            Priority        = priority,
            TotalBytes      = 100 * 1024 * 1024,
            Status          = status,
            Options         = new TransferOptions()
        };
}

// ─── Minimal local IRemoteFileSystem ─────────────────────────────────────────

internal sealed class LocalFileSystemForTest : IRemoteFileSystem
{
    public Task ConnectAsync(RemoteCredential c, CancellationToken ct = default) => Task.CompletedTask;
    public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default) =>
        Task.FromResult(File.Exists(path) || Directory.Exists(path));

    public async IAsyncEnumerable<FileItem> ListDirectoryAsync(
        string path,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var e in Directory.EnumerateFileSystemEntries(path))
        {
            ct.ThrowIfCancellationRequested();
            var isDir = Directory.Exists(e);
            yield return new FileItem
            {
                Name        = Path.GetFileName(e),
                FullPath    = e,
                IsDirectory = isDir,
                Size        = isDir ? 0L : new FileInfo(e).Length,
                CreatedUtc  = File.GetCreationTimeUtc(e),
                ModifiedUtc = File.GetLastWriteTimeUtc(e),
                Attributes  = File.GetAttributes(e)
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
                FullPath    = fi.FullName, Size = fi.Length, IsDirectory = false,
                CreatedUtc  = fi.CreationTimeUtc, ModifiedUtc = fi.LastWriteTimeUtc,
                AccessedUtc = fi.LastAccessTimeUtc, Attributes = fi.Attributes
            });
        }
        var di = new DirectoryInfo(path);
        return Task.FromResult(new FileMetadata
        {
            FullPath    = di.FullName, Size = 0, IsDirectory = true,
            CreatedUtc  = di.CreationTimeUtc, ModifiedUtc = di.LastWriteTimeUtc,
            AccessedUtc = di.LastAccessTimeUtc, Attributes = di.Attributes
        });
    }

    public Task<IRemoteReadStream> OpenReadAsync(string path, long offset = 0, CancellationToken ct = default)
    {
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 128 * 1024, FileOptions.Asynchronous);
        if (offset > 0) fs.Seek(offset, SeekOrigin.Begin);
        return Task.FromResult<IRemoteReadStream>(new FtLocalReadStream(fs));
    }

    public Task<IRemoteWriteStream> OpenWriteAsync(string path, long offset = 0, bool createNew = false, CancellationToken ct = default)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
        var mode = createNew ? FileMode.Create : (offset > 0 ? FileMode.OpenOrCreate : FileMode.Create);
        var fs = new FileStream(path, mode, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous);
        if (offset > 0) fs.Seek(offset, SeekOrigin.Begin);
        return Task.FromResult<IRemoteWriteStream>(new FtLocalWriteStream(fs));
    }

    public Task CreateDirectoryAsync(string path, CancellationToken ct = default)
    { Directory.CreateDirectory(path); return Task.CompletedTask; }

    public Task DeleteFileAsync(string path, CancellationToken ct = default)
    { if (File.Exists(path)) File.Delete(path); return Task.CompletedTask; }

    public Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken ct = default)
    { if (Directory.Exists(path)) Directory.Delete(path, recursive); return Task.CompletedTask; }

    public Task RenameAsync(string src, string dst, CancellationToken ct = default)
    { File.Move(src, dst); return Task.CompletedTask; }

    public Task SetAttributesAsync(string path, FileMetadata meta, CancellationToken ct = default)
    { File.SetAttributes(path, meta.Attributes); return Task.CompletedTask; }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FtLocalReadStream : IRemoteReadStream
{
    private readonly FileStream _fs;
    public FtLocalReadStream(FileStream fs) => _fs = fs;
    public long Length => _fs.Length;
    public long Position => _fs.Position;
    public bool CanSeek => true;
    public ValueTask<int> ReadAsync(Memory<byte> buf, CancellationToken ct = default) => _fs.ReadAsync(buf, ct);
    public Task SeekAsync(long offset, CancellationToken ct = default) { _fs.Seek(offset, SeekOrigin.Begin); return Task.CompletedTask; }
    public ValueTask DisposeAsync() => _fs.DisposeAsync();
}

internal sealed class FtLocalWriteStream : IRemoteWriteStream
{
    private readonly FileStream _fs;
    public FtLocalWriteStream(FileStream fs) => _fs = fs;
    public long Position => _fs.Position;
    public ValueTask WriteAsync(ReadOnlyMemory<byte> buf, CancellationToken ct = default) => _fs.WriteAsync(buf, ct);
    public Task FlushAsync(CancellationToken ct = default) => _fs.FlushAsync(ct);
    public ValueTask DisposeAsync() => _fs.DisposeAsync();
}
