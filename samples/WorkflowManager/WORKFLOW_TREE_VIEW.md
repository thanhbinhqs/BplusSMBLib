# Workflow Tree View

## Overview

**WorkflowProgressDialog** hiển thị toàn bộ quy trình copy package dưới dạng cây (tree structure) với 3 bước chính, cho phép bạn xem chi tiết từng bước và trạng thái của từng file.

## Workflow Steps

```
📦 Package Workflow: SGLC123A01C01G001S0007
├── Step 1: Download from SMB
│   ├── 📄 SGLC123A01C01G001S0007.hash - 236 B - Downloaded ✓
│   ├── 📄 SGLC123A01C01G001S0007.whd - 100 KB - Downloaded ✓
│   ├── 📄 SGLC123A01C01G001S0007.w01 - 10 MB - Copying... 45.2%
│   ├── 📄 SGLC123A01C01G001S0007.w02 - 10 MB - Pending
│   └── ...
│
├── Step 2: Verify Checksums
│   ├── 📄 SGLC123A01C01G001S0007.w01 - Verified ✓
│   │   ├── Expected: 921FF16EEDA2EAA492D0FE...
│   │   └── Actual:   921FF16EEDA2EAA492D0FE...
│   ├── 📄 SGLC123A01C01G001S0007.w02 - Computing MD5...
│   └── ...
│
└── Step 3: Copy to Multiple Destinations
    ├── 📍 Server 1 - Completed ✓ (8 verified)
    │   ├── 📄 SGLC123A01C01G001S0007.w01 - Verified ✓
    │   ├── 📄 SGLC123A01C01G001S0007.w02 - Verified ✓
    │   └── ...
    │
    └── 📍 Server 2 - Copying... 3/8
        ├── 📄 SGLC123A01C01G001S0007.w01 - Verified ✓
        ├── 📄 SGLC123A01C01G001S0007.w02 - Copying... 67.3%
        └── ...
```

## Features

### 1. **Hierarchical View**
- **Step Level**: 3 main steps với status tổng quan
- **File Level**: Chi tiết từng file với progress
- **Destination Level**: (Step 3) Mỗi destination có node riêng
- **Hash Detail**: (Step 2) Expected vs Actual hash cho mỗi file

### 2. **Color-Coded Status Icons**
- 🔵 **Blue (Running)**: Đang xử lý
- 🟢 **Green (Success)**: Thành công
- 🟡 **Orange (Warning)**: Hoàn thành với lỗi
- 🔴 **Red (Error)**: Thất bại
- ⚪ **Gray (Pending)**: Chưa bắt đầu
- 🟣 **Purple (Verifying)**: Đang verify checksum

### 3. **Real-Time Updates**
- Progress percentage cho file đang copy
- Status transitions: Pending → Running → Success/Error
- Expand/collapse nodes để xem chi tiết
- Smooth updates không flicker

### 4. **Overall Progress Bar**
- Hiển thị tiến độ tổng quan (3 steps)
- Status text mô tả bước hiện tại
- Completed steps / Total steps

## UI Layout

```
┌─────────────────────────────────────────────────────────────────┐
│ 📦 Package Workflow: SGLC123A01C01G001S0007                      │
│ Step 2/3: Verifying checksums...                                 │
│ ▓▓▓▓▓▓▓▓▓▓▓▓▓▓░░░░░░░░░░░░░░░░  2/3 steps                      │
├─────────────────────────────────────────────────────────────────┤
│ ┌─ Workflow Tree ────────────────────────────────────────────┐  │
│ │ ✅ Step 1: Download from SMB - Downloaded 8/8 files        │  │
│ │   ├─ ✅ file1.w01 - 10 MB - Downloaded ✓                   │  │
│ │   ├─ ✅ file2.w02 - 10 MB - Downloaded ✓                   │  │
│ │   └─ ...                                                    │  │
│ │                                                              │  │
│ │ 🔵 Step 2: Verify Checksums - Verifying 3/8 files          │  │
│ │   ├─ ✅ file1.w01 - Verified ✓                             │  │
│ │   │   ├─ Expected: 921FF16EEDA2EAA492D0FE...               │  │
│ │   │   └─ Actual:   921FF16EEDA2EAA492D0FE...               │  │
│ │   ├─ 🔵 file2.w02 - Computing MD5...                       │  │
│ │   └─ ⚪ file3.w03 - Pending                                │  │
│ │                                                              │  │
│ │ ⚪ Step 3: Copy to Multiple Destinations - Pending          │  │
│ │   ├─ ⚪ 📍 Server 1                                         │  │
│ │   └─ ⚪ 📍 Server 2                                         │  │
│ └──────────────────────────────────────────────────────────────┘  │
│                                                      [Close]      │
└─────────────────────────────────────────────────────────────────┘
```

