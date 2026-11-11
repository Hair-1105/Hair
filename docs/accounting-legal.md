# Kế toán và Pháp chế

## Phân quyền

- Giám đốc chỉ xem danh sách/chi tiết phòng ban. Đã bỏ giao diện và API tạo, sửa, xóa phòng ban; các đường dẫn ghi cũ trả 404/405. Phòng ban hiện có vẫn dùng để tạo/chuyển nhân sự theo quyền HCNS.
- Trang Phòng ban dùng chung thiết kế HanaMedia: ba thẻ tổng quan (tổng phòng, phòng hoạt động, nhân sự hiện tại); bảng Phòng ban / Trưởng phòng / Nhân sự / Trạng thái / Xem nhân sự. Điện thoại hiển thị từng thẻ phòng ban. Tìm kiếm hỗ trợ không dấu, lọc trạng thái; thống kê tổng không đổi theo bộ lọc. Đã bỏ chỉ số hiệu suất giả lập. Nhân sự hiện tại loại trừ người ngừng hoạt động và đối chiếu mã phòng không phân biệt hoa/thường.
- Giữ nguyên mã role `nv_ke_toan` và `nv_phap_ly`; tên hiển thị là **Kế toán** và **Pháp chế**. AdminIT cấp hai role này từ màn hình quản lý tài khoản hiện có.
- Kế toán đăng nhập vào `/Accounting/Receivables`, được xem mọi Booking, chi tiết, số tiền, tiến độ chi trả và tải tài liệu hợp đồng/nghiệm thu/báo giá. Các thao tác tạo, sửa, hủy, duyệt, hoàn thành, xác nhận khách thanh toán, ghi nhận đối soát và chi trả đều bị chặn phía máy chủ. Chỉ Giám đốc được ghi nhận đối soát/chi trả trong khu vực Accounting.
- Pháp chế giám sát Booking tại `/Legal`; soạn và lưu hợp đồng Booking, hợp đồng lao động, nội quy tại `/LegalDocuments`. Có thể nhập nội dung hoặc đính kèm PDF/DOCX/JPG/PNG tối đa 25 MB.
- Hợp đồng Booking chỉ xuất hiện trong hộp thư của người phụ trách/người tham gia sau khi Pháp chế bấm **Chuyển cho Booking**. Giám đốc và Kế toán được đọc hợp đồng Booking đã chuyển. Bản đã gửi bị khóa; bổ sung bằng hồ sơ mới để giữ bản cũ đối chiếu.
- Hợp đồng lao động, nội quy và bản soạn chưa gửi chỉ Pháp chế được truy cập. Tệp nằm trong `App_Data/legal-documents`, tải qua endpoint có kiểm tra quyền, không phục vụ như tệp tĩnh.

## Hạn thu

- Khi Booking báo hoàn thành thành công qua `/Bookings/Acceptance/{id}`, lưu `Booking.CompletedAt`. Việc bắt đầu/chỉnh sửa thông tin khác không làm đổi ngày này.
- Hạn thu là `CompletedAt.Date.AddMonths(1)`: 31/01 → 28/02 (29/02 trong năm nhuận), không cộng cố định 30 ngày.
- Màn hình công nợ chỉ gồm Booking hoàn thành, chưa xác nhận nhận đủ tiền. Có bộ lọc quá hạn, hôm nay, sắp đến hạn trong 7 ngày (gồm hôm nay), chưa đến hạn và thiếu ngày hoàn thành. Mỗi dòng liên kết tới chi tiết Booking ở chế độ chỉ xem.
- Tổng tiền trên các thẻ tính trên toàn bộ công nợ, không đổi theo bộ lọc. Công nợ là toàn bộ giá trị Booking; hệ thống hiện chưa ghi nhận thu từng phần.
- Khi người phụ trách Booking xác nhận nhận đủ tiền, Booking rời danh sách công nợ. Kế toán không có quyền tự xác nhận.
- Dữ liệu lịch sử chưa có ngày hoàn thành được đánh dấu **Thiếu ngày hoàn thành**; migration không suy đoán ngày từ deadline/ngày cập nhật/ngày đăng bài.

## Cập nhật cơ sở dữ liệu và triển khai

Migration mới: `20261105171111_AccountingReceivablesAndLegalDocuments` thêm cột nullable `bookings.CompletedAt` và bảng `legal_documents`. Không thay đổi giá trị tài chính lịch sử.

Từ thư mục chứa `HanaMedia.csproj`, với connection string của môi trường cần triển khai:

```powershell
dotnet tool restore
dotnet ef database update
```

