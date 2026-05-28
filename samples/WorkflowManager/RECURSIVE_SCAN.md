# Recursive Package Scanning

## Tổng quan

WorkflowManager sử dụng **recursive directory scanning** để tìm tất cả packages trên SMB share, không chỉ ở level đầu tiên mà còn trong tất cả các thư mục con.

## Cách hoạt động

### 1. Scan Flow

```
Start Scan
    ↓
Connect to SMB
    ↓
Scan Root Directory (\\server\share\path)
    ↓
For each subdirectory:
    ├── Is it a package? (has .hash file)
    │   ├── Yes → Analyze and add to results
    │   │         Stop scanning this branch
    │   └── No  → Continue scanning subdirectories
    │             (recursive call with depth + 1)
    ↓
Return all found packages
```

### 2. Package Detection

Một thư mục được coi là **package** khi:
- Có file với extension `.hash`
- Tên folder match format: `[A-Z0-9-]+`

Khi tìm thấy package:
- ✅ Phân tích chi tiết (analyze)
- ✅ Thêm vào danh sách kết quả
- 🛑 **Dừng scan trong thư mục này** (không scan subdirectories)

### 3. Depth Limit

```csharp
const int MaxDepth = 10;
```

**Lý do**:
- Tránh infinite recursion
- Tránh scan quá sâu (performance)
- Packages thường không nằm quá sâu trong cấu trúc thư mục

**Ví dụ cấu trúc**:
```
\\server\share\image\              (depth 0)
  └── 2024\                        (depth 1)
      └── Q1\                      (depth 2)
          └── Jan\                 (depth 3)
              └── PACKAGE001\      (depth 4) ✅ Found package
                  ├── *.hash
                  ├── *.whd
                  └── ...
```

## Ví dụ Scan

### Scenario 1: Flat Structure

```
\\192.168.1.250\share\image\
  ├── PACKAGE-A-001\     ✅ Package
  ├── PACKAGE-B-002\     ✅ Package
  └── PACKAGE-C-003\     ✅ Package

Result: 3 packages found from 4 directories scanned
```

### Scenario 2: Nested Structure

```
\\192.168.1.250\share\image\
  ├── 2024\
  │   ├── January\
  │   │   ├── PACKAGE-A-001\    ✅ Package
  │   │   └── PACKAGE-A-002\    ✅ Package
  │   └── February\
  │       └── PACKAGE-B-001\    ✅ Package
  └── 2023\
      └── December\
          └── PACKAGE-C-001\    ✅ Package

Result: 4 packages found from 8 directories scanned
```

### Scenario 3: Mixed Structure

```
\\192.168.1.250\share\image\
  ├── PACKAGE-ROOT\              ✅ Package (depth 1)
  │   └── subfolder\             🛑 Not scanned (inside package)
  ├── projects\
  │   ├── active\
  │   │   └── PACKAGE-A\         ✅ Package (depth 3)
  │   └── archived\
  │       └── old-files\
  └── temp\                      ⚠️ No packages

Result: 2 packages found from 6 directories scanned
```

## Progress Reporting

### Status Updates

During scan, UI receives progress updates:

```csharp
public sealed class ScanProgress
{
    public string Status { get; init; }              // Current action
    public int ScannedDirectories { get; init; }     // Total dirs scanned
    public int FoundPackages { get; init; }          // Packages found so far
    public int CurrentDepth { get; init; }           // Current depth level
}
```

### Progress Messages

```
🔄 Connecting to SMB share...
   (ScannedDirectories: 0, FoundPackages: 0)

🔍 Scanning: \\server\share\image
   (ScannedDirectories: 1, FoundPackages: 0)

🔍 Scanning: \\server\share\image\2024
   (ScannedDirectories: 2, FoundPackages: 0)

📦 Found package: PACKAGE-A-001
   (ScannedDirectories: 3, FoundPackages: 1)

✅ Scan completed: 4 packages found
   (ScannedDirectories: 125, FoundPackages: 4)
```

## Performance Characteristics

### Time Complexity

- **Best case**: O(n) - all packages at root level
- **Worst case**: O(n × d) - deep nested structure
  - n = total directories
  - d = average depth

### Network Calls

- **1 call per directory**: ListDirectoryAsync()
- **No redundant calls**: Each directory scanned once
- **Early termination**: Stop when package found

### Optimization Strategies

1. **Skip Package Contents**
   ```csharp
   if (IsPackageDirectory(files))
   {
       AnalyzePackage();
       return; // Don't scan subdirectories
   }
   ```

2. **Depth Limiting**
   ```csharp
   if (depth > MaxDepth)
       return;
   ```

3. **Parallel Scanning** (Future)
   ```csharp
   // Potential optimization
   await Parallel.ForEachAsync(directories, 
       async (dir, ct) => await ScanAsync(dir, ct));
   ```

## Error Handling

### Directory Access Errors

```csharp
try
{
    await ScanDirectoryRecursiveAsync(...);
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "Failed to scan subdirectory: {DirName}", dir.Name);
    // Continue scanning other directories
}
```

**Behavior**:
- ⚠️ Log warning
- ✅ Continue with other directories
- ✅ Don't fail entire scan

### Common Errors

| Error | Cause | Handling |
|-------|-------|----------|
| `UnauthorizedAccessException` | No read permission | Log + skip |
| `DirectoryNotFoundException` | Directory deleted during scan | Log + skip |
| `PathTooLongException` | Path exceeds limit | Log + skip |
| `IOException` | Network issue | Log + skip |

## Logging

### Log Levels