## Step Details

### Step 1: Download from SMB

**Purpose**: Copy files từ SMB share về local folder

**Tree Structure**:
```
Step 1: Download from SMB - Downloading 8 files from SMB...
├── file1.hash - 236 B - Pending
├── file2.whd - 100 KB - Copying... 45.2%
├── file3.w01 - 10 MB - Downloaded ✓
└── file4.w02 - 10 MB - Failed: Access denied
```

**Status Updates**:
- `Pending`: Chưa bắt đầu
- `Copying... X%`: Đang copy với progress
- `Downloaded ✓`: Copy thành công
- `Failed: <error>`: Copy thất bại với lý do

**Completion Criteria**:
- ✅ Success: Tất cả files downloaded
- ⚠️ Warning: Một số files failed
- ❌ Error: Không file nào downloaded

### Step 2: Verify Checksums

**Purpose**: Tính MD5 hash và so sánh với expected hash

**Tree Structure**:
```
Step 2: Verify Checksums - Verifying 8 files...
├── file1.w01 - Verified ✓
│   ├── Expected: 921FF16EEDA2EAA492D0FE...
│   └── Actual:   921FF16EEDA2EAA492D0FE...  (green, match)
├── file2.w02 - Hash Mismatch ⚠
│   ├── Expected: 89EA6B66F47421FEFAD55E...
│   └── Actual:   89EA6B66F47421FEFAD99E...  (red, mismatch)
├── file3.w03 - Computing MD5...
└── file4.w04 - Pending
```

**Status Updates**:
- `Pending`: Chưa verify
- `Computing MD5...`: Đang tính hash
- `Verified ✓`: Hash match (green)
- `Hash Mismatch ⚠`: Hash không khớp (red)
- `Failed ✗`: Không tính được hash

**Hash Display**:
- Expected hash hiển thị màu xám
- Actual hash:
  - Màu xanh (green) nếu match
  - Màu đỏ (red) nếu mismatch
- Expand file node để xem chi tiết hash

**Completion Criteria**:
- ✅ Success: Tất cả files verified
- ⚠️ Warning: Một số files mismatch/failed
- ❌ Error: Không file nào verified

### Step 3: Copy to Multiple Destinations

**Purpose**: Copy files từ local đến nhiều SMB destinations và verify

**Tree Structure**:
```
Step 3: Copy to Multiple Destinations - Copying to 2 destination(s)...
├── 📍 Server 1 - Completed ✓ (8 verified)
│   ├── file1.w01 - Verified ✓
│   ├── file2.w02 - Verified ✓
│   └── ...
│
└── 📍 Server 2 - Copying... 3/8
    ├── file1.w01 - Verified ✓
    ├── file2.w02 - Copying... 67.3%
    ├── file3.w03 - Pending
    └── ...
```

**Destination Status**:
- `Pending`: Chưa kết nối
- `Connecting...`: Đang kết nối SMB
- `Copying... X/Y`: Đang copy, X files completed
- `Verifying... X/Y`: Đang verify checksums
- `Completed ✓ (X verified)`: Hoàn thành thành công
- `Completed with errors (X failed)`: Có lỗi
- `Failed: <error>`: Kết nối/copy thất bại

**File Status per Destination**:
- `Pending`: Chưa bắt đầu
- `Copying... X%`: Đang copy
- `Copied ✓`: Copy xong, chưa verify
- `Verifying...`: Đang verify
- `Verified ✓`: Verified thành công
- `Hash Mismatch ⚠`: Hash không khớp
- `Failed: <error>`: Lỗi

