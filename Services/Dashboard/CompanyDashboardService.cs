using HanaMedia.Constants;
using HanaMedia.Models;
using HanaMedia.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HanaMedia.Services.Dashboard;

public sealed class CompanyDashboardService : ICompanyDashboardService
{
    private static readonly string[] ActiveEmployeeStatuses = ["dang_lam_viec", "thu_viec", "cho_duyet_nghi"];
    private static readonly (string Status, string Label)[] BookingStatusOrder =
    [
        ("dang_cho", "Đang chờ"),
        ("da_duyet", "Giám đốc duyệt"),
        ("dang_trien_khai", "Đang chạy"),
        ("hoan_thanh", "Hoàn thành"),
        ("da_nghiem_thu", "Đã nghiệm thu"),
        ("huy", "Đã hủy")
    ];

    private readonly ApplicationDbContext _context;

    public CompanyDashboardService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<CompanyDashboardViewModel> GetAsync(
        string? period,
        CancellationToken cancellationToken = default,
        bool includeDirectorMetrics = false)
    {
        var now = DateTime.Now;
        var normalizedPeriod = NormalizePeriod(period);
        var (startAt, endAt, periodLabel) = GetPeriodRange(normalizedPeriod, now);
        var startDate = DateOnly.FromDateTime(startAt);
        var endDate = DateOnly.FromDateTime(endAt);

        var departments = await _context.Departments.AsNoTracking()
            .OrderBy(item => item.Name)
            .Select(item => new { item.Code, item.Name })
            .ToListAsync(cancellationToken);
        var departmentNames = departments.ToDictionary(item => item.Code, item => item.Name);

        var employees = await _context.Employees.AsNoTracking()
            .Select(item => new EmployeeMetricRow
            {
                Id = item.Id,
                FullName = item.FullName,
                Department = item.Department,
                Position = item.Position,
                JoinedDate = item.JoinedDate,
                Status = item.Status,
                UpdatedAt = item.UpdatedAt
            })
            .ToListAsync(cancellationToken);
        var activeEmployees = employees
            .Where(item => ActiveEmployeeStatuses.Contains(item.Status ?? string.Empty))
            .ToList();

        var departmentRows = activeEmployees
            .GroupBy(item => item.Department)
            .Select(group => new CompanyDashboardDepartmentViewModel
            {
                Code = group.Key,
                Name = GetDepartmentName(group.Key, departmentNames),
                EmployeeCount = group.Count(),
                Percentage = activeEmployees.Count == 0
                    ? 0
                    : Math.Round(group.Count() * 100m / activeEmployees.Count, 1)
            })
            .OrderByDescending(item => item.EmployeeCount)
            .ThenBy(item => item.Name)
            .ToList();

        var bookings = await _context.Bookings.AsNoTracking()
            .Where(item => (item.RevenueDate ?? item.CreatedAt) >= startAt && (item.RevenueDate ?? item.CreatedAt) < endAt)
            .Select(item => new BookingMetricRow
            {
                Status = item.Status == "huy" ? "huy" : item.CustomerPaidAt != null ? "da_nghiem_thu" : item.ContractApprovedAt == null ? "dang_cho" : item.Status == "hoan_thanh" ? "hoan_thanh" : item.Status == "dang_trien_khai" ? "dang_trien_khai" : "da_duyet",
                BookingPrice = item.BookingPrice,
                ActualCost = item.ActualCost,
                FinanceVersion = item.FinanceVersion,
                CommissionPercent = item.CommissionPercent,
                CastPercent = item.CastPercent,
                CreatedAt = item.RevenueDate ?? item.CreatedAt
            })
            .ToListAsync(cancellationToken);
        var validBookings = await _context.Bookings.AsNoTracking()
            .Where(b => b.Status != "huy" && b.CustomerPaidAt != null &&
                (b.CustomerPaymentDate ?? b.CustomerPaidAt) >= startAt && (b.CustomerPaymentDate ?? b.CustomerPaidAt) < endAt)
            .Select(b => new BookingMetricRow { Status = "da_nghiem_thu", BookingPrice = b.BookingPrice, ActualCost = b.ActualCost,
                FinanceVersion = b.FinanceVersion, CommissionPercent = b.CommissionPercent, CastPercent = b.CastPercent,
                CreatedAt = b.CustomerPaymentDate ?? b.CustomerPaidAt }).ToListAsync(cancellationToken);
        // Use each Booking's saved rates and round per Booking, just like its payables.
        // Do not add staff allocations: they are already part of the commission pool.
        var v4Bookings = validBookings.Where(item => item.FinanceVersion == 1).ToList();
        int outstandingCount = 0;
        decimal outstandingAmount = 0;
        var outstandingCampaigns = new List<OutstandingCampaignViewModel>();
        if (includeDirectorMetrics)
        {
            // An outstanding balance is current stock, not limited to creations in this period.
            var outstanding = _context.Bookings.AsNoTracking()
                .Where(c => c.CustomerPaidAt == null && c.Status != "huy");
            outstandingCount = await outstanding.CountAsync(cancellationToken);
            outstandingAmount = await outstanding.SumAsync(b => (decimal?)b.BookingPrice, cancellationToken) ?? 0m;
            outstandingCampaigns = await outstanding
                .OrderBy(c => c.Status == "hoan_thanh" ? 0 : c.Status == "dang_trien_khai" ? 1 : 2)
                .ThenBy(c => c.CreatedAt).ThenBy(c => c.Id).Take(20)
                .Select(c => new OutstandingCampaignViewModel
                {
                    Id = c.Id, Name = c.CampaignName, Client = c.ClientName, Status = c.Status,
                    ManagerName = c.PrimaryManager == null ? "—" : c.PrimaryManager.FullName
                }).ToListAsync(cancellationToken);
        }

        var bookingStatusCounts = BookingStatusOrder
            .Select(item => new CompanyDashboardBookingStatusViewModel
            {
                Status = item.Status,
                Label = item.Label,
                Count = bookings.Count(booking => booking.Status == item.Status)
            })
            .ToList();

        var taskRows = await _context.WorkTasks.AsNoTracking()
            .Where(item => item.Deadline >= startAt && item.Deadline < endAt)
            .Select(item => new TaskMetricRow
            {
                Status = item.Status,
                Department = item.AssignedEmployee.Department
            })
            .ToListAsync(cancellationToken);
        var departmentPerformance = taskRows
            .GroupBy(item => item.Department)
            .Select(group => new CompanyDashboardDepartmentPerformanceViewModel
            {
                Code = group.Key,
                Name = GetDepartmentName(group.Key, departmentNames),
                TotalTasks = group.Count(),
                CompletedTasks = group.Count(item => item.Status == WorkTaskStatuses.Done),
                CompletionRate = Math.Round(group.Count(item => item.Status == WorkTaskStatuses.Done) * 100m / group.Count(), 1)
            })
            .OrderByDescending(item => item.CompletionRate)
            .ThenBy(item => item.Name)
            .ToList();

        var employeeChanges = BuildEmployeeChanges(employees, departmentNames, startAt, endAt);
        var monthlyRevenue = new List<MonthlyBookingRevenue>();
        if (includeDirectorMetrics)
        {
            var firstMonth = new DateTime(now.Year, now.Month, 1).AddMonths(-11);
            var afterLastMonth = firstMonth.AddMonths(12);
            var monthlyBookings = await _context.Bookings.AsNoTracking()
                .Where(b => b.Status != "huy" &&
                    (((b.RevenueDate ?? b.CreatedAt) >= firstMonth && (b.RevenueDate ?? b.CreatedAt) < afterLastMonth) ||
                     ((b.CustomerPaymentDate ?? b.CustomerPaidAt) >= firstMonth && (b.CustomerPaymentDate ?? b.CustomerPaidAt) < afterLastMonth)))
                .Select(b => new { CreatedAt = b.RevenueDate ?? b.CreatedAt, CustomerPaidAt = b.CustomerPaymentDate ?? b.CustomerPaidAt, b.BookingPrice })
                .ToListAsync(cancellationToken);
            for (var month = firstMonth; month < afterLastMonth; month = month.AddMonths(1))
            {
                var next = month.AddMonths(1);
                monthlyRevenue.Add(new MonthlyBookingRevenue
                {
                    Month = month,
                    BookingValue = monthlyBookings.Where(b => b.CreatedAt >= month && b.CreatedAt < next).Sum(b => b.BookingPrice),
                    CustomerReceipts = monthlyBookings.Where(b => b.CustomerPaidAt >= month && b.CustomerPaidAt < next).Sum(b => b.BookingPrice)
                });
            }
        }

        return new CompanyDashboardViewModel
        {
            Period = normalizedPeriod,
            MonthlyRevenue = monthlyRevenue,
            PeriodLabel = periodLabel,
            RangeStart = startAt,
            RangeEndExclusive = endAt,
            GeneratedAt = now,
            TotalEmployees = activeEmployees.Count,
            NewEmployees = employees.Count(item => item.JoinedDate >= startDate && item.JoinedDate < endDate),
            DepartedEmployees = employees.Count(item =>
                item.Status == "ngung_hoat_dong" &&
                item.UpdatedAt.HasValue && item.UpdatedAt >= startAt && item.UpdatedAt < endAt),
            BookingCount = includeDirectorMetrics ? await _context.Bookings.CountAsync(cancellationToken) : bookings.Count,
            RunningBookings = includeDirectorMetrics ? await _context.Bookings.CountAsync(b => b.Status == "dang_trien_khai", cancellationToken) : bookings.Count(item => item.Status == "dang_trien_khai"),
            BookingRevenue = validBookings.Sum(item => item.BookingPrice),
            BookingCost = validBookings.Sum(item => item.ActualCost),
            BookingRemuneration = includeDirectorMetrics ? v4Bookings.Sum(item => decimal.Round(item.BookingPrice * item.CommissionPercent / 100m, 2, MidpointRounding.AwayFromZero)) : 0,
            KolRemuneration = includeDirectorMetrics ? v4Bookings.Sum(item => decimal.Round(item.BookingPrice * item.CastPercent / 100m, 2, MidpointRounding.AwayFromZero)) : 0,
            LegacyRemunerationExcludedCount = includeDirectorMetrics ? validBookings.Count(item => item.FinanceVersion != 1) : 0,
            OutstandingCampaignCount = outstandingCount,
            OutstandingAmount = outstandingAmount,
            OutstandingCampaigns = outstandingCampaigns,
            RunningCampaigns = await _context.Bookings.AsNoTracking()
                .CountAsync(item => item.Status == "dang_trien_khai", cancellationToken),
            PendingIdeas = await _context.Ideas.AsNoTracking()
                .CountAsync(item =>
                    item.Status == IdeaStatuses.Review ||
                    (item.Status == IdeaStatuses.Approved &&
                     item.DirectorReviewStatus == DirectorIdeaReviewStatuses.Pending),
                    cancellationToken),
            OverdueTasks = await _context.WorkTasks.AsNoTracking()
                .CountAsync(item => item.Deadline < now && item.Status != WorkTaskStatuses.Done, cancellationToken),
            Departments = departmentRows,
            DepartmentPerformance = departmentPerformance,
            BookingStatuses = bookingStatusCounts,
            RevenueSeries = BuildRevenueSeries(validBookings, normalizedPeriod, startAt, endAt),
            EmployeeChanges = employeeChanges
        };
    }

