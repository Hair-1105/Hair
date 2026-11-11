using HanaMedia.Constants;
using HanaMedia.Models;
using HanaMedia.Services.Ideas;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json")
    .AddJsonFile("appsettings.Development.json", true).AddUserSecrets("HanaMedia-V2-Local-Development").AddEnvironmentVariables().Build();
var connection = new SqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection"));
connection.InitialCatalog = "HanaMedia_IdeaDashboardQA_" + Guid.NewGuid().ToString("N");
await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection.ConnectionString).Options);
var service = new IdeaDashboardService(db); int passed = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
var today = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "SE Asia Standard Time").Date;
try
{
    await db.Database.EnsureCreatedAsync();
    var empty = await service.GetAsync();
    Check(empty.TotalTasks == 0 && empty.CompletionPercent == 0 && empty.Employees.Count == 0 && empty.Review == 0, "Empty database has zeros and no demo staff");
    var user = new User { Username = "qa", Email = "qa@example.test", Role = AppRoles.IdeaStaff, PasswordHash = "test", SecurityStamp = Guid.NewGuid().ToString() };
    var employee = new Employee { User = user, FullName = "QA Creator", Email = user.Email, Phone = "0900000000", Address = "Test", Dob = new(2000,1,1), JoinedDate = new(2020,1,1), Department = "Y_tuong", Position = "Staff", ContractType = "thu_viec", Status = "dang_lam_viec" };
    var running = new Campaign { Name = "Visible", Client = "Test", StartDate = new(2026,1,1), EndDate = new(2026,12,31), Status = CampaignStatuses.Running, ConfirmedAt = today };
    var hidden = new Campaign { Name = "Not confirmed", Client = "Test", StartDate = running.StartDate, EndDate = running.EndDate, Status = CampaignStatuses.AwaitingSignature };
    running.ManagerEmployee = employee; hidden.ManagerEmployee = employee;
    db.AddRange(employee, running, hidden); await db.SaveChangesAsync();
    foreach (var status in IdeaStatuses.All)
        db.Ideas.Add(new Idea { Title = status, ClientName = "Test", Industry = "Test", Category = "trend", Campaign = running, Status = status, Deadline = new(2026,12,31), DirectorReviewStatus = DirectorIdeaReviewStatuses.Approved });
    var invisible = new Idea { Title = "Hidden", ClientName = "Test", Industry = "Test", Category = "trend", Campaign = hidden, Status = IdeaStatuses.Review, Deadline = new(2026,12,31) };
    db.Ideas.Add(invisible); await db.SaveChangesAsync();
    WorkTask TaskRow(string module, string status, DateTime deadline, Campaign? campaign = null) => new() { Title = "QA", Module = module, Status = status, Deadline = deadline, AssignedEmployeeId = employee.Id, CreatedByUserId = user.Id, ReviewerUserId = user.Id, Campaign = campaign, CreatedAt = today, UpdatedAt = today };
    db.AddRange(TaskRow(WorkTaskModules.Ideas, WorkTaskStatuses.Done, today.AddDays(-2), running), TaskRow(WorkTaskModules.Ideas, WorkTaskStatuses.Todo, today.AddDays(-1)), TaskRow(WorkTaskModules.Ideas, WorkTaskStatuses.Review, today));
    db.Add(TaskRow(WorkTaskModules.Booking, WorkTaskStatuses.Todo, today.AddDays(-3)));
    db.Add(TaskRow(WorkTaskModules.Ideas, WorkTaskStatuses.Todo, today.AddDays(-3), hidden));
    var hiddenRelated = TaskRow(WorkTaskModules.Ideas, WorkTaskStatuses.Todo, today.AddDays(-3)); hiddenRelated.RelatedType = WorkTaskRelatedTypes.Idea; hiddenRelated.RelatedId = invisible.Id; db.Add(hiddenRelated);
    await db.SaveChangesAsync();
    var result = await service.GetAsync();
    Check(result.NewIdeas == 1 && result.Review == 1 && result.NeedRevision == 1 && result.Approved == 1 && result.InProgress == 1 && result.Done == 1, "All six statuses loaded; unconfirmed campaign excluded");
    Check(result.TotalTasks == 3 && result.CompletedTasks == 1, "Only visible Idea tasks counted");
    Check(result.OverdueTasks == 1, "Overdue excludes done, today, other departments and hidden tasks");
    Check(result.CompletionPercent == 33, "Completion rate calculated from actual tasks");
    Check(result.RunningCampaigns == 1 && result.PendingDirector == 0, "Confirmed campaigns and director decisions counted separately");
    Check(result.Employees.Single().Name == employee.FullName && result.Employees.Single().OpenTasks == 2 && result.Employees.Single().DoneTasks == 1, "Employee table reflects database task assignment");
    var idea = await db.Ideas.FirstAsync(i => i.Status == IdeaStatuses.Review && i.CampaignId == running.Id);
    idea.Status = IdeaStatuses.Approved; idea.DirectorReviewStatus = DirectorIdeaReviewStatuses.Pending; await db.SaveChangesAsync();
    var refreshed = await service.GetAsync();
    Check(refreshed.Review == 0 && refreshed.Approved == 2 && refreshed.PendingDirector == 1, "Database updates reflected on next load");
    Console.WriteLine($"Passed {passed} checks.");
}
finally { await db.Database.EnsureDeletedAsync(); }
