# MD5 Checksum Verification

## Overview

Ứng dụng hiện đã tích hợp tính năng **MD5 checksum verification** để đảm bảo tính toàn vẹn (integrity) của các file sau khi copy từ SMB share xuống local.

## Features

### 1. **Automatic Hash File Detection**
- Tự động phát hiện file `.hash` trong package
- Parse nội dung file `.hash` theo format:
  ```
  [HASH]
  wcl=MD5_HASH_HERE
  w01=MD5_HASH_HERE
  w02=MD5_HASH_HERE
  ...
  ```

### 2. **Real-time Copy Progress Dialog**
- Hiển thị cửa sổ chi tiết quá trình copy
- Theo dõi từng file một cách riêng biệt:
  - File name
  - Size
  - Copy progress (%)
  - Status (Pending → Copying → Verifying → Verified/Failed)
  - Expected MD5 hash
  - Actual MD5 hash

### 3. **File Status Tracking**

Mỗi file đi qua các trạng thái sau:

| Status | Icon | Description |
|--------|------|-------------|
| `Pending` | ⏳ | File đang chờ copy |
| `Copying` | 📥 | Đang copy file từ SMB |
| `CopyComplete` | ✅ | Copy hoàn tất, chưa verify |
| `Verifying` | 🔍 | Đang tính MD5 checksum |
| `Verified` | ✅ | MD5 khớp với expected hash |
| `HashMismatch` | ❌ | MD5 không khớp - file có thể bị lỗi |
| `Failed` | ❌ | Copy thất bại do lỗi I/O |

### 4. **Visual Feedback**

#### Color Coding
- **Green background**: Hash verified thành công
- **Red background**: Hash mismatch hoặc failed
- **Blue text**: Đang copy
- **Orange text**: Đang verify

#### Overall Progress
- Progress bar tổng thể cho toàn bộ package
- Counter: `X / Y files completed`
- Status message cho file hiện tại

## Architecture

### Components

```
┌─────────────────────────────────────────┐
│         MainForm (UI Layer)              │
│  - BtnCopy_Click: Launch copy operation  │
│  - Show CopyProgressDialog               │
└──────────────┬──────────────────────────┘
               │
               ▼
┌─────────────────────────────────────────┐
│    CopyProgressDialog (Progress UI)      │
│  - ListView of files with status         │
│  - Overall progress bar                  │
│  - Cancellation support                  │
└──────────────┬──────────────────────────┘
               │ IProgress<FileCopyInfo>
               ▼
┌─────────────────────────────────────────┐
│     PackageCopyService (Business Logic)  │
│  - CopyPackageWithVerificationAsync()    │
│  - Copy file from SMB                    │
│  - Compute MD5                           │
│  - Compare with expected hash            │
└──────────────┬──────────────────────────┘
               │
               ▼
┌─────────────────────────────────────────┐
│     HashFileReader (Hash Service)        │
│  - ReadHashFileAsync()                   │
│  - ComputeMd5Async()                     │
│  - Stream-based MD5 calculation          │
└─────────────────────────────────────────┘
```

### Data Models

#### `FileCopyInfo`
```csharp
public sealed class FileCopyInfo
{
    public string FileName { get; init; }
    public long FileSize { get; init; }
    public long BytesCopied { get; set; }
    public string? ExpectedHash { get; init; }
    public string? ActualHash { get; set; }
    public FileCopyStatus Status { get; set; }
    public string? ErrorMessage { get; set; }

    public double Progress => ...
    public bool IsHashMatch => ...
}
```

#### `CopyResult`
```csharp
public sealed class CopyResult
{
    public string PackageName { get; init; }
    public string DestinationPath { get; init; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool Success { get; set; }
    public List<FileCopyInfo> FilesProcessed { get; }

    public int TotalFiles => ...
    public int VerifiedFiles => ...
    public int FailedFiles => ...
    public TimeSpan Duration => ...
}
```

## Workflow

### Copy & Verification Process

```
1. User clicks "Copy Selected Package"
   ↓
2. Create CopyProgressDialog
   ↓
3. Start background copy task:
   a. Read and parse .hash file first
   b. For each file in package:
      - Report status: Copying
      - Copy from SMB → local (buffered, 80KB chunks)
      - Report status: CopyComplete
      - Report status: Verifying
      - Compute MD5 of local file
      - Compare actual vs expected
      - Report status: Verified / HashMismatch
   ↓
4. Update dialog with results
   ↓
5. Show final summary dialog
```

