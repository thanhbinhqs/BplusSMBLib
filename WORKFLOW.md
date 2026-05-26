# Workflow: Copy folder về local, verify theo checksum manifest, rồi phát tán từng file ra nhiều destination

Tài liệu này mô tả cách sử dụng SmbEnterprise để thực hiện quy trình sau:

1. Copy cả thư mục nguồn từ SMB về máy local.
2. Verify các file đã copy về bằng cách so sánh hash của file local với hash đã có trong file checksum manifest.
3. Copy từng file trong folder local đến nhiều destination.
4. Hiển thị progress cho quá trình copy đến từng destination.
5. Sau mỗi lần copy, tính hash checksum của file tại destination và so sánh với hash trong file checksum manifest.
6. Nếu checksum đúng thì chuyển sang file tiếp theo. Nếu checksum sai thì xóa file tương ứng tại destination và copy lại.
7. Lặp lại tối đa 3 lần cho mỗi file. Nếu vẫn fail thì dừng toàn bộ workflow và gửi thông báo lỗi.
8. Nếu toàn bộ file đều copy và checksum thành công thì gửi thông báo hoàn tất.

## 1. Phạm vi và giả định

Thư viện đã có sẵn các capability cần thiết sau:

- `IRemoteFileSystem` để trừu tượng hóa SMB, local disk hoặc storage khác.
- `TransferEngine.TransferDirectoryAsync()` để copy đệ quy cả thư mục đến một đích.
- `TransferEngine.TransferMultiDestinationAsync()` để copy một file đến nhiều đích cùng lúc.
- `TransferVerifier.VerifyAsync()` để đối chiếu size và checksum giữa nguồn và đích.
- `GetMetadataAsync()` để lấy metadata của file hay directory.
- `IProgress<TransferProgress>` để báo tiến độ trong quá trình copy.
- `DeleteFileAsync()` để xóa file lỗi tại destination trước khi copy lại.

Thư viện hiện chưa có sẵn một API riêng cho:

- format checksum manifest của cả thư mục;
- copy "cả directory đến nhiều destination" trong một lệnh duy nhất.
- retry publish theo rule nghiệp vụ "xóa file đích nếu checksum sai rồi copy lại tối đa 3 lần";
- cơ chế notification hoàn tất hoặc thất bại.

Vì vậy workflow đúng là:

- ứng dụng tự định nghĩa file checksum manifest, ví dụ `checksums.sha256.json`;
- copy folder về local bằng `TransferDirectoryAsync()`;
- verify file local bằng checksum trong manifest;
- phát tán từng file local bằng `TransferMultiDestinationAsync()`;
- sau mỗi lần publish, tự tính checksum ở từng destination và quyết định pass, retry hay fail toàn workflow.

## 2. Định nghĩa checksum manifest

Nên dùng một file manifest đặt bên trong folder nguồn, ví dụ:

- tên file: `checksums.sha256.json`
- nội dung: danh sách file tương đối, kích thước, timestamp mong đợi và checksum

Ví dụ:

```json
[
  {
	"relativePath": "subA/report-2026-05.csv",
	"size": 184320,
	"lastWriteUtc": "2026-05-27T01:15:22Z",
	"checksum": "9F6EAA4F7E8C1D6A7DF2F4E8A58A7DA2E18B4D7B45B4E9C38E0A1A998A13C0A1"
  },
  {
	"relativePath": "subB/readme.txt",
	"size": 128,
	"lastWriteUtc": "2026-05-27T01:16:10Z",
	"checksum": "FF3CC09B3D1A0DE418F4B6F81D9D8723A74CA7A8D88F11D104E91B50A28F9E6F"
  }
]
```

Manifest này là hợp đồng do ứng dụng sở hữu. Thư viện chỉ cần đọc stream của file manifest và checksum các file dữ liệu để đối chiếu.

## 3. Kiến trúc đề xuất cho workflow

Nên dùng 3 nhóm đối tượng theo vai trò:

- `sourceSmbFs`: SMB chứa folder nguồn.
- `localFs`: local disk để staging và kiểm tra trung gian. Có thể dùng implementation mẫu tại `samples/SmbEnterprise.WinFormsApp/Services/LocalFileSystem.cs`.
- `destinationFileSystems`: một danh sách đích, mỗi đích là một `IRemoteFileSystem` riêng, có thể là SMB khác hoặc local/network storage khác.
- `checksumManifest`: dữ liệu hash chuẩn của từng file trong folder.
- `notificationService`: thành phần ứng dụng dùng để gửi thông báo thành công hoặc thất bại.

