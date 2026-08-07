using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Web.Areas.App.Controllers;

/// <summary>لیست و تصمیم‌گیری درخواست‌های تأیید — حداقل قابل‌استفاده.</summary>
public class ApprovalsController : AppControllerBase
{
    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;

    public ApprovalsController(CrmDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    [HttpGet("/App/approvals")]
    public async Task<IActionResult> Index()
    {
        var query = _db.ApprovalRequests.AsNoTracking()
            .Where(a => a.Status == ApprovalStatus.Pending);

        if (!_tenant.IsTenantAdmin)
        {
            if (_tenant.RoleId is not int roleId)
            {
                ViewData["Title"] = "تأییدیه‌ها";
                return View(Array.Empty<ApprovalListItem>());
            }

            var ruleIds = await _db.ApprovalRules.AsNoTracking()
                .Where(r => r.ApproverRoleId == roleId)
                .Select(r => r.Id)
                .ToListAsync();
            query = query.Where(a => ruleIds.Contains(a.RuleId));
        }

        var requests = await query
            .OrderByDescending(a => a.Id)
            .Take(100)
            .ToListAsync();

        var ruleMap = await _db.ApprovalRules.AsNoTracking()
            .Where(r => requests.Select(x => x.RuleId).Contains(r.Id))
            .ToDictionaryAsync(r => r.Id);

        var recordIds = requests.Select(r => r.RecordId).Distinct().ToList();
        var titles = await _db.Records.AsNoTracking()
            .Where(r => recordIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Title);

        var items = requests.Select(a => new ApprovalListItem
        {
            Request = a,
            RuleName = ruleMap.TryGetValue(a.RuleId, out var rule) ? rule.Name : $"#{a.RuleId}",
            RecordTitle = titles.TryGetValue(a.RecordId, out var t) ? t : $"#{a.RecordId}"
        }).ToList();

        ViewData["Title"] = "تأییدیه‌ها";
        return View(items);
    }

    [HttpPost("/App/approvals/{id:int}/approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, string? note)
    {
        var result = await DecideAsync(id, ApprovalStatus.Approved, note);
        if (result is not null)
            return result;
        TempData["Success"] = "درخواست تأیید شد.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/App/approvals/{id:int}/reject")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string? note)
    {
        var result = await DecideAsync(id, ApprovalStatus.Rejected, note);
        if (result is not null)
            return result;
        TempData["Success"] = "درخواست رد شد.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult?> DecideAsync(int id, ApprovalStatus status, string? note)
    {
        var request = await _db.ApprovalRequests.FirstOrDefaultAsync(a => a.Id == id);
        if (request is null)
            return NotFound();

        if (request.Status != ApprovalStatus.Pending)
        {
            TempData["Error"] = "این درخواست قبلاً تصمیم‌گیری شده است.";
            return RedirectToAction(nameof(Index));
        }

        var rule = await _db.ApprovalRules.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.RuleId);
        if (rule is null)
        {
            TempData["Error"] = "قانون تأیید یافت نشد.";
            return RedirectToAction(nameof(Index));
        }

        if (!_tenant.IsTenantAdmin && _tenant.RoleId != rule.ApproverRoleId)
            return Forbid("Identity.Application");

        request.Status = status;
        request.DecidedByUserId = _tenant.UserId;
        request.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        await _db.SaveChangesAsync();
        return null;
    }
}

public class ApprovalListItem
{
    public ApprovalRequest Request { get; set; } = null!;
    public string RuleName { get; set; } = "";
    public string RecordTitle { get; set; } = "";
}
