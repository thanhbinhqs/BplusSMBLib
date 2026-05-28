# Unified Workflow Dialog - Complete Guide

## 🎯 Tổng quan

**UnifiedWorkflowDialog** là giải pháp workflow hoàn chỉnh, gộp toàn bộ 3 steps vào 1 cửa sổ duy nhất với:
- ✅ Tree-based hierarchical view
- ✅ Step-by-step buttons (chỉ enable khi step trước hoàn thành)
- ✅ Real-time progress tracking
- ✅ Expected vs Actual hash comparison
- ✅ Per-destination and per-file status

---

## 🚀 Cách sử dụng

### 1. Mở Unified Workflow

```csharp
// From MainForm
private void BtnUnifiedWorkflow_Click(object? sender, EventArgs e)
{
    using var unifiedDialog = new UnifiedWorkflowDialog(
        _scannerService,
        _copyService,
        _multiCopyService,
        _config,
        selectedPackage,
        destPath,
        _destinations);

    unifiedDialog.ShowDialog(this);
}
```

### 2. Workflow Steps

#### **Step 1: Download from SMB** 🔽
- Click nút **"1️⃣ Download from SMB"**
- Tree view hiển thị:
  ```
  📦 SGLC123A01C01G001S0014
    └─ ⏳ Step 1: Download from SMB (In progress...)
         ├─ 📄 file1.w01 - ⏳ Copying (45.2%)
         ├─ 📄 file2.w02 - ✅ Downloaded
         └─ 📄 file3.whd - 🔍 Verifying...
  ```
- Mỗi file show:
  - Status icon (⏸️/⏳/✅/⚠️/❌)
  - Progress percentage khi đang copy
  - Hash comparison khi verify:
    ```
    📄 file.w01 - ✅ Verified
      ├─ Expected: 73C05A0A650FAB8137308763FA3405A2
      └─ Actual:   73C05A0A650FAB8137308763FA3405A2 ✅
    ```

- **Khi hoàn thành**: Nút **Step 2** tự động enable

#### **Step 2: Verify Checksums** 🔍
- Click nút **"2️⃣ Verify Checksums"**
- Verification đã được thực hiện trong Step 1
- Step này chỉ confirm và enable Step 3
- **Khi hoàn thành**: Nút **Step 3** tự động enable (nếu có destinations)

#### **Step 3: Copy to Multiple Destinations** 📤
- Click nút **"3️⃣ Copy to Destinations"**
- Tree view expand để show tất cả destinations:
  ```
  📦 SGLC123A01C01G001S0014
    └─ ⏳ Step 3: Copy to 5 Destination(s) (In progress...)
         ├─ 📍 A1 (\\192.168.1.250\share\A1)
         │   ├─ Copied: 3/8, Verified: 2/8
         │   ├─ 📄 file1.w01 - ✅ Verified
         │   │   ├─ Expected: ABC123...
         │   │   └─ Actual:   ABC123... ✅
         │   ├─ 📄 file2.w02 - ⏳ 67.5%
         │   └─ 📄 file3.w03 - ⏸️ Pending
         ├─ 📍 A2 (\\192.168.1.250\share\A2)
         │   └─ ✅ Complete - All verified
         ├─ 📍 A3 - ⏳ Copying...
         ├─ 📍 A4 - ⏸️ Pending
         └─ 📍 A5 - ⏸️ Pending
  ```

---

## 🎨 UI Components

### Tree View Structure

```
📦 Package Name
 ├─ ⏸️ Step 1: Download from SMB (Not started)
 ├─ ⏸️ Step 2: Verify Checksums (Not started)
 └─ ⏸️ Step 3: Copy to Multiple Destinations (Not started)
```

**After Step 1:**
```
📦 SGLC123A01C01G001S0014
 ├─ ✅ Step 1: Download from SMB (Complete - 8/8 verified)
 │   ├─ 📄 file1.w01 - ✅ Verified
 │   │   ├─ Expected: 73C05A0A650FAB8137308763FA3405A2
 │   │   └─ Actual:   73C05A0A650FAB8137308763FA3405A2
 │   ├─ 📄 file2.w02 - ✅ Verified
 │   └─ ... (more files)
 ├─ ⏸️ Step 2: Verify Checksums (Not started)
 └─ ⏸️ Step 3: Copy to Multiple Destinations (Not started)
```

**After Step 3:**
```
📦 SGLC123A01C01G001S0014
 ├─ ✅ Step 1: Complete
 ├─ ✅ Step 2: Complete
 └─ ✅ Step 3: Copy to 5 Destination(s) (Complete - 5/5 successful)
     ├─ ✅ A1 - Copied: 8/8, Verified: 8/8
     │   ├─ 📄 file1.w01 - ✅ Verified
     │   │   ├─ Expected: ABC...
     │   │   └─ Actual:   ABC...
     │   └─ ... (all files)
     ├─ ✅ A2 - Complete
     ├─ ✅ A3 - Complete
     ├─ ✅ A4 - Complete
     └─ ✅ A5 - Complete
```

### Status Icons

