# SmbEnterprise - Thư viện truyền file SMB cho .NET 8

SmbEnterprise là thư viện truyền file qua giao thức SMB được xây dựng cho .NET 8, tập trung vào ba mục tiêu chính: hiệu năng, độ ổn định và khả năng tích hợp vào hệ thống doanh nghiệp. Thư viện bọc SMBLibrary phía dưới một lớp trừu tượng rõ ràng để ứng dụng tiêu thụ không cần phụ thuộc trực tiếp vào API SMB mức thấp.

Thư viện phù hợp cho các bài toán như:

- Đồng bộ file từ SMB share về máy local hoặc một đích khác.
- Sao chép file dung lượng lớn trên mạng không ổn định.
- Xây dựng dịch vụ nền, ứng dụng desktop hoặc công cụ nội bộ cần truy cập SMB an toàn và có thể mở rộng.
- Quản lý hàng đợi tác vụ truyền file, resume khi gián đoạn và kiểm tra toàn vẹn dữ liệu sau khi copy.

---

## 1. Điểm nổi bật

- Tái sử dụng kết nối SMB bằng session pool.
- Tự động retry và reconnect khi gặp lỗi tạm thời.
- Tối ưu tốc độ truyền bằng adaptive chunk sizing.
- Hỗ trợ truyền một nguồn đến nhiều đích trong một lần đọc.
- Resume khi file đích đã tồn tại một phần.
- Kiểm tra checksum sau khi copy với nhiều thuật toán.
- Có hàng đợi job trong bộ nhớ và persistence bằng SQLite.
- Có telemetry và dashboard để theo dõi tiến độ truyền.

---

## 2. Kiến trúc tổng thể

Thư viện được chia thành các module độc lập để dễ thay thế và tái sử dụng:

```text
src/
  SmbEnterprise.Core/            Abstraction, model, result, path
  SmbEnterprise.Protocol.SMB/    Kết nối SMB, session pool, retry, provider
  SmbEnterprise.Transfer/        TransferEngine, pipeline, adaptive chunk
  SmbEnterprise.Checksum/        Checksum engine, verifier
  SmbEnterprise.Diagnostics/     Telemetry, dashboard
  SmbEnterprise.Jobs/            Hàng đợi job, scheduler
  SmbEnterprise.Persistence/     Lưu job bằng SQLite / EF Core
```

Luồng phụ thuộc chính:

```text
Application
	-> SmbEnterprise.Core
	-> SmbEnterprise.Protocol.SMB
	-> SmbEnterprise.Transfer
	-> SmbEnterprise.Checksum (tùy chọn)
	-> SmbEnterprise.Diagnostics (tùy chọn)
	-> SmbEnterprise.Jobs + Persistence (tùy chọn)
```

Thiết kế này giúp phần application chỉ làm việc với abstraction như `IRemoteFileSystem`, thay vì gọi trực tiếp SMBLibrary.

---

## 3. Các thành phần chính

### 3.1 SmbEnterprise.Core

Đây là lớp nền tảng của toàn bộ thư viện, chứa:

- `IRemoteFileSystem`: abstraction cho một file system từ xa hoặc cục bộ.
- `IFileSystemProvider`: factory để tạo file system cụ thể.
- `TransferOptions`, `TransferProgress`, `TransferResult`: model cho quá trình truyền file.
- `SmbPath`, `Result<T>` và các kiểu hỗ trợ khác.

`IRemoteFileSystem` là hợp đồng quan trọng nhất. Nếu muốn dùng thư viện với một đích khác ngoài SMB, bạn chỉ cần hiện thực interface này.

### 3.2 SmbEnterprise.Protocol.SMB

Module này hiện thực `IRemoteFileSystem` cho SMB. Nó chịu trách nhiệm:

- Quản lý kết nối đến server/share.
- Tái sử dụng session qua pool.
- Tự reconnect khi session bị rớt.
- Đọc/ghi file thông qua SMB stream.
- Cung cấp `AddSmbProvider()` để đăng ký DI.

### 3.3 SmbEnterprise.Transfer

Chứa `TransferEngine`, là thành phần điều phối việc truyền file:

- Lấy metadata file nguồn.
- Kiểm tra resume offset nếu file đích đã tồn tại.
- Tạo pipeline đọc và ghi theo chunk.
- Báo tiến độ qua `IProgress<TransferProgress>`.
- Hỗ trợ truyền thư mục và multi-destination.