    private static string NormalizePeriod(string? period) => period?.ToLowerInvariant() switch
    {
        "day" => "day",
        "quarter" => "quarter",
        _ => "month"
    };

    private static (DateTime StartAt, DateTime EndAt, string Label) GetPeriodRange(string period, DateTime now)
    {
        if (period == "day")
        {
            return (now.Date, now.Date.AddDays(1), "Hôm nay");
        }

        if (period == "quarter")
        {
            var startMonth = ((now.Month - 1) / 3) * 3 + 1;
            var startOfQuarter = new DateTime(now.Year, startMonth, 1);
            return (startOfQuarter, startOfQuarter.AddMonths(3), $"Quý {(startMonth - 1) / 3 + 1}/{now.Year}");
        }

        var startOfMonth = new DateTime(now.Year, now.Month, 1);
        return (startOfMonth, startOfMonth.AddMonths(1), $"Tháng {now.Month}/{now.Year}");
    }

    private static IReadOnlyList<CompanyDashboardRevenuePointViewModel> BuildRevenueSeries(
        IEnumerable<BookingMetricRow> bookingRows,
        string period,
        DateTime startAt,
        DateTime endAt)
    {
        var bookings = bookingRows
            .Where(item => item.Status != "huy" && item.CreatedAt is not null)
            .ToList();
        var result = new List<CompanyDashboardRevenuePointViewModel>();

        if (period == "quarter")
        {
            for (var cursor = startAt; cursor < endAt; cursor = cursor.AddMonths(1))
            {
                var bucketEnd = cursor.AddMonths(1);
                var rows = bookings.Where(item => item.CreatedAt!.Value >= cursor && item.CreatedAt.Value < bucketEnd).ToList();
                result.Add(new CompanyDashboardRevenuePointViewModel
                {
                    Label = $"T{cursor.Month}",
                    Revenue = rows.Sum(item => item.BookingPrice),
                    Cost = rows.Sum(item => item.ActualCost)
                });
            }

            return result;
        }

        if (period == "day")
        {
            result.Add(new CompanyDashboardRevenuePointViewModel
            {
                Label = "Hôm nay",
                Revenue = bookings.Sum(item => item.BookingPrice),
                Cost = bookings.Sum(item => item.ActualCost)
            });
            return result;
        }

        for (var cursor = startAt; cursor < endAt; cursor = cursor.AddDays(7))
        {
            var bucketEnd = cursor.AddDays(7) < endAt ? cursor.AddDays(7) : endAt;
            var rows = bookings.Where(item => item.CreatedAt!.Value >= cursor && item.CreatedAt.Value < bucketEnd).ToList();
            result.Add(new CompanyDashboardRevenuePointViewModel
            {
                Label = $"{cursor:dd/MM}",
                Revenue = rows.Sum(item => item.BookingPrice),
                Cost = rows.Sum(item => item.ActualCost)
            });
        }

        return result;
    }

