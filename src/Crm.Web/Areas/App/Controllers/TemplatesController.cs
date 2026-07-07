using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Web.Areas.App.Controllers;

/// <summary>قالب‌های پیام آماده — عمومی برای همه یا خصوصی سازنده.</summary>
public class TemplatesController : AppControllerBase
{
    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;

    public TemplatesController(CrmDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    [HttpGet("/App/templates")]
    public async Task<IActionResult> Index()
    {
        var templates = await _db.MessageTemplates.AsNoTracking()
            .Where(t => t.IsPublic || t.CreatedByUserId == _tenant.UserId)
            .OrderByDescending(t => t.Id).Take(300).ToListAsync();
        ViewData["Title"] = "قالب‌های پیام";
        return View(templates);
    }

    /// <summary>فهرست قالب‌ها برای دکمه «قالب» در نقاط ارسال پیام (AJAX).</summary>
    [HttpGet("/App/templates/list")]
    public async Task<IActionResult> List()
    {
        var templates = await _db.MessageTemplates.AsNoTracking()
            .Where(t => t.IsPublic || t.CreatedByUserId == _tenant.UserId)
            .OrderBy(t => t.Title)
            .Select(t => new { t.Id, t.Title, t.Body })
            .ToListAsync();
        return Json(templates);
    }

    [HttpPost("/App/templates/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int id, string title, string body, bool isPublic)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
        {
            TempData["Error"] = "عنوان و متن قالب الزامی است.";
            return RedirectToAction(nameof(Index));
        }

        MessageTemplate template;
        if (id == 0)
        {
            template = new MessageTemplate();
            _db.MessageTemplates.Add(template);
        }
        else
        {
            template = await _db.MessageTemplates.FirstAsync(t => t.Id == id);
            if (!template.IsPublic && template.CreatedByUserId != _tenant.UserId && !_tenant.IsTenantAdmin)
                return Forbid("Identity.Application");
        }

        template.Title = title.Trim();
        template.Body = body.Trim();
        template.IsPublic = isPublic;

        await _db.SaveChangesAsync();
        TempData["Success"] = "قالب ذخیره شد.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/App/templates/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var template = await _db.MessageTemplates.FirstOrDefaultAsync(t => t.Id == id);
        if (template is not null &&
            (template.CreatedByUserId == _tenant.UserId || _tenant.IsTenantAdmin))
        {
            template.IsDeleted = true;
            template.DeletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            TempData["Success"] = "قالب حذف شد.";
        }
        return RedirectToAction(nameof(Index));
    }
}