### 3.4 SmbEnterprise.Checksum

Chứa các thuật toán checksum:

- `XxHash64`
- `CRC32`
- `SHA256`
- `MD5`

Ngoài ra có `TransferVerifier` để đối chiếu checksum giữa nguồn và đích sau khi truyền xong.

### 3.5 SmbEnterprise.Diagnostics

Phục vụ theo dõi hệ thống:

- Thống kê số byte đã truyền.
- Theo dõi retry, reconnect, lỗi.
- Hiển thị dashboard hoặc console progress.

### 3.6 SmbEnterprise.Jobs và Persistence

Hai module này dùng khi bạn cần quản lý job dài hạn:

- `InMemoryJobQueue`: hàng đợi ưu tiên trong bộ nhớ.
- `JobScheduler`: xử lý job theo lịch.
- `SqliteJobRepository`: lưu trạng thái job vào SQLite.
- `AddSqlitePersistence()`: đăng ký DbContextFactory và repository.

---

## 4. Cách thư viện hoạt động

Với một tác vụ truyền file SMB -> đích khác, luồng xử lý điển hình như sau:

1. Ứng dụng tạo `IRemoteFileSystem` cho nguồn SMB.
2. Ứng dụng tạo `IRemoteFileSystem` cho đích.
3. `TransferEngine` đọc metadata để biết kích thước file.
4. Nếu bật `Resume`, engine kiểm tra file đích đã có bao nhiêu byte.
5. Pipeline đọc dữ liệu theo chunk từ nguồn.
6. Chunk được ghi ra một hoặc nhiều đích.
7. Tiến độ được đẩy về callback `IProgress<TransferProgress>`.
8. Nếu bật verify, `TransferVerifier` đọc lại và so sánh checksum.

Điểm quan trọng là phần application không cần biết cách SMB stream hoạt động, chỉ cần điều khiển bằng interface và options.

---

## 5. Cài đặt và tham chiếu từ dự án khác

Hiện tại repository được tổ chức theo dạng project library, nên cách dùng phổ biến nhất là tham chiếu project trực tiếp từ solution khác.

### 5.1 Yêu cầu

- .NET 8 SDK
- Có quyền truy cập SMB share
- Windows, Samba hoặc NAS hỗ trợ SMB2/SMB3

### 5.2 Thêm project reference

Trong file `.csproj` của dự án sử dụng thư viện, thêm các reference cần thiết:

```xml
<ItemGroup>
  <ProjectReference Include="..\Bplus\src\SmbEnterprise.Core\SmbEnterprise.Core.csproj" />
  <ProjectReference Include="..\Bplus\src\SmbEnterprise.Protocol.SMB\SmbEnterprise.Protocol.SMB.csproj" />
  <ProjectReference Include="..\Bplus\src\SmbEnterprise.Transfer\SmbEnterprise.Transfer.csproj" />
  <ProjectReference Include="..\Bplus\src\SmbEnterprise.Checksum\SmbEnterprise.Checksum.csproj" />
  <ProjectReference Include="..\Bplus\src\SmbEnterprise.Diagnostics\SmbEnterprise.Diagnostics.csproj" />
</ItemGroup>
```

Nếu bạn cần job persistence thì thêm:

```xml
<ItemGroup>
  <ProjectReference Include="..\Bplus\src\SmbEnterprise.Jobs\SmbEnterprise.Jobs.csproj" />
  <ProjectReference Include="..\Bplus\src\SmbEnterprise.Persistence\SmbEnterprise.Persistence.csproj" />
</ItemGroup>
```

Bạn không bắt buộc phải tham chiếu toàn bộ module. Có thể chọn đúng phần mình dùng.

---

## 6. Đăng ký Dependency Injection

Ví dụ cấu hình DI trong một ứng dụng console hoặc worker service:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmbEnterprise.Checksum;
using SmbEnterprise.Core.Models;
using SmbEnterprise.Persistence;
using SmbEnterprise.Protocol.SMB;

var services = new ServiceCollection();

services.AddLogging(builder => builder.AddConsole());

services.AddSmbProvider(options =>
{
	// Tùy chọn pool nếu cần cấu hình riêng.
});

services.AddChecksumEngine(ChecksumAlgorithm.XxHash64);
services.AddSqlitePersistence("smbjobs.db");

