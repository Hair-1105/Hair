using System.Security.Claims;
using HanaMedia.Constants;
using HanaMedia.Models;
using Microsoft.EntityFrameworkCore;

namespace HanaMedia.Services;

public static class LegalDocumentVisibility
{
    public static IQueryable<LegalDocument> For(ApplicationDbContext db, ClaimsPrincipal user)
    {
        var query = db.LegalDocuments.Include(d => d.Booking).Include(d => d.Employee).AsQueryable();
        if (user.IsInRole(AppRoles.LegalStaff)) return query;
        query = query.Where(d => d.Kind == "booking" && d.SentAt != null);
        if (user.IsInRole(AppRoles.Director) || user.IsInRole(AppRoles.Accountant)) return query;
        if (!(user.IsInRole(AppRoles.BookingManager) || user.IsInRole(AppRoles.BookingStaff)) ||
            !int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return query.Where(d => false);
        return query.Where(d => d.Booking != null && (d.Booking.PrimaryManager!.UserId == userId ||
            d.Booking.BookingWages.Any(w => w.Employee.UserId == userId)));
    }
}
