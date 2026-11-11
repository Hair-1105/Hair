using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HanaMedia.Constants;
using HanaMedia.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;

var root = Directory.GetCurrentDirectory();
var config = new ConfigurationBuilder().SetBasePath(root).AddJsonFile("appsettings.json")
    .AddJsonFile("appsettings.Development.json", true).AddUserSecrets("HanaMedia-V2-Local-Development").AddEnvironmentVariables().Build();
var connection = new SqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection")) {
    InitialCatalog = "HanaMedia_AccountingLegalQA_" + Guid.NewGuid().ToString("N"), ConnectTimeout = 10
};
ApplicationDbContext Db() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection.ConnectionString).Options);
int passed = 0;
void Check(bool value, string message) { if (!value) throw new Exception("FAIL " + message); Console.WriteLine("PASS " + message); passed++; }
bool Denied(HttpResponseMessage r) => r.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound ||
    (r.StatusCode == HttpStatusCode.Redirect && r.Headers.Location!.OriginalString.Contains("AccessDenied"));
var password = "Test!" + Guid.NewGuid().ToString("N");
Employee Person(string name, string role) => new() {
    User = new User { Username = name, Email = name + "@example.test", Role = role, Status = "active",
        PasswordHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))).ToLowerInvariant(), SecurityStamp = Guid.NewGuid().ToString("N") },
    FullName = name, Email = name + "@example.test", Phone = "0900000000", Address = "Test", Dob = new(2000,1,1), JoinedDate = new(2020,1,1),
    Department = "Booking", Position = "Staff", ContractType = "thu_viec", Status = "dang_lam_viec"
};
var people = new Dictionary<string, Employee> { ["booking"] = Person("booking", AppRoles.BookingStaff), ["other"] = Person("other", AppRoles.BookingStaff),
    ["legal"] = Person("legal", AppRoles.LegalStaff), ["accounting"] = Person("accounting", AppRoles.Accountant), ["director"] = Person("director", AppRoles.Director) };