var serviceProvider = services.BuildServiceProvider();
```

Trong trường hợp bạn chưa cần checksum hoặc persistence, có thể bỏ qua hai dòng tương ứng.

---

## 7. Kết nối tới SMB share

Sau khi đăng ký DI, bạn lấy `IFileSystemProvider` để tạo một file system SMB:

```csharp
using SmbEnterprise.Core.Abstractions;
using SmbEnterprise.Core.Models;

var provider = serviceProvider.GetRequiredService<IFileSystemProvider>();
await using var smbFs = provider.CreateFileSystem();

await smbFs.ConnectAsync(new RemoteCredential
{
	Server = "192.168.1.100",
	Share = "data",
	Username = "alice",
	Password = "secret"
});
```

Sau khi dùng xong, gọi `DisconnectAsync()` hoặc để `await using` tự dispose.

---

## 8. Các thao tác file cơ bản

Sau khi đã có `IRemoteFileSystem`, bạn có thể làm việc với SMB share mà không cần gọi API SMB mức thấp.

### 8.1 Liệt kê thư mục

```csharp
await foreach (var item in smbFs.ListDirectoryAsync(@"\documents"))
{
	Console.WriteLine($"{item.Name} | Dir={item.IsDirectory} | Size={item.Size}");
}
```

### 8.2 Lấy metadata

```csharp
var metadata = await smbFs.GetMetadataAsync(@"\documents\report.pdf");
Console.WriteLine($"{metadata.FullPath} - {metadata.Size} bytes");
```

### 8.3 Tạo thư mục, đổi tên, xóa

```csharp
await smbFs.CreateDirectoryAsync(@"\backup\2026");
await smbFs.RenameAsync(@"\backup\old.txt", @"\backup\new.txt");
await smbFs.DeleteFileAsync(@"\backup\temp.txt");
await smbFs.DeleteDirectoryAsync(@"\backup\obsolete", recursive: true);
```

### 8.4 Đọc và ghi stream mức thấp

```csharp
await using var readStream = await smbFs.OpenReadAsync(@"\video\movie.mkv");
var buffer = new byte[64 * 1024];
var bytesRead = await readStream.ReadAsync(buffer);
```

```csharp
await using var writeStream = await smbFs.OpenWriteAsync(@"\upload\output.bin", createNew: true);
await writeStream.WriteAsync(buffer.AsMemory(0, bytesRead));
await writeStream.FlushAsync();
```

---

## 9. Truyền file bằng TransferEngine

Đây là cách dùng phổ biến nhất của thư viện.

`TransferEngine` nhận:

- Một `IRemoteFileSystem` nguồn.
- Một danh sách `IRemoteFileSystem` đích.
- `ILogger<TransferEngine>`.

Ví dụ:

```csharp
using Microsoft.Extensions.Logging;
using SmbEnterprise.Core.Models;
using SmbEnterprise.Transfer;

var logger = serviceProvider.GetRequiredService<ILogger<TransferEngine>>();

var engine = new TransferEngine(
	sourceFs: smbFs,
	destinationFs: new[] { destinationFs },
	logger: logger);

var options = new TransferOptions
{
	ChunkSize = 256 * 1024,
	MinChunkSize = 64 * 1024,
	MaxChunkSize = 4 * 1024 * 1024,
	MaxParallelWorkers = 4,
	MaxChunkRetries = 5,
	VerifyAfterCopy = true,
	ChecksumAlgorithm = ChecksumAlgorithm.XxHash64,
	Resume = true,
	Overwrite = false,
	EnableReadAhead = true
};

var progress = new Progress<TransferProgress>(p =>
{
	Console.WriteLine($"{p.PercentComplete:F2}% - {p.BytesTransferred} bytes");
});

var result = await engine.TransferAsync(
	sourcePath: @"\input\large-file.zip",
	destinationPath: @"D:\Downloads\large-file.zip",
	options: options,
	progress: progress,
	cancellationToken: CancellationToken.None);

if (result.Success)
{
	Console.WriteLine($"Truyền thành công: {result.BytesTransferred} bytes");
}
else
{
	Console.WriteLine($"Truyền thất bại: {result.ErrorMessage}");
}
```

---

## 10. Truyền file sang nhiều đích

Nếu bạn muốn đọc một lần từ SMB và ghi ra nhiều đích, có thể dùng `TransferMultiDestinationAsync`.

```csharp
var multiResult = await engine.TransferMultiDestinationAsync(
	sourcePath: @"\releases\package.zip",
	destinationPaths: new[]
	{
		@"D:\DropA\package.zip",
		@"E:\DropB\package.zip"
	},
	options: options,
	progress: progress);

