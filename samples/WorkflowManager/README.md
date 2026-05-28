# Workflow Manager

Ứng dụng WinForms hiện đại để quét và copy các package từ SMB share theo đúng cấu trúc workflow đã định nghĩa trong `WORKFLOW.md`.

## 🎨 Giao diện hiện đại

Ứng dụng có giao diện đẹp mắt với:
- 🎯 **Modern UI**: Clean layout với color scheme chuyên nghiệp
- 🌈 **Color-coded**: Packages hợp lệ (xanh) / không hợp lệ (đỏ)
- 📊 **Status indicators**: Connection status, package count, progress tracking
- 💡 **Icons**: Emoji icons cho buttons và status messages
- 🎨 **Professional colors**: Màu xanh dương chủ đạo với accents
- ⚡ **Responsive**: Smooth animations và real-time updates

### UI Components

#### 🔗 SMB Connection Panel
- Share path, username, password inputs với styling đẹp
- Connection status indicator (🔴 Not Connected / 🟡 Connecting / 🟢 Connected)
- Modern scan button với icon

#### 📋 Package List Panel  
- Custom ListView với dark headers
- Color-coded rows (green cho valid, red cho invalid)
- Package count summary
- Sortable columns

#### 📁 Copy Panel
- Destination path selection
- Browse button với icon
- Large copy button
- Smooth progress bar

#### 📊 Status Bar
- Dark blue status bar at bottom
- Real-time status messages với icons

## Tính năng

1. **Quét packages từ SMB share (Recursive)**
   - Kết nối đến SMB share với thông tin xác thực
   - **Quét đệ quy** tất cả các thư mục con (max depth: 10 levels)
   - Validate cấu trúc package theo workflow
   - Hiển thị danh sách packages hợp lệ với màu sắc trực quan
   - Real-time progress: số thư mục đã scan, số packages đã tìm thấy
   - Tự động nhận diện package folders (có file `.hash`)
   - Bỏ qua nested packages (không scan trong package folders)

2. **Copy package xuống local**
   - Chọn package từ danh sách
   - Copy toàn bộ files trong package xuống local
   - Hiển thị progress bar và status real-time
   - Tự động tạo folder đích

## Cấu hình mặc định

- **Share Path**: `\\192.168.1.250\share\image`
- **Username**: `share`
- **Password**: `1234567890`

## Cấu trúc package hợp lệ

Một package được coi là hợp lệ khi có đầy đủ các file sau:

- `ABCD.hash` - File chứa MD5 checksum
- `ABCD.whd` - File metadata (100KB)
- `ABCD.wcl` - File dữ liệu chính (1GB)
- `ABCD.w01` đến `ABCD.wxx` - Files dữ liệu phân mảnh (5-12 files)

Trong đó:
- Tên folder và tên gốc file phải match format: `[A-Z0-9-]+`
- Số lượng file `.wxx` phải từ 5 đến 12
- Tất cả files phải cùng tên gốc (base name)

## Cách sử dụng

### 1. Cấu hình kết nối SMB

- Nhập **Share Path** (đường dẫn UNC đến share)
- Nhập **Username** và **Password** để xác thực
- Ứng dụng đã điền sẵn giá trị mặc định

### 2. Quét packages

- Click nút **🔍 Scan Packages** để quét các packages trên share
- Ứng dụng sẽ hiển thị danh sách packages với thông tin:
  - **Folder Name**: Tên thư mục
  - **Base Name**: Tên gốc của bộ files
  - **Total Size**: Tổng dung lượng
  - **Files**: Số lượng files
  - **Wxx**: Số lượng file `.wxx`
  - **Status**: Trạng thái (✅ Valid / ❌ Invalid)

- **Màu sắc packages**:
  - 🟢 **Xanh nhạt**: Package hợp lệ (có đủ files, đúng format)
  - 🔴 **Đỏ nhạt**: Package không hợp lệ (thiếu files, sai format)

### 3. Copy package xuống local

- Chọn **📁 Destination** (đường dẫn local để lưu package)
  - Mặc định: My Documents\Packages
  - Click **📂 Browse** để chọn folder khác
- Click vào package trong danh sách để chọn
- Click nút **⬇️ Copy Selected Package**
- Ứng dụng sẽ:
  - Tạo folder đích
  - Copy từng file với progress bar
  - Hiển thị % complete và tên file đang copy
  - Hiển thị thông báo khi hoàn thành

## Kiến trúc

### Models
- `PackageInfo`: Thông tin về một package
- `SmbConnectionConfig`: Cấu hình kết nối SMB
- `CopyProgress`: Thông tin về tiến trình copy

### Services
- `PackageScannerService`: Quét và validate packages
- `PackageCopyService`: Copy packages từ SMB xuống local

### Dependencies
- `SmbEnterprise.Core`: Core functionality
- `SmbEnterprise.Protocol.SMB`: SMB protocol implementation
- `SmbEnterprise.Transfer`: File transfer utilities
- `SmbEnterprise.Checksum`: Checksum calculation
- `Microsoft.Extensions.DependencyInjection`: Dependency injection
- `Microsoft.Extensions.Logging`: Logging
- `Serilog`: Structured logging

## Logs

Logs được lưu trong folder `logs/` với format:
- `workflow-manager-YYYYMMDD.txt`
- Rolling interval: Daily

## Xử lý lỗi

Ứng dụng xử lý các lỗi thường gặp với thông báo rõ ràng:
- 🔴 Lỗi kết nối SMB (sai thông tin xác thực, network issue)
- ⚠️ Lỗi quyền truy cập (không có quyền đọc/ghi)
- ❌ Lỗi folder structure không hợp lệ
- 💾 Lỗi disk space không đủ
- 🔒 Lỗi file đang được sử dụng

Tất cả lỗi đều được log vào file và hiển thị thông báo thân thiện cho người dùng.

## Build và chạy

```bash
# Build project
dotnet build samples/WorkflowManager/WorkflowManager.csproj

# Run project
dotnet run --project samples/WorkflowManager/WorkflowManager.csproj
```

Hoặc mở solution trong Visual Studio và chạy project `WorkflowManager`.

## Color Scheme

- **Primary Blue**: `#2980B9` - Buttons, headers, accents
- **Dark Blue**: `#34495E` - Status bar, table headers
- **Success Green**: `#27AE60` - Valid packages, success messages  
- **Error Red**: `#C0392B` - Invalid packages, error messages
- **Light Gray**: `#95A5A6` - Browse button, secondary elements
- **Background**: `#F0F0F5` - Form background
- **White**: `#FFFFFF` - Panel backgrounds

## Tips

- **Keyboard shortcuts**: Enter trong textbox để trigger scan
- **Double-click**: Double-click package để xem details (future feature)
- **Sorting**: Click column headers để sort (future feature)
- **Multi-select**: Giữ Ctrl để chọn nhiều packages (future feature)

