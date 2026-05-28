# WorkflowManager - Quick Start Guide

## Giới thiệu

WorkflowManager là ứng dụng WinForms để quét và copy các package từ SMB share theo đúng cấu trúc workflow được định nghĩa trong `WORKFLOW.md`.

## Tính năng chính

✅ **Quét packages từ SMB share**
- Tự động kết nối và authenticate với SMB server
- Quét tất cả folders và validate cấu trúc
- Hiển thị danh sách packages hợp lệ/không hợp lệ

✅ **Copy package xuống local**
- Chọn package từ danh sách
- Copy toàn bộ files với progress tracking
- Tự động tạo folder structure

✅ **Validation theo workflow**
- Kiểm tra tên folder format: `[A-Z0-9-]+`
- Kiểm tra cấu trúc files: `.hash`, `.whd`, `.wcl`, `.wxx`
- Kiểm tra số lượng files: 5-12 `.wxx` files

## Cấu hình mặc định

```
Share Path: \\192.168.1.250\share\image
Username:   share
Password:   1234567890
```

## Cài đặt và chạy

### Prerequisites

- .NET 8 SDK
- Windows 10/11
- Network access đến SMB share

### Build

```bash
# Clone repository (nếu chưa có)
git clone https://github.com/thanhbinhqs/BplusSMBLib
cd BplusSMBLib

# Build project
dotnet build samples/WorkflowManager/WorkflowManager.csproj

# Hoặc build toàn bộ solution
dotnet build
```

### Run

```bash
# Run từ command line
dotnet run --project samples/WorkflowManager/WorkflowManager.csproj

# Hoặc mở Visual Studio
# Set WorkflowManager làm StartUp Project
# Press F5
```

### Publish (tạo executable)

```bash
# Self-contained (không cần .NET runtime)
dotnet publish samples/WorkflowManager/WorkflowManager.csproj -c Release -r win-x64 --self-contained

# Framework-dependent (cần .NET runtime)
dotnet publish samples/WorkflowManager/WorkflowManager.csproj -c Release -r win-x64 --no-self-contained

# Output: samples/WorkflowManager/bin/Release/net8.0-windows/win-x64/publish/
```

## Cách sử dụng

### 1. Khởi động ứng dụng

Mở `WorkflowManager.exe` hoặc chạy từ Visual Studio.

### 2. Cấu hình SMB Connection

Ứng dụng đã điền sẵn cấu hình mặc định:
- **Share Path**: `\\192.168.1.250\share\image`
- **Username**: `share`
- **Password**: `1234567890`

Bạn có thể thay đổi nếu cần.

### 3. Scan packages

Click nút **Scan** để quét packages trên share.

Ứng dụng sẽ:
- Kết nối đến SMB share
- Liệt kê tất cả folders
- Validate từng folder theo workflow
- Hiển thị kết quả trong bảng

**Màu sắc**:
- 🟢 Xanh: Package hợp lệ (có đủ files, đúng format)
- 🔴 Đỏ: Package không hợp lệ (thiếu files, sai format)

### 4. Copy package xuống local

1. Chọn **Destination Path** (mặc định: `My Documents\Packages`)
2. Click vào package trong danh sách để chọn
3. Click nút **Copy Selected Package**

Ứng dụng sẽ:
- Tạo folder đích (nếu chưa có)
- Copy từng file với progress bar
- Hiển thị thông báo khi hoàn thành

## Cấu trúc package hợp lệ

Một package được coi là hợp lệ khi:

```
SGLC123A01C01G001S0001/
├── SGLC123A01-C01-G001-S0001.hash    ✓ Bắt buộc
├── SGLC123A01-C01-G001-S0001.whd     ✓ Bắt buộc (100KB)
├── SGLC123A01-C01-G001-S0001.wcl     ✓ Bắt buộc (1GB)
├── SGLC123A01-C01-G001-S0001.w01     ✓ Bắt buộc (1GB)
├── SGLC123A01-C01-G001-S0001.w02     ✓ Bắt buộc (1GB)
├── SGLC123A01-C01-G001-S0001.w03     ✓ Bắt buộc (1GB)
├── SGLC123A01-C01-G001-S0001.w04     ✓ Bắt buộc (1GB)
└── SGLC123A01-C01-G001-S0001.w05     ✓ Bắt buộc (≤1GB)
```

**Quy tắc**:
- Tên folder: `[A-Z0-9-]+` (uppercase, digits, hyphens)
- Tên files: Cùng base name
- Số lượng `.wxx`: 5-12 files
- File `.hash` chứa MD5 checksums

## Logs

Logs được lưu tại: `logs/workflow-manager-YYYYMMDD.txt`

```bash
# View logs
Get-Content logs\workflow-manager-*.txt -Tail 50

# Follow logs
Get-Content logs\workflow-manager-*.txt -Wait
```

## Troubleshooting

### ❌ "Connection failed: Access denied"

**Nguyên nhân**: Sai username/password hoặc không có quyền truy cập

**Giải pháp**:
1. Kiểm tra username/password
2. Thử kết nối bằng Windows Explorer: `\\192.168.1.250\share`
3. Kiểm tra quyền share trên server

### ❌ "Connection failed: Timeout"

**Nguyên nhân**: Network không kết nối được đến server

**Giải pháp**:
1. Kiểm tra network: `ping 192.168.1.250`
2. Kiểm tra port 445: `Test-NetConnection -ComputerName 192.168.1.250 -Port 445`
3. Kiểm tra firewall

### ❌ "No packages found"

**Nguyên nhân**: Không có package nào hợp lệ trong share

**Giải pháp**:
1. Kiểm tra share path có đúng không
2. Kiểm tra có folders trong share không
3. Xem logs để biết folder nào bị skip và tại sao

### ❌ "Copy failed: Insufficient disk space"

**Nguyên nhân**: Không đủ dung lượng trên ổ đích

**Giải pháp**:
1. Giải phóng disk space
2. Chọn ổ đĩa khác
3. Copy từng phần nhỏ hơn

## Documentation

Để biết thêm chi tiết, xem:

- **README.md**: Tổng quan về project
- **ARCHITECTURE.md**: Chi tiết về kiến trúc và design decisions
- **TESTING.md**: Hướng dẫn test và debug
- **WORKFLOW.md**: Spec về cấu trúc package

## Support

- **GitHub Issues**: https://github.com/thanhbinhqs/BplusSMBLib/issues
- **Documentation**: Xem các file `.md` trong project

## Version History

### v1.0.0 (Current)
- ✅ Scan packages từ SMB share
- ✅ Validate package structure theo workflow
- ✅ Copy packages với progress tracking
- ✅ Comprehensive logging
- ✅ Error handling

### Future Roadmap
- ⏳ Parallel scan cho performance
- ⏳ Checksum verification sau khi copy
- ⏳ Batch copy multiple packages
- ⏳ Resume interrupted transfers
- ⏳ Background monitoring service

## License

[Xem LICENSE file trong repository]

---

**Developed with**: .NET 8, WinForms, SmbEnterprise libraries
