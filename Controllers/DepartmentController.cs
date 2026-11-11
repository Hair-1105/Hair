using System;
using System.Linq;
using System.Threading.Tasks;
using HanaMedia.Constants;
using HanaMedia.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanaMedia.Controllers
{
    [Authorize(Roles = AppRoles.Director)]
    [Route("Director/Department")]
    public class DepartmentController : Controller
    {
        private readonly ApplicationDbContext _db;

        public DepartmentController(ApplicationDbContext db)
        {
            _db = db;
        }

        // GET: /Director/Department
        [HttpGet("")]
        [HttpGet("Index")]
        public IActionResult Index()
        {
            return View("~/Views/Director/Department.cshtml");
        }

        // GET: /Director/Department/ApiList
        // Read-only department overview with actual employee counts.
        [HttpGet("ApiList")]
        public async Task<IActionResult> ApiList(string? q, string? status)
        {
            var query = _db.Departments.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var kw = q.Trim().ToLower();
                query = query.Where(d =>
                    d.Code.ToLower().Contains(kw) ||
                    d.Name.ToLower().Contains(kw) ||
                    (d.Description != null && d.Description.ToLower().Contains(kw)));
            }

            if (!string.IsNullOrWhiteSpace(status) &&
                (status == "active" || status == "inactive"))
            {
                query = query.Where(d => d.Status == status);
            }

            var depts = await query.OrderBy(d => d.Name).ToListAsync();

            // Tính headcount + manager cho từng phòng
            var empByDept = await _db.Employees.AsNoTracking()
                .Where(e => e.Status == "dang_lam_viec" || e.Status == "thu_viec" || e.Status == "cho_duyet_nghi")
                .GroupBy(e => e.Department)
                .Select(g => new EmpCountByDept { DeptCode = g.Key, Count = g.Count() })
                .ToListAsync();

            // Only current managers are displayed; departed staff are not department heads.
            var managerByDept = await _db.Employees.AsNoTracking()
                .Where(e => e.IsManager && (e.Status == "dang_lam_viec" || e.Status == "thu_viec" || e.Status == "cho_duyet_nghi"))
                .OrderBy(e => e.Status == "dang_lam_viec" ? 0 : 1)
                .ThenBy(e => e.FullName)
                .Select(e => new ManagerRow { Department = e.Department, FullName = e.FullName, Position = e.Position, IsManager = e.IsManager, Status = e.Status })
                .ToListAsync();


            var list = depts.Select(d => BuildDeptRow(d, empByDept, managerByDept)).ToList();

            return Ok(new
            {
                success = true,
                data = new { list, summary = new { departments = depts.Count, activeDepartments = depts.Count(d => d.Status == "active"), employees = empByDept.Where(e => depts.Any(d => string.Equals(d.Code, e.DeptCode, StringComparison.OrdinalIgnoreCase))).Sum(e => e.Count) } },
                total = list.Count
            });
        }

        private static object BuildDeptRow(Department d,
            System.Collections.Generic.List<EmpCountByDept> empByDept,
            System.Collections.Generic.List<ManagerRow> managerByDept)
        {
            var count = empByDept.Where(x => string.Equals(x.DeptCode, d.Code, StringComparison.OrdinalIgnoreCase)).Sum(x => x.Count);

            // Match department codes without case differences, including legacy codes.
            var managersInDept = managerByDept
                .Where(x => string.Equals(x.Department, d.Code, StringComparison.OrdinalIgnoreCase))
                .ToList();

            string manager;
            if (managersInDept.Count > 0)
            {
                // Trang Giám đốc chỉ hiển thị họ tên, không kèm chức vụ.
                manager = managersInDept[0].FullName;
            }
            else
            {
                manager = "Chưa bổ nhiệm";
            }

            string statusLabel = d.Status == "active" ? "Đang hoạt động" : "Ngừng hoạt động";

            return new
            {
                id = d.Id,
                code = d.Code,
                name = d.Name,
                description = d.Description,
                status = d.Status,
                statusLabel,
                manager,
                headcount = count
            };
        }

        private class EmpCountByDept
        {
            public string DeptCode { get; set; } = "";
            public int Count { get; set; }
        }

        private class ManagerRow
        {
            public string Department { get; set; } = "";
            public string FullName { get; set; } = "";
            public string Position { get; set; } = "";
            public bool IsManager { get; set; }
            public string? Status { get; set; }
        }

        // GET: /Director/Department/ApiGet/{id}
        [HttpGet("ApiGet/{id:int}")]
        public async Task<IActionResult> ApiGet(int id)
        {
            var dept = await _db.Departments.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id);

            if (dept == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy phòng ban." });
            }

            var count = await _db.Employees.CountAsync(e => e.Department == dept.Code && (e.Status == "dang_lam_viec" || e.Status == "thu_viec" || e.Status == "cho_duyet_nghi"));

            // Trưởng phòng / quản lý theo cờ IsManager = true. Ưu tiên người đang làm việc.
            var managerRow = await _db.Employees
                .Where(e => e.Department == dept.Code && e.IsManager && (e.Status == "dang_lam_viec" || e.Status == "thu_viec" || e.Status == "cho_duyet_nghi"))
                .OrderBy(e => e.Status == "dang_lam_viec" ? 0 : 1)
                .ThenBy(e => e.FullName)
                .Select(e => new { e.FullName, e.Position })
                .FirstOrDefaultAsync();

            string manager;
            if (managerRow == null)
                manager = "Chưa bổ nhiệm";
            else
                manager = managerRow.FullName;

            return Ok(new
            {
                success = true,
                data = new
                {
                    id = dept.Id,
                    code = dept.Code,
                    name = dept.Name,
                    description = dept.Description,
                    status = dept.Status,
                    manager,
                    headcount = count
                }
            });
        }

    }
}
