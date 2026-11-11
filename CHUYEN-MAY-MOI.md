# Chuyển HanaMedia sang máy mới

Source cập nhật ngày 11/11/2026, đã đối chiếu với bản web đang chạy tại `C:\HanaMedia`. Bao gồm luồng Pháp chế soạn/chuyển hợp đồng, Booking nhận để xem/tải (không tải hợp đồng lên), Kế toán chỉ xem và theo dõi hạn thu, Giám đốc chỉ xem phòng ban.

## Code và dữ liệu

- Thư mục này là source để đưa lên Git. `.gitignore` loại trừ database backup, tài liệu người dùng, cấu hình môi trường riêng, log và file build.
- Full database backup và các tệp người dùng nằm riêng ở thư mục `../private-backups/`. Xem `latest-backup.txt` để tìm bản mới nhất.
- `HanaMedia_FULL.bak` đã được phục hồi thử, kiểm tra `DBCC CHECKDB` và đối chiếu số bản ghi toàn bộ 25 bảng. `files/` chứa đầy đủ tệp upload trong `App_Data` và `wwwroot/uploads`, giữ nguyên đường dẫn tương đối.
- File `.bak` chứa dữ liệu tài khoản, nhân sự, Booking và nhật ký. Không đưa backup hoặc tệp upload lên Git public.

## Khôi phục trên máy mới

1. Restore `.bak` bằng SQL Server tương thích với nguồn SQL Server 2022. Làm theo `README-RESTORE.txt` trong thư mục backup. Không chạy script tạo lại schema/seed dữ liệu lên database đã restore.
2. Chép nội dung thư mục `files/` trong backup vào thư mục gốc của ứng dụng.
3. Cấu hình `ConnectionStrings__DefaultConnection` cho SQL Server đích; cấp quyền database cho tài khoản dịch vụ web. Mật khẩu và cấu hình production đặt bên ngoài Git.
4. Tại thư mục có `HanaMedia.csproj`, chạy `dotnet restore` và `dotnet build -c Release`.
5. Kiểm tra đăng nhập từng role, số liệu, hợp đồng, bản nghiệm thu và ảnh/QR.

## Trước khi đưa web public

Bản này hiện được vận hành theo cấu trúc source và `bin/Release/net8.0` trên Windows. `Program.cs` đang xác định content root bằng ba cấp thư mục phía trên binary; cần cấu hình lại theo hosting nếu dùng thư mục `dotnet publish` độc lập. Chưa coi việc chép riêng file exe là một bản triển khai hoàn chỉnh.

`appsettings.json` hiện là cấu hình SQL Express và giới hạn mạng LAN. Hosting public cần cấu hình domain/HTTPS, môi trường Production, kết nối database và chính sách IP/proxy phù hợp. Script trong `ops/` là vận hành local, không phải cấu hình public hosting đã hoàn tất.

Full backup chỉ chứa dữ liệu đến thời điểm ghi trong `snapshot-completed-at.txt`. Khi chuyển chính thức, dừng ghi ở máy cũ, tạo full backup và đồng bộ tệp lần cuối; kiểm tra máy mới rồi mới mở cho người dùng.