```csharp
// Information: Major events
_logger.LogInformation("Found valid package: {Path}", path);

// Debug: Detailed scanning
_logger.LogDebug("Scanning directory: {Path} (depth: {Depth})", path, depth);

// Warning: Non-critical issues
_logger.LogWarning("Max depth {MaxDepth} reached at path: {Path}", MaxDepth, path);
_logger.LogWarning(ex, "Failed to scan subdirectory: {DirName}", dirName);
```

### Example Log Output

```
[10:30:15 INF] Connecting to SMB share: \\192.168.1.250\share\image
[10:30:16 INF] Connected successfully
[10:30:16 DBG] Scanning directory: \\image (depth: 0)
[10:30:16 DBG] Scanning directory: \\image\2024 (depth: 1)
[10:30:17 DBG] Scanning directory: \\image\2024\Jan (depth: 2)
[10:30:18 INF] Found valid package: \\image\2024\Jan\PACKAGE-A
[10:30:18 DBG] Scanning directory: \\image\2024\Feb (depth: 2)
[10:30:19 WRN] Failed to scan subdirectory: restricted-folder - Access denied
[10:30:20 INF] Scan completed: Found 4 packages from 125 directories
```

## Configuration

### Adjustable Parameters

#### Max Depth
```csharp
// In PackageScannerService.cs
const int MaxDepth = 10; // Change this to scan deeper/shallower
```

**Recommendations**:
- 5-10: Normal enterprise shares
- 15-20: Complex folder structures
- 3-5: Flat structures

#### Timeout (Future)
```csharp
// Potential addition
public async Task<List<PackageInfo>> ScanPackagesAsync(
    SmbConnectionConfig config,
    IProgress<ScanProgress>? progress = null,
    TimeSpan? timeout = null,
    CancellationToken cancellationToken = default)
```

## Comparison: Non-Recursive vs Recursive

### Before (Non-Recursive)

```csharp
// Only scans immediate children
foreach (var entry in fileSystem.ListDirectoryAsync(basePath))
{
    if (entry.IsDirectory)
        AnalyzePackage(entry);
}
```

**Pros**:
- ⚡ Fast (fewer network calls)
- 🎯 Predictable performance

**Cons**:
- ❌ Misses nested packages
- ❌ Requires flat structure
- ❌ Less flexible

### After (Recursive)

```csharp
// Scans all subdirectories recursively
async Task ScanRecursive(string path, int depth)
{
    foreach (var entry in await ListDirectory(path))
    {
        if (IsPackage(entry))
            AnalyzePackage(entry);
        else if (entry.IsDirectory)
            await ScanRecursive(entry.Path, depth + 1);
    }
}
```

**Pros**:
- ✅ Finds all packages regardless of location
- ✅ Flexible folder structure
- ✅ Better user experience

**Cons**:
- 🐢 Slower (more network calls)
- 📊 Variable performance
- 🔄 More complex logic

## Best Practices

### 1. Organize Packages Efficiently

```
✅ Good: Flat or shallow structure
\\share\packages\
  ├── PACKAGE-001\
  ├── PACKAGE-002\
  └── PACKAGE-003\

⚠️ Acceptable: Organized by category
\\share\packages\
  ├── active\
  │   └── PACKAGE-001\
  └── archived\
      └── PACKAGE-002\

❌ Avoid: Too deep nesting
\\share\packages\
  └── year\
      └── quarter\
          └── month\
              └── week\
                  └── day\
                      └── PACKAGE-001\  (depth 6!)
```

### 2. Use Naming Conventions

```
✅ Good: Consistent, valid names
SGLC123A01-C01-G001-S0001
PACKAGE-A-001
DATA-2024-Q1-001

❌ Bad: Invalid characters, spaces
Package #001
data (2024).backup
temp_folder
```

### 3. Monitor Scan Performance

```
- Track ScannedDirectories count
- Monitor scan time
- Check logs for warnings
- Optimize folder structure if needed
```

## Future Enhancements

### 1. Parallel Scanning
```csharp
await Parallel.ForEachAsync(
    directories,
    new ParallelOptions { MaxDegreeOfParallelism = 4 },
    async (dir, ct) => await ScanDirectoryAsync(dir, ct)
);
```

### 2. Caching
```csharp
// Cache scan results
var cacheKey = $"{sharePath}:{timestamp}";
_cache.Set(cacheKey, packages, TimeSpan.FromMinutes(10));
```

### 3. Filtering
```csharp
// Allow user to filter by date, size, etc.
public async Task<List<PackageInfo>> ScanPackagesAsync(
    SmbConnectionConfig config,
    ScanFilter? filter = null,
    ...)
{
    // Apply filters during scan
}
```

### 4. Incremental Scan
```csharp
// Only scan changed directories
var lastScanTime = GetLastScanTime();
if (directory.ModifiedTime > lastScanTime)
    await ScanDirectoryAsync(directory);
```

## Troubleshooting

### Problem: Scan takes too long

**Solutions**:
1. Reduce MaxDepth
2. Organize packages in shallower structure
3. Implement parallel scanning
4. Add timeout mechanism

### Problem: Some packages not found

**Check**:
1. Package has `.hash` file?
2. Folder name valid format?
3. Within MaxDepth limit?
4. No access permission issues?

### Problem: Out of memory

**Solutions**:
1. Don't load all packages at once
2. Implement paging/streaming
3. Process packages as found
4. Reduce cached data

## Summary

Recursive scanning enables WorkflowManager to find packages **anywhere** in the share structure, making it more flexible and user-friendly compared to flat-only scanning. The implementation balances:

- ✅ **Completeness**: Find all packages
- ⚡ **Performance**: Reasonable speed
- 🛡️ **Safety**: Depth limit, error handling
- 📊 **Visibility**: Progress reporting
- 🔧 **Maintainability**: Clean, testable code
