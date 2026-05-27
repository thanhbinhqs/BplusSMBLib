using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using SmbEnterprise.Core.Abstractions;
using SmbEnterprise.Core.Models;
using SmbEnterprise.Checksum;
using SmbEnterprise.Protocol.SMB;
using SmbEnterprise.Transfer;
using SmbEnterprise.Transfer.Abstractions;

namespace SmbEnterprise.MultiDestinationDemo;

/// <summary>
/// Demo các tính năng multi-destination transfer với progress tracking riêng
/// và xử lý slow connection.
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        // Setup logging
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File("logs/multi-destination-.txt", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        try
        {
            Console.WriteLine("=== SMB Multi-Destination Transfer Demo ===\n");

            // Configure DI
            var services = new ServiceCollection();
            services.AddLogging(b => b.AddSerilog(dispose: true));
            services.AddSmbProvider();
            services.AddSingleton<ITransferEngine, TransferEngine>();
            services.AddChecksumEngine(ChecksumAlgorithm.XxHash64);

            var serviceProvider = services.BuildServiceProvider();

            // Get configuration from user
            Console.Write("Nhập SMB Server (ví dụ: 192.168.1.100): ");
            var server = Console.ReadLine() ?? "192.168.1.100";

            Console.Write("Nhập Share Name: ");
            var share = Console.ReadLine() ?? "data";

            Console.Write("Nhập Username: ");
            var username = Console.ReadLine() ?? "user";

            Console.Write("Nhập Password: ");
            var password = ReadPassword();

            Console.Write("\nNhập số lượng destinations (2-5): ");
            var destCount = int.TryParse(Console.ReadLine(), out var count) ? Math.Clamp(count, 2, 5) : 3;

            Console.Write("Nhập source file path (UNC format, ví dụ: \\source\\file.bin): ");
            var sourcePath = Console.ReadLine() ?? @"\source\test.bin";

            // Setup credential
            var credential = new RemoteCredential
            {
                ServerName = server,
                ShareName = share,
                Domain = "WORKGROUP",
                Username = username,
                Password = password
            };

            var provider = serviceProvider.GetRequiredService<IFileSystemProvider>();
            var transferEngine = serviceProvider.GetRequiredService<ITransferEngine>();

            // Connect
            Console.WriteLine("\nConnecting to SMB share...");
            var sourceFs = await provider.CreateAsync(credential);
            await sourceFs.ConnectAsync(credential);
            Console.WriteLine("✓ Connected successfully!\n");

            // Demo menu
            while (true)
            {
                Console.WriteLine("\n=== Demo Menu ===");
                Console.WriteLine("1. Single File Multi-Destination Transfer (với progress riêng)");
                Console.WriteLine("2. Directory Multi-Destination Transfer");
                Console.WriteLine("3. Demo Slow Connection Handling");
                Console.WriteLine("4. Test với các SlowConnectionAction khác nhau");
                Console.WriteLine("0. Exit");
                Console.Write("\nChọn demo: ");

                var choice = Console.ReadLine();

                switch (choice)
                {
                    case "1":
                        await DemoSingleFileMultiDestAsync(transferEngine, sourcePath, destCount);
                        break;
                    case "2":
                        await DemoDirectoryMultiDestAsync(transferEngine);
                        break;
                    case "3":
                        await DemoSlowConnectionHandlingAsync(transferEngine, sourcePath, destCount);
                        break;
                    case "4":
                        await DemoSlowConnectionActionsAsync(transferEngine, sourcePath, destCount);
                        break;
                    case "0":
                        await sourceFs.DisconnectAsync();
                        return;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n✗ Error: {ex.Message}");
            Log.Error(ex, "Demo failed");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static async Task DemoSingleFileMultiDestAsync(
        ITransferEngine transferEngine,
        string sourcePath,
        int destCount)
    {
        Console.WriteLine("\n=== Demo 1: Single File Multi-Destination Transfer ===");

        // Generate destination paths
        var destinations = Enumerable.Range(1, destCount)
            .Select(i => $@"\backup{i}\{Path.GetFileName(sourcePath)}")
            .ToArray();

        Console.WriteLine($"\nSource: {sourcePath}");
        Console.WriteLine($"Destinations ({destinations.Length}):");
        foreach (var dest in destinations)
            Console.WriteLine($"  - {dest}");

        // Configure options
        var options = new TransferOptions
        {
            ChunkSize = 1 * 1024 * 1024,
            MaxChunkSize = 16 * 1024 * 1024,
            MinChunkSize = 64 * 1024,
            MaxParallelWorkers = 1,
            Resume = true,
            Overwrite = true,
            EnableReadAhead = true
        };

        // Configure slow policy
        var slowPolicy = new SlowConnectionPolicy
        {
            EnableSlowConnectionHandling = true,
            SlowSpeedThresholdPercent = 30.0,
            MinimumEvaluationDurationSeconds = 5,
            Action = SlowConnectionAction.Throttle,
            ThrottleMaxBytesPerSecond = 5 * 1024 * 1024,
            ContinueOnSlowConnectionFailure = true
        };

        // Progress callback
        var lastUpdate = DateTime.UtcNow;
        var progressReporter = new Progress<AggregatedMultiDestinationProgress>(aggProgress =>
        {
            // Update every 500ms
            if ((DateTime.UtcNow - lastUpdate).TotalMilliseconds < 500)
                return;
            lastUpdate = DateTime.UtcNow;

            Console.Clear();
            Console.WriteLine("=== Multi-Destination Transfer Progress ===\n");
            Console.WriteLine($"Source: {aggProgress.SourcePath}");
            Console.WriteLine($"Read: {FormatBytes(aggProgress.SourceBytesRead)} / {FormatBytes(aggProgress.TotalBytes)}");
            Console.WriteLine($"Speed: {aggProgress.SourceReadSpeedBytesPerSecond / (1024.0 * 1024.0):F2} MB/s");
            Console.WriteLine($"Elapsed: {aggProgress.Elapsed:hh\\:mm\\:ss}");
            Console.WriteLine($"Status: Completed={aggProgress.CompletedCount}/{aggProgress.DestinationProgresses.Count} | " +
                             $"Throttled={aggProgress.ThrottledCount} | Failed={aggProgress.FailedCount}");
            Console.WriteLine($"Avg Write Speed: {aggProgress.AverageWriteSpeedBytesPerSecond / (1024.0 * 1024.0):F2} MB/s");
            Console.WriteLine($"Slowest Write: {aggProgress.SlowestWriteSpeedBytesPerSecond / (1024.0 * 1024.0):F2} MB/s\n");

            // Progress table
            Console.WriteLine("┌─────┬────────────────────────────────────────┬──────────┬────────────┬───────────┬────────┐");
            Console.WriteLine("│ #   │ Destination                            │ Progress │ Speed      │ ETA       │ Status │");
            Console.WriteLine("├─────┼────────────────────────────────────────┼──────────┼────────────┼───────────┼────────┤");

            foreach (var destProgress in aggProgress.DestinationProgresses)
            {
                var status = destProgress.Stage switch
                {
                    TransferStage.Completed => "✓ DONE",
                    TransferStage.Failed => "✗ FAIL",
                    TransferStage.Writing => "→ XFER",
                    TransferStage.Connecting => "⋯ CONN",
                    _ => "⋯ WAIT"
                };

                if (destProgress.IsThrottled)
                    status += " [SLOW]";

                var dest = destProgress.DestinationPath.Length > 38
                    ? "..." + destProgress.DestinationPath[^35..]
                    : destProgress.DestinationPath;

                Console.WriteLine($"│ {destProgress.DestinationIndex,-3} │ {dest,-38} │ {destProgress.PercentComplete,7:F1}% │ " +
                                 $"{destProgress.SpeedBytesPerSecond / (1024.0 * 1024.0),7:F2} MB/s │ " +
                                 $"{(destProgress.Eta?.ToString(@"mm\:ss") ?? "N/A"),-9} │ {status,-13} │");

                if (!string.IsNullOrEmpty(destProgress.ErrorMessage))
                {
                    Console.WriteLine($"│     │ Error: {destProgress.ErrorMessage.Substring(0, Math.Min(destProgress.ErrorMessage.Length, 70)),-70} │");
                }
            }

            Console.WriteLine("└─────┴────────────────────────────────────────┴──────────┴────────────┴───────────┴────────┘");
        });

        Console.WriteLine("\nPress any key to start transfer...");
        Console.ReadKey(true);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await transferEngine.TransferMultiDestinationWithIndividualProgressAsync(
            sourcePath: sourcePath,
            destinationPaths: destinations,
            options: options,
            slowPolicy: slowPolicy,
            progress: progressReporter,
            cancellationToken: CancellationToken.None
        );
        sw.Stop();

        // Final results
        Console.WriteLine($"\n\n=== Transfer Complete ===");
        Console.WriteLine($"Duration: {sw.Elapsed:hh\\:mm\\:ss}");
        Console.WriteLine($"Total bytes transferred: {FormatBytes(result.BytesRead)}");
        Console.WriteLine($"Average speed: {result.BytesRead / sw.Elapsed.TotalSeconds / (1024.0 * 1024.0):F2} MB/s\n");

        Console.WriteLine("Results per destination:");
        foreach (var (dest, transferResult) in result.Results)
        {
            var status = transferResult.Success ? "✓ SUCCESS" : "✗ FAILED";
            Console.WriteLine($"  {status} {dest}");
            if (!transferResult.Success)
                Console.WriteLine($"           Error: {transferResult.ErrorMessage}");
        }

        Console.WriteLine("\nPress any key to continue...");
        Console.ReadKey(true);
    }

    private static async Task DemoDirectoryMultiDestAsync(ITransferEngine transferEngine)
    {
        Console.WriteLine("\n=== Demo 2: Directory Multi-Destination Transfer ===");

        Console.Write("\nNhập source directory (ví dụ: \\source\\documents): ");
        var sourceDir = Console.ReadLine() ?? @"\source\documents";

        Console.Write("Nhập số destinations (2-5): ");
        var destCount = int.TryParse(Console.ReadLine(), out var count) ? Math.Clamp(count, 2, 5) : 3;

        var destDirs = Enumerable.Range(1, destCount)
            .Select(i => $@"\backup{i}\documents")
            .ToArray();

        Console.WriteLine($"\nSource: {sourceDir}");
        Console.WriteLine($"Destinations ({destDirs.Length}):");
        foreach (var dest in destDirs)
            Console.WriteLine($"  - {dest}");

        var options = new TransferOptions
        {
            ChunkSize = 1 * 1024 * 1024,
            MaxChunkSize = 16 * 1024 * 1024,
            Resume = true,
            Overwrite = true
        };

        var slowPolicy = new SlowConnectionPolicy
        {
            EnableSlowConnectionHandling = true,
            Action = SlowConnectionAction.Throttle,
            SlowSpeedThresholdPercent = 30.0
        };

        var dirProgress = new Progress<DirectoryTransferProgress>(p =>
        {
            Console.Clear();
            Console.WriteLine("=== Directory Multi-Destination Transfer ===\n");
            Console.WriteLine($"Source: {p.SourceDirectory}");
            Console.WriteLine($"Files: {p.ProcessedFiles}/{p.TotalFiles} | Success: {p.SuccessfulFiles} | Failed: {p.FailedFiles}");
            Console.WriteLine($"Overall: {p.OverallPercentComplete:F1}% | {FormatBytes(p.TransferredBytes)}/{FormatBytes(p.TotalBytes)}");
            Console.WriteLine($"Elapsed: {p.Elapsed:hh\\:mm\\:ss}");

            if (!string.IsNullOrEmpty(p.CurrentFile))
            {
                Console.WriteLine($"\nCurrent file: {p.CurrentFile}");

                if (p.CurrentFileProgress != null)
                {
                    var fp = p.CurrentFileProgress;
                    Console.WriteLine($"  Read: {FormatBytes(fp.SourceBytesRead)}/{FormatBytes(fp.TotalBytes)} @ {fp.SourceReadSpeedBytesPerSecond / (1024.0 * 1024.0):F2} MB/s");

                    foreach (var dest in fp.DestinationProgresses.Take(5))
                    {
                        var status = dest.Stage == TransferStage.Writing ? "→" : (dest.Stage == TransferStage.Completed ? "✓" : "⋯");
                        Console.WriteLine($"  {status} [{dest.DestinationIndex}] {dest.PercentComplete:F1}% | {dest.SpeedBytesPerSecond / (1024.0 * 1024.0):F2} MB/s");
                    }
                }
            }

            if (p.CompletedFiles.Count > 0)
            {
                Console.WriteLine($"\nRecent completed files:");
                foreach (var file in p.CompletedFiles.TakeLast(5))
                {
                    var status = file.AllDestinationsSucceeded ? "✓" : "✗";
                    Console.WriteLine($"  {status} {Path.GetFileName(file.SourcePath)} ({file.SuccessfulDestinations}/{file.DestinationResults.Count})");
                }
            }
        });

        Console.WriteLine("\nPress any key to start transfer...");
        Console.ReadKey(true);

        var result = await transferEngine.TransferDirectoryMultiDestinationAsync(
            sourceDirectory: sourceDir,
            destinationDirectories: destDirs,
            options: options,
            slowPolicy: slowPolicy,
            progress: dirProgress,
            cancellationToken: CancellationToken.None
        );

        Console.WriteLine($"\n\n=== Directory Transfer Complete ===");
        Console.WriteLine($"Total files: {result.TotalFiles}");
        Console.WriteLine($"Successful: {result.SuccessfulFiles}");
        Console.WriteLine($"Failed: {result.FailedFiles}");
        Console.WriteLine($"Total bytes: {FormatBytes(result.TotalBytesTransferred)}");
        Console.WriteLine($"Duration: {result.Duration:hh\\:mm\\:ss}");
        Console.WriteLine($"Destinations: {result.DestinationCount}");

        Console.WriteLine("\nPress any key to continue...");
        Console.ReadKey(true);
    }

    private static async Task DemoSlowConnectionHandlingAsync(
        ITransferEngine transferEngine,
        string sourcePath,
        int destCount)
    {
        Console.WriteLine("\n=== Demo 3: Slow Connection Handling ===");
        Console.WriteLine("\nDemo này sẽ mô phỏng việc có một destination chậm hơn các destination khác.");

        var destinations = Enumerable.Range(1, destCount)
            .Select(i => $@"\backup{i}\{Path.GetFileName(sourcePath)}")
            .ToArray();

        var slowPolicy = new SlowConnectionPolicy
        {
            EnableSlowConnectionHandling = true,
            SlowSpeedThresholdPercent = 30.0,
            MinimumEvaluationDurationSeconds = 5,
            Action = SlowConnectionAction.Throttle,
            ThrottleMaxBytesPerSecond = 3 * 1024 * 1024, // 3 MB/s
            ContinueOnSlowConnectionFailure = true
        };

        Console.WriteLine("\nSlow Connection Policy:");
        Console.WriteLine($"  - Enabled: {slowPolicy.EnableSlowConnectionHandling}");
        Console.WriteLine($"  - Threshold: {slowPolicy.SlowSpeedThresholdPercent}% of average speed");
        Console.WriteLine($"  - Evaluation delay: {slowPolicy.MinimumEvaluationDurationSeconds}s");
        Console.WriteLine($"  - Action: {slowPolicy.Action}");
        Console.WriteLine($"  - Throttle max speed: {slowPolicy.ThrottleMaxBytesPerSecond / (1024.0 * 1024.0):F2} MB/s");

        var options = new TransferOptions
        {
            ChunkSize = 1 * 1024 * 1024,
            Resume = true,
            Overwrite = true
        };

        Console.WriteLine("\nKhi transfer bắt đầu, hệ thống sẽ:");
        Console.WriteLine("  1. Monitor tốc độ của tất cả destinations");
        Console.WriteLine("  2. Tính tốc độ trung bình");
        Console.WriteLine("  3. Phát hiện destination nào < 30% tốc độ trung bình");
        Console.WriteLine("  4. Throttle destination chậm về 3 MB/s để không ảnh hưởng nguồn đọc");

        Console.WriteLine("\nPress any key to start...");
        Console.ReadKey(true);

        var result = await transferEngine.TransferMultiDestinationWithIndividualProgressAsync(
            sourcePath: sourcePath,
            destinationPaths: destinations,
            options: options,
            slowPolicy: slowPolicy,
            progress: null,
            cancellationToken: CancellationToken.None
        );

        Console.WriteLine($"\n✓ Transfer completed!");
        Console.WriteLine("\nPress any key to continue...");
        Console.ReadKey(true);
    }

    private static async Task DemoSlowConnectionActionsAsync(
        ITransferEngine transferEngine,
        string sourcePath,
        int destCount)
    {
        Console.WriteLine("\n=== Demo 4: Slow Connection Actions ===");
        Console.WriteLine("\nDemo các action khác nhau khi phát hiện slow connection:\n");

        var destinations = Enumerable.Range(1, destCount)
            .Select(i => $@"\backup{i}\{Path.GetFileName(sourcePath)}")
            .ToArray();

        var options = new TransferOptions
        {
            ChunkSize = 1 * 1024 * 1024,
            Resume = true,
            Overwrite = true
        };

        var actions = new[]
        {
            SlowConnectionAction.LogOnly,
            SlowConnectionAction.Throttle,
            SlowConnectionAction.Pause,
            SlowConnectionAction.Skip
        };

        Console.WriteLine("Các action khả dụng:");
        Console.WriteLine("1. LogOnly - Chỉ log warning, không làm gì");
        Console.WriteLine("2. Throttle - Giới hạn tốc độ destination chậm");
        Console.WriteLine("3. Pause - Tạm dừng destination chậm");
        Console.WriteLine("4. Skip - Bỏ qua destination chậm hoàn toàn");

        Console.Write("\nChọn action (1-4): ");
        var choice = int.TryParse(Console.ReadLine(), out var c) ? Math.Clamp(c - 1, 0, 3) : 1;
        var selectedAction = actions[choice];

        var slowPolicy = new SlowConnectionPolicy
        {
            EnableSlowConnectionHandling = true,
            SlowSpeedThresholdPercent = 30.0,
            MinimumEvaluationDurationSeconds = 5,
            Action = selectedAction,
            ThrottleMaxBytesPerSecond = 3 * 1024 * 1024,
            ContinueOnSlowConnectionFailure = true
        };

        Console.WriteLine($"\n✓ Selected action: {selectedAction}");
        Console.WriteLine("\nPress any key to start transfer...");
        Console.ReadKey(true);

        var result = await transferEngine.TransferMultiDestinationWithIndividualProgressAsync(
            sourcePath: sourcePath,
            destinationPaths: destinations,
            options: options,
            slowPolicy: slowPolicy,
            progress: null,
            cancellationToken: CancellationToken.None
        );

        Console.WriteLine($"\n✓ Transfer completed with action: {selectedAction}");
        Console.WriteLine("\nPress any key to continue...");
        Console.ReadKey(true);
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:F2} {sizes[order]}";
    }

    private static string ReadPassword()
    {
        var password = string.Empty;
        ConsoleKey key;
        do
        {
            var keyInfo = Console.ReadKey(intercept: true);
            key = keyInfo.Key;

            if (key == ConsoleKey.Backspace && password.Length > 0)
            {
                Console.Write("\b \b");
                password = password[0..^1];
            }
            else if (!char.IsControl(keyInfo.KeyChar))
            {
                Console.Write("*");
                password += keyInfo.KeyChar;
            }
        } while (key != ConsoleKey.Enter);

        Console.WriteLine();
        return password;
    }
}
