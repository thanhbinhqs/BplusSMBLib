# Quick Fix: Duplicate Files in Copy Progress Dialog

## Issue

Khi copy package, danh sách files trong dialog bị add nhiều lần (duplicates) thay vì chỉ hiển thị mỗi file một lần.

## Root Cause

### Problem Flow

1. Service report progress cho mỗi file nhiều lần:
   - `Status = Copying` (lần 1)
   - `Status = CopyComplete` (lần 2)
   - `Status = Verifying` (lần 3)
   - `Status = Verified` (lần 4)

2. MainForm check điều kiện sai:
```csharp
// SAI: completedFileNames chỉ có files đã complete
if (!completedFileNames.Contains(fileInfo.FileName) && 
    fileInfo.Status == FileCopyStatus.Copying)
{
    progressDialog.AddFile(fileInfo); // ❌ Vẫn có thể add nhiều lần
}
```

3. Dialog không check duplicate:
```csharp
public void AddFile(FileCopyInfo fileInfo)
{
    var item = new ListViewItem(fileInfo.FileName);
    // ...
    _lvFiles.Items.Add(item); // ❌ Add trực tiếp không check
    _fileItems[fileInfo.FileName] = item;
}
```

### Result
File bị add mỗi khi có progress report mới → duplicates trong ListView.

## Solution

### Fix #1: Dialog Check Duplicate

Thêm check trong `AddFile()`:

```csharp
public void AddFile(FileCopyInfo fileInfo)
{
    if (InvokeRequired)
    {
        Invoke(() => AddFile(fileInfo));
        return;
    }

    // ✅ Check if already added
    if (_fileItems.ContainsKey(fileInfo.FileName))
    {
        return; // Skip duplicate
    }

    var item = new ListViewItem(fileInfo.FileName);
    // ... setup item
    _lvFiles.Items.Add(item);
    _fileItems[fileInfo.FileName] = item;
}
```

**Dictionary `_fileItems`** đã track tất cả files, dùng nó để check duplicate.

### Fix #2: MainForm Track Added Files

Dùng HashSet riêng để track files đã add vào dialog:

```csharp
var completedFileNames = new HashSet<string>(); // For counting
var addedFileNames = new HashSet<string>();     // ✅ For tracking adds
var lockObj = new object();

var progress = new Progress<FileCopyInfo>(fileInfo =>
{
    // ✅ Add only once
    lock (lockObj)
    {
        if (addedFileNames.Add(fileInfo.FileName))
        {
            progressDialog.AddFile(fileInfo);
        }
    }

    // Always update (multiple times OK)
    progressDialog.UpdateFileProgress(fileInfo.FileName, fileInfo);

    // Count completed once
    if (isTerminalStatus)
    {
        lock (lockObj)
        {
            if (completedFileNames.Add(fileInfo.FileName))
            {
                progressDialog.UpdateOverallProgress(...);
            }
        }
    }
});
```

### Key Points

1. **Two HashSets**:
   - `addedFileNames`: Track files đã add vào dialog (add 1 lần)
   - `completedFileNames`: Track files đã complete (count 1 lần)

2. **HashSet.Add() returns bool**:
   - `true` nếu item mới (chưa tồn tại)
   - `false` nếu item đã tồn tại
   - Thread-safe khi dùng với `lock`

3. **Dialog double-checks**:
   - MainForm check trước khi add
   - Dialog check lại trong `AddFile()`
   - Defense in depth → không bao giờ có duplicate

## Testing

### Before Fix
```
ListView Items:
- File1.whd
- File1.whd        ← duplicate
- File1.whd        ← duplicate
- File1.whd        ← duplicate
- File2.wcl
- File2.wcl        ← duplicate
...
```

### After Fix
```
ListView Items:
- File1.whd        ← only once ✅
- File2.wcl        ← only once ✅
- File3.w01        ← only once ✅
...
```

## Code Changes Summary

### File: `CopyProgressDialog.cs`

```diff
public void AddFile(FileCopyInfo fileInfo)
{
    if (InvokeRequired)
    {
        Invoke(() => AddFile(fileInfo));
        return;
    }

+   // Check if file already exists in the list
+   if (_fileItems.ContainsKey(fileInfo.FileName))
+   {
+       return; // Already added, just skip
+   }

    var item = new ListViewItem(fileInfo.FileName);
    // ... rest of setup
}
```

### File: `Form1.cs`

```diff
// Track which files have been counted as complete
var completedFileNames = new HashSet<string>();
+var addedFileNames = new HashSet<string>(); // Track files already added
var lockObj = new object();

var progress = new Progress<FileCopyInfo>(fileInfo =>
{
-   // Old: check completedFileNames + status
-   if (!completedFileNames.Contains(fileInfo.FileName) && 
-       fileInfo.Status == FileCopyStatus.Copying)
-   {
-       progressDialog.AddFile(fileInfo);
-   }

+   // New: check addedFileNames only
+   lock (lockObj)
+   {
+       if (addedFileNames.Add(fileInfo.FileName))
+       {
+           progressDialog.AddFile(fileInfo);
+       }
+   }

    progressDialog.UpdateFileProgress(fileInfo.FileName, fileInfo);
    // ... rest of handler
});
```

## Prevention

### Best Practices

1. **Always check before add to collections**:
```csharp
if (!collection.Contains(item))
    collection.Add(item);

// Or better with HashSet:
if (hashSet.Add(item)) // Returns false if exists
    DoSomething();
```

2. **Use dictionaries for lookup**:
```csharp
private readonly Dictionary<string, ListViewItem> _fileItems = new();

if (!_fileItems.ContainsKey(key))
    _fileItems[key] = item;
```

3. **Thread-safe updates**:
```csharp
lock (lockObj)
{
    if (hashSet.Add(item))
    {
        // Do work only once
    }
}
```

4. **Progress<T> can fire multiple times**:
- Design handler to be idempotent
- Always check state before mutating

## Related Issues

- [x] Cross-thread operation exception → Fixed with `Progress<T>`
- [x] File lock exception → Fixed with flush + delay
- [x] Missing info in dialog → Fixed with dynamic add
- [x] Duplicate files in dialog → **Fixed now**

## Performance Impact

- **Before**: O(n * m) where n = files, m = progress reports per file
- **After**: O(n) - each file added exactly once
- **Memory**: Minimal (one HashSet with ~10-100 strings)
- **UI responsiveness**: Improved (fewer ListView operations)

## References

- [HashSet<T>.Add Method](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.hashset-1.add)
- [Dictionary<TKey,TValue>.ContainsKey](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.dictionary-2.containskey)
- [Progress<T> Class](https://learn.microsoft.com/en-us/dotnet/api/system.progress-1)