var bookings = new Dictionary<string, Booking>();
Process? app = null;
var filePaths = new HashSet<string>();
try
{
    Check(new Booking { CompletedAt = new DateTime(2026,1,31,14,30,0) }.CollectionDueDate == new DateTime(2026,2,28), "Month-end due date uses one calendar month");
    Check(new Booking { CompletedAt = new DateTime(2024,1,31) }.CollectionDueDate == new DateTime(2024,2,29), "Leap-year due date");
    await using (var db = Db())
    {
        await db.Database.EnsureCreatedAsync();
        db.AddRange(people.Values); await db.SaveChangesAsync();
        foreach (var key in new[] { "running", "overdue", "today", "soon", "future", "unknown", "paid", "cancelled" })
        {
            // Pick a completion date whose AddMonths(1) matches the intended due date.
            var due = key switch { "overdue" => DateTime.Today.AddDays(-2), "soon" => DateTime.Today.AddDays(3), "future" => DateTime.Today.AddDays(15), _ => DateTime.Today };
            bookings[key] = new Booking { ClientName = key, CampaignName = "QA-" + key, BookingPrice = 10000000,
                PrimaryManagerId = people["booking"].Id, Deadline = DateOnly.FromDateTime(DateTime.Today), FinanceVersion = 1,
                ContractStatus = "da_duyet", ContractApprovedAt = DateTime.Today.AddDays(-40), Status = key == "running" ? "dang_trien_khai" : key == "cancelled" ? "huy" : "hoan_thanh",
                CompletedAt = key is "running" or "unknown" ? null : due.AddMonths(-1), CustomerPaidAt = key == "paid" ? DateTime.Today : null,
                CustomerPaymentDate = key == "paid" ? DateTime.Today : null, CompanyPercent = 50, CommissionPercent = 10, CastPercent = 40 };
        }
        db.AddRange(bookings.Values); await db.SaveChangesAsync();
        // Exercise the new migration against an existing schema and historical data in this disposable DB.
        var assembly = db.GetService<IMigrationsAssembly>();
        var entry = assembly.Migrations.Single(x => x.Key.EndsWith("_AccountingReceivablesAndLegalDocuments"));
        var migration = assembly.CreateMigration(entry.Value, db.Database.ProviderName!);
        var generator = db.GetService<IMigrationsSqlGenerator>();
        foreach (var sql in generator.Generate(migration.DownOperations)) await db.Database.ExecuteSqlRawAsync(sql.CommandText);
        foreach (var sql in generator.Generate(migration.UpOperations)) await db.Database.ExecuteSqlRawAsync(sql.CommandText);
        db.ChangeTracker.Clear();
        Check(await db.Bookings.SumAsync(b => b.BookingPrice) == 80000000, "Migration preserves historical booking values");
        Check(await db.Bookings.AllAsync(b => b.CompletedAt == null), "Migration leaves unknown historical completion dates unset");
        foreach (var b in bookings.Values) await db.Bookings.Where(x => x.Id == b.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CompletedAt, b.CompletedAt));
    }
    var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
    start.ArgumentList.Add(Environment.GetEnvironmentVariable("ACCOUNTING_QA_APP") ?? Path.Combine(root,"bin","Debug","net8.0","HanaMedia.dll"));
    start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development"; start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:15039";
    start.Environment["ConnectionStrings__DefaultConnection"] = connection.ConnectionString;
    start.Environment["Security__IpRestriction__AllowLoopback"] = "true";
    app = Process.Start(start)!;
    var logs = new StringBuilder();
    app.OutputDataReceived += (_,e) => { if(e.Data != null) lock(logs) logs.AppendLine(e.Data); };
    app.ErrorDataReceived += (_,e) => { if(e.Data != null) lock(logs) logs.AppendLine(e.Data); };
    app.BeginOutputReadLine(); app.BeginErrorReadLine();
    HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() }) { BaseAddress = new Uri("http://127.0.0.1:15039") };
    using var anonymous = Client();
    for (int i=0;i<80;i++) { try { if((await anonymous.GetAsync("/Login")).IsSuccessStatusCode) break; } catch(HttpRequestException) {} await Task.Delay(250); }
    string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    async Task<HttpResponseMessage> Post(HttpClient client, string path, Dictionary<string,string>? data = null, string? fileField = null, string fileName = "test.pdf")
    {
        data ??= new(); data["__RequestVerificationToken"] = Token(await client.GetStringAsync("/Profile"));
        if (fileField == null) return await client.PostAsync(path,new FormUrlEncodedContent(data));
        using var form = new MultipartFormDataContent();
        foreach(var pair in data) form.Add(new StringContent(pair.Value),pair.Key);
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-1.4\nQA")), fileField, fileName);
        return await client.PostAsync(path,form);
    }
    var clients = new Dictionary<string,HttpClient>();
    foreach(var key in people.Keys)
    {
        var client = Client(); clients[key] = client;
        var token = Token(await client.GetStringAsync("/Login"));
        var result = await client.PostAsync("/Login",new FormUrlEncodedContent(new Dictionary<string,string> { ["username"] = key, ["password"] = password, ["__RequestVerificationToken"] = token }));
        Check(result.StatusCode == HttpStatusCode.Redirect, "Login " + key);
        if(key == "accounting") Check(result.Headers.Location!.OriginalString == "/Accounting/Receivables", "Accountant lands on receivables");
    }
    var accountant = clients["accounting"]; var legal = clients["legal"]; var owner = clients["booking"]; var other = clients["other"];
    // Director's department screen is read-only, including direct legacy API calls.
    int departmentId;
    await using (var db = Db())
    {
        var department = new Department { Code = "Booking", Name = "QA Department", Status = "active" };
        db.Departments.Add(department); await db.SaveChangesAsync(); departmentId = department.Id;
    }
    var director = clients["director"];
    var departmentHtml = await director.GetStringAsync("/Director/Department");
    Check(!departmentHtml.Contains("deptForm") && !departmentHtml.Contains("openAddModal") &&
        !departmentHtml.Contains("openEditModal") && !departmentHtml.Contains("deleteDept") && !departmentHtml.Contains("API_CREATE"),
        "Director department page renders without mutation controls or scripts");
    Check((await director.GetStringAsync("/Director/Department/ApiList")).Contains("QA Department"), "Director can read department list");
    Check((await director.GetStringAsync($"/Director/Department/ApiGet/{departmentId}")).Contains("QA Department"), "Director can read department details");
    foreach (var (method, path) in new[] {
        (HttpMethod.Post, "/Director/Department/ApiCreate"),
        (HttpMethod.Post, $"/Director/Department/ApiUpdate/{departmentId}"),
        (HttpMethod.Put, $"/Director/Department/ApiUpdate/{departmentId}"),
        (HttpMethod.Post, $"/Director/Department/ApiDelete/{departmentId}") })
    {
        using var request = new HttpRequestMessage(method, path) {
            Content = new StringContent("{\"code\":\"QA_CHANGED\",\"name\":\"Changed\",\"status\":\"inactive\"}", Encoding.UTF8, "application/json") };
        var response = await director.SendAsync(request);
        Check(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"Department mutation route removed: {method} {path}");
    }
    await using (var db = Db())
        Check(await db.Departments.CountAsync() == 1 && await db.Departments.AnyAsync(d => d.Id == departmentId && d.Code == "Booking" && d.Name == "QA Department" && d.Status == "active"),
            "Rejected department writes preserve existing data");
    Check(Denied(await accountant.GetAsync("/Director/Department/ApiList")), "Department access remains restricted to Director");
    if (args.Contains("--departments-ui"))
    {
        await using (var db = Db())
        {
            var bookingDepartment = await db.Departments.SingleAsync(); bookingDepartment.Name = "Booking & Đối tác";
            var bookingManager = await db.Employees.FindAsync(people["booking"].Id);
            bookingManager!.FullName = "Nguyễn Minh Anh"; bookingManager.IsManager = true; bookingManager.Department = "booking";
            var departed = await db.Employees.FindAsync(people["other"].Id); departed!.Status = "ngung_hoat_dong";
            foreach (var (code, name, active) in new[] { ("HCNS", "Hành chính nhân sự", true), ("Y_tuong", "Ý tưởng & Sáng tạo", true), ("PHAP_LY", "Pháp chế", true), ("Ke_Toan", "Kế toán", true), ("IT", "Công nghệ thông tin", false) })
            {
                db.Departments.Add(new Department { Code = code, Name = name, Status = active ? "active" : "inactive" });
                if (!active) continue;
                for (var i = 0; i < 2; i++)
                {
                    var employee = Person("ui_" + code + i, AppRoles.HumanResourcesStaff);
                    employee.Department = code; employee.FullName = i == 0 ? "Trần Hoàng Phương" : "Lê Thanh Hà";
                    employee.IsManager = i == 0 && code != "PHAP_LY";
                    db.Employees.Add(employee);
                }
            }
            await db.SaveChangesAsync();
        }
        var overview = System.Text.Json.JsonDocument.Parse(await director.GetStringAsync("/Director/Department/ApiList")).RootElement.GetProperty("data");
        Check(overview.GetProperty("summary").GetProperty("employees").GetInt32() == 12, "Department summary excludes departed staff");
        Check(overview.GetProperty("list").EnumerateArray().Single(d => d.GetProperty("code").GetString() == "Booking").GetProperty("headcount").GetInt32() == 4, "Department counts match codes without case differences");
        Check(!overview.GetProperty("list")[0].TryGetProperty("performance", out _), "Department API no longer returns fabricated performance");
        var ui = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true };
        ui.ArgumentList.Add("tests/departments-ui.cjs"); ui.Environment["DEPARTMENT_QA_PASSWORD"] = password;
        using var browserTest = Process.Start(ui)!; await browserTest.WaitForExitAsync();
        Check(browserTest.ExitCode == 0, "Department browser layout and filtering checks pass");
    }
    if (args.Contains("--departments-only") || args.Contains("--departments-ui"))
    {
        foreach (var client in clients.Values) client.Dispose();
        Console.WriteLine($"Passed {passed} checks.");
        return;
    }
    var runningId = bookings["running"].Id;
    var beforeHandoff = await owner.GetStringAsync($"/Bookings/Details/{runningId}");
    Check(!beforeHandoff.Contains("name=\"contractFile\"") && !beforeHandoff.Contains("/Bookings/UploadContract/"), "Booking owner has no contract upload form before completion");
    Check(Denied(await Post(owner,$"/Bookings/UploadContract/{runningId}",new(),"contractFile")), "Booking owner direct contract upload is forbidden");
    await using (var db = Db())
        Check(await db.Bookings.AnyAsync(b => b.Id == runningId && b.ContractFileUrl == null && b.ContractRevision == 0), "Blocked upload does not create or replace a contract");
    await Post(owner,$"/Bookings/Acceptance/{runningId}",new(),"acceptanceFile");
    await using(var db=Db())
    {
        var completed = await db.Bookings.FindAsync(runningId);
        Check(completed!.Status == "hoan_thanh" && completed.CompletedAt?.Date == DateTime.Today && completed.CollectionDueDate == DateTime.Today.AddMonths(1), "Booking completion starts one-month collection clock");
        filePaths.Add(Path.Combine(root,"App_Data","booking-documents",Path.GetFileName(completed.AcceptanceFileUrl!)));
    }
    var all = WebUtility.HtmlDecode(await accountant.GetStringAsync("/Accounting/Receivables"));
    Check(all.Contains("QA-running") && all.Contains("QA-unknown") && !all.Contains("QA-paid") && !all.Contains("QA-cancelled"), "Receivables show completed unpaid bookings only, including missing dates");
    foreach(var (filter,key) in new[] { ("overdue","overdue"), ("today","today"), ("soon","soon"), ("unknown","unknown") })
    {
        var html = await accountant.GetStringAsync("/Accounting/Receivables?status=" + filter);
        Check(html.Contains("QA-" + key) && !html.Contains("QA-future"), "Receivables filter " + filter);
    }
    var list = await accountant.GetStringAsync("/Bookings");
    Check(!list.Contains("id=\"bookingForm\"") && !list.Contains("onclick=\"openCreateModal()\""), "Accountant booking list contains no editing controls");
    foreach(var b in bookings.Values) Check((await accountant.GetAsync($"/Bookings/Details/{b.Id}")).IsSuccessStatusCode, "Accountant sees booking details " + b.ClientName);
    var detail = await accountant.GetStringAsync($"/Bookings/Details/{runningId}");
    Check(!Regex.IsMatch(detail,"<form[^>]+action=\"/Bookings/",RegexOptions.IgnoreCase), "No booking mutation forms on accountant details");
    foreach(var action in new[] { "Edit", "Delete", "SubmitApproval", "Approve", "Reject", "UploadContract", "SignContract", "Acceptance", "Start", "ConfirmCustomerPayment", "UpdateWages" })
        Check(Denied(await Post(accountant,$"/Bookings/{action}/{runningId}")), "Accountant blocked from " + action);
    Check(Denied(await Post(accountant,"/Bookings/Create")), "Accountant cannot create bookings");
    Check(Denied(await Post(accountant,$"/Accounting/Payment/{runningId}")), "Accountant cannot change reconciliation");
    Check(Denied(await Post(accountant,$"/Accounting/ConfirmDisbursement/{runningId}")), "Accountant cannot confirm disbursement");
    Check(Denied(await accountant.GetAsync("/Legal")), "Accountant cannot operate legal workspace");
    Check((await legal.GetAsync("/Legal")).IsSuccessStatusCode && (await legal.GetAsync("/LegalDocuments/Edit")).IsSuccessStatusCode, "Legal monitoring and editor render");
    async Task<LegalDocument> Create(string kind, string title, bool attachment=false)
    {
        var input = new Dictionary<string,string> { ["Kind"]=kind,["Title"]=title,["Content"]="QA content <script>alert(1)</script>", ["BookingId"]=runningId.ToString(), ["EmployeeId"]=people["booking"].Id.ToString() };
        var result=await Post(legal,"/LegalDocuments/Save",input,attachment?"Attachment":null);
        Check(result.StatusCode==HttpStatusCode.Redirect,"Save legal " + kind);
        await using var db=Db(); var doc=await db.LegalDocuments.SingleAsync(d=>d.Title==title);
        if(doc.AttachmentName!=null)filePaths.Add(Path.Combine(root,"App_Data","legal-documents",doc.AttachmentName));
        return doc;
    }
    var contract = await Create("booking","QA contract",true);
    var labor = await Create("employment","QA employment",true);
    var policy = await Create("policy","QA policy");
    Check(labor.BookingId==null && policy.BookingId==null && policy.EmployeeId==null,"Legal documents link only to relevant entities");
    Check((await legal.GetStringAsync($"/LegalDocuments/Details/{contract.Id}")).Contains("&lt;script&gt;"),"Draft content is HTML-escaped");
    Check(Denied(await owner.GetAsync($"/LegalDocuments/Details/{contract.Id}")),"Unsent contract is private");
    Check(Denied(await owner.GetAsync($"/LegalDocuments/Attachment/{contract.Id}")),"Unsent attachment is private");
    Check(!(await owner.GetStringAsync($"/Bookings/Details/{runningId}")).Contains($"/LegalDocuments/Details/{contract.Id}"), "Draft is absent from Booking contract panel");
    Check((await Post(legal,$"/LegalDocuments/Send/{contract.Id}",new(){["revision"]="0"})).StatusCode==HttpStatusCode.Conflict,"Stale send rejected");
    Check((await Post(legal,$"/LegalDocuments/Send/{contract.Id}",new(){["revision"]=contract.Revision.ToString()})).StatusCode==HttpStatusCode.Redirect,"Legal sends contract to Booking");
    Check((await owner.GetAsync($"/LegalDocuments/Details/{contract.Id}")).IsSuccessStatusCode && (await owner.GetAsync($"/LegalDocuments/Attachment/{contract.Id}")).IsSuccessStatusCode,"Owner reads sent contract and attachment");
    var ownerDetails = await owner.GetStringAsync($"/Bookings/Details/{runningId}");
    Check(ownerDetails.Contains($"/LegalDocuments/Details/{contract.Id}") && ownerDetails.Contains($"/LegalDocuments/Attachment/{contract.Id}"), "Legal handoff appears directly in Booking details with download link");
    var contractCheck = System.Text.Json.JsonDocument.Parse(await owner.GetStringAsync($"/Bookings/{runningId}/DocumentCheck?kind=contract")).RootElement;
    Check(contractCheck.GetProperty("available").GetBoolean() && contractCheck.GetProperty("url").GetString() == $"/LegalDocuments/Attachment/{contract.Id}", "Booking document check resolves the Legal attachment");
    Check(!(await other.GetStringAsync($"/Bookings/Details/{runningId}")).Contains($"/LegalDocuments/Details/{contract.Id}"), "Unrelated user does not see Legal contract via Booking panel");
    Check(!System.Text.Json.JsonDocument.Parse(await other.GetStringAsync($"/Bookings/{runningId}/DocumentCheck?kind=contract")).RootElement.GetProperty("available").GetBoolean(), "Document check does not disclose Legal contract to unrelated user");
    Check(WebUtility.HtmlDecode(await director.GetStringAsync("/Director/BookingCampaign")).Contains("Pháp chế đã chuyển"), "Director contract status uses Legal handoff");
    Check(Denied(await other.GetAsync($"/LegalDocuments/Details/{contract.Id}")),"Unrelated Booking user cannot read sent contract");
    Check((await accountant.GetAsync($"/LegalDocuments/Details/{contract.Id}")).IsSuccessStatusCode,"Accountant reads sent booking contract");
    foreach(var doc in new[]{labor,policy}) Check(Denied(await accountant.GetAsync($"/LegalDocuments/Details/{doc.Id}")) && Denied(await owner.GetAsync($"/LegalDocuments/Details/{doc.Id}")),"Employment/policy documents remain in Legal workspace");
    Check(Denied(await accountant.GetAsync($"/LegalDocuments/Attachment/{labor.Id}")),"Employment attachment is private");
    Check(Denied(await Post(accountant,"/LegalDocuments/Save")),"Accountant cannot save legal documents");
    Check((await Post(legal,$"/LegalDocuments/Send/{contract.Id}",new(){["revision"]=contract.Revision.ToString()})).StatusCode==HttpStatusCode.Conflict,"Duplicate send rejected");
    Check((await Post(legal,"/LegalDocuments/Save",new(){["Id"]=contract.Id.ToString(),["Kind"]="booking",["Title"]="Overwrite",["Content"]="Overwrite",["Revision"]="2",["BookingId"]=runningId.ToString()})).StatusCode==HttpStatusCode.Conflict,"Sent contract cannot be overwritten");
    Check((await Post(legal,$"/LegalDocuments/Send/{labor.Id}",new(){["revision"]=labor.Revision.ToString()})).StatusCode==HttpStatusCode.BadRequest,"Employment document cannot be sent to Booking");
    var invalid=await Post(legal,"/LegalDocuments/Save",new(){["Kind"]="policy",["Title"]="Bad file"},"Attachment","bad.html");
    Check(invalid.IsSuccessStatusCode && (await invalid.Content.ReadAsStringAsync()).Contains("validation-summary-errors"),"Invalid attachment returns validation feedback");
    await using(var db=Db())Check(!await db.LegalDocuments.AnyAsync(d=>d.Title=="Bad file"),"Invalid attachment does not create document");
    var noCsrf=await legal.PostAsync($"/LegalDocuments/Send/{contract.Id}",new FormUrlEncodedContent(new Dictionary<string,string>()));
    Check(noCsrf.StatusCode==HttpStatusCode.BadRequest,"Legal send requires anti-forgery token");
    await using(var db=Db())
    {
        var completed=await db.Bookings.FindAsync(runningId);
        Check((await accountant.GetAsync(completed!.AcceptanceFileUrl)).IsSuccessStatusCode,"Accountant downloads acceptance before payment");
        Check((await accountant.GetAsync($"/Bookings/{runningId}/DocumentCheck?kind=acceptance")).IsSuccessStatusCode,"Accountant checks booking documents");
        Check(await db.SystemAuditLogs.AnyAsync(a=>a.ActionType=="legal_document_sent"),"Legal handoff is audited");
    }
    Check(!(await legal.GetStringAsync("/Legal?status=missing")).Contains("QA-running"), "Sent legal contract satisfies monitor even without uploaded signed file");
    var supplement = await Create("booking", "QA content-only supplement");
    await Post(legal,$"/LegalDocuments/Send/{supplement.Id}",new(){["revision"]=supplement.Revision.ToString()});
    var latest = System.Text.Json.JsonDocument.Parse(await owner.GetStringAsync($"/Bookings/{runningId}/DocumentCheck?kind=contract")).RootElement;
    Check(latest.GetProperty("url").GetString() == $"/LegalDocuments/Details/{supplement.Id}", "Latest content-only contract opens its content instead of an older file");
    var history = await owner.GetStringAsync($"/Bookings/Details/{runningId}");
    Check(history.Contains($"/LegalDocuments/Details/{contract.Id}") && history.Contains($"/LegalDocuments/Details/{supplement.Id}"), "Booking preserves all sent Legal versions");
    Check((await legal.GetStringAsync($"/LegalDocuments?kind=booking&bookingId={runningId}")).Contains($"name=\"bookingId\" value=\"{runningId}\""), "Legal filter preserves selected Booking");
    if (args.Contains("--ui"))
    {
        var ui = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true };
        ui.ArgumentList.Add(Environment.GetEnvironmentVariable("ACCOUNTING_QA_UI_SCRIPT") ?? "tests/operations-ui.cjs"); ui.Environment["OPERATIONS_QA_PASSWORD"] = password;
        ui.Environment["OPERATIONS_BOOKING_ID"] = runningId.ToString(); ui.Environment["OPERATIONS_EMPLOYEE_ID"] = people["booking"].Id.ToString();
        using var browserTest = Process.Start(ui)!; await browserTest.WaitForExitAsync();
        Check(browserTest.ExitCode == 0, "Accounting/Legal browser workflows and responsive layouts pass");
    }
    var payment=await Post(owner,$"/Bookings/ConfirmCustomerPayment/{runningId}",new(){["receivedPayment"]="true",["customerPaymentDate"]=DateTime.Today.ToString("yyyy-MM-dd")});
    Check(payment.StatusCode==HttpStatusCode.Redirect && !(await accountant.GetStringAsync("/Accounting/Receivables")).Contains("QA-running"),"Receipt confirmation removes booking from receivables");
    foreach(var client in clients.Values)client.Dispose();
    Console.WriteLine($"Passed {passed} checks.");
}
finally
{
    if(app is { HasExited:false }) { app.Kill(true); await app.WaitForExitAsync(); }
    await using var db=Db();
    if(connection.InitialCatalog.StartsWith("HanaMedia_AccountingLegalQA_"))await db.Database.EnsureDeletedAsync();
    foreach(var file in filePaths)if(File.Exists(file))File.Delete(file);
}
