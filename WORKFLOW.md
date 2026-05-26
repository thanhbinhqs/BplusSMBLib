# Workflow: Tìm folder trên server theo file .hash, copy về local, verify, rồi phát tán ra nhiều destination

Tài liệu này mô tả cách sử dụng SmbEnterprise để thực hiện quy trình sau:

1. Duyệt recursive toàn bộ cây thư mục trên server để lấy đầy đủ danh sách các folder thỏa rule của file `.hash`.
2. Copy cả folder đó từ SMB về local.
3. Verify các file local bằng hash đã có trong file `.hash`.
4. Copy từng file được liệt kê trong `.hash` đến nhiều destination.
5. Sau mỗi lần copy, tính lại checksum ở destination và so sánh với hash trong file `.hash`.
6. Nếu checksum đúng thì chuyển sang file tiếp theo.
7. Nếu checksum local hoặc checksum destination sai thì xóa file lỗi và copy lại tối đa 3 lần.
8. Nếu sau 3 lần vẫn fail thì dừng toàn bộ workflow và gửi thông báo lỗi.
9. Nếu toàn bộ file đều copy và checksum thành công thì gửi thông báo hoàn tất.

## 1. Phạm vi và giả định

Thư viện đã có sẵn các capability cần thiết sau:

- `IRemoteFileSystem` để trừu tượng hóa SMB, local disk hoặc storage khác.
- `ListDirectoryAsync()` để duyệt folder trên server.
- `OpenReadAsync()` để đọc nội dung file `.hash` và tính checksum từ stream.
- `GetMetadataAsync()` để lấy metadata của file.
- `TransferEngine.TransferDirectoryAsync()` để copy đệ quy cả thư mục đến một đích.
- `TransferEngine.TransferMultiDestinationAsync()` để copy một file đến nhiều đích cùng lúc.
- `IProgress<TransferProgress>` để báo tiến độ trong quá trình copy.
- `DeleteFileAsync()` để xóa file lỗi tại destination trước khi copy lại.

Thư viện hiện chưa có sẵn một API riêng cho:

- parse file `.hash` theo format nghiệp vụ cụ thể;
- tìm folder hợp lệ theo rule basename + extension + sự hiện diện đầy đủ file;
- copy "cả directory đến nhiều destination" trong một lệnh duy nhất;
- retry publish theo rule nghiệp vụ "checksum sai thì xóa file đích rồi copy lại tối đa 3 lần";
- cơ chế notification hoàn tất hoặc thất bại;
- progress riêng cho từng destination trong một lần gọi `TransferMultiDestinationAsync()`.

Điểm cuối cùng cần nói rõ: với API hiện tại, `TransferMultiDestinationAsync()` phù hợp để hiển thị progress cho một lần publish của một file ra nhiều đích. Nếu bắt buộc phải có progress tách riêng theo từng destination, ứng dụng cần:

- hoặc publish từng destination bằng `TransferAsync()`;
- hoặc mở rộng engine để emit progress per-destination.

## 2. Định nghĩa file `.hash`

Folder hợp lệ phải chứa đúng một file có phần mở rộng `.hash`. Nội dung file này có dạng:

```text
.wcl: HASH_WCL
.w01: HASH_W01
.w02: HASH_W02
...
.w0x: HASH_W0X
```

Ý nghĩa của file `.hash` trong workflow này:

- mỗi dòng ánh xạ một extension sang một checksum chuẩn;
- các file dữ liệu phải có cùng basename với file `.hash`;
- basename này áp dụng cho toàn bộ nhóm file, bao gồm cả chính file `.hash`.

Ví dụ, nếu trong folder có file `package.hash` thì các file được phép tham gia workflow phải là:

- `package.wcl`
- `package.w01`
- `package.w02`
- ...

và trong `package.hash` sẽ có nội dung như:

```text
.wcl: 9F6EAA4F7E8C1D6A7DF2F4E8A58A7DA2E18B4D7B45B4E9C38E0A1A998A13C0A1
.w01: FF3CC09B3D1A0DE418F4B6F81D9D8723A74CA7A8D88F11D104E91B50A28F9E6F
.w02: 8A3B4F919A838EF720F1C0FA1278540F1234567890ABCDEF1234567890ABCDEF
```