**Completion Criteria**:
- ✅ Success: Tất cả destinations completed
- ⚠️ Warning: Một số destinations có errors
- ❌ Error: Tất cả destinations failed

## User Interactions

### Expand/Collapse Nodes
- Click `[+]` để expand node và xem chi tiết
- Click `[-]` để collapse node
- Default: All major steps expanded, files collapsed

### Node Selection
- Click vào node để select
- Selected node highlighted
- Full row select mode

### Auto-Scroll
- Tree tự động scroll đến node đang active
- User có thể scroll thủ công để xem các phần khác

### Status Icons
- Icons update real-time theo status
- Hover để xem tooltip (nếu có error message)

## Integration with Workflow

### Starting Workflow

```csharp
// Create dialog
using var workflowDialog = new WorkflowProgressDialog(packageName);

// Show dialog (non-blocking, ShowDialog for blocking)
workflowDialog.Show(this);

// Run workflow in background
var workflowTask = Task.Run(async () =>
{
    // Step 1: Download
    workflowDialog.StartDownload(totalFiles);
    foreach (var file in files)
    {
        workflowDialog.AddDownloadFile(fileInfo);
        // ... copy file ...
        workflowDialog.UpdateDownloadProgress(fileInfo);
    }
    workflowDialog.CompleteDownload(success, message);

    // Step 2: Verify
    workflowDialog.StartVerification(totalFiles);
    foreach (var file in files)
    {
        workflowDialog.AddVerifyFile(fileInfo);
        // ... verify hash ...
        workflowDialog.UpdateVerifyProgress(fileInfo);
    }
    workflowDialog.CompleteVerification(success, message);

    // Step 3: Multi-Copy
    workflowDialog.StartMultiCopy(destinations);
    var progress = new Progress<List<DestinationCopyResult>>(results =>
    {
        workflowDialog.UpdateMultiCopyProgress(results);
    });
    await multiCopyService.CopyToMultipleDestinationsAsync(..., progress);
});

await workflowTask;
```

### Progress Reporting

**Step 1 & 2**: File-based progress
```csharp
var progress = new Progress<FileCopyInfo>(fileInfo =>
{
    workflowDialog.AddDownloadFile(fileInfo);       // Once per file
    workflowDialog.UpdateDownloadProgress(fileInfo); // Multiple times

    workflowDialog.AddVerifyFile(fileInfo);          // Once per file
    workflowDialog.UpdateVerifyProgress(fileInfo);   // Multiple times
});
```

**Step 3**: Destination-based progress
```csharp
var progress = new Progress<List<DestinationCopyResult>>(results =>
{
    workflowDialog.UpdateMultiCopyProgress(results);
});
```

## Advantages over Simple Dialog

### 1. **Complete Workflow Visibility**
- Simple Dialog: Chỉ thấy một step tại một thời điểm
- Tree Dialog: Thấy toàn bộ workflow, completed và upcoming steps

### 2. **Hierarchical Organization**
- Simple Dialog: Flat file list
- Tree Dialog: Organized by steps, destinations, files

### 3. **Detailed Hash Comparison**
- Simple Dialog: Expected và Actual trong columns
- Tree Dialog: Parent-child relationship, color-coded

### 4. **Multi-Destination Clarity**
- Simple Dialog: Separate tabs
- Tree Dialog: Tree structure, easy to compare

### 5. **Status History**
- Simple Dialog: Current status only
- Tree Dialog: See completed steps với final status

## Performance

### Memory Usage
- TreeView nodes: ~100 bytes per node
- Typical: < 5MB for large package với multiple destinations
- Images: Pre-cached ImageList

### Update Frequency
- Download/Verify: Every 1MB or status change
- Multi-copy: Every 1MB or status change per destination
- UI thread: Progress<T> marshals updates safely

### Rendering
- WinForms TreeView native control
- Double-buffered to prevent flicker
- Smooth expand/collapse animations

## Troubleshooting

### Tree not updating
- Check Progress<T> is being reported
- Verify InvokeRequired logic
- Review thread marshaling

### Icons not showing
- Ensure ImageList is attached: `TreeView.ImageList = _imageList`
- Check ImageKey names match
- Verify images are added to ImageList