### Hash File Format

File `.hash` trong package có format:

```ini
[HASH]
wcl=A1B2C3D4E5F6789012345678901234AB
whd=1234567890ABCDEF1234567890ABCDEF
w01=FEDCBA0987654321FEDCBA0987654321
w02=0011223344556677889900AABBCCDDEE
...
```

Service sẽ:
1. Tìm section `[HASH]`
2. Parse từng dòng theo regex: `^(\w+)=([a-fA-F0-9]{32})$`
3. Map key (wcl, w01, ...) với MD5 hash

### Hash Matching Logic

```csharp
// Extract key from filename: "packageName.wcl" → "wcl"
var key = Path.GetFileNameWithoutExtension(fileName).Split('.')[^1];

// Lookup expected hash
if (hashes.TryGetValue(key, out var expectedHash))
{
    // Compute actual hash
    var actualHash = await ComputeMd5Async(localFilePath);

    // Compare (case-insensitive)
    if (expectedHash.Equals(actualHash, StringComparison.OrdinalIgnoreCase))
        → Verified ✅
    else
        → HashMismatch ❌
}
```

## User Experience

### During Copy

1. **Dialog appears** immediately after clicking Copy
2. **Files are listed** with initial status "Pending"
3. **Each file updates** in real-time:
   - Progress percentage (0% → 100%)
   - Status icon changes (⏳ → 📥 → 🔍 → ✅/❌)
   - Hash values appear when computed
4. **Overall progress bar** advances as files complete
5. **Cancel button** allows stopping mid-operation

### After Copy

1. **Dialog shows final state** with all files color-coded
2. **"Close" button** enabled to dismiss dialog
3. **Summary message box** shows:
   - Total files verified
   - Failed files count
   - Duration
   - Destination path

### Error Handling

- **Hash mismatch**: File marked red with ❌, but copy continues for other files
- **Copy failure**: File marked failed, error message logged
- **Cancellation**: Operation stops gracefully, partial results shown

## Performance

### Optimizations

1. **Buffered I/O**: 80KB buffer for SMB reads and local writes
2. **Streaming MD5**: Hash computed incrementally, no memory spike
3. **Progress throttling**: UI updates batched to avoid lag
4. **Async/await**: Non-blocking operations throughout

### Typical Performance

- **Small files (<1MB)**: Near-instant copy + verify
- **Large files (100MB+)**: ~5-10 seconds per file (network-dependent)
- **MD5 computation**: ~50-100 MB/s on typical hardware

## Testing

### Manual Test Scenarios

1. **Valid package with matching hashes**
   - Expected: All files show ✅ Verified with green highlights

2. **Package with corrupted file (hash mismatch)**
   - Expected: Specific file shows ❌ HashMismatch with red background

3. **Package without .hash file**
   - Expected: Files copy successfully, no hash verification (status: CopyComplete)

4. **Network interruption during copy**
   - Expected: Failed files marked ❌, error logged, operation stops

5. **User cancellation mid-copy**
   - Expected: Operation stops, dialog shows partial results

## Future Enhancements

1. **Resume capability**: Save partial state, resume failed/cancelled copies
2. **Parallel copy**: Copy multiple files concurrently (with limit)
3. **SHA256 support**: Add support for SHA256 checksums
4. **Auto-retry**: Retry failed files automatically
5. **Export report**: Save verification results to log/CSV file

## Troubleshooting

### Issue: All files show "Hash Mismatch"

**Possible Causes:**
- .hash file format incorrect
- Case sensitivity mismatch in keys
- Hash file from different package version

**Solution:**
- Check .hash file format (must have `[HASH]` section)
- Verify key names match file extensions exactly

### Issue: Verification is slow

**Possible Causes:**
- Large files (100MB+)
- Slow disk I/O
- Antivirus scanning local files

**Solution:**
- Expected for large files (MD5 is CPU-bound)
- Check disk performance (SSD recommended)
- Temporarily disable antivirus if safe

### Issue: Dialog freezes during copy

**Possible Causes:**
- UI thread blocking (bug)
- Very long SMB timeout

**Solution:**
- Report issue (should never block UI)
- Check network connectivity to SMB server

## References

- **MD5 Algorithm**: RFC 1321
- **.hash File Format**: See `WORKFLOW.md`
- **Progress Reporting**: `IProgress<T>` pattern in .NET
- **Async I/O**: Best practices for `async/await` with file streams