| Icon | Meaning |
|------|---------|
| ⏸️ | Pending (chưa bắt đầu) |
| 🔗 | Connecting (đang kết nối SMB) |
| ⏳ | In Progress (đang thực hiện) |
| 🔍 | Verifying (đang verify checksum) |
| ✅ | Success (thành công) |
| ⚠️ | Warning (có errors nhưng không fatal) |
| ❌ | Failed (thất bại hoàn toàn) |

### Color Coding

| Color | Meaning |
|-------|---------|
| 🔵 Blue | Running/In Progress |
| 🟢 Green | Success/Verified |
| 🟠 Orange | Warning/Hash Mismatch |
| 🔴 Red | Error/Failed |
| ⚫ Gray | Pending/Waiting |

---

## 🎯 Step Buttons Logic

### Button States

```csharp
// Initial state
_btnStep1.Enabled = true;   // ✅ Can start
_btnStep2.Enabled = false;  // ❌ Disabled
_btnStep3.Enabled = false;  // ❌ Disabled

// After Step 1 success
_btnStep1.Enabled = false;  // ✅ Already done
_btnStep2.Enabled = true;   // ✅ Now available
_btnStep3.Enabled = false;  // ❌ Still disabled

// After Step 2 complete
_btnStep1.Enabled = false;  // ✅ Already done
_btnStep2.Enabled = false;  // ✅ Already done
_btnStep3.Enabled = true;   // ✅ Now available
```

### Button Styling

```csharp
// Enabled button
BackColor = Color.FromArgb(52, 152, 219);  // Blue
Cursor = Cursors.Hand;

// Disabled button
BackColor = Color.FromArgb(189, 195, 199); // Gray
Cursor = Cursors.Default;
```

---

## 📊 Progress Tracking

### Download Progress (Step 1)

```csharp
var progress = new Progress<FileCopyInfo>(fileInfo =>
{
    UpdateDownloadProgress(stepNode, fileInfo);
});

await _copyService.CopyPackageWithVerificationAsync(
    _config,
    _package,
    _localDestPath,
    progress);
```

**Updates:**
- File-by-file progress percentage
- Copy status (Copying → Downloaded → Verifying → Verified)
- Hash comparison results
- Error messages if any

### Multi-Destination Progress (Step 3)

```csharp
var progress = new Progress<List<DestinationCopyResult>>(results =>
{
    UpdateMultiCopyProgress(stepNode, results);
});

await _multiCopyService.CopyToMultipleDestinationsAsync(
    _copiedPackagePath,
    _package.FolderName,
    enabledDests,
    progress);
```

**Updates:**
- Per-destination status (Pending → Connecting → Copying → Verifying → Complete)
- Files copied/verified count per destination
- Per-file progress and hash verification per destination
- Parallel execution - tất cả destinations run đồng thời

---

## 🔧 Technical Implementation

### Node Data Structure

```csharp
private class NodeData
{
    public NodeType Type { get; set; }        // Package, Step, Destination, File
    public int StepNumber { get; set; }       // 1, 2, 3
    public string? DestinationName { get; set; }
    public FileCopyInfo? FileInfo { get; set; }
}

private enum NodeType
{
    Package,      // Root node
    Step,         // Step 1/2/3 nodes
    Destination,  // A1, A2, etc.
    File          // Individual files
}
```

### Finding Nodes

```csharp
private TreeNode? FindStepNode(int stepNumber)
{
    var rootNode = _treeView.Nodes[0];

    foreach (TreeNode node in rootNode.Nodes)
    {
        if (node.Tag is NodeData data && 
            data.Type == NodeType.Step && 
            data.StepNumber == stepNumber)
        {
            return node;
        }
    }

    return null;
}
```

### Updating Status

```csharp
private void UpdateDownloadProgress(TreeNode parentNode, FileCopyInfo fileInfo)
{
    // Create or update file node
    if (!_fileNodes.ContainsKey(fileInfo.FileName))
    {
        var fileNode = new TreeNode($"📄 {fileInfo.FileName}");
        parentNode.Nodes.Add(fileNode);
        _fileNodes[fileInfo.FileName] = fileNode;
    }

    var node = _fileNodes[fileInfo.FileName];

    // Update text based on status
    node.Text = fileInfo.Status switch
    {
        FileCopyStatus.Copying => $"📄 {fileInfo.FileName} - ⏳ {fileInfo.Progress:F1}%",
        FileCopyStatus.Verified => $"📄 {fileInfo.FileName} - ✅ Verified",
        // ... other statuses
    };

    // Update color
    node.ForeColor = fileInfo.Status switch
    {
        FileCopyStatus.Verified => Color.Green,
        FileCopyStatus.Failed => Color.Red,
        // ... other colors
    };

    // Add hash comparison child nodes
    if (fileInfo.Status == FileCopyStatus.Verified)
    {
        node.Nodes.Add(new TreeNode($"Expected: {fileInfo.ExpectedHash}"));
        node.Nodes.Add(new TreeNode($"Actual:   {fileInfo.ActualHash}"));
    }
}
```