## 4. Trình tự xử lý khuyến nghị

### Bước 1: Copy cả folder nguồn về local

Trước hết, ứng dụng cần biết sẵn:

- `remoteFolderPath`: path folder nguồn trên SMB;
- `manifestPath`: path tới file checksum manifest nằm trong folder nguồn hoặc đi kèm folder nguồn.

Dùng `TransferEngine` với `sourceSmbFs` làm nguồn và `localFs` làm đích:

```csharp
var downloadEngine = new TransferEngine(
	sourceSmbFs,
	new IRemoteFileSystem[] { localFs },
	logger);

var downloadResult = await downloadEngine.TransferDirectoryAsync(
	sourceDirectory: remoteFolderPath,
	destinationDirectory: localFolderPath,
	options: new TransferOptions
	{
		Resume = true,
		Overwrite = false,
		VerifyAfterCopy = false,
		ChecksumAlgorithm = ChecksumAlgorithm.Sha256
	},
	progress: progress,
	cancellationToken: ct);

if (!downloadResult.Success)
{
	throw new InvalidOperationException(downloadResult.ErrorMessage);
}
```

Lý do để `VerifyAfterCopy = false` trong bước này: ta cần verify theo file checksum manifest, nên sẽ thực hiện riêng ở bước kế tiếp.

### Bước 2: Verify các file local bằng file checksum manifest

Sau khi copy xong về local, ứng dụng đọc manifest rồi duyệt từng entry và kiểm tra:

1. file local có tồn tại;
2. `GetMetadataAsync()` trả về đúng `Size`;
3. nếu cần, đối chiếu `ModifiedUtc` hoặc field metadata khác theo quy ước nghiệp vụ;
4. tính checksum file local và so với `checksum` trong manifest.

Nếu có bất kỳ file local nào không khớp checksum manifest thì workflow phải dừng ngay và gửi thông báo lỗi, không chuyển sang bước publish.

### Bước 3: Copy từng file đến nhiều destination và hiển thị progress

`TransferMultiDestinationAsync()` hoạt động ở mức file. Vì vậy cần:

1. duyệt tất cả file trong `localFolderPath`;
2. tính `relativePath` của mỗi file so với root local;
3. map sang danh sách path đích tương ứng cho mỗi destination root;
4. tạo progress reporter để hiển thị tiến độ cho lần copy hiện tại;
5. gọi `TransferMultiDestinationAsync()` cho mỗi file.

Ví dụ, nếu có 3 destination root:

- `\\nas-a\drop\release-2026-05`
- `\\nas-b\archive\release-2026-05`
- `D:\offline-backup\release-2026-05`

thì với file local `C:\staging\release-2026-05\subA\report.csv`, danh sách destination path sẽ là:

- `\\nas-a\drop\release-2026-05\subA\report.csv`
- `\\nas-b\archive\release-2026-05\subA\report.csv`
- `D:\offline-backup\release-2026-05\subA\report.csv`

Và code sẽ có dạng:

```csharp
var publishEngine = new TransferEngine(
	localFs,
	destinationFileSystems,
	logger);

foreach (var file in filesToPublish)
{
	var relativePath = Path.GetRelativePath(localFolderPath, file);
	var destinationPaths = destinationRoots
		.Select(root => Path.Combine(root, relativePath))
		.ToArray();

	var progress = new Progress<TransferProgress>(p =>
	{
		Console.WriteLine($"[{relativePath}] {p.PercentComplete:F2}% - {p.BytesTransferred}/{p.TotalBytes} bytes - {p.SpeedBytesPerSecond / 1024.0 / 1024.0:F2} MB/s");
	});

	var result = await publishEngine.TransferMultiDestinationAsync(
		sourcePath: file,
		destinationPaths: destinationPaths,
		options: new TransferOptions
		{
			Resume = true,
			Overwrite = true,
			VerifyAfterCopy = false,
			ChecksumAlgorithm = ChecksumAlgorithm.Sha256
		},
		progress: progress,
		cancellationToken: ct);
}
```

Lưu ý: `destinationFileSystems.Count` phải khớp với `destinationPaths.Count` cho mỗi lần gọi.

