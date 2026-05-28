# Multi-Destination Copy Feature

## Overview

Sau khi copy package từ SMB về local và verify checksum thành công, bạn có thể copy package đó lên nhiều SMB destinations khác nhau đồng thời với verification tự động.

## Workflow

```
┌─────────────────────────────────────────────────────────────────┐
│                    MULTI-DESTINATION COPY                        │
└─────────────────────────────────────────────────────────────────┘

1. Copy từ SMB → Local (với verification)
   ├── Download files
   ├── Verify MD5 checksums
   └── ✅ "Copy to Multiple Destinations" button appears

2. Configure Destinations
   ├── Click "⚙️ Manage Destinations"
   ├── Add/Edit/Remove destinations
   │   ├── Name: Friendly name
   │   ├── UNC Path: \\server\share\path
   │   ├── Username & Password
   │   └── Enable/Disable checkbox
   └── Save configuration

3. Multi-Destination Copy
   ├── Click "📤 Copy to Multiple Destinations"
   ├── Review and confirm destinations
   └── Progress dialog shows per-destination status

4. Parallel Execution
   ├── Connect to all destinations simultaneously
   ├── Copy files independently
   │   ├── Track bytes copied per file
   │   ├── Handle slow/failed destinations
   │   └── Continue with other destinations
   └── Verify checksums on each destination

5. Results
   ├── ✅ Completed: All files verified
   ├── ⚠️ Completed with errors: Some files failed
   └── ❌ Failed: Connection or critical error
```

## Features

### 1. Parallel Copy to Multiple Destinations
- Copy đến nhiều SMB shares cùng lúc
- Mỗi destination chạy độc lập trong parallel tasks
- Destination chậm không ảnh hưởng đến destinations khác

### 2. Independent Progress Tracking
- **Per-Destination Tabs**: Mỗi destination có tab riêng
- **Per-File Status**: Theo dõi từng file trên mỗi destination
  - Pending → Copying → Copy Complete → Verifying → Verified
- **Real-time Progress**: Bytes copied, progress percentage
- **Overall Summary**: Tổng quan trạng thái all destinations

### 3. Checksum Verification
- Verify MD5 hash trên TỪNG destination
- So sánh với hash trong file `.hash` gốc
- Hiển thị Expected vs Actual hash
- Highlight match/mismatch

### 4. Error Handling
- Destination failed không ảnh hưởng destinations khác
- File failed trên một destination không stop các files khác
- Error messages hiển thị trong tooltip và status
- Log chi tiết cho debugging

## UI Components

### Main Form - After Successful Copy

```
┌────────────────────────────────────────────────────────────────┐
│ 📁 Destination: C:\Users\...\Documents\Packages  [📂 Browse]   │
│ [⬇️ Copy Selected Package]                                      │
│                                                                 │
│ [📤 Copy to Multiple Destinations]  [⚙️ Manage Destinations]   │
│ ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓░░░░░░░░░░░░░░░░  45%                      │
└────────────────────────────────────────────────────────────────┘
```

### Destination Manager Dialog

```
┌─────────────────────────────────────────────────────────────────┐
│ 📤 Copy Destinations                                             │
│ Configure SMB destinations where packages will be copied         │
├─────────────────────────────────────────────────────────────────┤
│ [✓] Enabled │ Name      │ UNC Path              │ Username │ Pwd│
│ [✓]         │ Server 1  │ \\192.168.1.100\data  │ admin    │ ***│
│ [✓]         │ Server 2  │ \\192.168.1.101\data  │ admin    │ ***│
│ [ ]         │ Backup    │ \\nas\backup          │ user     │ ***│
├─────────────────────────────────────────────────────────────────┤
│ [➕ Add]  [✏️ Edit]  [🗑️ Remove]              [Cancel]  [OK]   │
└─────────────────────────────────────────────────────────────────┘
```

### Multi-Destination Progress Dialog

```
┌─────────────────────────────────────────────────────────────────┐
│ 📤 Copying to 2 Destination(s)                                   │
│ Destinations: 1/2 | Files: 5 verified, 0 failed, 5/8 total      │
│ ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓░░░░░░░░░░░░  62%                       │
├─────────────────────────────────────────────────────────────────┤
│ ┌─Server 1─┐ ┌─Server 2─┐                                       │
│ │                                                                │
│ │ ✅ Completed                  ⏳ Copying...                   │
│ │ 8 verified, 0 failed          3 verified, 0 failed            │
│ │ Duration: 00:45                                                │
│ │ ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓ 100%     ▓▓▓▓▓▓▓░░░░░░░░  37%           │
│ │                                                                │
│ │ File Name          Size    Progress  Status      Expected Hash│
│ │ package.w01       10 MB    100.0%   ✅ Verified  921FF16...   │
│ │ package.w02       10 MB    100.0%   ✅ Verified  89EA6B6...   │
│ │ package.w01       10 MB     45.2%   📥 Copying   921FF16...   │
│ │                                                                │
└─────────────────────────────────────────────────────────────────┘
```

## Architecture

### Models

```csharp
// DestinationInfo: Configuration cho mỗi destination
public class DestinationInfo
{
    string Name;              // Friendly name
    string UncPath;           // \\server\share\path
    string Username;          // SMB username
    string Password;          // SMB password
    bool IsEnabled;           // Enable/disable for copy
}

// DestinationCopyResult: Kết quả copy đến một destination
public class DestinationCopyResult
{
    string DestinationName;
    string DestinationPath;
    DestinationStatus Status; // Pending/Copying/Verifying/Completed/Failed
    int FilesTotal;
    int FilesCopied;
    int FilesVerified;
    int FilesFailed;
    List<FileDestinationCopyInfo> Files;
    DateTime? StartTime;
    DateTime? EndTime;
}

// FileDestinationCopyInfo: Status của một file trên destination
public class FileDestinationCopyInfo
{
    string FileName;
    long FileSize;
    long BytesCopied;
    string? ExpectedHash;
    string? ActualHash;
    FileCopyStatus Status;
    string? ErrorMessage;
}
```

