using HanaMedia.Constants;
using HanaMedia.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HanaMedia.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HanaMedia.Controllers
{
    [Authorize(Roles = AppRoles.BookingStaff)]
    public class BookingStaffController : Controller
    {
        private readonly ApplicationDbContext _context;

        public BookingStaffController(ApplicationDbContext context) => _context = context;

        public async Task<IActionResult> Booking(CancellationToken cancellationToken)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return Challenge();
            var employeeId = await _context.Employees.AsNoTracking()
                .Where(employee => employee.UserId == userId)
                .Select(employee => (int?)employee.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (employeeId is null)
                return View(new BookingStaffViewModel());

            var bookings = await _context.BookingWages.AsNoTracking()
                .Where(wage => wage.EmployeeId == employeeId.Value)
                .OrderByDescending(wage => wage.Booking.CreatedAt).ThenByDescending(wage => wage.BookingId)
                .Select(wage => new BookingStaffRow
                {
                    Id = wage.BookingId,
                    ClientName = wage.Booking.ClientName,
                    CampaignName = wage.Booking.Campaign != null ? wage.Booking.Campaign.Name : wage.Booking.CampaignName,
                    KolNames = wage.Booking.BookingKols.OrderBy(item => item.KolId).Select(item => item.Kol.Name).ToList(),
                    LegacyKolName = wage.Booking.Kol != null ? wage.Booking.Kol.Name : null,
                    Deadline = wage.Booking.Deadline,
                    AllocatedWage = wage.AllocatedWage,
                    Status = wage.Booking.WorkflowStatus,
                    ContractStatus = wage.Booking.ContractStatus,
                    PostLink = wage.Booking.PostLink
                }).ToListAsync(cancellationToken);
            return View(new BookingStaffViewModel { HasEmployeeProfile = true, Bookings = bookings });
        }

        public IActionResult KOL_KOC()
        {
            return RedirectToAction("Index", "Kol");
        }

        public IActionResult Reported() => RedirectToAction("Index", "Reports", new { type = ReportTypes.Booking });
    }
}