### Bước 4: Verify từng destination, xóa file lỗi và retry tối đa 3 lần

Sau mỗi lần copy một file ra nhiều destination, workflow phải verify từng destination theo đúng checksum trong manifest:

1. Lấy metadata tại destination qua `GetMetadataAsync()`.
2. So `Size` với giá trị trong manifest.
3. Tính checksum file tại destination.
4. So checksum vừa tính với checksum trong manifest.
5. Nếu checksum sai, gọi `DeleteFileAsync(destinationPath)` để xóa file lỗi.
6. Copy lại file đó.
7. Lặp tối đa 3 lần.
8. Nếu sau 3 lần vẫn còn bất kỳ destination nào fail thì dừng toàn bộ workflow và gửi thông báo lỗi.

Điểm cần lưu ý ở bước này:

- Vì rule nghiệp vụ yêu cầu so với file checksum đã có, giá trị hash chuẩn phải lấy từ manifest, không lấy từ kết quả copy tạm thời.
- Nếu chỉ một destination fail checksum thì vẫn phải xem đây là fail của file hiện tại, xóa file lỗi ở destination đó và copy lại toàn bộ file cho nhóm destination của lần chạy đó, hoặc tách logic để retry riêng destination fail.
- Nếu muốn đúng sát yêu cầu và dễ kiểm soát hơn, nên retry riêng từng destination bị lỗi thay vì chạy lại toàn bộ nhóm destination đã pass.

### Bước 5: Gửi thông báo kết quả workflow

Workflow cần có 2 loại thông báo:

- thông báo thất bại: gửi ngay khi local verify fail hoặc một file fail checksum sau 3 lần publish;
- thông báo thành công: gửi khi toàn bộ file ở toàn bộ destination đã copy và verify thành công.

Thông báo có thể đi qua email, webhook, message queue hoặc UI notification, tùy ứng dụng tích hợp.

## 5. Skeleton code để xây workflow end-to-end

Đoạn code dưới đây là skeleton để triển khai service workflow. Mục tiêu là mô tả đúng thứ tự xử lý, không phải một sample UI hoàn chỉnh.

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmbEnterprise.Checksum;
using SmbEnterprise.Core.Abstractions;
using SmbEnterprise.Core.Models;
using SmbEnterprise.Transfer;

public sealed record ChecksumManifestEntry(
	string RelativePath,
	long Size,
	DateTimeOffset? LastWriteUtc,
	string Checksum);

public interface INotificationService
{
	Task NotifySuccessAsync(string message, CancellationToken ct = default);
	Task NotifyFailureAsync(string message, CancellationToken ct = default);
}

public sealed class FolderReplicationWorkflow
{
	private readonly ILogger<FolderReplicationWorkflow> _logger;
	private readonly ILogger<TransferEngine> _transferLogger;
	private readonly INotificationService _notificationService;
	private readonly IChecksumEngine _checksumEngine;

	public FolderReplicationWorkflow(
		ILogger<FolderReplicationWorkflow> logger,
		ILogger<TransferEngine> transferLogger,
		INotificationService notificationService,
		IChecksumEngine checksumEngine)
	{
		_logger = logger;
		_transferLogger = transferLogger;
		_notificationService = notificationService;
		_checksumEngine = checksumEngine;
	}

	public async Task ExecuteAsync(
		IRemoteFileSystem sourceSmbFs,
		IRemoteFileSystem localFs,
		IReadOnlyList<IRemoteFileSystem> destinationFileSystems,
		string remoteFolderPath,
		string manifestPath,
		string localFolderPath,
		IReadOnlyList<string> destinationRoots,
		CancellationToken ct = default)
	{
		try
		{
			var manifestEntries = await ReadManifestAsync(sourceSmbFs, manifestPath, ct);

			var downloadEngine = new TransferEngine(
				sourceSmbFs,
				new IRemoteFileSystem[] { localFs },
				_transferLogger);

			var downloadResult = await downloadEngine.TransferDirectoryAsync(
				remoteFolderPath,
				localFolderPath,
				new TransferOptions
				{
					Resume = true,
					Overwrite = true,
					VerifyAfterCopy = false,
					ChecksumAlgorithm = ChecksumAlgorithm.Sha256
				},
				cancellationToken: ct);

			if (!downloadResult.Success)
			{
				throw new InvalidOperationException(downloadResult.ErrorMessage);
			}

			await VerifyLocalFolderAsync(localFs, localFolderPath, manifestEntries, ct);

			var publishEngine = new TransferEngine(localFs, destinationFileSystems, _transferLogger);
			await PublishFolderAsync(localFs, destinationFileSystems, publishEngine, localFolderPath, destinationRoots, manifestEntries, ct);

			await _notificationService.NotifySuccessAsync("Toàn bộ file đã copy và checksum thành công.", ct);
		}
		catch (Exception ex)
		{
			await _notificationService.NotifyFailureAsync($"Workflow thất bại: {ex.Message}", ct);
			throw;
		}
	}

