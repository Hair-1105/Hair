using System.Security.Claims;
using HanaMedia.Constants;
using HanaMedia.Controllers;
using HanaMedia.Migrations;
using HanaMedia.Models;
using HanaMedia.Models.Dto;
using HanaMedia.Services;
using HanaMedia.Services.Accounts;
using HanaMedia.Services.Auditing;
using HanaMedia.Services.Security;
using HanaMedia.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

var config = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json",true).AddUserSecrets("HanaMedia-V2-Local-Development").AddEnvironmentVariables().Build();
var connection = new SqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection"));
if(args.Contains("--inspect"))
{
    await using var sql = new SqlConnection(connection.ConnectionString); await sql.OpenAsync();
    foreach(var query in new[]{"SELECT name, definition, is_disabled, is_not_trusted FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('employees') AND name='chk_emp_dept'", "SELECT code,name,status FROM departments ORDER BY code"})
    {
        await using var cmd = new SqlCommand(query,sql); await using var reader = await cmd.ExecuteReaderAsync();
        while(await reader.ReadAsync()) Console.WriteLine(string.Join(" | ",Enumerable.Range(0,reader.FieldCount).Select(i=>reader.GetValue(i).ToString())));
    }
    return;
}
if(args.Contains("--export-repair"))
{
    File.WriteAllText("database_patches/20261111_repair_legal_accounting_departments.sql",
        "-- Standalone department repair; safe to rerun before/after EF migrations.\nSET XACT_ABORT ON;\nBEGIN TRANSACTION;\n" + RepairLegalAccountingDepartments.RepairSql + "\nCOMMIT;\n");
    return;
}
if(args.Contains("--apply-repair"))
{
    // Explicit maintenance mode. No fixture insertion and no other pending migration.
    await using var live = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection.ConnectionString).Options);
    await using var transaction = await live.Database.BeginTransactionAsync();
    await live.Database.ExecuteSqlRawAsync(RepairLegalAccountingDepartments.RepairSql);
    await transaction.CommitAsync();
    Console.WriteLine("Department repair applied; employee/account data preserved.");
    return;
}
if(args.Length > 0) throw new ArgumentException("Unknown option.");
connection.InitialCatalog = "HanaMedia_DepartmentRolesQA_" + Guid.NewGuid().ToString("N");
await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection.ConnectionString).Options);
var created = false; int passed=0;
void Check(bool ok,string name) { if(!ok)throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); passed++; }
var environment = new Env();
var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Role,AppRoles.HumanResourcesManager)},"test")) };
var controller = new ManageHumanEmployeeApiController(db,NullLogger<ManageHumanEmployeeApiController>.Instance,environment,new EmployeeAvatarService(environment,new ImageProcessingService())) { ControllerContext = new ControllerContext { HttpContext = http } };
EmployeeCreateUpdateDto Input(string department,string suffix) => new() { FullName="QA "+suffix,Dob=new(1990,1,1),JoinedDate=new(2020,1,1),Phone="0900000000",Email=suffix+"@example.test",Address="Test",Department=department,Position="Staff",ContractType="thu_viec",Status="dang_lam_viec" };
try
{
    await db.Database.EnsureCreatedAsync(); created=true;
    // Reproduce the actual old database, including uppercase existing legal code.
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE employees DROP CONSTRAINT chk_emp_dept; ALTER TABLE employees ADD CONSTRAINT chk_emp_dept CHECK ([department] IN ('HCNS','Booking','Y_tuong','IT'));");
    db.Departments.AddRange(new Department{Code="HCNS",Name="HCNS"},new Department{Code="PHAP_LY",Name="Pháp chế"}); await db.SaveChangesAsync();
    var legalBefore=Input("PHAP_LY","legal-before");
    var failure=await controller.ApiCreate(legalBefore);
    Check(failure is ObjectResult { StatusCode:400 },"Reproduces legal department rejection against legacy constraint");
    db.ChangeTracker.Clear();
    await db.Database.ExecuteSqlRawAsync(RepairLegalAccountingDepartments.RepairSql);
    await db.Database.ExecuteSqlRawAsync(RepairLegalAccountingDepartments.RepairSql);
    Check(await db.Departments.CountAsync(d=>d.Code.ToUpper()=="PHAP_LY")==1,"Repair preserves existing uppercase legal department without duplicates");
    Check(await db.Departments.CountAsync(d=>d.Code.ToUpper()=="KE_TOAN" && d.Status=="active")==1,"Repair seeds active accounting department, idempotently");
    var admin=new User{Username="qa_admin",Email="admin@example.test",Role=AppRoles.AdminIT,PasswordHash="test",Status="active",SecurityStamp=Guid.NewGuid().ToString()};db.Users.Add(admin);await db.SaveChangesAsync();
    var passwords=new AccountPasswordService(new PasswordHasher<User>());
    var accounts=new AccountManagementService(db,passwords,new SystemAuditService(db,new HttpContextAccessor {HttpContext=http}));
    foreach(var (dept,role,suffix) in new[]{("PHAP_LY",AppRoles.LegalStaff,"legal"),("Ke_Toan",AppRoles.Accountant,"accountant")})
    {
        var input=Input(dept,suffix);
        Check(await controller.ApiCreate(input) is OkObjectResult,"Create employee in "+dept);
        var employee=await db.Employees.SingleAsync(e=>e.Email==input.Email);
        input.Position="Updated";
        Check(await controller.ApiUpdate(employee.Id,input) is OkObjectResult,"Update employee in "+dept);
        var result=await accounts.CreateAccountAsync(new CreateAccountInputModel{EmployeeId=employee.Id,Role=role,Username="qa_"+suffix,InitialPassword="QA-test!493802"},admin.Id);
        Check(result.Succeeded,"Assign account role "+role);
        Check((await db.Users.SingleAsync(u=>u.Username=="qa_"+suffix)).Role==role,"Persist correct role "+role);
    }
    var legalId=await db.Employees.Where(e=>e.Email=="legal@example.test").Select(e=>e.Id).SingleAsync();
    var moved=Input("Ke_Toan","legal");
    Check(await controller.ApiUpdate(legalId,moved) is OkObjectResult,"Transfer employee to accounting department");
    Check(await controller.ApiCreate(Input("DOES_NOT_EXIST","invalid")) is BadRequestObjectResult,"Unknown department remains rejected");
    var deptAccounting=await db.Departments.SingleAsync(d=>d.Code=="Ke_Toan");deptAccounting.Status="inactive";await db.SaveChangesAsync();
    await db.Database.ExecuteSqlRawAsync(RepairLegalAccountingDepartments.RepairSql);db.ChangeTracker.Clear();
    Check(await db.Departments.AnyAsync(d=>d.Code=="Ke_Toan" && d.Status=="inactive"),"Rerunning repair preserves intentionally inactive department");
    Check(await controller.ApiCreate(Input("Ke_Toan","inactive")) is BadRequestObjectResult,"Inactive department remains rejected");
    Check(await controller.ApiCreate(Input("HCNS","original")) is OkObjectResult,"Existing department still accepts employees");
    await using(var tx=await db.Database.BeginTransactionAsync())
    {
        var invalid=db.Employees.Single(e=>e.Email=="original@example.test");invalid.Department="INVALID";
        try { await db.SaveChangesAsync(); Check(false,"Database rejects invalid department"); }
        catch(DbUpdateException) { Check(true,"Database still enforces department constraint"); }
        await tx.RollbackAsync();db.ChangeTracker.Clear();
    }
    Console.WriteLine($"Passed {passed} checks.");
}
finally
{
    if(created && connection.InitialCatalog.StartsWith("HanaMedia_DepartmentRolesQA_"))await db.Database.EnsureDeletedAsync();
}
sealed class Env:IWebHostEnvironment
{
    public string ApplicationName{get;set;}="QA";public string EnvironmentName{get;set;}="Testing";
    public string WebRootPath{get;set;}=Path.GetTempPath();public string ContentRootPath{get;set;}=Path.GetTempPath();
    public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
}