Có thể dùng script `database_patches/20261105_accounting_legal.sql` cho cơ sở dữ liệu đã ở migration `20261006073852_DirectDirectorContractReview`. Script có kiểm tra lịch sử migration. Sau khi cập nhật DB, triển khai bản build mới và khởi động lại ứng dụng. Luồng khởi động thông thường không tự chạy migration.

## Kiểm thử

### Sửa lỗi phòng ban khi tạo nhân sự (11/11/2026)

Database nâng cấp từ bản cũ có thể vẫn giữ `chk_emp_dept` chỉ gồm HCNS, Booking, Y_tuong, IT, dù EF snapshot đã có Phap_Ly và Ke_Toan. Điều này khiến tạo nhân sự Pháp chế/Kế toán bị báo phòng ban không hợp lệ.

Migration `20261111045152_RepairLegalAccountingDepartments` sửa ràng buộc thành sáu mã và thêm hai phòng ban nếu thiếu. Giữ nguyên phòng ban đã có, kể cả mã viết hoa `PHAP_LY`, tên và trạng thái ngừng hoạt động. Không xóa hoặc thay đổi nhân viên/tài khoản. Down của migration giữ nguyên phần sửa dữ liệu để không loại bỏ phòng ban đang có nhân viên.

Có thể chạy riêng `database_patches/20261111_repair_legal_accounting_departments.sql` để xử lý lỗi này mà không triển khai các migration khác. Script chạy trong transaction và có thể chạy lại. Bản sửa riêng không đánh dấu các migration chưa chạy là hoàn tất; lần chạy EF tiếp theo sẽ áp dụng migration sửa chữa an toàn lần nữa.

Kiểm thử hồi quy: `dotnet run --project tests/DepartmentRoles/DepartmentRoles.csproj`. 17 kiểm tra đã đạt: tái hiện ràng buộc cũ, sửa, tạo/sửa nhân sự, cấp tài khoản hai role, chuyển phòng, chạy lại không trùng và vẫn chặn phòng không tồn tại/ngừng hoạt động. Database QA riêng được tự dọn sau kiểm thử. Tham số `--inspect` chỉ đọc cấu hình phòng ban của database đang dùng; `--apply-repair` áp dụng riêng bản sửa; `--export-repair` xuất script SQL.

### Luồng Kế toán và Pháp chế

Kiểm thử riêng màn hình phòng ban chỉ xem: `dotnet run --project tests/AccountingLegal/AccountingLegal.csproj -- --departments-only`. Đã đạt 19 kiểm tra (gồm thiết lập/đăng nhập), xác nhận Giám đốc đọc được danh sách/chi tiết, không còn nút hoặc script ghi, POST/PUT vào các API cũ bị từ chối và dữ liệu không đổi.

Kiểm thử giao diện: `dotnet run --project tests/AccountingLegal/AccountingLegal.csproj -- --departments-ui` (Node cần resolve được `playwright`, có Edge). Đạt 23 kiểm tra phía .NET và các kiểm tra trình duyệt tại 1440/1024/768/390px: căn cột, không tràn trang, menu mobile, lọc, liên kết nhân sự, không có dữ liệu, lỗi API/thử lại. Ảnh kiểm tra lưu ở `tests/artifacts/departments/`.

Ngày 11/11/2026 đã triển khai đầy đủ Kế toán/Pháp chế, migration và giao diện chung lên web đang chạy. Luồng hợp đồng hiện chỉ Pháp chế được soạn/tải và chuyển; Booking xem/tải trực tiếp trong chi tiết, API UploadContract cũ bị chặn. Giám đốc hiển thị cùng trạng thái Pháp chế đã chuyển. Đạt 92 kiểm tra AccountingLegal (gồm trình duyệt tại 4 kích thước) và 78 kiểm tra hồi quy StandaloneBooking. Bản Release đã được kiểm thử và đối chiếu mã băm sau triển khai.

```powershell
dotnet run --project tests/AccountingLegal/AccountingLegal.csproj
```

Bộ kiểm thử tạo database riêng tên `HanaMedia_AccountingLegalQA_<GUID>` trên SQL Server cấu hình, chạy HTTP qua ứng dụng thật ở cổng 15039 rồi dọn database/tệp thử. Không ghi fixture vào database đang dùng. Tài khoản SQL chạy kiểm thử cần quyền tạo/xóa database thử nghiệm.

68 kiểm tra gồm tính ngày cuối tháng/năm nhuận, migration giữ dữ liệu cũ, báo hoàn thành → công nợ → nhận đủ tiền, các bộ lọc, đọc toàn bộ Booking, chặn các POST của Kế toán, giao diện chỉ xem, lưu ba loại hồ sơ, chuyển hợp đồng, phân quyền tệp, chống gửi trùng/sai phiên bản, khóa bản đã gửi, kiểm tra đầu vào, chống CSRF và audit.