	private async Task VerifyLocalFolderAsync(
		IRemoteFileSystem localFs,
		string localFolderPath,
		IReadOnlyList<ChecksumManifestEntry> manifestEntries,
		CancellationToken ct)
	{
		foreach (var entry in manifestEntries)
		{
			var localFile = Path.Combine(localFolderPath, entry.RelativePath);

			var localMeta = await localFs.GetMetadataAsync(localFile, ct);
			if (localMeta.Size != entry.Size)
			{
				throw new InvalidOperationException($"Local size mismatch for {entry.RelativePath}");
			}

			var localChecksum = await ComputeChecksumAsync(localFs, localFile, localMeta.Size, ct);
			if (!localChecksum.Equals(entry.Checksum, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException($"Local checksum mismatch for {entry.RelativePath}");
			}
		}
	}

	private async Task PublishFolderAsync(
		IRemoteFileSystem localFs,
		IReadOnlyList<IRemoteFileSystem> destinationFileSystems,
		TransferEngine publishEngine,
		string localFolderPath,
		IReadOnlyList<string> destinationRoots,
		IReadOnlyList<ChecksumManifestEntry> manifestEntries,
		CancellationToken ct)
	{
		var manifestByPath = manifestEntries.ToDictionary(x => x.RelativePath, StringComparer.OrdinalIgnoreCase);

		foreach (var file in Directory.EnumerateFiles(localFolderPath, "*", SearchOption.AllDirectories))
		{
			var relativePath = Path.GetRelativePath(localFolderPath, file);
			if (!manifestByPath.TryGetValue(relativePath, out var manifestEntry))
			{
				throw new InvalidOperationException($"Không tìm thấy checksum manifest cho file {relativePath}");
			}

			var success = false;

			for (int attempt = 1; attempt <= 3; attempt++)
			{
			var destinationPaths = destinationRoots
				.Select(root => Path.Combine(root, relativePath))
				.ToArray();

				var progress = new Progress<TransferProgress>(p =>
				{
					_logger.LogInformation("Copying {File} attempt {Attempt}/3: {Percent:F2}% {Bytes}/{Total} bytes Speed={Speed:F2} MB/s",
						relativePath,
						attempt,
						p.PercentComplete,
						p.BytesTransferred,
						p.TotalBytes,
						p.SpeedBytesPerSecond / 1024.0 / 1024.0);
				});

				var result = await publishEngine.TransferMultiDestinationAsync(
					file,
					destinationPaths,
					new TransferOptions
					{
						Resume = true,
						Overwrite = true,
						VerifyAfterCopy = false,
						ChecksumAlgorithm = ChecksumAlgorithm.Sha256
					},
					progress: progress,
					cancellationToken: ct);

				if (result.Results.Any(x => !x.transferResult.Success))
				{
					await DeleteFailedDestinationsAsync(destinationFileSystems, destinationPaths, ct);
					if (attempt == 3)
					{
						throw new InvalidOperationException($"Copy failed after 3 attempts for {relativePath}");
					}

					continue;
				}

				var verifyOk = await VerifyPublishedFileAsync(
					destinationFileSystems,
					destinationPaths,
					manifestEntry,
					ct);

				if (verifyOk)
				{
					success = true;
					break;
				}

				await DeleteFailedDestinationsAsync(destinationFileSystems, destinationPaths, ct);
				if (attempt == 3)
				{
					throw new InvalidOperationException($"Checksum failed after 3 attempts for {relativePath}");
				}
			}

			if (!success)
			{
				throw new InvalidOperationException($"Workflow stopped because file {relativePath} could not be published successfully.");
			}
		}
	}

	private async Task<bool> VerifyPublishedFileAsync(
		IReadOnlyList<IRemoteFileSystem> destinationFileSystems,
		IReadOnlyList<string> destinationPaths,
		ChecksumManifestEntry manifestEntry,
		CancellationToken ct)
	{
		for (int i = 0; i < destinationFileSystems.Count; i++)
		{
			var destinationPath = destinationPaths[i];
			var destinationMeta = await destinationFileSystems[i].GetMetadataAsync(destinationPath, ct);

			if (destinationMeta.Size != manifestEntry.Size)
			{
				return false;
			}

			var destinationChecksum = await ComputeChecksumAsync(destinationFileSystems[i], destinationPath, destinationMeta.Size, ct);
			if (!destinationChecksum.Equals(manifestEntry.Checksum, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
		}

		return true;
	}

	private async Task DeleteFailedDestinationsAsync(
		IReadOnlyList<IRemoteFileSystem> destinationFileSystems,
		IReadOnlyList<string> destinationPaths,
		CancellationToken ct)
	{
		for (int i = 0; i < destinationFileSystems.Count; i++)
		{
			try
			{
				if (await destinationFileSystems[i].ExistsAsync(destinationPaths[i], ct))
				{
					await destinationFileSystems[i].DeleteFileAsync(destinationPaths[i], ct);
				}
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Không thể xóa file lỗi tại destination {DestinationPath}", destinationPaths[i]);
			}
		}
	}

	private async Task<string> ComputeChecksumAsync(
		IRemoteFileSystem fs,
		string filePath,
		long fileSize,
		CancellationToken ct)
	{
		await using var stream = await fs.OpenReadAsync(filePath, 0, ct);

		var checksum = await _checksumEngine.ComputeFileAsync(
			filePath,
			async (offset, buffer, token) =>
			{
				await stream.SeekAsync(offset, token);
				return await stream.ReadAsync(buffer, token);
			},
			fileSize,
			ct);

		return checksum.HexHash;
	}

	private static async Task<IReadOnlyList<ChecksumManifestEntry>> ReadManifestAsync(
		IRemoteFileSystem fs,
		string manifestPath,
		CancellationToken ct)
	{
		await using var stream = await fs.OpenReadAsync(manifestPath, 0, ct);
		using var ms = new MemoryStream();
		var buffer = new byte[64 * 1024];

		while (true)
		{
			var read = await stream.ReadAsync(buffer, ct);
			if (read == 0)
			{
				break;
			}

			await ms.WriteAsync(buffer.AsMemory(0, read), ct);
		}

		ms.Position = 0;
		return await JsonSerializer.DeserializeAsync<List<ChecksumManifestEntry>>(ms, cancellationToken: ct)
			?? throw new InvalidOperationException("Manifest không hợp lệ.");
	}

}
```

## 6. Điểm cần chốt trong implementation thực tế

- Nếu folder có rất nhiều file, nên tách logging và reporting theo cấp `file -> attempt -> destination`.
- Nếu local staging là thư mục tạm, nên xóa sau khi verify local và publish hoàn tất.
- Nếu một destination fail checksum, rule nghiệp vụ hiện tại là xóa file lỗi rồi retry; cần log rõ destination nào fail ở lần nào.
- Nếu cần idempotent workflow, nên lưu `manifest hash`, `current file`, `attempt`, `destination status` vào database.
- Nếu timestamp giữa các filesystem không ổn định, nên xem `Size + checksum` là tiêu chí chính, timestamp chỉ là thông tin phụ.
- Notification nên chứa tối thiểu: file lỗi, destination lỗi, attempt cuối cùng và nguyên nhân.

## 7. Tóm tắt mapping giữa yêu cầu và API

- Copy folder về local: `TransferDirectoryAsync()`.
- Verify file local: `GetMetadataAsync()` + tự tính hash bằng `IChecksumEngine` + đối chiếu manifest.
- Copy từng file lên nhiều destination: duyệt từng file local và gọi `TransferMultiDestinationAsync()` + `IProgress<TransferProgress>`.
- Verify từng destination: `GetMetadataAsync()` + tự tính hash tại destination + đối chiếu manifest.
- Retry khi checksum fail: `DeleteFileAsync()` + copy lại tối đa 3 lần.
- Gửi thông báo: ứng dụng tự tích hợp `INotificationService` hoặc cơ chế tương đương.
