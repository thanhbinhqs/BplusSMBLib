# Hướng dẫn Test và Debug WorkflowManager

## Chuẩn bị môi trường test

### 1. Tạo test data trên SMB share

Để test ứng dụng, bạn cần có dữ liệu test trên SMB share. Bạn có thể:

**Option A: Sử dụng dữ liệu thực có sẵn**
- Đảm bảo SMB share `\\192.168.1.250\share\image` có các folder với cấu trúc hợp lệ

**Option B: Tạo test data thủ công**

Tạo một folder test với cấu trúc như sau:

```
\\192.168.1.250\share\image\
  └── SGLC123A01C01G001S0001\
      ├── SGLC123A01-C01-G001-S0001.hash
      ├── SGLC123A01-C01-G001-S0001.whd (100KB)
      ├── SGLC123A01-C01-G001-S0001.wcl (1GB)
      ├── SGLC123A01-C01-G001-S0001.w01 (1GB)
      ├── SGLC123A01-C01-G001-S0001.w02 (1GB)
      ├── SGLC123A01-C01-G001-S0001.w03 (1GB)
      ├── SGLC123A01-C01-G001-S0001.w04 (1GB)
      └── SGLC123A01-C01-G001-S0001.w05 (512MB)
```

Nội dung file `.hash`:
```ini
[HASH]
wcl=d41d8cd98f00b204e9800998ecf8427e
w01=0cc175b9c0f1b6a831c399e269772661
w02=92eb5ffee6ae2fec3ad71c777531578f
w03=4a8a08f09d37b73795649038408b5f33
w04=8277e0910d750195b448797616e091ad
w05=e1671797c52e15f763380b45e841ec32
```

### 2. Kiểm tra kết nối network

```powershell
# Test ping đến server
ping 192.168.1.250

# Test SMB port
Test-NetConnection -ComputerName 192.168.1.250 -Port 445

# Thử mount share
net use Z: \\192.168.1.250\share /user:share 1234567890
```

## Build và chạy

### Từ Visual Studio

1. Mở solution trong Visual Studio
2. Set `WorkflowManager` làm StartUp Project (right-click -> Set as StartUp Project)
3. Press F5 để build và run với debugger
4. Hoặc Ctrl+F5 để run không debugger

### Từ command line

```bash
# Build
dotnet build samples/WorkflowManager/WorkflowManager.csproj

# Run
dotnet run --project samples/WorkflowManager/WorkflowManager.csproj

# Build release
dotnet build samples/WorkflowManager/WorkflowManager.csproj -c Release

# Run release
dotnet run --project samples/WorkflowManager/WorkflowManager.csproj -c Release
```

## Test scenarios

### Test 1: Kết nối và quét packages

1. Mở ứng dụng
2. Kiểm tra cấu hình SMB (mặc định đã điền sẵn)
3. Click nút **Scan**
4. **Expected**: 
   - Status hiển thị "Scanning packages..."
   - Sau vài giây hiển thị danh sách packages
   - Packages hợp lệ có màu nền xanh
   - Packages không hợp lệ có màu nền đỏ

### Test 2: Copy package xuống local

1. Sau khi scan thành công
2. Chọn destination path (hoặc giữ mặc định: My Documents\Packages)
3. Click vào một package trong danh sách
4. Click nút **Copy Selected Package**
5. **Expected**:
   - Progress bar hiển thị tiến trình
   - Status hiển thị tên file đang copy
   - Khi hoàn thành hiển thị message box success
   - Kiểm tra folder đích có đầy đủ files

### Test 3: Thay đổi cấu hình SMB

1. Thay đổi Share Path, Username, Password
2. Click **Scan**
3. **Expected**: Ứng dụng kết nối đến share mới

### Test 4: Xử lý lỗi

**Test 4.1: Sai username/password**
1. Nhập sai password
2. Click **Scan**
3. **Expected**: Hiển thị error message "Connection failed..."

