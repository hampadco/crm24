using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Services;

namespace Crm.Web.Areas.App.Controllers;

/// <summary>وب‌فرم‌ساز: فرم عمومی از فیلدهای ماژول متادیتا + لینک عمومی و کد iFrame.</summary>
public class WebFormsController : AppControllerBase
{
    public class WebFormFieldConfig
    {
        public string Name { get; set; } = string.Empty;
        public bool Hidden { get; set; }
        public string? DefaultValue { get; set; }
    }

    private readonly CrmDbContext _db;
    private readonly MetadataService _metadata;

    public WebFormsController(CrmDbContext db, MetadataService metadata)
    {
        _db = db;
        _metadata = metadata;
    }

    [HttpGet("/App/webforms")]
    public async Task<IActionResult> Index()
    {
        var forms = await _db.WebForms.AsNoTracking()
            .Include(f => f.Module)
            .OrderByDescending(f => f.Id).Take(300).ToListAsync();
        ViewData["Title"] = "وب‌فرم‌ها";
        return View(forms);
    }

    [HttpGet("/App/webforms/create")]
    public async Task<IActionResult> Create()
    {
        await FillModulesAsync();
        ViewData["Title"] = "وب‌فرم جدید";
        return View("Form", new WebForm { SuccessMessage = "اطلاعات شما با موفقیت ثبت شد." });
    }

    [HttpGet("/App/webforms/{id:int}/edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var form = await _db.WebForms.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id);
        if (form is null)
            return NotFound();
        await FillModulesAsync();
        ViewData["Title"] = $"ویرایش {form.Name}";
        return View("Form", form);
    }

    /// <summary>فیلدهای یک ماژول برای سازنده فرم (AJAX).</summary>
    [HttpGet("/App/webforms/module-fields/{moduleId:int}")]
    public async Task<IActionResult> ModuleFields(int moduleId)
    {
        var fields = await _metadata.GetFieldsAsync(moduleId);
        return Json(fields.Select(f => new { name = f.Name, label = f.Label, required = f.IsRequired }));
    }

    [HttpPost("/App/webforms/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int id, string name, int moduleId, string fieldsJson,
        string successMessage, bool useCaptcha, int? assignToUserId, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name) || moduleId == 0)
        {
            TempData["Error"] = "نام فرم و ماژول الزامی است.";
            return RedirectToAction(nameof(Index));
        }

        try { JsonSerializer.Deserialize<List<WebFormFieldConfig>>(fieldsJson); }
        catch { fieldsJson = "[]"; }

        WebForm form;
        if (id == 0)
        {
            form = new WebForm { PublicKey = Guid.NewGuid().ToString("N")[..12] };
            _db.WebForms.Add(form);
        }
        else
        {
            form = await _db.WebForms.FirstAsync(f => f.Id == id);
        }

        form.Name = name.Trim();
        form.ModuleId = moduleId;
        form.FieldsJson = fieldsJson;
        form.SuccessMessage = successMessage?.Trim() ?? "";
        form.UseCaptcha = useCaptcha;
        form.AssignToUserId = assignToUserId;
        form.IsActive = isActive;

        await _db.SaveChangesAsync();
        TempData["Success"] = "وب‌فرم ذخیره شد.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/App/webforms/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var form = await _db.WebForms.FirstOrDefaultAsync(f => f.Id == id);
        if (form is not null)
        {
            form.IsDeleted = true;
            form.DeletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            TempData["Success"] = "وب‌فرم حذف شد.";
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task FillModulesAsync()
    {
        ViewBag.Modules = await _metadata.GetActiveModulesAsync();
        ViewBag.Users = await _db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.FullName);
    }
}
