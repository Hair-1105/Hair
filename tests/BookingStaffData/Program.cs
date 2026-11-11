using System.Security.Claims;
using HanaMedia.Controllers;
using HanaMedia.Models;
using HanaMedia.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", true)
    .AddUserSecrets("HanaMedia-V2-Local-Development").AddEnvironmentVariables().Build();
var connection = new SqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection"));
var testDatabase = "HanaMedia_StaffQA_" + Guid.NewGuid().ToString("N");
connection.InitialCatalog = testDatabase;
await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseSqlServer(connection.ConnectionString).Options);
var passed = 0;
void Check(bool result, string name) { if (!result) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
BookingStaffController Controller(int? id) => new(db)
{
    ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(id.HasValue
            ? new[] { new Claim(ClaimTypes.NameIdentifier, id.Value.ToString()), new Claim(ClaimTypes.Role, "nv_booking") }
            : Array.Empty<Claim>(), "test"))
    } }
};
async Task<BookingStaffViewModel> Page(int id) => (BookingStaffViewModel)((ViewResult)await Controller(id).Booking(default)).Model!;
User UserRow(string name) => new() { Username = name, Email = name + "@example.test", Role = "nv_booking", PasswordHash = "test", SecurityStamp = Guid.NewGuid().ToString() };
Employee EmployeeRow(User user) => new() { User = user, FullName = user.Username, Email = user.Email, Phone = "0900000000", Address = "Test", Dob = new(2000, 1, 1), JoinedDate = new(2020, 1, 1), Department = "Booking", Position = "Staff", ContractType = "thu_viec", Status = "dang_lam_viec" };
Kol KolRow(string name) => new() { Name = name, Platform = "tiktok", ProfileLink = "https://example.test", Location = "Test", ContactInfo = "Test", Status = "tiem_nang" };
try
{
    await db.Database.EnsureCreatedAsync();
    var one = UserRow("staff_one"); var two = UserRow("staff_two"); var unlinked = UserRow("unlinked");
    var employeeOne = EmployeeRow(one); var employeeTwo = EmployeeRow(two);
    db.AddRange(employeeOne, employeeTwo, unlinked);
    await db.SaveChangesAsync();
    Check(!((await Page(unlinked.Id)).HasEmployeeProfile), "Unlinked account sees no sample data");
    Check((await Page(one.Id)).Bookings.Count == 0, "Linked employee with no assignments sees empty list");
    Check(await Controller(null).Booking(default) is ChallengeResult, "Missing user identity denied");
    var kolA = KolRow("Creator A"); var kolB = KolRow("Creator B");
    var shared = new Booking { ClientName = "Real client", CampaignName = "Real campaign", Deadline = new(2027, 1, 15), Status = "dang_trien_khai", ContractStatus = "cho_phap_ly", CreatedAt = DateTime.Now,
        BookingKols = new List<BookingKol> { new() { Kol = kolA }, new() { Kol = kolB } },
        BookingWages = new List<BookingWage> { new() { Employee = employeeOne, AllocatedWage = 1234567 }, new() { Employee = employeeTwo, AllocatedWage = 7654321 } } };
    var privateBooking = new Booking { ClientName = "Other client", CampaignName = "Hidden campaign", Deadline = new(2027, 2, 1), Status = "dang_cho", ContractStatus = "nhap", Kol = kolA,
        BookingWages = new List<BookingWage> { new() { Employee = employeeTwo, AllocatedWage = 9 } } };
    db.AddRange(shared, privateBooking); await db.SaveChangesAsync();
    var first = await Page(one.Id); var second = await Page(two.Id);
    Check(first.Bookings.Count == 1 && first.Bookings[0].Id == shared.Id, "Employee sees only assigned booking");
    Check(first.Bookings[0].AllocatedWage == 1234567, "Only own wage shown on shared booking");
    Check(second.Bookings.Count == 2 && second.Bookings.Single(b => b.Id == shared.Id).AllocatedWage == 7654321, "Different account gets its own assignments and wage");
    Check(first.Bookings[0].KolNames.SequenceEqual(new[] { "Creator A", "Creator B" }), "Multiple KOL names loaded from database");
    Check(first.Bookings[0].ContractLabel == "Chờ Pháp lý kiểm tra", "Real contract workflow label");
    Check(second.Bookings.Single(b => b.Id == privateBooking.Id).LegacyKolName == "Creator A", "Legacy single KOL supported");
    shared.BookingWages.First(w => w.EmployeeId == employeeOne.Id).AllocatedWage = 222;
    shared.ContractStatus = "da_ky"; shared.Status = "hoan_thanh"; await db.SaveChangesAsync();
    var refreshed = (await Page(one.Id)).Bookings.Single();
    Check(refreshed.AllocatedWage == 222 && refreshed.ContractLabel == "Đã ký" && refreshed.StatusLabel == "Hoàn thành", "Database updates reflected on next request");
    Check(!new BookingStaffRow { PostLink = "javascript:alert(1)" }.HasSafePostLink, "Unsafe stored link rejected");
    Console.WriteLine($"Passed {passed} checks.");
}
finally
{
    if (db.Database.GetDbConnection().Database != testDatabase) throw new Exception("Unexpected cleanup database");
    await db.Database.EnsureDeletedAsync();
}