**Test 4.2: Network không khả dụng**
1. Disconnect network hoặc nhập sai IP
2. Click **Scan**
3. **Expected**: Hiển thị error message timeout

**Test 4.3: Không đủ disk space**
1. Chọn destination path trên ổ đĩa gần đầy
2. Copy package lớn
3. **Expected**: Error message về disk space

**Test 4.4: Destination path không tồn tại**
1. Nhập path không hợp lệ: `C:\NonExistentFolder\Packages`
2. Copy package
3. **Expected**: Ứng dụng tự động tạo folder

## Debug tips

### Enable verbose logging

Mở file `Program.cs` và thay đổi log level:

```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Verbose()  // Changed from Debug
    .WriteTo.Console()
    .WriteTo.File("logs/workflow-manager-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();
```

### Xem logs

Logs được lưu trong folder `logs/` tại thư mục exe:
- Format: `workflow-manager-YYYYMMDD.txt`
- Mỗi ngày một file mới

```bash
# View latest log
Get-Content logs\workflow-manager-*.txt -Tail 50

# Follow log in real-time
Get-Content logs\workflow-manager-*.txt -Wait -Tail 50
```

### Breakpoint quan trọng

Đặt breakpoint tại các vị trí sau để debug:

1. **PackageScannerService.cs**:
   - Line `ScanPackagesAsync()`: Bắt đầu scan
   - Line `AnalyzePackageAsync()`: Phân tích từng package
   - Line `ValidFolderNameRegex()`: Validate tên folder

2. **PackageCopyService.cs**:
   - Line `CopyPackageAsync()`: Bắt đầu copy
   - Line `OpenReadAsync()`: Mở file từ SMB
   - Line `WriteAsync()`: Ghi file xuống local

3. **MainForm.cs**:
   - Line `BtnScan_Click()`: Xử lý click scan
   - Line `BtnCopy_Click()`: Xử lý click copy

### Common issues

**Issue 1: "Connection failed: Access denied"**
- Kiểm tra username/password
- Kiểm tra quyền truy cập SMB share
- Thử kết nối bằng Windows Explorer

**Issue 2: "No packages found"**
- Kiểm tra share path có đúng không
- Kiểm tra có folder nào trong share không
- Kiểm tra log để xem folder nào bị skip và tại sao

**Issue 3: "Copy failed: Destination path invalid"**
- Kiểm tra destination path có quyền ghi không
- Kiểm tra disk space
- Thử chọn folder khác

**Issue 4: Progress bar không update**
- Kiểm tra có đang block UI thread không
- Thêm `Application.DoEvents()` nếu cần (không khuyến khích)
- Verify `Progress<T>` đang được report đúng cách

## Performance testing

### Test với dữ liệu lớn

1. Tạo package với nhiều file (12 files x 1GB = 12GB)
2. Test copy và monitor:
   - Network throughput
   - Memory usage
   - CPU usage
   - Disk I/O

### Benchmark copy speed

```csharp
// Add to PackageCopyService
var stopwatch = Stopwatch.StartNew();
// ... copy logic ...
stopwatch.Stop();
var throughput = totalBytes / stopwatch.Elapsed.TotalSeconds / 1024 / 1024;
_logger.LogInformation("Copy completed in {ElapsedMs}ms, throughput: {Throughput:F2} MB/s", 
    stopwatch.ElapsedMilliseconds, throughput);
```

## Validation checklist

Trước khi release, kiểm tra:

- [ ] Scan thành công với valid packages
- [ ] Scan không crash với invalid packages
- [ ] Copy thành công với packages nhỏ (< 1GB)
- [ ] Copy thành công với packages lớn (> 10GB)
- [ ] Progress bar update đúng
- [ ] Error messages rõ ràng và hữu ích
- [ ] Logs đầy đủ thông tin debug
- [ ] Không memory leak sau nhiều lần scan/copy
- [ ] UI không bị freeze trong quá trình copy
- [ ] Cancel operation hoạt động đúng (nếu implement)
- [ ] Xử lý đúng các trường hợp network disconnect