foreach (var (destination, transferResult) in multiResult.Results)
{
	Console.WriteLine($"{destination}: success={transferResult.Success}");
}
```

Kịch bản này hữu ích khi cần phát tán cùng một file ra nhiều nơi mà không muốn đọc lại file nguồn nhiều lần.

---

## 11. Truyền cả thư mục

`TransferEngine` cũng hỗ trợ truyền thư mục theo kiểu đệ quy:

```csharp
var directoryResult = await engine.TransferDirectoryAsync(
	sourceDirectory: @"\source-folder",
	destinationDirectory: @"D:\Backup\source-folder",
	options: options,
	progress: progress);
```

Engine sẽ duyệt thư mục nguồn, tạo thư mục đích nếu cần và truyền từng file con.

---

## 12. Kiểm tra toàn vẹn dữ liệu

Bạn có hai cách kiểm tra checksum:

### 12.1 Bật verify trực tiếp trong TransferOptions

```csharp
var options = new TransferOptions
{
	VerifyAfterCopy = true,
	ChecksumAlgorithm = ChecksumAlgorithm.Sha256
};
```

### 12.2 Gọi TransferVerifier thủ công

Điều này hữu ích khi bạn muốn verify ở một bước riêng.

```csharp
using SmbEnterprise.Checksum;

var verifier = serviceProvider.GetRequiredService<TransferVerifier>();

var sourceMeta = await smbFs.GetMetadataAsync(@"\input\data.bin");

var verifyResult = await verifier.VerifyAsync(
	sourceFs: smbFs,
	sourcePath: @"\input\data.bin",
	destFs: destinationFs,
	destinationPath: @"D:\Downloads\data.bin",
	expectedSize: sourceMeta.Size);

if (verifyResult.IsValid)
{
	Console.WriteLine($"Checksum hop le: {verifyResult.Hash}");
}
else
{
	Console.WriteLine($"Checksum loi: {verifyResult.ErrorMessage}");
}
```

---

## 13. Cách dùng trong một dự án khác

Đây là phần quan trọng nhất nếu bạn muốn nhúng thư viện vào ứng dụng riêng.

### 13.1 Trường hợp 1: SMB là nguồn, local disk là đích

Thư viện cốt lõi đã có `SmbFileSystem` cho SMB. Với local disk, repository hiện có một implementation mẫu `LocalFileSystem` trong sample WinForms, hiện thực `IRemoteFileSystem` bằng `System.IO`.

Trong dự án của bạn, bạn có thể:

- Sao chép implementation `LocalFileSystem` từ sample và đưa vào project riêng.
- Hoặc tự viết một implementation tương tự cho local disk, FTP, S3, cloud storage hoặc bất kỳ hệ lưu trữ nào khác.

Ý tưởng là mọi nguồn/đích đều đi qua cùng interface `IRemoteFileSystem`, nên `TransferEngine` không cần thay đổi.

Ví dụ khởi tạo:

```csharp
await using var smbFs = provider.CreateFileSystem();
await smbFs.ConnectAsync(new RemoteCredential
{
	Server = "192.168.1.100",
	Share = "incoming",
	Username = "svc-user",
	Password = "password"
});

await using var localFs = new LocalFileSystem();

var engine = new TransferEngine(
	smbFs,
	new[] { localFs },
	serviceProvider.GetRequiredService<ILogger<TransferEngine>>());
```

### 13.2 Trường hợp 2: SMB là cả nguồn và đích

Bạn có thể tạo hai file system SMB riêng biệt, kết nối đến hai share khác nhau:

```csharp
var provider = serviceProvider.GetRequiredService<IFileSystemProvider>();

await using var sourceSmb = provider.CreateFileSystem();
await using var destSmb = provider.CreateFileSystem();

await sourceSmb.ConnectAsync(new RemoteCredential
{
	Server = "10.0.0.10",
	Share = "source-share",
	Username = "user1",
	Password = "pwd1"
});

await destSmb.ConnectAsync(new RemoteCredential
{
	Server = "10.0.0.20",
	Share = "dest-share",
	Username = "user2",
	Password = "pwd2"
});

