using HanaMedia.Models;
using HanaMedia.Constants;
using HanaMedia.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HanaMedia.Services;

public sealed class PersonalBookingCommissionService(ApplicationDbContext db)
{
    public async Task<PersonalBookingCommissionViewModel> GetAsync(int userId, CancellationToken ct = default)
    {
        var employeeId = await db.Employees.AsNoTracking().Where(e => e.UserId == userId)
            .Select(e => (int?)e.Id).SingleOrDefaultAsync(ct);
        if (!employeeId.HasValue) return new();
        var rows = await db.Bookings.AsNoTracking()
            .Where(b => b.PrimaryManagerId == employeeId && b.Status != "huy")
            .Select(b => new { b.FinanceVersion, b.BookingPrice, b.CommissionPercent,
                CustomerPaid = b.CustomerPaidAt != null,
                Paid = b.Payments.Where(p => p.Kind == "manager" && p.PayeeId == employeeId && p.IsPaid).Sum(p => (decimal?)p.Amount) ?? 0m,
                Allocated = b.BookingWages.Sum(w => (decimal?)w.AllocatedWage) ?? 0m }).ToListAsync(ct);
        // Match the manager payable in Accounting; do not count staff allocations twice.
        var current = rows.Where(b => b.FinanceVersion == 1).Select(b => new
        {
            Amount = decimal.Round(b.BookingPrice * b.CommissionPercent / 100m, 2, MidpointRounding.AwayFromZero) - b.Allocated,
            b.CustomerPaid,
            b.Paid
        }).ToList();
        return new()
        {
            HasEmployeeProfile = true,
            BookingCount = current.Count,
            CustomerPaidCount = current.Count(b => b.CustomerPaid),
            LegacyBookingCount = rows.Count(b => b.FinanceVersion != 1),
            Total = current.Where(b => b.CustomerPaid).Sum(b => b.Amount),
            PendingCustomerPayment = current.Where(b => !b.CustomerPaid).Sum(b => b.Amount),
            CompanyPaid = current.Where(b => b.CustomerPaid).Sum(b => Math.Clamp(b.Paid, 0m, Math.Max(0m, b.Amount)))
        };
    }
}
