# Workflow: Sinh bộ file theo cấu trúc `ABCD.hash`, `ABCD.whd`, `ABCD.wcl`, `ABCD.wxx`

Tài liệu này mô tả workflow và yêu cầu implementation cho một script dùng để sinh ra một bộ file dữ liệu theo đúng cấu trúc nghiệp vụ đã chốt.

Mục tiêu của script:

1. Tạo một thư mục đầu ra hợp lệ.
2. Tạo trong thư mục đó một bộ file dùng chung một tên gốc hợp lệ.
3. Sinh file `.whd` dung lượng `100KB`.
4. Sinh file `.wcl` dung lượng `1GB`.
5. Sinh từ `5` đến `12` file `.wxx`, trong đó các file trước file cuối có dung lượng `1GB`, còn file cuối có thể nhỏ hơn.
6. Tính MD5 cho file `.wcl` và toàn bộ file `.wxx`.
7. Ghi file `.hash` theo đúng format `[HASH]` với cặp khóa/giá trị `wcl=<md5>` và `wxx=<md5>`.

## 1. Phạm vi và đầu ra mong muốn

Script phải tạo đúng một bộ file trong một thư mục đầu ra. Một bộ hợp lệ gồm:

- `ABCD.hash`
- `ABCD.whd`
- `ABCD.wcl`
- `ABCD.w01`
- `ABCD.w02`
- ...
- `ABCD.wxx`

Trong đó:

- `ABCD` là tên gốc của bộ file.
- `xx` là số thứ tự từ `01` đến tối đa `12`.
- Tổng số file `.wxx` phải nằm trong khoảng từ `5` đến `12`.

Script không cần phát sinh thêm file ngoài nhóm trên, trừ khi có yêu cầu cấu hình riêng ở tầng ứng dụng gọi script.

## 2. Quy tắc đặt tên

Tên hợp lệ có dạng nghiệp vụ:

- Có dấu `-`: `SGLC123A01-C01-G001-S0001`
- Không có dấu `-`: `SGLC123A01C01G001S0001`

Các quy tắc bắt buộc:

1. Tên thư mục đầu ra phải là một tên hợp lệ theo một trong hai dạng trên.
2. Tên gốc của bộ file cũng phải là một tên hợp lệ theo một trong hai dạng trên.
3. Tên thư mục và tên gốc file không bắt buộc phải giống nhau.
4. Trong cùng một bộ file, `.hash`, `.whd`, `.wcl` và toàn bộ `.wxx` phải dùng đúng cùng một tên gốc.
5. Nếu đã chọn kiểu tên có dấu `-` hoặc không có dấu `-` cho bộ file thì toàn bộ file trong bộ phải đồng nhất theo đúng tên gốc đó.

Ví dụ hợp lệ:

- Tên folder: `SGLC123A01C01G001S0001`
- Tên gốc bộ file: `SGLC123A01-C01-G001-S0001`

## 3. Quy tắc số lượng và dung lượng file

Script phải tuân thủ các ràng buộc sau:

### 3.1. File cố định

- `ABCD.whd`: dung lượng đúng `100KB`
- `ABCD.wcl`: dung lượng đúng `1GB`

### 3.2. File dữ liệu chia nhỏ `ABCD.wxx`

1. Tổng số file `.wxx` nằm trong khoảng từ `5` đến `12`.
2. Các file từ `w01` đến `w(xx-1)` phải có dung lượng đúng `1GB`.
3. File cuối `wxx` có thể nhỏ hơn `1GB`.
4. Nếu người dùng cấu hình file cuối cũng bằng `1GB` thì trường hợp đó vẫn hợp lệ.

Cách hiểu nghiệp vụ của nhóm `.wxx`:

- Đây là kết quả của việc cắt một file lớn thành nhiều phần.
- `xx - 1` phần đầu là các phần đầy đủ `1GB`.
- Phần cuối là phần còn lại, nên thường nhỏ hơn `1GB`.

## 4. Format file `.hash`

File `ABCD.hash` phải có nội dung theo đúng cấu trúc sau:

```ini
[HASH]
wcl=<hash md5 của file ABCD.wcl>
w01=<hash md5 của file ABCD.w01>
w02=<hash md5 của file ABCD.w02>
...
wxx=<hash md5 của file ABCD.wxx>
```

Quy tắc ghi file `.hash`:

1. Dòng đầu tiên luôn là `[HASH]`.
2. Phải có khóa `wcl`.
3. Phải có đầy đủ các khóa từ `w01` đến `wxx` theo đúng số file thực tế đã tạo.
4. Không ghi entry cho file `.whd`.
5. Giá trị mỗi dòng là MD5 dạng hex của đúng file tương ứng.
6. Thứ tự khuyến nghị khi ghi là `wcl`, sau đó `w01`, `w02`, ... đến `wxx`.

Ví dụ:

```ini
[HASH]
wcl=d41d8cd98f00b204e9800998ecf8427e
w01=0cc175b9c0f1b6a831c399e269772661
w02=92eb5ffee6ae2fec3ad71c777531578f
w03=4a8a08f09d37b73795649038408b5f33
w04=8277e0910d750195b448797616e091ad
w05=e1671797c52e15f763380b45e841ec32
```

## 5. Trình tự xử lý khuyến nghị

Script nên xử lý theo đúng thứ tự sau để tránh sai lệch giữa nội dung file và checksum:

### Bước 1: Nhận và validate input

Input tối thiểu nên gồm:

- đường dẫn thư mục cha đầu ra;
- tên folder cần tạo;
- tên gốc bộ file;
- số lượng file `.wxx` từ `5` đến `12`;
- dung lượng file cuối cùng `wxx`.

Ở bước này cần validate:

1. Tên folder hợp lệ theo rule nghiệp vụ.
2. Tên gốc bộ file hợp lệ theo rule nghiệp vụ.
3. Số lượng file `.wxx` nằm trong khoảng `5..12`.
4. Dung lượng file cuối lớn hơn `0` và không vượt quá `1GB`.

### Bước 2: Tạo thư mục đầu ra

Tạo thư mục theo tên folder đã chọn bên dưới thư mục cha đầu ra.

Ví dụ:

```text
<output-root>\\SGLC123A01C01G001S0001
```

Nếu thư mục đã tồn tại thì implementation thực tế cần quyết định rõ một trong các cách sau:

- báo lỗi và dừng;
- cho phép ghi đè;
- hoặc yêu cầu thư mục phải trống trước khi chạy.

Khuyến nghị mặc định là báo lỗi nếu thư mục đã tồn tại để tránh ghi đè nhầm dữ liệu.

### Bước 3: Sinh file `.whd`

Tạo file:

```text
ABCD.whd
```

với dung lượng đúng `100KB`.

File này không cần ghi vào manifest `.hash`, nhưng vẫn phải dùng đúng cùng tên gốc với cả bộ file.

### Bước 4: Sinh file `.wcl`

Tạo file:

```text
ABCD.wcl
```

với dung lượng đúng `1GB`.

Sau khi tạo xong file này, cần tính MD5 để chuẩn bị ghi vào `.hash`.

### Bước 5: Sinh dãy file `.wxx`

Tạo lần lượt các file:

```text
ABCD.w01
ABCD.w02
...
ABCD.wxx
```

Quy tắc tạo dung lượng:

1. Từ `w01` đến `w(xx-1)`: mỗi file đúng `1GB`.
2. File cuối `wxx`: dùng dung lượng cấu hình, có thể nhỏ hơn `1GB`.

Sau khi tạo từng file xong, tính ngay MD5 của file đó và lưu vào danh sách manifest tạm trong bộ nhớ.

### Bước 6: Ghi file `.hash`

Sau khi đã có đầy đủ checksum của `wcl` và toàn bộ `wxx`, tạo file:

```text
ABCD.hash
```

với nội dung theo đúng format `[HASH]`.

Không được ghi `.hash` trước khi hoàn tất việc tạo và tính MD5 cho các file cần tham chiếu.

## 6. Cấu trúc dữ liệu đề xuất cho implementation

Nếu cần triển khai thành script hoặc utility có tái sử dụng, nên tổ chức dữ liệu đầu vào và đầu ra theo các model tương tự sau:

```csharp
public sealed record PackageGenerationOptions(
    string OutputRoot,
    string FolderName,
    string BaseName,
    int SegmentCount,
    long LastSegmentSizeBytes,
    bool Overwrite);

public sealed record GeneratedFileEntry(
    string Key,
    string FileName,
    long SizeBytes,
    string Md5);

public sealed record PackageGenerationResult(
    string OutputDirectory,
    string HashFilePath,
    IReadOnlyList<GeneratedFileEntry> ManifestEntries);
```

Ý nghĩa:

- `FolderName`: tên thư mục đầu ra.
- `BaseName`: tên gốc dùng chung cho `.hash`, `.whd`, `.wcl`, `.wxx`.
- `SegmentCount`: số lượng file `.wxx`.
- `LastSegmentSizeBytes`: dung lượng file cuối.
- `ManifestEntries`: danh sách checksum thực tế đã ghi vào `.hash`.

## 7. Pseudocode xử lý end-to-end

```text
1. Validate folderName và baseName theo format hợp lệ.
2. Validate segmentCount trong khoảng 5..12.
3. Validate lastSegmentSizeBytes > 0 và <= 1GB.
4. Tạo outputDirectory.
5. Tạo file <baseName>.whd với kích thước 100KB.
6. Tạo file <baseName>.wcl với kích thước 1GB.
7. Tính MD5 cho <baseName>.wcl và lưu với key wcl.
8. Với i từ 1 đến segmentCount:
9.   Tạo tên key = w + i dạng 2 chữ số.
10.  Nếu i < segmentCount thì size = 1GB, ngược lại size = lastSegmentSizeBytes.
11.  Tạo file <baseName>.<key> với dung lượng size.
12.  Tính MD5 cho file vừa tạo.
13.  Lưu { key, fileName, size, md5 } vào manifest.
14. Ghi file <baseName>.hash theo format [HASH].
15. Trả về thông tin thư mục đầu ra và danh sách file đã sinh.
```

## 8. Ví dụ đầu ra hợp lệ

Ví dụ:

- Tên folder: `SGLC123A01C01G001S0001`
- Tên gốc bộ file: `SGLC123A01-C01-G001-S0001`
- Số file dữ liệu chia nhỏ: `5`

Cấu trúc thư mục:

```text
SGLC123A01C01G001S0001\\
  SGLC123A01-C01-G001-S0001.hash
  SGLC123A01-C01-G001-S0001.whd
  SGLC123A01-C01-G001-S0001.wcl
  SGLC123A01-C01-G001-S0001.w01
  SGLC123A01-C01-G001-S0001.w02
  SGLC123A01-C01-G001-S0001.w03
  SGLC123A01-C01-G001-S0001.w04
  SGLC123A01-C01-G001-S0001.w05
```

Diễn giải ví dụ:

1. Folder không có dấu `-`, nhưng vẫn hợp lệ.
2. Tên gốc bộ file có dấu `-`, và toàn bộ file trong bộ dùng cùng tên này.
3. `w01` đến `w04` có dung lượng `1GB`.
4. `w05` là file cuối nên có thể nhỏ hơn `1GB`.

## 9. Các điểm cần chốt trong implementation thực tế

1. Script tạo file rỗng, file random, hay file có pattern dữ liệu lặp lại. Điều này ảnh hưởng trực tiếp đến thời gian tạo file và giá trị MD5.
2. Có cần hỗ trợ resume nếu đang tạo dở một bộ file lớn hay không.
3. Có cần xóa file đã tạo nếu một bước giữa chừng thất bại hay không.
4. Có cần cấm thư mục đầu ra đã tồn tại để tránh ghi đè dữ liệu hay không.
5. Có cần cho phép tên folder và tên gốc file dùng hai kiểu định dạng khác nhau trong cùng một lần chạy hay không. Theo rule hiện tại thì được phép.
6. Có cần expose tham số chọn giữ dấu `-` hay bỏ dấu `-`, hay chỉ cần nhận trực tiếp chuỗi tên hoàn chỉnh đã hợp lệ.

## 10. Tóm tắt mapping giữa yêu cầu và hành vi script

- Tạo thư mục đầu ra: dùng `FolderName` hợp lệ.
- Tạo file metadata nhỏ: sinh `ABCD.whd` dung lượng `100KB`.
- Tạo file dữ liệu chính: sinh `ABCD.wcl` dung lượng `1GB`.
- Tạo file phân mảnh: sinh `ABCD.w01` đến `ABCD.wxx`, với `xx` trong khoảng `5..12`.
- Tính checksum: dùng MD5 cho `ABCD.wcl` và toàn bộ file `ABCD.wxx`.
- Tạo manifest: ghi `ABCD.hash` theo format `[HASH]`, gồm `wcl` và toàn bộ `wxx`.
- Giữ đồng nhất tên gốc: toàn bộ `.hash`, `.whd`, `.wcl`, `.wxx` phải cùng một `BaseName`.