## 3. Logic xác định folder hợp lệ trên server

Một folder trên server được xem là hợp lệ khi đồng thời thỏa tất cả điều kiện sau:

1. Trong folder có đúng một file `.hash`.
2. Tên file `.hash` xác định basename chung cho cả nhóm file. Ví dụ `package.hash` thì basename là `package`.
3. Mỗi dòng trong file `.hash` phải có format `<extension>: <hash>`, trong đó extension có dạng `.wcl`, `.w01`, `.w02`, ...
4. Với mỗi extension trong file `.hash`, trong folder phải tồn tại đúng file tương ứng với cùng basename. Ví dụ `.w01` thì phải có `package.w01`.

Lưu ý: ở bước tìm kiếm này không cần checksum trên remote server. Việc checksum chỉ thực hiện sau khi copy về local.

Kết quả của bước tìm kiếm phải trả về danh sách các folder hợp lệ, trong đó mỗi phần tử gồm:

- `remoteFolderPath`: folder nguồn hợp lệ trên server;
- `hashFilePath`: đường dẫn đến file `.hash`;
- `manifestEntries`: danh sách file thực tế cần copy và checksum.

Người dùng hoặc tầng ứng dụng phía trên sẽ tự chọn một folder trong danh sách này theo logic riêng.

## 4. Kiến trúc đề xuất cho workflow

Nên dùng các đối tượng sau:

- `sourceSmbFs`: SMB chứa folder nguồn.
- `localFs`: local disk để staging và kiểm tra trung gian. Có thể dùng implementation mẫu tại `samples/SmbEnterprise.WinFormsApp/Services/LocalFileSystem.cs`.
- `destinationFileSystems`: một danh sách đích, mỗi đích là một `IRemoteFileSystem` riêng.
- `hashManifest`: dữ liệu parse từ file `.hash`.
- `notificationService`: thành phần ứng dụng dùng để gửi thông báo thành công hoặc thất bại.

## 5. Trình tự xử lý khuyến nghị

### Bước 1: Duyệt recursive để lấy đầy đủ danh sách folder hợp lệ trên server theo file `.hash`

Ứng dụng phải duyệt đệ quy toàn bộ cây thư mục trên SMB bắt đầu từ `remoteRoot`. Với mỗi folder ứng viên tìm được trong quá trình duyệt recursive, workflow sẽ thực hiện các bước:

1. Tìm file `.hash` trong folder.
2. Kiểm tra folder chỉ có đúng một file `.hash`.
3. Lấy basename từ file `.hash`.
4. Đọc nội dung file `.hash` và parse thành danh sách `<extension, checksum>`.
5. Từ mỗi extension, suy ra tên file đích theo công thức `<basename><extension>`.
6. Kiểm tra các file tương ứng có tồn tại trong folder.
7. Nếu folder có đủ các file theo danh sách trong `.hash` thì xem là folder hợp lệ.

Kết quả của bước này là một danh sách `candidateFolders` đầy đủ trong toàn bộ cây thư mục bên dưới `remoteRoot`, không phải chỉ các folder con trực tiếp ở cấp đầu tiên.

Workflow không tự chọn folder cuối cùng ở bước này. Thay vào đó:

1. tầng ứng dụng trả danh sách candidate folders ra UI hoặc lớp nghiệp vụ;
2. người dùng hoặc business logic riêng sẽ chọn một folder cụ thể;
3. folder được chọn sẽ trở thành `remoteFolderPath` để chạy các bước tiếp theo.

### Bước 2: Copy cả folder nguồn về local

Sau khi người dùng hoặc business logic chọn được `remoteFolderPath`, dùng `TransferEngine` với `sourceSmbFs` làm nguồn và `localFs` làm đích:

```csharp
var downloadEngine = new TransferEngine(
	sourceSmbFs,
	new IRemoteFileSystem[] { localFs },
	logger);

var downloadResult = await downloadEngine.TransferDirectoryAsync(
	sourceDirectory: selectedFolder.RemoteFolderPath,
	destinationDirectory: localFolderPath,
	options: new TransferOptions
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
```

### Bước 3: Verify các file local bằng file `.hash`

Sau khi copy xong về local, chỉ verify các file đã được liệt kê trong file `.hash`:

1. file local phải tồn tại;
2. `GetMetadataAsync()` phải trả về đúng `Size` nếu kích thước đã được biết;
3. tính checksum file local;
4. so checksum local với checksum tương ứng trong `.hash`.

Nếu có file local nào không khớp checksum thì không chuyển sang bước publish ngay. Thay vào đó:

1. xóa file local lỗi;
2. copy lại riêng file đó từ remote về local;
3. checksum lại;
4. lặp tối đa 3 lần cho từng file lỗi.

Nếu sau 3 lần file local vẫn sai checksum thì workflow dừng và gửi thông báo lỗi.

### Bước 4: Copy từng file đến nhiều destination

Không nên duyệt mù toàn bộ folder local. Nên duyệt đúng danh sách file đã parse từ `.hash`.

Với mỗi file trong manifest:

1. tạo path local tương ứng;
2. map sang danh sách destination path cho tất cả destination roots;
3. tạo progress reporter cho lần publish hiện tại;
4. gọi `TransferMultiDestinationAsync()`;
5. verify từng destination theo checksum trong `.hash`.

Ví dụ:

```csharp
foreach (var entry in selectedFolder.ManifestEntries)
{
	var localFile = Path.Combine(localFolderPath, entry.FileName);
	var destinationPaths = destinationRoots
		.Select(root => Path.Combine(root, entry.FileName))
		.ToArray();

	var progress = new Progress<TransferProgress>(p =>
	{
		Console.WriteLine($"[{entry.FileName}] {p.PercentComplete:F2}% - {p.BytesTransferred}/{p.TotalBytes} bytes");
	});

	await publishEngine.TransferMultiDestinationAsync(
		sourcePath: localFile,
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

### Bước 5: Verify từng destination, xóa file lỗi và retry tối đa 3 lần

Sau mỗi lần publish một file:

1. Lấy metadata tại từng destination bằng `GetMetadataAsync()`.
2. Tính checksum thực tế của file tại từng destination.
3. So checksum thực tế với checksum trong file `.hash`.
4. Nếu checksum sai ở bất kỳ destination nào, xóa file lỗi tại destination đó bằng `DeleteFileAsync()`.
5. Copy lại file đó.
6. Lặp tối đa 3 lần.
7. Nếu sau 3 lần vẫn còn lỗi, dừng toàn bộ workflow và gửi thông báo thất bại.

Để bám sát yêu cầu nghiệp vụ, hash chuẩn phải luôn lấy từ file `.hash`, không lấy từ checksum của bản local sau khi copy.

### Bước 6: Gửi thông báo kết quả workflow

Workflow cần có 2 loại thông báo:

- thông báo thất bại: gửi ngay khi không tìm được folder hợp lệ, local verify fail, hoặc publish fail sau 3 lần;
- thông báo thành công: gửi khi toàn bộ file trong `.hash` đã copy và verify thành công ở mọi destination.

## 6. Skeleton code để xây workflow end-to-end

Đoạn code dưới đây là skeleton để mô tả đúng thứ tự xử lý. Đây là workflow sample, không phải implementation production-ready hoàn chỉnh.

```csharp
using Microsoft.Extensions.Logging;
using SmbEnterprise.Checksum;
using SmbEnterprise.Core.Abstractions;
using SmbEnterprise.Core.Models;
using SmbEnterprise.Transfer;

public sealed record HashManifestEntry(
	string Extension,
	string FileName,
	string Checksum);

