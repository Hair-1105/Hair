using HanaMedia.Constants;
using HanaMedia.Models;
using HanaMedia.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HanaMedia.Services.Ideas;

public sealed class IdeaDashboardService(ApplicationDbContext db)
{
    public async Task<IdeaDashboardViewModel> GetAsync(CancellationToken ct = default)
    {
        // Match the Idea library and task workspace visibility rules.
        var ideas = await db.Ideas.AsNoTracking()
            .Where(i => i.Booking != null && i.Booking.ContractApprovedAt != null && i.Booking.Status != "huy")
            .Select(i => new { i.Status, i.DirectorReviewStatus }).ToListAsync(ct);
        var tasks = await db.WorkTasks.AsNoTracking()
            .Where(t => t.Module == WorkTaskModules.Ideas && (t.CampaignId == null || t.Campaign!.ConfirmedAt != null)
                && (t.RelatedType != WorkTaskRelatedTypes.Idea || db.Ideas.Any(i => i.Id == t.RelatedId && i.Booking != null && i.Booking.ContractApprovedAt != null && i.Booking.Status != "huy")))
            .Select(t => new { t.AssignedEmployeeId, t.Status, t.Deadline }).ToListAsync(ct);
        var assignedIds = tasks.Select(t => t.AssignedEmployeeId).Distinct().ToArray();
        var employees = await db.Employees.AsNoTracking()
            .Where(e => e.Department == "Y_tuong" || e.Department == "Ý tưởng" ||
                (e.User != null && (e.User.Role == AppRoles.IdeaStaff || e.User.Role == AppRoles.IdeaManager)) || assignedIds.Contains(e.Id))
            .OrderBy(e => e.FullName).Select(e => new { e.Id, e.FullName, e.Status }).ToListAsync(ct);
        var today = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "SE Asia Standard Time").Date;
        return new IdeaDashboardViewModel
        {
            NewIdeas = ideas.Count(i => i.Status == IdeaStatuses.Idea),
            Review = ideas.Count(i => i.Status == IdeaStatuses.Review),
            NeedRevision = ideas.Count(i => i.Status == IdeaStatuses.NeedRevision),
            Approved = ideas.Count(i => i.Status == IdeaStatuses.Approved),
            InProgress = ideas.Count(i => i.Status == IdeaStatuses.InProgress),
            Done = ideas.Count(i => i.Status == IdeaStatuses.Done),
            PendingDirector = ideas.Count(i => i.DirectorReviewStatus == DirectorIdeaReviewStatuses.Pending),
            OverdueTasks = tasks.Count(t => t.Status != WorkTaskStatuses.Done && t.Deadline < today),
            TotalTasks = tasks.Count,
            CompletedTasks = tasks.Count(t => t.Status == WorkTaskStatuses.Done),
            RunningCampaigns = await db.Bookings.CountAsync(c => c.ContractApprovedAt != null && c.Status == "dang_trien_khai", ct),
            Employees = employees.Select(e => new IdeaDashboardEmployee(e.FullName, e.Status ?? "Chưa cập nhật",
                tasks.Count(t => t.AssignedEmployeeId == e.Id && t.Status != WorkTaskStatuses.Done),
                tasks.Count(t => t.AssignedEmployeeId == e.Id && t.Status == WorkTaskStatuses.Done))).ToList()
        };
    }
}