    private static IReadOnlyList<CompanyDashboardEmployeeChangeViewModel> BuildEmployeeChanges(
        IEnumerable<EmployeeMetricRow> employeeRows,
        IReadOnlyDictionary<string, string> departmentNames,
        DateTime startAt,
        DateTime endAt)
    {
        var changes = new List<CompanyDashboardEmployeeChangeViewModel>();
        foreach (var employee in employeeRows)
        {
            var joinedAt = employee.JoinedDate.ToDateTime(TimeOnly.MinValue);
            if (joinedAt >= startAt && joinedAt < endAt)
            {
                changes.Add(new CompanyDashboardEmployeeChangeViewModel
                {
                    EmployeeId = employee.Id,
                    FullName = employee.FullName,
                    DepartmentName = GetDepartmentName(employee.Department, departmentNames),
                    Position = employee.Position,
                    EventDate = joinedAt,
                    ChangeType = "joined",
                    ChangeLabel = "Nhân sự mới"
                });
            }

            if (employee.Status == "ngung_hoat_dong" && employee.UpdatedAt is DateTime departedAt &&
                departedAt >= startAt && departedAt < endAt)
            {
                changes.Add(new CompanyDashboardEmployeeChangeViewModel
                {
                    EmployeeId = employee.Id,
                    FullName = employee.FullName,
                    DepartmentName = GetDepartmentName(employee.Department, departmentNames),
                    Position = employee.Position,
                    EventDate = departedAt,
                    ChangeType = "departed",
                    ChangeLabel = "Nghỉ việc"
                });
            }
        }

        return changes.OrderByDescending(item => item.EventDate).Take(8).ToList();
    }

    private static string GetDepartmentName(string code, IReadOnlyDictionary<string, string> departmentNames)
        => departmentNames.TryGetValue(code, out var name) ? name : code;

    private sealed class EmployeeMetricRow
    {
        public int Id { get; init; }
        public string FullName { get; init; } = string.Empty;
        public string Department { get; init; } = string.Empty;
        public string Position { get; init; } = string.Empty;
        public DateOnly JoinedDate { get; init; }
        public string? Status { get; init; }
        public DateTime? UpdatedAt { get; init; }
    }

    private sealed class BookingMetricRow
    {
        public int FinanceVersion { get; init; }
        public decimal CommissionPercent { get; init; }
        public decimal CastPercent { get; init; }
        public string? Status { get; init; }
        public decimal BookingPrice { get; init; }
        public decimal ActualCost { get; init; }
        public DateTime? CreatedAt { get; init; }
    }

    private sealed class TaskMetricRow
    {
        public string Status { get; init; } = string.Empty;
        public string Department { get; init; } = string.Empty;
    }
}
