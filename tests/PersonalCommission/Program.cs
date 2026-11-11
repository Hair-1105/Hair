using HanaMedia.Constants;
using HanaMedia.Models;
using HanaMedia.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json")
    .AddJsonFile("appsettings.Development.json", true).AddUserSecrets("HanaMedia-V2-Local-Development").AddEnvironmentVariables().Build();
var connection = new SqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection"));
connection.InitialCatalog = "HanaMedia_CommissionQA_" + Guid.NewGuid().ToString("N");
await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection.ConnectionString).Options);
var service = new PersonalBookingCommissionService(db); int passed = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
Employee Employee(string name) => new() { User = new User { Username = name, Email = name + "@example.test", Role = AppRoles.BookingStaff, PasswordHash = "test", SecurityStamp = Guid.NewGuid().ToString() }, FullName = name, Email = name + "@example.test", Phone = "0900000000", Address = "Test", Dob = new(2000,1,1), JoinedDate = new(2020,1,1), Department = "Booking", Position = "Staff", ContractType = "thu_viec", Status = "dang_lam_viec" };
Booking Row(Employee employee, decimal price, string contract, string status = "dang_cho", int version = 1, decimal percent = 10) => new() { PrimaryManager = employee, BookingPrice = price, ClientName = "Test", CampaignName = "QA", ContractStatus = contract, Status = status, FinanceVersion = version, CommissionPercent = percent, Deadline = new(2026,12,31) };
try
{
    await db.Database.EnsureCreatedAsync();
    Check(!(await service.GetAsync(999)).HasEmployeeProfile, "Unlinked account does not see others' commissions");
    var a = Employee("a"); var b = Employee("b"); db.AddRange(a,b); await db.SaveChangesAsync();
    Check((await service.GetAsync(a.User!.Id)).Total == 0, "New employee has zero commission");
    var draft = Row(a,1000000,"nhap"); var signed = Row(a,2000000,"da_ky",percent:15);
    var campaign = new Campaign { Name = "QA", Client = "Test", ManagerEmployee = a, StartDate = new(2026,1,1), EndDate = new(2026,12,31), Status = CampaignStatuses.Running, ConfirmedAt = DateTime.Now };
    signed.Campaign = campaign;
    signed.BookingWages.Add(new BookingWage { Employee = b, AllocatedWage = 50000 });
    db.AddRange(draft, signed, Row(a,9000000,"da_ky","huy"), Row(a,8000000,"da_ky",version:0), Row(b,7000000,"da_ky"));
    await db.SaveChangesAsync();
    var first = await service.GetAsync(a.User!.Id);
    Check(first.BookingCount == 2 && first.Total == 0 && first.PendingCustomerPayment == 350000, "Signed contract alone does not earn commission; own uncancelled bookings only");
    Check(first.LegacyBookingCount == 1, "Legacy finance explicitly excluded");
    Check((await service.GetAsync(b.User!.Id)).PendingCustomerPayment == 700000, "Different login gets different personal total");
    draft.ContractStatus = "da_ky"; await db.SaveChangesAsync();
    var afterSigning = await service.GetAsync(a.User.Id);
    Check(afterSigning.Total == 0 && afterSigning.CompanyUnpaid == 0, "Signing does not mark customer or company payment");
    campaign.Status = CampaignStatuses.Completed; await db.SaveChangesAsync();
    Check((await service.GetAsync(a.User.Id)).Total == 0, "Completion without payment does not earn commission");
    campaign.Status = CampaignStatuses.Accepted; campaign.AcceptedAt = DateTime.Now; await db.SaveChangesAsync();
    var earned = await service.GetAsync(a.User.Id);
    Check(earned.Total == 250000 && earned.CompanyUnpaid == 250000 && earned.CompanyPaid == 0 && earned.PendingCustomerPayment == 100000, "Customer receipt earns commission and shows company outstanding");
    signed.Payments.Add(new BookingPayment { Kind = "manager", PayeeId = a.Id, Amount = 250000, IsPaid = true, UpdatedByUserId = a.User.Id, UpdatedAt = DateTime.Now });
    signed.Payments.Add(new BookingPayment { Kind = "staff", PayeeId = b.Id, Amount = 50000, IsPaid = true, UpdatedByUserId = a.User.Id, UpdatedAt = DateTime.Now });
    await db.SaveChangesAsync();
    var paid = await service.GetAsync(a.User.Id);
    Check(paid.Total == 250000 && paid.CompanyPaid == 250000 && paid.CompanyUnpaid == 0, "Only own manager payment reduces company debt");
    signed.Payments.First(p => p.Kind == "manager").IsPaid = false; await db.SaveChangesAsync();
    Check((await service.GetAsync(a.User.Id)).CompanyUnpaid == 250000, "Reversing reconciliation restores outstanding commission");
    signed.Status = "huy"; await db.SaveChangesAsync();
    Check((await service.GetAsync(a.User.Id)).Total == 0, "Cancelled booking excluded even after customer receipt");
    Console.WriteLine($"Passed {passed} checks.");
}
finally { await db.Database.EnsureDeletedAsync(); }
