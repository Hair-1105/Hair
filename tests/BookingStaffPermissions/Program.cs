using System.Security.Claims;
using HanaMedia.Constants;
using HanaMedia.Controllers;
using HanaMedia.Models;
using HanaMedia.Services.Auditing;
using HanaMedia.Services.Config;
using HanaMedia.Services.Tasks;
using HanaMedia.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json")
    .AddJsonFile("appsettings.Development.json", true).AddUserSecrets("HanaMedia-V2-Local-Development").AddEnvironmentVariables().Build();
var connection = new SqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection"));
connection.InitialCatalog = "HanaMedia_BookingPermissionsQA_" + Guid.NewGuid().ToString("N");
await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection.ConnectionString).Options);
var audit = new TestAudit(); int passed = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
T As<T>(T controller, User user) where T : Controller
{
    var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Role, user.Role) }, "test")) };
    controller.ControllerContext = new ControllerContext { HttpContext = http };
    controller.TempData = new TempDataDictionary(http, new TestTempData()); return controller;
}
User Account(string name, string role) => new() { Username = name, Email = name + "@example.test", Role = role, PasswordHash = "test", SecurityStamp = Guid.NewGuid().ToString() };
Employee Staff(User user) => new() { User = user, FullName = user.Username, Email = user.Email, Phone = "0900000000", Address = "Test", Dob = new(2000,1,1), JoinedDate = new(2020,1,1), Department = "Booking", Position = "Staff", ContractType = "thu_viec", Status = "dang_lam_viec" };
try
{
    await db.Database.EnsureCreatedAsync();
    var staff = Account("staff", AppRoles.BookingStaff); var manager = Account("manager", AppRoles.BookingManager); var director = Account("director", AppRoles.Director); var unlinked = Account("unlinked", AppRoles.BookingStaff);
    var employee = Staff(staff); db.AddRange(employee, Staff(manager), director, unlinked);
    var kol = new Kol { Name = "Creator", ProfileLink = "https://example.test", ContactInfo = "Test", Location = "Test", Platform = "tiktok", Status = "tiem_nang" }; db.Add(kol); await db.SaveChangesAsync();
    var campaigns = As(new CampaignController(db, audit), staff);
    var campaign = new Campaign { Name = "Staff campaign", Client = "Test", Budget = 1000000, ManagerEmployeeId = employee.Id, Status = CampaignStatuses.Running, ConfirmedAt = DateTime.Now };
    await campaigns.Create(campaign, "2026-10-01", "2026-12-31", default);
    Check(campaign.Id > 0 && campaign.Status == CampaignStatuses.AwaitingSignature && campaign.ConfirmedAt == null, "Staff creates campaign but cannot bypass director confirmation");
    var confirmRoles = typeof(CampaignController).GetMethod(nameof(CampaignController.Confirm))!.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Roles;
    Check(confirmRoles == AppRoles.Director, "Campaign confirmation remains director-only");
    var bookings = As(new BookingController(db, audit, new TestConfig()), staff);
    Booking Input() => new() { CampaignId = campaign.Id, BookingPrice = 1000000, Status = "dang_cho", JobDescription = "Test", PrimaryManagerId = 999 };
    await bookings.Create(Input(), [kol.Id], null, "2026-12-01", null, new(), default);
    Check(await db.Bookings.CountAsync() == 0, "Booking cannot use unconfirmed campaign");
    await As(new CampaignController(db, audit), director).Confirm(campaign.Id, default);
    await bookings.Create(Input(), [kol.Id], null, "2026-12-01", null, new() { [kol.Id] = 350000 }, default);
    var booking = await db.Bookings.Include(b => b.BookingKols).SingleAsync();
    Check(booking.PrimaryManagerId == employee.Id, "Staff becomes responsible employee; forged owner ignored");
    Check(booking.BookingKols.Single().CastAmount == 350000 && booking.CommissionPercent == 10 && booking.CompanyPercent == 50, "Staff allocates KOL money with existing finance rules");
    var page = await bookings.Index(null, null, null, null, null, null, default);
    Check(page is ViewResult && bookings.ViewBag.IsWritable == true, "Staff receives shared writable Booking page");
    await bookings.Edit(booking.Id, Input(), [kol.Id], null, "2026-12-02", null, new() { [kol.Id] = 400000 }, default);
    Check(booking.BookingKols.Single().CastAmount == 400000, "Staff edits own allocation");
    var other = As(new BookingController(db, audit, new TestConfig()), manager);
    Check(await other.Edit(booking.Id, Input(), [kol.Id], null, "2026-12-02", null, new(), default) is ForbidResult, "Same ownership restriction applies to manager");
    Check(await As(new BookingController(db, audit, new TestConfig()), unlinked).Create(Input(), [kol.Id], null, "2026-12-01", null, new(), default) is ForbidResult, "Unlinked account cannot create ownerless Booking");
    Check(await bookings.Approve(booking.Id, default) is ForbidResult, "Staff cannot approve own Booking");
    await bookings.SubmitApproval(booking.Id, default);
    Check(booking.ContractStatus == "cho_duyet", "Staff submits own Booking for director approval");
    await bookings.Edit(booking.Id, Input(), [kol.Id], null, "2026-12-02", null, new() { [kol.Id] = 1 }, default);
    Check(booking.BookingKols.Single().CastAmount == 400000, "Submitted Booking locks allocations");
    var tasks = new WorkTaskService(db, audit);
    var staffTasks = await tasks.GetPageAsync(staff.Id, AppRoles.BookingStaff, WorkTaskModules.Booking, null, 1);
    var managerTasks = await tasks.GetPageAsync(manager.Id, AppRoles.BookingManager, WorkTaskModules.Booking, null, 1);
    Check(!staffTasks.CanCreate && managerTasks.CanCreate, "Only manager can assign work");
    await campaigns.Complete(campaign.Id, default);
    Check(campaign.Status == CampaignStatuses.Completed, "Staff can complete confirmed campaign");
    Check(await campaigns.Accept(campaign.Id, false, default) is BadRequestObjectResult, "Acceptance still requires payment confirmation");
    await campaigns.Accept(campaign.Id, true, default);
    Check(campaign.Status == CampaignStatuses.Accepted, "Staff confirms receipt and accepts campaign");
    Console.WriteLine($"Passed {passed} checks.");
}
finally { await db.Database.EnsureDeletedAsync(); }
sealed class TestAudit : ISystemAuditService
{
    public void AddEvent(AuditEvent e) { }
    public Task WriteAsync(AuditEvent e, CancellationToken ct = default) => Task.CompletedTask;
    public void AddAccountEvent(int? userId, string actionType, string detail) { }
}
sealed class TestTempData : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
}
sealed class TestConfig : IBusinessConfigService
{
    public Task<BusinessConfigViewModel> GetAsync(CancellationToken ct = default) => Task.FromResult(new BusinessConfigViewModel());
    public Task<BusinessConfigUpdateResult> UpdateAsync(BusinessConfigViewModel input, int actorUserId, CancellationToken ct = default) => throw new NotSupportedException();
}
