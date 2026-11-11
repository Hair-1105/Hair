using System.Data;
using System.Security.Claims;
using HanaMedia.Constants;
using HanaMedia.Models;
using HanaMedia.Services.Auditing;
using HanaMedia.Services.Config;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanaMedia.Controllers;

[Authorize(Roles = AppRoles.Director + "," + AppRoles.BookingManager + "," + AppRoles.BookingStaff + "," + AppRoles.IdeaManager + "," + AppRoles.Accountant)]
public class BookingController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ISystemAuditService _auditService;
    private readonly IBusinessConfigService _businessConfigService;
    public BookingController(ApplicationDbContext context, ISystemAuditService auditService, IBusinessConfigService businessConfigService)
    { _context = context; _auditService = auditService; _businessConfigService = businessConfigService; }
    private string Role => User.FindFirstValue(ClaimTypes.Role) ?? "";
    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    private int EmployeeId => _context.Employees.Where(e => e.UserId == UserId).Select(e => e.Id).FirstOrDefault();
    private bool IsBookingOperator => Role is AppRoles.BookingManager or AppRoles.BookingStaff;
    public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        if ((Role is AppRoles.IdeaManager or AppRoles.Accountant) && !HttpMethods.IsGet(Request.Method) && !HttpMethods.IsHead(Request.Method))
            context.Result = Forbid();
        base.OnActionExecuting(context);
    }
    private bool Owns(Booking b) => IsBookingOperator && EmployeeId > 0 && b.PrimaryManagerId == EmployeeId;
    private IQueryable<Booking> Query() => _context.Bookings.Include(b => b.Campaign).Include(b => b.Kol)
        .Include(b => b.BookingKols).ThenInclude(k => k.Kol).Include(b => b.PrimaryManager)
        .Include(b => b.BookingWages).ThenInclude(w => w.Employee).Include(b => b.Payments);
    private IQueryable<Booking> Visible() => Query();
    private void Audit(Booking b, string action, string detail) => _auditService.AddEvent(
        new AuditEvent(AuditModules.Booking, action, detail, UserId, "Booking", b.Id.ToString()));
    private IActionResult Result(int id, bool success, string message)
    {
        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers.Accept.ToString().Contains("json"))
            return Json(new { success, message });
        TempData[success ? "SuccessMessage" : "ErrorMessage"] = message;
        return id > 0 ? RedirectToAction(nameof(Details), new { id }) : RedirectToAction(nameof(Index));
    }
    private async Task<IActionResult> Persist(Booking b, string action, string message, CancellationToken ct)
    {
        b.UpdatedAt = DateTime.Now;
        Audit(b, action, message);
        try { await _context.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Result(b.Id, false, "Dữ liệu vừa được thay đổi. Tải lại trang rồi thử lại."); }
        return Result(b.Id, true, message);
    }

    [HttpGet("Bookings")]
    public async Task<IActionResult> Index(string? search, int? campaignId, int? kolId, string? status, int? managerId, int? participantId, CancellationToken cancellationToken)
    {
        var query = Visible();
        // Match Vietnamese customer names with/without accents and ignore extra spaces.
        search = string.Join(" ", (search ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Normalize(System.Text.NormalizationForm.FormC);
        foreach (var word in search.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var term = word.Replace('đ', 'd').Replace('Đ', 'D');
            query = query.Where(b =>
                EF.Functions.Collate(b.ClientName.Replace("đ", "d").Replace("Đ", "D"), "Latin1_General_100_CI_AI").Contains(term) ||
                EF.Functions.Collate(b.CampaignName.Replace("đ", "d").Replace("Đ", "D"), "Latin1_General_100_CI_AI").Contains(term) ||
                (b.Notes != null && EF.Functions.Collate(b.Notes.Replace("đ", "d").Replace("Đ", "D"), "Latin1_General_100_CI_AI").Contains(term)));
        }
        if (kolId.HasValue) query = query.Where(b => b.BookingKols.Any(k => k.KolId == kolId) || b.KolId == kolId);
        if (managerId.HasValue) query = query.Where(b => b.PrimaryManagerId == managerId);
        // The owner participates through the remaining commission, without a BookingWage row.
        if (participantId.HasValue) query = query.Where(b => b.PrimaryManagerId == participantId || b.BookingWages.Any(w => w.EmployeeId == participantId));
        if (status == "da_nghiem_thu") query = query.Where(b => b.CustomerPaidAt != null && b.Status != "huy");
        else if (status == "hoan_thanh") query = query.Where(b => b.Status == "hoan_thanh" && b.CustomerPaidAt == null);
        else if (status == "da_duyet") query = query.Where(b => b.ContractApprovedAt != null && b.Status != "dang_trien_khai" && b.Status != "hoan_thanh" && b.Status != "huy" && b.CustomerPaidAt == null);
        else if (status == "dang_cho") query = query.Where(b => (b.ContractStatus == "nhap" || b.ContractStatus == "cho_duyet" || b.ContractStatus == "tu_choi") && b.Status != "huy");
        else if (!string.IsNullOrWhiteSpace(status)) query = query.Where(b => b.Status == status);
        ViewBag.Kols = await _context.Kols.Where(k => k.IsActive).OrderBy(k => k.Name).ToListAsync(cancellationToken);
        ViewBag.Employees = await _context.Employees.Include(e => e.User).OrderBy(e => e.FullName).ToListAsync(cancellationToken);
        ViewBag.Search = search; ViewBag.CampaignId = campaignId; ViewBag.KolId = kolId; ViewBag.Status = status;
        ViewBag.ManagerId = managerId; ViewBag.ParticipantId = participantId; ViewBag.EmployeeId = EmployeeId;
        ViewBag.IsWritable = IsBookingOperator && EmployeeId > 0;
        ViewBag.FinanceConfig = await _businessConfigService.GetAsync(cancellationToken);
        ViewBag.Participants = await EligibleParticipants().OrderBy(e => e.FullName).ToListAsync(cancellationToken);
        return View(await query.OrderByDescending(b => b.CreatedAt).ToListAsync(cancellationToken));
    }

    [HttpGet("Bookings/Details/{id}")]
    [HttpGet("Booking/Details/{id}")]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var booking = await Visible().Include(b => b.ContractApprovedBy).Include(b => b.ContractSignedBy).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (booking == null) return NotFound();
        ViewBag.IsWritable = Owns(booking); ViewBag.Role = Role;
        ViewBag.CanDraft = false;
        ViewBag.LegalContracts = await HanaMedia.Services.LegalDocumentVisibility.For(_context, User).AsNoTracking()
            .Where(d => d.BookingId == id && d.Kind == "booking" && d.SentAt != null)
            .OrderByDescending(d => d.SentAt).ThenByDescending(d => d.Id).ToListAsync(cancellationToken);
        ViewBag.Participants = await EligibleParticipants().OrderBy(e => e.FullName).ToListAsync(cancellationToken);
        ViewBag.WagePool = booking.FinanceVersion == 1 ? booking.CommissionPool : decimal.Round(booking.BookingPrice * (await _businessConfigService.GetAsync(cancellationToken)).CommissionPercent / 100m, 2);
        ViewBag.EditKols = await _context.Kols.Where(k => k.IsActive).OrderBy(k => k.Name).ToListAsync(cancellationToken);
        ViewBag.EditEmployees = await _context.Employees.OrderBy(e => e.FullName).ToListAsync(cancellationToken);
        ViewBag.EditEmployeeId = EmployeeId;
        return View(booking);
    }

    // GET /Bookings/{id}/DocumentCheck?kind=contract|quotation|acceptance
    // Trả JSON để trang chi tiết kiểm tra file vật lý còn tồn tại trước khi cho tải.
    // Tránh user bấm "Tải hợp đồng" mà server trả 404 trang trắng.
    [HttpGet("Bookings/{id:int}/DocumentCheck")]
    public async Task<IActionResult> CheckDocument(int id, string kind, CancellationToken cancellationToken)
    {
        var b = await Visible().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (b == null) return NotFound(new { available = false, message = "Không tìm thấy Booking." });

        if (string.Equals(kind, "contract", StringComparison.OrdinalIgnoreCase))
        {
            var contract = await HanaMedia.Services.LegalDocumentVisibility.For(_context, User).AsNoTracking()
                .Where(d => d.BookingId == id && d.Kind == "booking" && d.SentAt != null)
                .OrderByDescending(d => d.SentAt).ThenByDescending(d => d.Id).FirstOrDefaultAsync(cancellationToken);
            if (contract != null)
            {
                if (contract.AttachmentName == null)
                    return Ok(new { available = true, url = $"/LegalDocuments/Details/{contract.Id}" });
                var path = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "legal-documents", contract.AttachmentName);
                if (contract.AttachmentName != Path.GetFileName(contract.AttachmentName) || !System.IO.File.Exists(path))
                    return Ok(new { available = false, code = "missing", message = "Tệp hợp đồng Pháp chế không còn khả dụng. Liên hệ Pháp chế để kiểm tra." });
                return Ok(new { available = true, url = $"/LegalDocuments/Attachment/{contract.Id}" });
            }
        }

        string? url = kind.ToLowerInvariant() switch
        {
            "contract" => b.ContractFileUrl,
            "quotation" => b.QuotationFileUrl,
            "acceptance" => b.AcceptanceFileUrl,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(url))
        {
            return Ok(new
            {
                available = false,
                code = "empty",
                message = "Booking chưa đính kèm tài liệu này."
            });
        }

        var fileName = Path.GetFileName(new Uri("http://x" + (url.StartsWith("/") ? url : "/" + url)).AbsolutePath);
        if (string.IsNullOrEmpty(fileName))
        {
            return Ok(new { available = false, code = "bad_url", message = "Đường dẫn tài liệu không hợp lệ." });
        }

        // Tìm ở cả 2 vị trí: legacy (wwwroot/uploads/bookings) và mới (App_Data/booking-documents).
        var appData = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "booking-documents", fileName);
        var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "bookings", fileName);
        var exists = System.IO.File.Exists(appData) || System.IO.File.Exists(webRoot);

        if (!exists)
        {
            // Ghi audit để admin biết tài liệu booking mất trên disk.
            await _auditService.WriteAsync(new AuditEvent(
                AuditModules.Booking,
                AuditActions.Deleted,
                $"Phát hiện tài liệu booking #{id} ({kind}) không còn trên disk: {fileName}. URL DB: {url}",
                UserId > 0 ? UserId : null,
                "Booking",
                id.ToString()
            ), cancellationToken);

            return Ok(new
            {
                available = false,
                code = "file_missing",
                message = $"Tài liệu \"{fileName}\" không còn trên hệ thống. Có thể đã bị xóa sau khi Booking được tạo. Vui lòng liên hệ QL Booking để tải lại.",
                fileName
            });
        }

        return Ok(new { available = true, url });
    }

    [HttpPost("Bookings/Create"), ValidateAntiForgeryToken]
    public Task<IActionResult> Create(Booking input, int[] kolIds, IFormFile? acceptanceFile, string deadlineStr, string? postingDateStr, [Bind(Prefix = "kolCastAmount")] Dictionary<int, decimal> kolCastAmount, CancellationToken cancellationToken)
        => Save(0, input, kolIds, acceptanceFile, deadlineStr, postingDateStr, kolCastAmount, cancellationToken);
    [HttpPost("Bookings/Edit/{id}"), ValidateAntiForgeryToken]
    public Task<IActionResult> Edit(int id, Booking input, int[] kolIds, IFormFile? acceptanceFile, string deadlineStr, string? postingDateStr, [Bind(Prefix = "kolCastAmount")] Dictionary<int, decimal> kolCastAmount, CancellationToken cancellationToken)
        => Save(id, input, kolIds, acceptanceFile, deadlineStr, postingDateStr, kolCastAmount, cancellationToken);

    private IQueryable<Employee> EligibleParticipants() => _context.Employees.Where(e =>
        e.Status == "dang_lam_viec" || e.Status == "thu_viec");

    // Quy tắc phân bổ cố định: 50% lợi nhuận công ty, 10% hoa hồng QL Booking phụ trách, 40% cát-xê chia cho KOL/KOC
    private const decimal FixedCompanyPercent = 50m;
    private const decimal FixedCommissionPercent = 10m;
    private const decimal FixedCastPercent = 40m;

    private async Task<IActionResult> Save(int id, Booking input, int[] kolIds, IFormFile? acceptanceFile, string deadlineStr, string? postingDateStr, Dictionary<int, decimal>? kolCastAmount, CancellationToken ct)
    {
        if (!IsBookingOperator || EmployeeId == 0) return Forbid();
        if (input.RevenueDate == null || input.RevenueDate.Value.Date > DateTime.Today || input.RevenueDate.Value.Year < 1900)
            return Result(id, false, "Nhập ngày ghi nhận Booking hợp lệ, không vượt quá hôm nay.");
        await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var b = id == 0 ? new Booking() : await Query().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b == null) return NotFound();
        if (id != 0 && !Owns(b)) return Forbid();
        if (id != 0 && (b.ContractStatus is not ("nhap" or "tu_choi") || b.Payments.Any(p => p.IsPaid)))
            return Result(id, false, "Booking đang duyệt/đã duyệt đã khóa thông tin. Không được thay đổi nội dung hợp đồng đã trình.");
        if (string.IsNullOrWhiteSpace(input.ClientName) || input.ClientName.Length > 150 || string.IsNullOrWhiteSpace(input.CampaignName) || input.CampaignName.Length > 150)
            return Result(id, false, "Nhập đối tác và tên Booking (tối đa 150 ký tự).");
        kolIds = kolIds.Distinct().Order().ToArray();
        if (kolIds.Length == 0 || kolIds.Length > 10 || await _context.Kols.CountAsync(k => kolIds.Contains(k.Id) && k.IsActive, ct) != kolIds.Length)
            return Result(id, false, "Chọn ít nhất một KOL/KOC đang hoạt động.");
        if (input.BookingPrice <= 0 || input.BookingPrice > 9999999999999.99m || !DateOnly.TryParse(deadlineStr, out var deadline))
            return Result(id, false, "Giá Booking hoặc deadline không hợp lệ.");
        if (kolCastAmount == null || kolCastAmount.Count != kolIds.Length || kolIds.Any(k => !kolCastAmount.ContainsKey(k) || kolCastAmount[k] <= 0 || decimal.Round(kolCastAmount[k], 2) != kolCastAmount[k]) || kolCastAmount.Values.Sum() != input.BookingPrice)
            return Result(id, false, "Tổng giá trị phân bổ cho KOL phải bằng ngân sách Booking; mỗi KOL phải có số tiền lớn hơn 0 và tối đa 2 chữ số thập phân.");
        if (!new[] { "dang_cho", "thuong_luong", "da_chot", "huy" }.Contains(input.Status))
            return Result(id, false, "Trạng thái Booking không hợp lệ.");
        if (input.JobDescription?.Length > 4000 || input.Notes?.Length > 4000 || input.PostLink?.Length > 255)
            return Result(id, false, "Nội dung/ghi chú tối đa 4.000 ký tự, link tối đa 255 ký tự.");

        b.BookingPrice = decimal.Round(input.BookingPrice, 2, MidpointRounding.AwayFromZero);
        b.RevenueDate = input.RevenueDate.Value.Date;
        // Áp dụng quy tắc phân bổ cố định
        b.FinanceVersion = 1;
        b.CompanyPercent = FixedCompanyPercent;
        b.CommissionPercent = FixedCommissionPercent;
        b.CastPercent = FixedCastPercent;

        if (acceptanceFile != null)
        {
            var path = await SaveDocument(acceptanceFile, "acceptance", ct);
            if (path == null) return Result(id, false, "Tài liệu phải là PDF/DOCX/JPG/PNG, tối đa 25 MB.");
            b.AcceptanceFileUrl = path;
        }
        b.ActualCost = b.CommissionPool + b.CastPool;
        b.CampaignId = null; b.CampaignName = input.CampaignName.Trim(); b.ClientName = input.ClientName.Trim();
        b.PrimaryManagerId = EmployeeId; b.KolId = kolIds[0]; b.Deadline = deadline;
        b.PostingDate = DateOnly.TryParse(postingDateStr, out var posted) ? posted : null;
        if (!string.IsNullOrWhiteSpace(input.PostLink) && (!Uri.TryCreate(input.PostLink, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
            return Result(id, false, "Link bài đăng phải là HTTP/HTTPS.");
        b.JobDescription = input.JobDescription; b.Notes = input.Notes; b.PostLink = input.PostLink; b.Status = input.Status == "huy" ? "huy" : "dang_cho";
        foreach (var removed in b.BookingKols.Where(k => !kolIds.Contains(k.KolId)).ToList()) _context.BookingKols.Remove(removed);
        // Form amounts are allocated contract values, not cash paid to KOL.
        decimal allocatedCast = 0m;
        foreach (var k in kolIds)
        {
            var row = b.BookingKols.FirstOrDefault(x => x.KolId == k);
            if (row == null) { row = new BookingKol { KolId = k }; b.BookingKols.Add(row); }
            row.AllocationAmount = kolCastAmount[k];
            row.CastAmount = k == kolIds[^1] ? b.CastPool - allocatedCast
                : decimal.Floor(row.AllocationAmount * FixedCastPercent) / 100m;
            allocatedCast += row.CastAmount;
        }
        if (id == 0) { b.ContractStatus = "nhap"; b.CreatedAt = DateTime.Now; _context.Bookings.Add(b); }
        b.UpdatedAt = DateTime.Now;
        await _context.SaveChangesAsync(ct);
        Audit(b, id == 0 ? AuditActions.Created : AuditActions.Updated,
            $"Lưu Booking #{b.Id}; phân bổ ngân sách cho {kolIds.Length} KOL, tổng cát-xê {b.CastPool:N2} đ; tỷ lệ công ty/Booking/KOL: 50/10/40.");
        await _context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Result(b.Id, true, "Đã lưu Booking. Tổng phân bổ bằng ngân sách; cát-xê KOL tự tính 40%, hoa hồng Booking 10%, phần công ty 50%.");
    }

    private async Task<string?> SaveDocument(IFormFile file, string kind, CancellationToken ct)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (file.Length <= 0 || file.Length > 25 * 1024 * 1024 || !new[] { ".pdf", ".docx", ".jpg", ".jpeg", ".png" }.Contains(extension)) return null;
        var folder = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "booking-documents");
        Directory.CreateDirectory(folder);
        var name = $"{kind}_{Guid.NewGuid():N}{extension}";
        await using var stream = System.IO.File.Create(Path.Combine(folder, name));
        await file.CopyToAsync(stream, ct);
        return "/BookingDocuments/" + name;
    }

    [HttpPost("Bookings/Delete/{id}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var b = await Query().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (b == null) return NotFound();
        if (!IsBookingOperator) return Forbid();
        if (b.ContractStatus is not ("nhap" or "tu_choi") || b.Payments.Any(p => p.IsPaid)) return Result(id, false, "Không thể hủy Booking đã trình duyệt hoặc đã thanh toán.");
        b.Status = "huy";
        return await Persist(b, AuditActions.Deleted, "Đã hủy Booking; giữ lịch sử.", cancellationToken);
    }

    [HttpPost("Bookings/UpdateWages/{id}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateWages(int id, CancellationToken cancellationToken)
    {
        // Theo quy tắc mới (50/10/40 cố định), Booking chỉ chia cát-xê cho KOL/KOC — không còn phân bổ wages cho nhân viên.
        // Action giữ lại để tương thích route cũ nhưng không làm gì.
        return await Task.FromResult(Result(id, false, "Phân bổ thù lao nhân viên đã được thay thế bằng quy tắc 50/10/40. Vui lòng quản lý KOL/KOC qua trang chi tiết."));
    }

    [HttpPost("Bookings/SubmitApproval/{id}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitApproval(int id, CancellationToken cancellationToken)
    {
        var b = await Query().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (b == null) return NotFound();
        if (!Owns(b)) return Forbid();
        if (b.ContractStatus is not ("nhap" or "tu_choi") || b.Status == "huy") return Result(id, false, "Chỉ gửi Booking nháp hoặc bị từ chối.");
        if (b.FinanceVersion != 1 || string.IsNullOrWhiteSpace(b.JobDescription) || b.BookingKols.Count == 0)
            return Result(id, false, "Cần cập nhật đầy đủ nội dung, KOL và công thức tài chính trước khi gửi duyệt.");
        b.ContractStatus = "cho_duyet"; b.RejectionReason = null;
        return await Persist(b, "submitted", "Đã gửi đơn phê duyệt Booking lên Giám đốc.", cancellationToken);
    }

    [HttpPost("Bookings/Approve/{id}"), ValidateAntiForgeryToken]
    public Task<IActionResult> Approve(int id, CancellationToken cancellationToken) => DirectorDecision(id, true, null, cancellationToken);
    [HttpPost("Bookings/Reject/{id}"), ValidateAntiForgeryToken]
    public Task<IActionResult> Reject(int id, string? rejectionReason, CancellationToken cancellationToken) => DirectorDecision(id, false, rejectionReason, cancellationToken);
    private async Task<IActionResult> DirectorDecision(int id, bool approve, string? reason, CancellationToken ct)
    {
        if (Role != AppRoles.Director) return Forbid();
        var b = await Query().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b == null) return NotFound();
        if (b.ContractStatus != "cho_duyet" || b.Status == "huy") return Result(id, false, "Booking không ở bước chờ duyệt.");
        if (!approve && (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)) return Result(id, false, "Nhập lý do từ chối, tối đa 1.000 ký tự.");
        b.ContractStatus = approve ? "da_duyet" : "tu_choi"; b.RejectionReason = reason;
        if (approve) { b.ContractApprovedAt = DateTime.Now; b.ContractApprovedById = EmployeeId == 0 ? null : EmployeeId; }
        return await Persist(b, approve ? AuditActions.Approved : AuditActions.Rejected, approve ? "Giám đốc đã duyệt; chờ soạn hợp đồng." : "Giám đốc từ chối: " + reason, ct);
    }

    // Contracts are authored and handed off exclusively by LegalDocumentsController.
    // Keep the legacy route explicitly forbidden for stale clients and direct callers.
    [HttpPost("Bookings/UploadContract/{id}"), ValidateAntiForgeryToken]
    public Task<IActionResult> UploadContract(int id, IFormFile? contractFile, CancellationToken cancellationToken)
        => Task.FromResult<IActionResult>(Forbid());
    [HttpPost("Bookings/SignContract/{id}"), ValidateAntiForgeryToken]
    public Task<IActionResult> SignContract(int id, bool confirmed, CancellationToken cancellationToken)
        => Task.FromResult<IActionResult>(BadRequest("Luồng ký riêng đã bỏ. File hợp đồng chỉ để xem; dùng luồng duyệt Booking."));

    [HttpPost("Bookings/Acceptance/{id}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Acceptance(int id, IFormFile? acceptanceFile, string? postLink, CancellationToken ct)
    {
        var b = await Query().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b == null) return NotFound();
        if (!Owns(b)) return Forbid();
        if (!b.IsDirectorApproved || b.Status != "dang_trien_khai" || b.CustomerPaidAt != null) return Result(id, false, "Chỉ hoàn thành Booking đang chạy và đã được Giám đốc duyệt.");
        if (postLink?.Length > 255) return Result(id, false, "Link tối đa 255 ký tự.");
        if (!string.IsNullOrWhiteSpace(postLink) && (!Uri.TryCreate(postLink, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
            return Result(id, false, "Link phải là HTTP/HTTPS.");
        var path = acceptanceFile == null ? null : await SaveDocument(acceptanceFile, "acceptance", ct);
        if (path == null) return Result(id, false, "Chọn bản nghiệm thu hợp lệ.");
        b.AcceptanceFileUrl = path; b.PostLink = postLink; b.PostingDate = DateOnly.FromDateTime(DateTime.Today); b.Status = "hoan_thanh";
        b.CompletedAt = DateTime.Now;
        return await Persist(b, "completed", "Đã hoàn thành công việc; chờ khách thanh toán để nghiệm thu.", ct);
    }

    [HttpPost("Bookings/Start/{id}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(int id, CancellationToken ct)
    {
        var b = await Query().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b == null) return NotFound();
        if (!Owns(b)) return Forbid();
        if (!b.IsDirectorApproved || b.Status is "huy" or "hoan_thanh" || b.CustomerPaidAt != null)
            return Result(id, false, "Chỉ triển khai Booking đã được Giám đốc duyệt, chưa hoàn thành/hủy.");
        b.Status = "dang_trien_khai";
        return await Persist(b, AuditActions.Updated, "Đã bắt đầu triển khai Booking.", ct);
    }

    [HttpPost("Bookings/ConfirmCustomerPayment/{id}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmCustomerPayment(int id, bool receivedPayment, CancellationToken ct, DateTime? customerPaymentDate = null)
    {
        var b = await Query().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b == null) return NotFound();
        if (!Owns(b)) return Forbid();
        if (!receivedPayment || !b.IsDirectorApproved || b.Status != "hoan_thanh")
            return Result(id, false, "Chỉ nghiệm thu và xác nhận nhận tiền cho Booking đã hoàn thành.");
        if (b.CustomerPaidAt != null) return Result(id, true, "Booking đã được xác nhận nhận tiền.");
        if (customerPaymentDate == null || customerPaymentDate.Value.Date > DateTime.Today || customerPaymentDate.Value.Year < 1900)
            return Result(id, false, "Nhập ngày khách thanh toán thực tế hợp lệ, không vượt quá hôm nay.");
        b.CustomerPaymentDate = customerPaymentDate.Value.Date;
        b.CustomerPaidAt = DateTime.Now; b.CustomerPaidByUserId = UserId;
        return await Persist(b, "customer_paid", "Đã xác nhận nhận đủ tiền khách hàng; ghi nhận hoa hồng Booking.", ct);
    }

        [HttpGet("Bookings/Dashboard")]
        public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
        {
            if (Role is not (AppRoles.Director or AppRoles.Accountant) && !IsBookingOperator)
            {
                return Forbid();
            }

            var bookings = await _context.Bookings
                .Include(b => b.PrimaryManager)
                .ToListAsync(cancellationToken);

            var today = DateOnly.FromDateTime(DateTime.Today);

            // 1. Count by Status
            var statuses = new Dictionary<string, int>
            {
                { "dang_cho", bookings.Count(b => b.WorkflowStatus == "dang_cho") },
                { "thuong_luong", bookings.Count(b => b.WorkflowStatus == "da_duyet") },
                { "da_chot", bookings.Count(b => b.WorkflowStatus == "da_nghiem_thu") },
                { "dang_trien_khai", bookings.Count(b => b.Status == "dang_trien_khai") },
                { "hoan_thanh", bookings.Count(b => b.WorkflowStatus == "hoan_thanh") },
                { "huy", bookings.Count(b => b.Status == "huy") }
            };
            ViewBag.Statuses = statuses;

            // 2. Financial Metrics
            decimal revenue = bookings.Where(b => b.Status != "huy" && b.CustomerPaidAt != null).Sum(b => b.BookingPrice);
            decimal cost = bookings.Where(b => b.Status != "huy" && b.CustomerPaidAt != null).Sum(b => b.ActualCost);
            decimal profit = revenue - cost;

            ViewBag.Revenue = revenue;
            ViewBag.Cost = cost;
            ViewBag.Profit = profit;

            // 3. Overdue Bookings (Deadline passed, status is not hoan_thanh or huy)
            var overdueBookings = bookings
                .Where(b => b.Deadline < today && b.Status != "hoan_thanh" && b.Status != "huy")
                .OrderBy(b => b.Deadline)
                .ToList();
            ViewBag.OverdueBookings = overdueBookings;

            // 4. Employee Performance (Group by PrimaryManagerId)
            var managerStats = bookings
                .Where(b => b.PrimaryManagerId.HasValue)
                .GroupBy(b => b.PrimaryManager)
                .Select(g => new ManagerPerformanceViewModel
                {
                    ManagerName = g.Key?.FullName ?? "—",
                    Position = g.Key?.Position ?? "—",
                    CompletedCount = g.Count(b => b.Status == "hoan_thanh"),
                    RunningCount = g.Count(b => b.Status == "dang_trien_khai"),
                    TotalCount = g.Count()
                })
                .OrderByDescending(x => x.CompletedCount)
                .ToList();
            ViewBag.ManagerStats = managerStats;

            return View();
        }


}
    public class ManagerPerformanceViewModel
    {
        public string ManagerName { get; set; } = null!;
        public string Position { get; set; } = null!;
        public int CompletedCount { get; set; }
        public int RunningCount { get; set; }
        public int TotalCount { get; set; }
    }