public sealed record FolderSearchResult(
	string RemoteFolderPath,
	string HashFilePath,
	IReadOnlyList<HashManifestEntry> ManifestEntries);

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
		FolderSearchResult selectedFolder,
		string localFolderPath,
		IReadOnlyList<string> destinationRoots,
		CancellationToken ct = default)
	{
		try
		{
			var downloadEngine = new TransferEngine(
				sourceSmbFs,
				new IRemoteFileSystem[] { localFs },
				_transferLogger);

			var downloadResult = await downloadEngine.TransferDirectoryAsync(
				selectedFolder.RemoteFolderPath,
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

			await VerifyLocalFilesWithRetryAsync(
				sourceSmbFs,
				localFs,
				selectedFolder.RemoteFolderPath,
				localFolderPath,
				selectedFolder.ManifestEntries,
				ct);

			var publishEngine = new TransferEngine(localFs, destinationFileSystems, _transferLogger);
			await PublishFilesAsync(
				localFs,
				destinationFileSystems,
				publishEngine,
				localFolderPath,
				destinationRoots,
				selectedFolder.ManifestEntries,
				ct);

			await _notificationService.NotifySuccessAsync("Toàn bộ file trong .hash đã copy và checksum thành công.", ct);
		}
		catch (Exception ex)
		{
			await _notificationService.NotifyFailureAsync($"Workflow thất bại: {ex.Message}", ct);
			throw;
		}
	}

	public async Task<IReadOnlyList<FolderSearchResult>> FindCandidateFoldersAsync(
		IRemoteFileSystem sourceSmbFs,
		string remoteRoot,
		CancellationToken ct)
	{
		var candidates = new List<FolderSearchResult>();

		// Duyệt đệ quy toàn bộ cây thư mục để lấy đầy đủ candidate folders.
		await foreach (var folder in EnumerateDirectoriesRecursiveAsync(sourceSmbFs, remoteRoot, ct))
		{
			var validation = await TryValidateHashFolderAsync(sourceSmbFs, folder.FullPath, ct);
			if (validation is not null)
			{
				candidates.Add(validation);
			}
		}

		return candidates;
	}

	private async Task<FolderSearchResult?> TryValidateHashFolderAsync(
		IRemoteFileSystem fs,
		string folderPath,
		CancellationToken ct)
	{
		var items = new List<FileItem>();
		await foreach (var item in fs.ListDirectoryAsync(folderPath, ct))
		{
			items.Add(item);
		}

		var hashFiles = items
			.Where(x => !x.IsDirectory && Path.GetExtension(x.Name).Equals(".hash", StringComparison.OrdinalIgnoreCase))
			.ToList();

		if (hashFiles.Count != 1)
		{
			return null;
		}

		var hashFile = hashFiles[0];
		var baseName = Path.GetFileNameWithoutExtension(hashFile.Name);
		var manifestEntries = await ParseHashManifestAsync(fs, hashFile.FullPath, baseName, ct);

		foreach (var entry in manifestEntries)
		{
			var fileItem = items.FirstOrDefault(x =>
				!x.IsDirectory &&
				x.Name.Equals(entry.FileName, StringComparison.OrdinalIgnoreCase));

			if (fileItem is null)
			{
				return null;
			}
		}

		return new FolderSearchResult(folderPath, hashFile.FullPath, manifestEntries);
	}

	public static FolderSearchResult SelectFolder(
		IReadOnlyList<FolderSearchResult> candidates,
		Func<IReadOnlyList<FolderSearchResult>, FolderSearchResult> selectionPolicy)
	{
		if (candidates.Count == 0)
		{
			throw new InvalidOperationException("Không có folder nào hợp lệ để chọn.");
		}

		return selectionPolicy(candidates);
	}

	private async Task<IReadOnlyList<HashManifestEntry>> ParseHashManifestAsync(
		IRemoteFileSystem fs,
		string hashFilePath,
		string baseName,
		CancellationToken ct)
	{
		var content = await ReadAllTextAsync(fs, hashFilePath, ct);
		var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
		var result = new List<HashManifestEntry>();

		foreach (var rawLine in lines)
		{
			var line = rawLine.Trim();
			var parts = line.Split(':', 2, StringSplitOptions.TrimEntries);
			if (parts.Length != 2)
			{
				throw new InvalidOperationException($"Dòng không hợp lệ trong file .hash: {line}");
			}

			var extension = parts[0];
			var checksum = parts[1];
			if (!extension.StartsWith('.', StringComparison.Ordinal))
			{
				throw new InvalidOperationException($"Extension không hợp lệ trong file .hash: {extension}");
			}

			result.Add(new HashManifestEntry(
				Extension: extension,
				FileName: baseName + extension,
				Checksum: checksum));
		}

		return result;
	}

	private async Task VerifyLocalFilesWithRetryAsync(
		IRemoteFileSystem sourceSmbFs,
		IRemoteFileSystem localFs,
		string remoteFolderPath,
		string localFolderPath,
		IReadOnlyList<HashManifestEntry> manifestEntries,
		CancellationToken ct)
	{
		foreach (var entry in manifestEntries)
		{
			var verified = false;

			for (int attempt = 1; attempt <= 3; attempt++)
			{
				var localFile = Path.Combine(localFolderPath, entry.FileName);
				var meta = await localFs.GetMetadataAsync(localFile, ct);
				var checksum = await ComputeChecksumAsync(localFs, localFile, meta.Size, ct);

				if (checksum.Equals(entry.Checksum, StringComparison.OrdinalIgnoreCase))
				{
					verified = true;
					break;
				}

				if (await localFs.ExistsAsync(localFile, ct))
				{
					await localFs.DeleteFileAsync(localFile, ct);
				}

				var remoteFile = Path.Combine(remoteFolderPath, entry.FileName);
				await CopySingleFileAsync(sourceSmbFs, remoteFile, localFs, localFile, ct);
			}

			if (!verified)
			{
				throw new InvalidOperationException($"Local checksum mismatch after 3 attempts for {entry.FileName}");
			}
		}
	}

	private async Task PublishFilesAsync(
		IRemoteFileSystem localFs,
		IReadOnlyList<IRemoteFileSystem> destinationFileSystems,
		TransferEngine publishEngine,
		string localFolderPath,
		IReadOnlyList<string> destinationRoots,
		IReadOnlyList<HashManifestEntry> manifestEntries,
		CancellationToken ct)
	{
		foreach (var entry in manifestEntries)
		{
			var localFile = Path.Combine(localFolderPath, entry.FileName);
			var success = false;

			for (int attempt = 1; attempt <= 3; attempt++)
			{
				var destinationPaths = destinationRoots
					.Select(root => Path.Combine(root, entry.FileName))
					.ToArray();

				var progress = new Progress<TransferProgress>(p =>
				{
					_logger.LogInformation(
						"Copying {File} attempt {Attempt}/3: {Percent:F2}% {Bytes}/{Total} bytes",
						entry.FileName,
						attempt,
						p.PercentComplete,
						p.BytesTransferred,
						p.TotalBytes);
				});

				var result = await publishEngine.TransferMultiDestinationAsync(
					localFile,
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
					await DeleteDestinationFilesAsync(destinationFileSystems, destinationPaths, ct);
					if (attempt == 3)
					{
						throw new InvalidOperationException($"Copy failed after 3 attempts for {entry.FileName}");
					}

					continue;
				}

				var verifyOk = await VerifyDestinationFilesAsync(
					destinationFileSystems,
					destinationPaths,
					entry,
					ct);

				if (verifyOk)
				{
					success = true;
					break;
				}

				await DeleteDestinationFilesAsync(destinationFileSystems, destinationPaths, ct);
				if (attempt == 3)
				{
					throw new InvalidOperationException($"Checksum failed after 3 attempts for {entry.FileName}");
				}
			}

			if (!success)
			{
				throw new InvalidOperationException($"Workflow stopped because file {entry.FileName} could not be published successfully.");
			}
		}
	}

	private async Task<bool> VerifyDestinationFilesAsync(
		IReadOnlyList<IRemoteFileSystem> destinationFileSystems,
		IReadOnlyList<string> destinationPaths,
		HashManifestEntry entry,
		CancellationToken ct)
	{
		for (int i = 0; i < destinationFileSystems.Count; i++)
		{
			var meta = await destinationFileSystems[i].GetMetadataAsync(destinationPaths[i], ct);
			var checksum = await ComputeChecksumAsync(destinationFileSystems[i], destinationPaths[i], meta.Size, ct);
			if (!checksum.Equals(entry.Checksum, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
		}

		return true;
	}

	private async Task DeleteDestinationFilesAsync(
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

	private static async Task CopySingleFileAsync(
		IRemoteFileSystem sourceFs,
		string sourcePath,
		IRemoteFileSystem destinationFs,
		string destinationPath,
		CancellationToken ct)
	{
		await using var sourceStream = await sourceFs.OpenReadAsync(sourcePath, 0, ct);
		await using var destinationStream = await destinationFs.OpenWriteAsync(destinationPath, 0, createNew: true, cancellationToken: ct);
		var buffer = new byte[128 * 1024];

		while (true)
		{
			var read = await sourceStream.ReadAsync(buffer, ct);
			if (read == 0)
			{
				break;
			}

			await destinationStream.WriteAsync(buffer.AsMemory(0, read), ct);
		}

		await destinationStream.FlushAsync(ct);
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

	private static async Task<string> ReadAllTextAsync(
		IRemoteFileSystem fs,
		string path,
		CancellationToken ct)
	{
		await using var stream = await fs.OpenReadAsync(path, 0, ct);
		using var ms = new MemoryStream();
		var buffer = new byte[16 * 1024];

		while (true)
		{
			var read = await stream.ReadAsync(buffer, ct);
			if (read == 0)
			{
				break;
			}

			await ms.WriteAsync(buffer.AsMemory(0, read), ct);
		}

		return System.Text.Encoding.UTF8.GetString(ms.ToArray());
	}

	private static async IAsyncEnumerable<FileItem> EnumerateDirectoriesRecursiveAsync(
		IRemoteFileSystem fs,
		string root,
		[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
	{
		await foreach (var entry in fs.ListDirectoryAsync(root, ct))
		{
			ct.ThrowIfCancellationRequested();

			if (!entry.IsDirectory)
			{
				continue;
			}

			yield return entry;

			await foreach (var child in EnumerateDirectoriesRecursiveAsync(fs, entry.FullPath, ct))
			{
				yield return child;
			}
		}
	}
}
```

## 7. Điểm cần chốt trong implementation thực tế

- Nếu trong folder có file ngoài nhóm basename đang xét, cần quyết định rõ là bỏ qua hay xem đó là invalid folder. Workflow ở đây chỉ yêu cầu các file được liệt kê trong `.hash` phải hợp lệ.
- Nếu file `.hash` có dòng trùng extension, nên coi đó là manifest lỗi và loại folder này.
- Nếu folder có rất nhiều file, nên tách logging và reporting theo cấp `file -> attempt -> destination`.
- Ở bước tìm kiếm folder trên server chỉ cần kiểm tra cấu trúc và sự hiện diện đầy đủ file, không checksum remote để tránh tốn thời gian quét ban đầu.
- Bước tìm kiếm nên trả về danh sách candidate folders để người dùng hoặc business logic riêng tự chọn, không nên hard-code chọn folder đầu tiên.
- Nếu local staging là thư mục tạm, nên xóa sau khi verify local và publish hoàn tất.
- Nếu một destination fail checksum, rule nghiệp vụ hiện tại là xóa file lỗi rồi retry; cần log rõ destination nào fail ở lần nào.
- Nếu file local fail checksum sau khi tải về, nên retry riêng file đó thay vì tải lại toàn bộ folder.
- Nếu cần idempotent workflow, nên lưu `hash file path`, `current file`, `attempt`, `destination status` vào database.
- Notification nên chứa tối thiểu: folder nguồn, file lỗi, destination lỗi, attempt cuối cùng và nguyên nhân.
- Nếu UI bắt buộc hiển thị progress riêng cho từng destination, không nên chỉ dựa vào `TransferMultiDestinationAsync()` như hiện tại.

## 8. Tóm tắt mapping giữa yêu cầu và API

- Lấy đầy đủ danh sách folder theo rule `.hash`: duyệt recursive bằng `ListDirectoryAsync()` + `OpenReadAsync()` + tự parse file `.hash` + kiểm tra sự hiện diện đầy đủ file.
- Chọn folder cuối cùng: người dùng hoặc tầng nghiệp vụ tự quyết định từ danh sách candidate folders.
- Copy folder về local: `TransferDirectoryAsync()`.
- Verify file local: `GetMetadataAsync()` + tự tính hash bằng `IChecksumEngine` + đối chiếu file `.hash` + retry riêng file lỗi tối đa 3 lần.
- Copy từng file lên nhiều destination: duyệt danh sách file từ `.hash` và gọi `TransferMultiDestinationAsync()`.
- Verify từng destination: `GetMetadataAsync()` + tự tính hash tại destination + đối chiếu file `.hash`.
- Retry khi checksum fail: `DeleteFileAsync()` + copy lại tối đa 3 lần.
- Gửi thông báo: ứng dụng tự tích hợp `INotificationService` hoặc cơ chế tương đương.
