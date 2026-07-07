using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Web.Areas.App.Controllers;

/// <summary>مرخصی و مأموریت: درخواست پرسنل + تأیید/رد مدیر.</summary>
public class LeavesController : AppControllerBase
{
    public static string TypeLabel(LeaveType type) => type == LeaveType.Leave ? "مرخصی" : "مأموریت";

    public static string StatusLabel(LeaveStatus status) => status switch
    {
        LeaveStatus.Pending => "در انتظار",
        LeaveStatus.Approved => "تأیید شده",
        _ => "رد شده"
    };

    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;

    public LeavesController(CrmDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    [HttpGet("/App/leaves")]
    public async Task<IActionResult> Index()
    {
        var isAdmin = _tenant.IsTenantAdmin;
        var query = _db.LeaveRequests.AsNoTracking().AsQueryable();
        if (!isAdmin)
            query = query.Where(l => l.UserId == _tenant.UserId);

        var requests = await query.OrderByDescending(l => l.Id).Take(300).ToListAsync();

        var userIds = requests.Select(r => r.UserId).Distinct().ToList();
        ViewBag.Users = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);
        ViewBag.IsAdmin = isAdmin;

        ViewData["Title"] = "مرخصی و مأموریت";
        return View(requests);
    }

    [HttpPost("/App/leaves/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LeaveType type, DateTime fromUtc, DateTime toUtc, string? reason)
    {
        if (_tenant.UserId is not int userId || toUtc < fromUtc)
        {
            TempData["Error"] = "بازه زمانی معتبر نیست.";
            return RedirectToAction(nameof(Index));
        }

        _db.LeaveRequests.Add(new LeaveRequest
        {
            UserId = userId,
            Type = type,
            FromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc),
            ToUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc),
            Reason = reason?.Trim()
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = "درخواست ثبت شد.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/App/leaves/{id:int}/review")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(int id, bool approve, string? note)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var request = await _db.LeaveRequests.FirstOrDefaultAsync(l => l.Id == id);
        if (request is not null && request.Status == LeaveStatus.Pending)
        {
            request.Status = approve ? LeaveStatus.Approved : LeaveStatus.Rejected;
            request.ReviewedByUserId = _tenant.UserId;
            request.ReviewNote = note?.Trim();
            await _db.SaveChangesAsync();

            _db.Notifications.Add(new Notification
            {
                UserId = request.UserId,
                Title = approve ? "درخواست تأیید شد" : "درخواست رد شد",
                Body = $"{TypeLabel(request.Type)} شما {StatusLabel(request.Status)}." +
                       (string.IsNullOrEmpty(note) ? "" : $" توضیح: {note}"),
                LinkUrl = "/App/leaves"
            });
            await _db.SaveChangesAsync();

            TempData["Success"] = "بررسی ثبت شد.";
        }
        return RedirectToAction(nameof(Index));
    }
}
