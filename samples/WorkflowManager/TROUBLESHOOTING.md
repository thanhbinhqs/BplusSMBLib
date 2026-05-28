# Troubleshooting và Fixes

## Issue #1: Cross-Thread Operation Exception

### Vấn đề
```
System.InvalidOperationException: Cross-thread operation not valid: 
Control '' accessed from a thread other than the thread it was created on.
```

### Nguyên nhân
Code cũ cố gắng truy cập ListView từ background thread để đếm số file đã hoàn thành.

### Giải pháp
- Sử dụng `HashSet<string>` để track completed files thay vì truy cập UI controls
- Thread-safe counting với `lock` statement
- `Progress<T>` tự động marshal callbacks về UI thread

## Issue #2: File Lock Exception

### Vấn đề
```
The process cannot access the file '...\SGLC123A01C01G001S0002.hash' 
because it is being used by another process.
```

### Nguyên nhân
File `.hash` sau khi copy chưa được đóng hoàn toàn (file handle vẫn đang được giữ).

### Giải pháp
1. **Xóa file cũ trước khi copy**:
```csharp
if (File.Exists(hashDestPath))
{
    File.Delete(hashDestPath);
    await Task.Delay(100, cancellationToken);
}
```

2. **Sử dụng FileStream với control tốt hơn**:
```csharp
await using var localHashStream = new FileStream(
    hashDestPath,
    FileMode.Create,
    FileAccess.Write,
    FileShare.None,
    bufferSize: 81920,
    useAsync: true);
```

3. **Flush và đợi stream đóng**:
```csharp
await localHashStream.FlushAsync(cancellationToken);
// await using tự động dispose
await Task.Delay(100, cancellationToken); // Đợi OS release handle
```

## Issue #3: Missing Information in Dialog

### Vấn đề
- Size hiển thị "0 B"
- Expected Hash hiển thị "N/A"  
- Actual Hash hiển thị "-"

### Nguyên nhân
1. Files được add vào dialog trước với dummy data (`FileSize = 0`)
2. `GetExpectedHash()` logic sai - không match được extension với keys trong `.hash` file

### Giải pháp

#### 1. Add files động khi copy
Thay vì add files trước với dummy data, giờ add files khi bắt đầu copy mỗi file:

```csharp
var progress = new Progress<FileCopyInfo>(fileInfo =>
{
    // Add file lần đầu khi status = Copying
    if (!completedFileNames.Contains(fileInfo.FileName) && 
        fileInfo.Status == FileCopyStatus.Copying)
    {
        progressDialog.AddFile(fileInfo);
    }

    // Update progress với full info
    progressDialog.UpdateFileProgress(fileInfo.FileName, fileInfo);
});
```

#### 2. Fix GetExpectedHash logic

**Old logic (SAI)**:
```csharp
// File: "SGLC123A01-C01-G001-S0002.whd"
var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName); 
// => "SGLC123A01-C01-G001-S0002"
var parts = nameWithoutExt.Split('.');
// => ["SGLC123A01-C01-G001-S0002"] (không có dot)
var key = parts[^1]; 
// => "SGLC123A01-C01-G001-S0002" ❌ WRONG!
```

**New logic (ĐÚNG)**:
```csharp
// File: "SGLC123A01-C01-G001-S0002.whd"
var extension = Path.GetExtension(fileName).TrimStart('.');
// => "whd" ✅ CORRECT!

if (hashes.TryGetValue(extension, out var hash))
    return hash;
```

#### 3. Hash file format
File `.hash` có format:
```ini
[HASH]
whd=A1B2C3D4E5F6789012345678901234AB
wcl=1234567890ABCDEF1234567890ABCDEF
w01=FEDCBA0987654321FEDCBA0987654321
w02=0011223344556677889900AABBCCDDEE
...
```

Keys là **file extension** (whd, wcl, w01, w02, ...), không phải basename parts.

## Testing Results

### Before Fixes
- ❌ Cross-thread exception
- ❌ File lock error on second copy
- ❌ Size shows "0 B"
- ❌ Expected Hash shows "N/A"
- ❌ Actual Hash shows "-"

### After Fixes
- ✅ No cross-thread exception
- ✅ File locks released properly
- ✅ Size shows actual bytes (e.g., "1.2 MB")
- ✅ Expected Hash shows from `.hash` file
- ✅ Actual Hash shows computed MD5
- ✅ Hash match/mismatch colored correctly

## Code Quality Improvements

### Thread Safety
- `HashSet<string>` với `lock` cho tracking
- `Progress<T>` cho UI marshaling
- Async/await throughout

### Resource Management
- `await using` cho streams
- Explicit `FlushAsync()` trước dispose
- Delay để đảm bảo OS release handles

### Error Handling
- Try-catch per file (không stop toàn bộ operation)
- Detailed logging cho debugging
- User-friendly error messages

## Performance Notes

### Typical Operation
- **Small files (<1MB)**: Near-instant copy + verify
- **Large files (100MB+)**: 5-10s per file (network-dependent)
- **MD5 computation**: ~50-100 MB/s

### Bottlenecks
1. **Network speed**: SMB read performance
2. **Disk I/O**: Local write + MD5 read
3. **CPU**: MD5 hash calculation

### Optimizations Applied
- 80KB buffers for copy operations
- Streaming MD5 (no memory spike)
- Async I/O throughout
- UI updates throttled via `Progress<T>`

## Future Improvements

1. **Parallel copy**: Copy multiple files concurrently (with semaphore limit)
2. **Resume capability**: Save state, resume failed operations
3. **Retry logic**: Auto-retry failed files with exponential backoff
4. **SHA256 support**: Stronger checksums if available
5. **Progress estimation**: Show ETA based on current speed
6. **Export report**: Save verification results to CSV/JSON

## References

- [Progress&lt;T&gt; Class](https://learn.microsoft.com/en-us/dotnet/api/system.progress-1)
- [Thread-Safe Collections](https://learn.microsoft.com/en-us/dotnet/standard/collections/thread-safe/)
- [Async File I/O](https://learn.microsoft.com/en-us/dotnet/standard/io/asynchronous-file-i-o)
- [MD5 Algorithm RFC 1321](https://www.ietf.org/rfc/rfc1321.txt)