### Slow performance
- Limit update frequency (throttle to 1MB)
- Use BeginUpdate/EndUpdate for batch updates
- Collapse completed steps

## Future Enhancements

1. **Search/Filter**: Find files by name or status
2. **Export**: Export tree to text/CSV
3. **Refresh**: Retry failed steps
4. **Context Menu**: Right-click actions (retry, skip, etc.)
5. **Color Themes**: Light/dark mode
6. **Font Scaling**: Adjust for accessibility
7. **Status Summary**: Statistics panel (files/bytes by status)
8. **Time Tracking**: Show elapsed time per step/file
9. **Log Integration**: Click file to see detailed logs
10. **Notifications**: Sound/toast on step completion

## Comparison: Workflow Tree vs Tabs

| Feature | Workflow Tree | Tab-Based (Old) |
|---------|--------------|-----------------|
| **Workflow Visibility** | All steps visible | One step at a time |
| **Organization** | Hierarchical | Flat tabs |
| **Status History** | Full history | Current only |
| **Multi-Destination** | Tree nodes | Separate tabs |
| **Hash Comparison** | Parent-child | Side-by-side |
| **Navigation** | Expand/collapse | Tab switching |
| **Progress Tracking** | Step-level + file-level | File-level only |
| **Space Efficiency** | Compact when collapsed | Fixed tab space |

## Best Practices

### For Developers
1. **Report Early**: Call `AddFile()` before starting file operation
2. **Update Frequently**: Report progress every 1MB
3. **Complete Always**: Call `Complete()` even on errors
4. **Thread-Safe**: Use `Progress<T>` for cross-thread updates
5. **Error Details**: Include error messages in FileInfo

### For Users
1. **Expand Nodes**: See detailed status and hash comparison
2. **Watch Overall Progress**: Top progress bar shows 3-step progress
3. **Check Failed Items**: Red icons indicate errors, check child nodes for details
4. **Wait for Completion**: Dialog enables Close button when done
5. **Review History**: Scroll up to see completed steps

## Example Output

```
✅ Step 1: Download from SMB - Completed: 8/8 successful
  ├─ ✅ SGLC123A01C01G001S0007.hash - 236 B - Downloaded ✓
  ├─ ✅ SGLC123A01C01G001S0007.whd - 100 KB - Downloaded ✓
  ├─ ✅ SGLC123A01C01G001S0007.w01 - 10 MB - Downloaded ✓
  ├─ ✅ SGLC123A01C01G001S0007.w02 - 10 MB - Downloaded ✓
  ├─ ✅ SGLC123A01C01G001S0007.w03 - 10 MB - Downloaded ✓
  ├─ ✅ SGLC123A01C01G001S0007.w04 - 10 MB - Downloaded ✓
  ├─ ✅ SGLC123A01C01G001S0007.w05 - 2.88 MB - Downloaded ✓
  └─ ✅ SGLC123A01C01G001S0007.wcl - 10 MB - Downloaded ✓

✅ Step 2: Verify Checksums - All 8 files verified
  ├─ ✅ SGLC123A01C01G001S0007.w01 - Verified ✓
  │   ├─ Expected: 921FF16EEDA2EAA492D0FE...
  │   └─ Actual:   921FF16EEDA2EAA492D0FE... ✓
  ├─ ✅ SGLC123A01C01G001S0007.w02 - Verified ✓
  │   ├─ Expected: 89EA6B66F47421FEFAD55E...
  │   └─ Actual:   89EA6B66F47421FEFAD55E... ✓
  └─ ...

✅ Step 3: Copy to Multiple Destinations - Completed: 2/2 successful
  ├─ ✅ 📍 Server 1 - Completed ✓ (8 verified)
  │   ├─ ✅ SGLC123A01C01G001S0007.w01 - Verified ✓
  │   ├─ ✅ SGLC123A01C01G001S0007.w02 - Verified ✓
  │   └─ ...
  └─ ✅ 📍 Server 2 - Completed ✓ (8 verified)
      ├─ ✅ SGLC123A01C01G001S0007.w01 - Verified ✓
      ├─ ✅ SGLC123A01C01G001S0007.w02 - Verified ✓
      └─ ...
```

Workflow completed successfully! 🎉
