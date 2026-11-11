using System.Security.Claims;
using HanaMedia.Constants;
using HanaMedia.Controllers;
using HanaMedia.Models;
using HanaMedia.Services.Auditing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", true)
    .AddUserSecrets("HanaMedia-V2-Local-Development").AddEnvironmentVariables().Build();
var connection = new SqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection"));
connection.InitialCatalog = "HanaMedia_KolQA_" + Guid.NewGuid().ToString("N");
await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection.ConnectionString).Options);
var audit = new TestAudit();
int passed = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); passed++; }
KolController Controller(string role)
{
    var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, role) }, "test")) };
    return new(db, audit) { ControllerContext = new ControllerContext { HttpContext = http }, TempData = new TempDataDictionary(http, new TestTempData()) };
}
Kol Row(string name) => new() { Name = name, ProfileLink = "https://example.test", ContactInfo = "Test", Location = "Test", Status = "tiem_nang" };
try
{
    await db.Database.EnsureCreatedAsync();
    var roles = typeof(KolController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Roles!.Split(',');
    Check(roles.Contains(AppRoles.IdeaManager), "Idea manager authorized for KOL pages");
    var manager = Controller(AppRoles.IdeaManager);
    var kol = Row("Creator QA");
    await manager.Create(kol, ["tiktok"], ["beauty"], default);
    Check(kol.Id > 0 && await db.Kols.CountAsync() == 1, "Idea manager creates real KOL profile");
    foreach (var role in new[] { AppRoles.BookingManager, AppRoles.BookingStaff, AppRoles.Director })
    {
        var reader = Controller(role);
        Check(await reader.Create(Row("Forbidden"), ["tiktok"], ["beauty"], default) is ForbidResult, role + " cannot create");
        Check(await reader.Edit(kol.Id, Row("Forbidden"), ["tiktok"], ["beauty"], default) is ForbidResult, role + " cannot edit");
        Check(await reader.Delete(kol.Id, default) is ForbidResult, role + " cannot delete");
        Check(await reader.Details(kol.Id, default) is ViewResult, role + " retains read access");
    }
    await manager.Edit(kol.Id, Row("Updated creator"), ["youtube"], ["travel"], default);
    Check((await db.Kols.SingleAsync()).Name == "Updated creator", "Idea manager edits profile");
    var list = (ViewResult)await manager.Index("Updated", null, null, default);
    Check(((IEnumerable<Kol>)list.Model!).Count() == 1, "Search returns updated database profile");
    await manager.Delete(kol.Id, default);
    Check(!kol.IsActive && await db.Kols.CountAsync() == 1, "Soft delete preserves KOL record");
    Check(audit.Events.Count == 3, "Create edit delete audited");
    foreach (var path in new[] { "Views/Shared/_SidebarPartial.cshtml", "Views/Shared/_WorkTaskSidebarPartial.cshtml" })
    {
        var text = File.ReadAllText(path);
        var bookingSection = text.Split("else if (role == AppRoles.BookingManager)")[1].Split("else if (role == AppRoles.IdeaManager)")[0];
        var ideaSection = text.Split("else if (role == AppRoles.IdeaManager)")[1].Split("else if (role == AppRoles.IdeaStaff)")[0];
        Check(!bookingSection.Contains("href=\"/Kols\"") && ideaSection.Contains("href=\"/Kols\""), path + " moves menu to idea manager");
    }
    Console.WriteLine($"Passed {passed} checks.");
}
finally { await db.Database.EnsureDeletedAsync(); }

sealed class TestAudit : ISystemAuditService
{
    public List<AuditEvent> Events { get; } = new();
    public void AddEvent(AuditEvent e) => Events.Add(e);
    public Task WriteAsync(AuditEvent e, CancellationToken ct = default) { Events.Add(e); return Task.CompletedTask; }
    public void AddAccountEvent(int? userId, string actionType, string detail) { }
}
sealed class TestTempData : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
}
