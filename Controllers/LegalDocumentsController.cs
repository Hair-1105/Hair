using System.Data;
using System.Security.Claims;
using HanaMedia.Constants;
using HanaMedia.Models;
using HanaMedia.Services.Auditing;
using HanaMedia.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HanaMedia.Controllers;

[Authorize(Roles = AppRoles.LegalStaff + "," + AppRoles.BookingManager + "," + AppRoles.BookingStaff + "," + AppRoles.Director + "," + AppRoles.Accountant)]
public class LegalDocumentsController(ApplicationDbContext db, ISystemAuditService audit, IWebHostEnvironment env) : Controller
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private IQueryable<LegalDocument> Visible() => HanaMedia.Services.LegalDocumentVisibility.For(db, User);

    [HttpGet("LegalDocuments")]
    public async Task<IActionResult> Index(string? kind, int? bookingId, CancellationToken ct)
    {
        var query = Visible();
        if (kind is "booking" or "employment" or "policy") query = query.Where(d => d.Kind == kind);
        if (bookingId.HasValue) query = query.Where(d => d.BookingId == bookingId);
        ViewBag.Kind = kind;
        ViewBag.BookingId = bookingId;
        return View(await query.AsNoTracking().OrderByDescending(d => d.UpdatedAt).ToListAsync(ct));
    }

    [HttpGet("LegalDocuments/Details/{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var doc = await Visible().AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);
        return doc == null ? NotFound() : View(doc);
    }

    [HttpGet("LegalDocuments/Attachment/{id:int}")]
    public async Task<IActionResult> Attachment(int id, CancellationToken ct)
    {
        var doc = await Visible().AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);
        if (doc?.AttachmentName == null || doc.AttachmentName != Path.GetFileName(doc.AttachmentName)) return NotFound();
        var path = Path.Combine(env.ContentRootPath, "App_Data", "legal-documents", doc.AttachmentName);
        if (!System.IO.File.Exists(path)) return NotFound();
        Response.Headers.CacheControl = "no-store";
        return PhysicalFile(path, "application/octet-stream", doc.AttachmentOriginalName ?? doc.AttachmentName);
    }

    private async Task Choices(CancellationToken ct)
    {
        ViewBag.Bookings = await db.Bookings.Where(b => b.Status != "huy")
            .OrderByDescending(b => b.Id).Select(b => new SelectListItem($"#{b.Id} — {b.ClientName} / {b.CampaignName}", b.Id.ToString())).ToListAsync(ct);
        ViewBag.Employees = await db.Employees.OrderBy(e => e.FullName)
            .Select(e => new SelectListItem($"#{e.Id} — {e.FullName}", e.Id.ToString())).ToListAsync(ct);
    }

    [Authorize(Roles = AppRoles.LegalStaff)]
    [HttpGet("LegalDocuments/Edit/{id:int?}")]
    public async Task<IActionResult> Edit(int? id, string? kind, int? bookingId, CancellationToken ct)
    {
        var input = new LegalDocumentEditViewModel { Kind = kind is "employment" or "policy" ? kind : "booking", BookingId = bookingId };
        if (id.HasValue)
        {
            var doc = await db.LegalDocuments.FindAsync([id.Value], ct);
            if (doc == null) return NotFound();
            if (doc.SentAt != null) return Conflict("Hồ sơ đã chuyển Booking được giữ nguyên. Tạo bản soạn mới để bổ sung.");
            input = new() { Id = doc.Id, Kind = doc.Kind, Title = doc.Title, Content = doc.Content,
                BookingId = doc.BookingId, EmployeeId = doc.EmployeeId, Revision = doc.Revision };
            ViewBag.ExistingAttachment = doc.AttachmentOriginalName;
        }
        await Choices(ct);
        return View(input);
    }

    [Authorize(Roles = AppRoles.LegalStaff)]
    [HttpPost("LegalDocuments/Save"), ValidateAntiForgeryToken]
    [RequestSizeLimit(27 * 1024 * 1024)]
    public async Task<IActionResult> Save(LegalDocumentEditViewModel input, CancellationToken ct)
    {
        if (!User.IsInRole(AppRoles.LegalStaff)) return Forbid();
        var doc = input.Id == 0 ? new LegalDocument { CreatedAt = DateTime.Now } : await db.LegalDocuments.FindAsync([input.Id], ct);
        if (doc == null) return NotFound();
        if (doc.SentAt != null || (input.Id != 0 && doc.Revision != input.Revision))
            return Conflict("Hồ sơ đã thay đổi hoặc đã chuyển Booking. Tải lại trang.");
        if (input.Kind == "booking" && !await db.Bookings.AnyAsync(b => b.Id == input.BookingId && b.Status != "huy", ct))
            ModelState.AddModelError(nameof(input.BookingId), "Chọn Booking chưa hủy.");
        if (input.Kind == "employment" && !await db.Employees.AnyAsync(e => e.Id == input.EmployeeId, ct))
            ModelState.AddModelError(nameof(input.EmployeeId), "Chọn nhân viên cho hợp đồng lao động.");
        if (string.IsNullOrWhiteSpace(input.Content) && input.Attachment == null && doc.AttachmentName == null)
            ModelState.AddModelError(nameof(input.Content), "Nhập nội dung hoặc đính kèm tài liệu.");
        var extension = input.Attachment == null ? "" : Path.GetExtension(input.Attachment.FileName).ToLowerInvariant();
        if (input.Attachment != null && (input.Attachment.Length <= 0 || input.Attachment.Length > 25 * 1024 * 1024 ||
            extension is not (".pdf" or ".docx" or ".jpg" or ".jpeg" or ".png") || Path.GetFileName(input.Attachment.FileName).Length > 255))
            ModelState.AddModelError(nameof(input.Attachment), "Chọn PDF/DOCX/JPG/PNG, tối đa 25 MB; tên tệp tối đa 255 ký tự.");
        if (!ModelState.IsValid)
        {
            ViewBag.ExistingAttachment = doc.AttachmentOriginalName;
            await Choices(ct);
            return View("Edit", input);
        }
        string? newPath = null;
        try
        {
            if (input.Attachment != null)
            {
                var folder = Path.Combine(env.ContentRootPath, "App_Data", "legal-documents");
                Directory.CreateDirectory(folder);
                doc.AttachmentName = Guid.NewGuid().ToString("N") + extension;
                doc.AttachmentOriginalName = Path.GetFileName(input.Attachment.FileName);
                newPath = Path.Combine(folder, doc.AttachmentName);
                await using var stream = System.IO.File.Create(newPath);
                await input.Attachment.CopyToAsync(stream, ct);
            }
            doc.Title = input.Title.Trim(); doc.Content = input.Content?.Trim() ?? ""; doc.Kind = input.Kind;
            doc.BookingId = input.Kind == "booking" ? input.BookingId : null;
            doc.EmployeeId = input.Kind == "employment" ? input.EmployeeId : null;
            doc.UpdatedAt = DateTime.Now; doc.UpdatedByUserId = UserId;
            if (input.Id == 0) db.LegalDocuments.Add(doc); else doc.Revision++;
            audit.AddEvent(new AuditEvent(AuditModules.Booking, "legal_document_saved",
                $"Lưu {LegalDocument.KindLabel(doc.Kind)}: {doc.Title}, phiên bản {doc.Revision}.", UserId, "LegalDocument", input.Id == 0 ? null : doc.Id.ToString()));
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (newPath != null) System.IO.File.Delete(newPath);
            return Conflict("Hồ sơ vừa được thay đổi. Tải lại trang.");
        }
        catch
        {
            if (newPath != null) System.IO.File.Delete(newPath);
            throw;
        }
        TempData["SuccessMessage"] = "Đã lưu hồ sơ pháp chế.";
        return RedirectToAction(nameof(Details), new { id = doc.Id });
    }

    [Authorize(Roles = AppRoles.LegalStaff)]
    [HttpPost("LegalDocuments/Send/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(int id, int revision, CancellationToken ct)
    {
        if (!User.IsInRole(AppRoles.LegalStaff)) return Forbid();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var doc = await db.LegalDocuments.Include(d => d.Booking).SingleOrDefaultAsync(d => d.Id == id, ct);
        if (doc == null) return NotFound();
        if (doc.Kind != "booking" || doc.Booking == null || doc.Booking.Status == "huy" || doc.Booking.PrimaryManagerId == null)
            return BadRequest("Chỉ chuyển hợp đồng cho Booking chưa hủy và có người phụ trách.");
        if (doc.Revision != revision || doc.SentAt != null) return Conflict("Hồ sơ đã thay đổi hoặc đã chuyển Booking. Tải lại trang.");
        doc.SentAt = doc.UpdatedAt = DateTime.Now; doc.SentByUserId = doc.UpdatedByUserId = UserId; doc.Revision++;
        audit.AddEvent(new AuditEvent(AuditModules.Booking, "legal_document_sent",
            $"Chuyển hồ sơ #{id} cho Booking #{doc.BookingId}, người phụ trách #{doc.Booking.PrimaryManagerId}.", UserId, "LegalDocument", id.ToString()));
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Conflict("Hồ sơ vừa được thay đổi. Tải lại trang."); }
        TempData["SuccessMessage"] = "Đã chuyển hợp đồng. Booking có thể mở tại mục Hợp đồng từ Pháp chế.";
        return RedirectToAction(nameof(Details), new { id });
    }
}