var engine = new TransferEngine(
	sourceSmb,
	new[] { destSmb },
	serviceProvider.GetRequiredService<ILogger<TransferEngine>>());
```

### 13.3 Trường hợp 3: Dùng trong worker service hoặc background service

Thư viện rất phù hợp để đóng gói trong một service nền:

- Nhận job từ queue hoặc database.
- Tạo `IRemoteFileSystem` theo cấu hình từng job.
- Gọi `TransferEngine.TransferAsync()`.
- Ghi log, telemetry và trạng thái hoàn thành.
- Resume nếu service bị khởi động lại.

Nếu hệ thống của bạn xử lý nhiều job dài hạn, hãy kết hợp thêm `SmbEnterprise.Jobs` và `SmbEnterprise.Persistence`.

---

## 14. Tích hợp job queue và SQLite persistence

Ví dụ đăng ký persistence:

```csharp
services.AddSqlitePersistence("jobs.db");
```

Khi đó bạn có thể dùng SQLite để lưu trạng thái job thay vì chỉ giữ trong bộ nhớ. Kịch bản điển hình:

- Người dùng tạo yêu cầu copy file.
- Ứng dụng tạo transfer job.
- Job được lưu xuống SQLite.
- Background scheduler đọc job và chạy.
- Nếu process chết giữa chừng, job có thể được nạp lại sau khi khởi động lại.

Điều này phù hợp cho ứng dụng nội bộ hoặc service xử lý batch theo lịch.

---

## 15. Các tùy chọn quan trọng trong TransferOptions

`TransferOptions` quyết định phần lớn hành vi của engine.

```csharp
var options = new TransferOptions
{
	ChunkSize = 1 * 1024 * 1024,
	MaxChunkSize = 16 * 1024 * 1024,
	MinChunkSize = 64 * 1024,
	MaxParallelWorkers = 4,
	MaxChunkRetries = 5,
	MaxReconnectRetries = 3,
	VerifyAfterCopy = true,
	ChecksumAlgorithm = ChecksumAlgorithm.XxHash64,
	WriteQueueDepth = 16,
	EnableReadAhead = true,
	Overwrite = false,
	Resume = true,
	BandwidthLimitBytesPerSecond = 0
};
```

Giải thích nhanh:

- `ChunkSize`: kích thước chunk khởi tạo.
- `MinChunkSize` / `MaxChunkSize`: biên cho adaptive chunk sizing.
- `MaxParallelWorkers`: số worker ghi song song.
- `MaxChunkRetries`: số lần retry cho mỗi chunk.
- `MaxReconnectRetries`: số chu kỳ reconnect khi lỗi kết nối.
- `VerifyAfterCopy`: có đối chiếu checksum sau khi copy hay không.
- `ChecksumAlgorithm`: thuật toán checksum.
- `WriteQueueDepth`: độ sâu queue cho pipeline.
- `EnableReadAhead`: bật đọc đón đầu.
- `Overwrite`: ghi đè file đích.
- `Resume`: tiếp tục từ phần đã copy.
- `BandwidthLimitBytesPerSecond`: giới hạn băng thông nếu cần.

---

## 16. Khi nào nên tự implement IRemoteFileSystem

Bạn nên tự hiện thực `IRemoteFileSystem` khi:

- Muốn ghi ra local disk theo cách riêng.
- Muốn dùng cloud object storage làm đích.
- Muốn truyền dữ liệu giữa SMB và một hệ lưu trữ nội bộ khác.
- Muốn mock hoặc fake file system để test.

Bạn chỉ cần cung cấp các thao tác chuẩn:

- connect / disconnect
- exists
- list directory
- get metadata
- open read / open write
- create / delete / rename
- set attributes

Khi interface này được hiện thực đầy đủ, `TransferEngine` sẽ dùng được ngay mà không cần sửa engine.

---

## 17. Gợi ý triển khai trong dự án thực tế

Một cách tổ chức hợp lý trong hệ thống khác:

```text
MyCompany.FileMover/
  Services/
	RemoteFileSystemFactory.cs
	TransferJobRunner.cs
	LocalFileSystem.cs
  Workers/
	TransferBackgroundService.cs
  Configuration/
	SmbOptions.cs
  Program.cs