### Services

**MultiDestinationCopyService**
- `CopyToMultipleDestinationsAsync()`: Main method
  - Read source files from local path
  - Load expected hashes from `.hash` file
  - Initialize progress tracking for each destination
  - Run parallel copy tasks (one per destination)
  - Return aggregated results

- `CopyToDestinationAsync()`: Copy đến một destination
  - Connect to SMB với IRemoteFileSystem
  - Create destination directory
  - Copy all files với progress tracking
  - Verify checksums
  - Handle errors gracefully

- `CopyFileToDestinationAsync()`: Copy một file
  - Read from local FileStream
  - Write to IRemoteWriteStream
  - Track bytes copied
  - Report progress periodically (every 1MB)

- `VerifyFileOnDestinationAsync()`: Verify checksum
  - Read file từ destination via IRemoteReadStream
  - Compute MD5 hash
  - Compare with expected hash
  - Update status: Verified / HashMismatch

### UI Forms

**DestinationManagerDialog**
- ListView showing all configured destinations
- Add/Edit/Remove destinations
- Enable/disable individual destinations

**DestinationEditDialog**
- Input form for destination details
- Validate UNC path format
- Secure password input

**MultiDestinationCopyDialog**
- TabControl với một tab per destination
- Overall progress summary
- Per-destination file list với progress
- Real-time status updates via Progress<T>

## Usage Example

```csharp
// 1. User copies package from SMB to local
var copyResult = await _copyService.CopyPackageWithVerificationAsync(...);

// 2. Configure destinations
using var destManager = new DestinationManagerDialog();
if (destManager.ShowDialog() == DialogResult.OK)
{
    var destinations = destManager.Destinations;

    // 3. Copy to multiple destinations
    var progress = new Progress<List<DestinationCopyResult>>(results => 
    {
        progressDialog.UpdateProgress(results);
    });

    var results = await _multiCopyService.CopyToMultipleDestinationsAsync(
        localPackagePath,
        packageName,
        destinations,
        progress);

    // 4. Check results
    foreach (var result in results)
    {
        if (result.Status == DestinationStatus.Completed)
            Console.WriteLine($"✅ {result.DestinationName}: Success");
        else
            Console.WriteLine($"❌ {result.DestinationName}: {result.ErrorMessage}");
    }
}
```

## Error Handling

### Connection Errors
```
Destination Status: ❌ Failed
Error: Failed to connect to 192.168.1.100
```
- Destination marked as Failed
- Other destinations continue
- Error logged for debugging

### File Copy Errors
```
File Status: ❌ Failed
Error: Access denied
```
- File marked as Failed
- Other files on same destination continue
- Destination status becomes "Completed with Errors"

### Network Slow/Timeout
- Progress updates show current status
- User can see which destination is slow
- No timeout - let it run until complete or error
- Other destinations proceed independently

### Hash Mismatch
```
File Status: ❌ Hash Mismatch
Expected: 921FF16EEDA2EAA492D0FE...
Actual:   921FF16EEDA2EAA492D0FF...
```
- File marked as HashMismatch
- User can see both hashes to compare
- Counted as failed file

## Performance

### Parallel Execution
- Each destination runs in separate Task
- No blocking between destinations
- Maximum throughput: limited by network bandwidth

### Progress Reporting
- Throttled to every 1MB or file completion
- Reduces UI thread overhead
- Smooth progress updates without flickering

### Memory Usage
- 81KB buffer per active copy stream
- Max memory: ~100KB × (number of destinations × concurrent files)
- Typical: < 10MB for 10 destinations

## Configuration Persistence

Destinations are stored in memory for the session. To persist across restarts, you can:

1. Add JSON serialization to DestinationInfo
2. Save to user settings or local file
3. Load on application startup

```csharp
// Example persistence
var json = JsonSerializer.Serialize(_destinations);
File.WriteAllText("destinations.json", json);

// Load
var json = File.ReadAllText("destinations.json");
_destinations = JsonSerializer.Deserialize<List<DestinationInfo>>(json);
```

## Troubleshooting

### "No valid local package found"
- Ensure you've copied a package from SMB first
- Check that local folder still exists
- Copy flow must complete successfully

### "No destinations configured"
- Click "⚙️ Manage Destinations"
- Add at least one destination
- Enable the destination (checkbox)

### Destination stays "Connecting"
- Check network connectivity
- Verify SMB server is reachable
- Check username/password are correct
- Review firewall settings

### Files fail with "Access Denied"
- Verify SMB user has write permissions
- Check destination folder exists and is writable
- Try manual file copy to test permissions

### Slow performance
- Check network bandwidth between client and destinations
- Consider copying to fewer destinations at once
- Review SMB server load and disk performance

## Future Enhancements

1. **Resume Support**: Resume failed files on retry
2. **Bandwidth Throttling**: Limit copy speed per destination
3. **Scheduling**: Queue copies for off-peak hours
4. **Notifications**: Email/SMS on completion
5. **Incremental Copy**: Skip unchanged files
6. **Compression**: Compress before upload
7. **Destination Templates**: Save common destination sets
8. **Copy History**: Track past copy operations
