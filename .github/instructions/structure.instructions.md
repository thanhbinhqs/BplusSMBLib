---
description: All rules for create this script
# applyTo: 'All rules for create this script' # when provided, instructions will automatically be added to the request context when the pattern matches an attached file
---

<!-- Tip: Use /create-instructions in chat to generate content with agent assistance -->

# Yêu cầu tạo script

## Mục tiêu

Tạo một script để sinh ra một bộ file theo đúng cấu trúc và quy tắc đặt tên được mô tả bên dưới.

## Yêu cầu công việc

1. Tạo thư mục chứa các file đầu ra.
2. Trong thư mục có tên `ABCD` cần tạo các file sau:
   - `ABCD.hash`
   - `ABCD.whd` có dung lượng `100KB`
   - `ABCD.wcl` có dung lượng `1GB`
   - Một số file `ABCD.wxx` có dung lượng chuẩn `1GB`, ngoại trừ file cuối có thể nhỏ hơn theo quy tắc ở phần dưới
3. File `ABCD.hash` phải chứa nội dung theo đúng cấu trúc:

```ini
[HASH]
wcl=<hash md5 của file ABCD.wcl>
w01=<hash md5 của file ABCD.w01>
w02=<hash md5 của file ABCD.w02>
...
wxx=<hash md5 của file ABCD.wxx>
```

## Quy tắc đặt tên

- `ABCD` có dạng: `SGLC123A01-C01-G001-S0001`
- Tên thư mục và tên file đều phải đúng cấu trúc quy định, nhưng không bắt buộc phải giống nhau
- Có thể áp dụng một trong hai kiểu đặt tên sau:
  - Giữ dấu `-`: `SGLC123A01-C01-G001-S0001`
  - Bỏ toàn bộ dấu `-`: `SGLC123A01C01G001S0001`
- Trong từng nhóm file được tạo ra, tất cả file phải dùng cùng một tên gốc
- File `.hash`, `.whd`, `.wcl` và toàn bộ các file `.wxx` phải đồng nhất tên gốc với nhau
- Các file dữ liệu mở rộng theo mẫu:
  - `ABCD.w01`
  - `ABCD.w02`
  - ...
  - `ABCD.wxx`

## Quy tắc số lượng file

- `xx` nằm trong khoảng từ `5` đến `12`
- Nghĩa là tổng số file `wxx` được tạo sẽ từ `5` đến `12` file

## Quy tắc dung lượng các file `wxx`

- Các file từ `w01` đến `w(xx-1)` thường có dung lượng `1GB`
- File cuối cùng `wxx` thường có dung lượng nhỏ hơn các file còn lại
- Cách hiểu nghiệp vụ: đây là kết quả của việc cắt một file lớn thành `xx - 1` file đủ `1GB` và 1 file cuối là phần còn lại

## Ghi chú cần xác nhận

- Trong mô tả gốc có dòng `ABCD.xxx (1GB)`, nhưng phần cấu trúc file hash dùng định dạng `w01`, `w02`, ..., `wxx`
- Vì vậy bản hiểu thống nhất trong tài liệu này là các file cần tạo theo mẫu `ABCD.w01` đến `ABCD.wxx`
- Quy tắc tên đã được bổ sung: tên thư mục và tên file không cần giống nhau, nhưng từng tên phải đúng cấu trúc hợp lệ; riêng file `.hash`, `.whd`, `.wcl` và toàn bộ file `.wxx` trong cùng một bộ phải đồng nhất cùng một tên gốc
- Nếu cần, script có thể cho phép cấu hình:
  - tên `ABCD`
  - có giữ dấu `-` trong tên hay không
  - số lượng file từ `5` đến `12`
  - dung lượng file cuối cùng

## Ví dụ

Ví dụ một trường hợp hợp lệ:

- Tên folder: `SGLC123A01C01G001S0001`
- Tên gốc của bộ file: `SGLC123A01-C01-G001-S0001`
- Số file dữ liệu cắt: `5`

Danh sách file trong folder:

```text
SGLC123A01C01G001S0001\
  SGLC123A01-C01-G001-S0001.hash
  SGLC123A01-C01-G001-S0001.whd
  SGLC123A01-C01-G001-S0001.wcl
  SGLC123A01-C01-G001-S0001.w01
  SGLC123A01-C01-G001-S0001.w02
  SGLC123A01-C01-G001-S0001.w03
  SGLC123A01-C01-G001-S0001.w04
  SGLC123A01-C01-G001-S0001.w05
```

Ví dụ nội dung file `.hash`:

```ini
[HASH]
wcl=d41d8cd98f00b204e9800998ecf8427e
w01=0cc175b9c0f1b6a831c399e269772661
w02=92eb5ffee6ae2fec3ad71c777531578f
w03=4a8a08f09d37b73795649038408b5f33
w04=8277e0910d750195b448797616e091ad
w05=e1671797c52e15f763380b45e841ec32
```

Diễn giải ví dụ:

- Folder không có dấu `-`, nhưng vẫn đúng cấu trúc hợp lệ
- Bộ file có dấu `-`, và toàn bộ file trong bộ đều cùng một tên gốc
- `w01` đến `w04` có thể là `1GB`
- `w05` có thể nhỏ hơn `1GB` nếu đó là phần còn lại sau khi cắt file lớn