```

Ý tưởng triển khai:

1. Đọc cấu hình SMB từ `appsettings.json`.
2. Đăng ký `AddSmbProvider()`, logging, checksum và persistence.
3. Viết `LocalFileSystem` hoặc adapter riêng cho đích.
4. Viết service tạo nguồn và đích theo từng job.
5. Dùng `TransferEngine` để chạy công việc.
6. Ghi trạng thái job ra database nếu cần resume sau restart.

---

## 18. Ví dụ hoàn chỉnh tối thiểu

Ví dụ dưới đây minh họa cách dùng thư viện trong một ứng dụng console khác để copy file từ SMB về local.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmbEnterprise.Checksum;
using SmbEnterprise.Core.Abstractions;
using SmbEnterprise.Core.Models;
using SmbEnterprise.Protocol.SMB;
using SmbEnterprise.Transfer;

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());
services.AddSmbProvider();
services.AddChecksumEngine(ChecksumAlgorithm.XxHash64);

var sp = services.BuildServiceProvider();
var provider = sp.GetRequiredService<IFileSystemProvider>();

await using var sourceFs = provider.CreateFileSystem();
await sourceFs.ConnectAsync(new RemoteCredential
{
	Server = "192.168.1.100",
	Share = "public",
	Username = "demo",
	Password = "demo-password"
});

await using var destFs = new LocalFileSystem();

var engine = new TransferEngine(
	sourceFs,
	new[] { destFs },
	sp.GetRequiredService<ILogger<TransferEngine>>());

var result = await engine.TransferAsync(
	sourcePath: @"\release\installer.zip",
	destinationPath: @"C:\Temp\installer.zip",
	options: new TransferOptions
	{
		Resume = true,
		VerifyAfterCopy = true,
		ChecksumAlgorithm = ChecksumAlgorithm.XxHash64
	},
	progress: new Progress<TransferProgress>(p =>
	{
		Console.WriteLine($"{p.PercentComplete:F1}% - {p.SpeedBytesPerSecond / 1024.0 / 1024:F2} MB/s");
	}));

Console.WriteLine(result.Success
	? "Hoan tat"
	: $"That bai: {result.ErrorMessage}");

await sourceFs.DisconnectAsync();
```

Lưu ý: `LocalFileSystem` trong ví dụ trên là implementation adapter local mà bạn có thể sao chép từ sample hoặc tự viết theo `IRemoteFileSystem`.

---

## 19. Hiệu năng và lưu ý vận hành

- Với SMB server phổ biến, nên bắt đầu bằng `ChunkSize` từ 256 KB đến 1 MB.
- Không phải lúc nào tăng `MaxParallelWorkers` cũng giúp nhanh hơn; nhiều SMB server hoạt động ổn định hơn khi số worker thấp.
- Với file rất lớn, `Resume = true` là cấu hình nên bật mặc định.
- Nếu cần tốc độ verify nhanh, `XxHash64` là lựa chọn hợp lý.
- Nếu cần độ tin cậy kiểm chứng cao hơn cho nghiệp vụ đặc biệt, dùng `SHA256`.

---

## 20. Khi nào nên dùng module nào

- Chỉ cần đọc/ghi SMB cơ bản: `Core` + `Protocol.SMB`
- Cần copy file ổn định: thêm `Transfer`
- Cần verify checksum: thêm `Checksum`
- Cần theo dõi tiến độ và thống kê: thêm `Diagnostics`
- Cần queue job nội bộ: thêm `Jobs`
- Cần job bền vững qua restart: thêm `Persistence`

---

## 21. Tóm tắt

SmbEnterprise không chỉ là một wrapper cho SMB, mà là một bộ thư viện theo hướng production-ready để:

- chuẩn hóa truy cập SMB bằng abstraction,
- truyền file lớn ổn định và có resume,
- hỗ trợ verify checksum,
- mở rộng sang nhiều loại đích khác nhau thông qua `IRemoteFileSystem`,
- dễ nhúng vào console app, worker service, WinForms app hoặc hệ thống enterprise khác.

Nếu bạn tích hợp thư viện vào dự án riêng, cách tiếp cận đúng là:

1. Tham chiếu các project cần thiết.
2. Đăng ký `AddSmbProvider()` trong DI.
3. Tạo hoặc tự implement file system cho đích.
4. Dùng `TransferEngine` làm lớp điều phối chính.
5. Bật checksum, jobs và persistence khi bài toán cần độ tin cậy cao hơn.