---

## 💡 Key Features

### 1. **Sequential Step Execution**
- Steps must be done in order
- Next step only enables after current step succeeds
- Clear visual feedback on what to do next

### 2. **Hierarchical Tree View**
- Package → Steps → Destinations → Files
- Expandable/collapsible for better organization
- Icons and colors for quick status identification

### 3. **Real-Time Progress**
- Progress percentage for file copies
- Status updates as they happen
- No need to refresh or reload

### 4. **Hash Verification Display**
- Expected hash from `.hash` file
- Actual hash computed after copy
- Visual comparison (green = match, red = mismatch)
- Shown both in Step 1 (local) and Step 3 (destinations)

### 5. **Multi-Destination Parallel Copy**
- All destinations copy simultaneously
- Independent progress tracking
- One slow/failed destination doesn't block others
- Per-destination per-file status

### 6. **Cancellation Support**
- `CancellationTokenSource` for each operation
- Graceful shutdown on dialog close
- Cleanup in `OnFormClosing`

---

## 🐛 Error Handling

### Download Errors (Step 1)

```csharp
try
{
    var result = await _copyService.CopyPackageWithVerificationAsync(...);

    if (result.Success)
    {
        step1Node.Text = "✅ Step 1: Complete";
        _btnStep2.Enabled = true;
    }
    else
    {
        step1Node.Text = $"⚠️ Step 1: Completed with {result.FailedFiles} errors";
    }
}
catch (Exception ex)
{
    step1Node.Text = "❌ Step 1: Failed";
    MessageBox.Show($"Error: {ex.Message}");
}
```

### Multi-Destination Errors (Step 3)

```csharp
// Per-destination error handling
foreach (var result in results)
{
    if (result.Status == DestinationStatus.Failed)
    {
        destNode.Text = $"❌ {result.DestinationName} - Failed: {result.ErrorMessage}";
        destNode.ForeColor = Color.Red;
    }
    else if (result.Status == DestinationStatus.CompletedWithErrors)
    {
        destNode.Text = $"⚠️ {result.DestinationName} - {result.FilesFailed} files failed";
        destNode.ForeColor = Color.Orange;
    }
}
```

---

## 📋 Comparison with Old Workflow

### Old: WorkflowProgressDialog

```
✅ Tree view cho workflow steps
✅ Real-time progress
❌ Requires separate calls from MainForm for each step
❌ Complex coordination between form and dialog
❌ Dialog closes automatically
❌ No step buttons
```

### New: UnifiedWorkflowDialog

```
✅ All-in-one dialog
✅ Step buttons built-in
✅ Self-contained - no external coordination needed
✅ Dialog stays open until user closes
✅ Clear sequential flow
✅ Better UX with step-by-step guidance
```

---

## 🎓 Usage Example

### From MainForm

```csharp
private void BtnUnifiedWorkflow_Click(object? sender, EventArgs e)
{
    // Get selected package
    var package = GetSelectedPackage();

    // Open unified dialog
    using var dialog = new UnifiedWorkflowDialog(
        _scannerService,
        _copyService,
        _multiCopyService,
        _config,
        package,
        _localDestPath,
        _destinations);

    // Show modal
    var result = dialog.ShowDialog(this);

    if (result == DialogResult.OK)
    {
        // Workflow completed
        _lastCopiedPackagePath = Path.Combine(_localDestPath, package.FolderName);
        ShowDestinationButtons();
    }
}
```

### User Flow

```
1. User clicks "🚀 Start Unified Workflow"
2. Dialog opens with 3 step buttons
3. User clicks "1️⃣ Download from SMB"
   → Files download, tree updates in real-time
   → Hash verification shown inline
4. Step 1 completes → Step 2 button enables
5. User clicks "2️⃣ Verify Checksums"
   → Instant complete (already done in Step 1)
   → Step 3 button enables (if destinations configured)
6. User clicks "3️⃣ Copy to Destinations"
   → All destinations copy in parallel
   → Tree shows per-destination per-file status
   → Hash verification shown for each destination
7. All steps complete → User reviews results
8. User clicks "Close" when satisfied
```

---

## 🚀 Benefits

1. **Simplified UX**: One dialog for entire workflow
2. **Clear Guidance**: Step buttons show what to do next
3. **Better Visibility**: Tree view shows all details
4. **Parallel Execution**: Multi-destination copy runs concurrently
5. **Hash Transparency**: Expected vs Actual always visible
6. **Error Resilience**: Failed destination doesn't stop others
7. **Self-Contained**: No coordination needed from parent form

---

## 📝 Notes

- Dialog is **modal** (`ShowDialog`) for simplicity
- Each step can be **re-run** if needed (just disable the button after first run)
- **CancellationToken** passed to all async operations for clean cancellation
- Tree nodes use **Tag** property to store metadata for easy lookup
- **Progress bars** updated incrementally for smooth visual feedback
- **Colors and icons** chosen for accessibility and clarity

---

**Created by**: Copilot AI  
**Version**: 3.0 - Unified Workflow Release  
**Date**: 2025
