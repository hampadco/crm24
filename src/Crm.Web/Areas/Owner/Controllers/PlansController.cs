using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Web.Areas.Owner.Controllers;

public class PlanFormModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "نام پلن الزامی است.")]
    [Display(Name = "نام پلن")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "توضیح کوتاه")]
    public string? Description { get; set; }

    [Display(Name = "قیمت ماهانه (تومان)")]
    [Range(0, 999_999_999)]
    public decimal PriceMonthly { get; set; }

    [Display(Name = "قیمت سالانه (تومان)")]
    [Range(0, 9_999_999_999)]
    public decimal PriceYearly { get; set; }

    [Display(Name = "حداکثر کاربر")]
    [Range(1, 10_000)]
    public int MaxUsers { get; set; } = 5;

    [Display(Name = "حداکثر رکورد")]
    [Range(100, 100_000_000)]
    public int MaxRecords { get; set; } = 10_000;

    [Display(Name = "فضای ذخیره‌سازی (مگابایت)")]
    [Range(100, 1_000_000)]
    public int MaxStorageMb { get; set; } = 1024;

    [Display(Name = "امکانات (هر خط یک مورد)")]
    public string Features { get; set; } = string.Empty;

    [Display(Name = "فعال")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "پلن پیشنهادی (نمایش ویژه)")]
    public bool IsFeatured { get; set; }

    [Display(Name = "ترتیب نمایش")]
    public int SortOrder { get; set; }
}

public class PlansController : OwnerControllerBase
{
    private readonly CrmDbContext _db;

    public PlansController(CrmDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var plans = await _db.Plans.AsNoTracking().OrderBy(p => p.SortOrder).ToListAsync();
        return View(plans);
    }

    [HttpGet]
    public IActionResult Create() => View("Form", new PlanFormModel());

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var plan = await _db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (plan is null)
            return NotFound();

        return View("Form", new PlanFormModel
        {
            Id = plan.Id,
            Name = plan.Name,
            Description = plan.Description,
            PriceMonthly = plan.PriceMonthly,
            PriceYearly = plan.PriceYearly,
            MaxUsers = plan.MaxUsers,
            MaxRecords = plan.MaxRecords,
            MaxStorageMb = plan.MaxStorageMb,
            Features = plan.Features,
            IsActive = plan.IsActive,
            IsFeatured = plan.IsFeatured,
            SortOrder = plan.SortOrder
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(PlanFormModel model)
    {
        if (!ModelState.IsValid)
            return View("Form", model);

        Plan plan;
        if (model.Id is int id)
        {
            plan = await _db.Plans.FirstOrDefaultAsync(p => p.Id == id) ?? new Plan();
            if (plan.Id == 0)
                return NotFound();
        }
        else
        {
            plan = new Plan();
            _db.Plans.Add(plan);
        }

        plan.Name = model.Name.Trim();
        plan.Description = model.Description?.Trim();
        plan.PriceMonthly = model.PriceMonthly;
        plan.PriceYearly = model.PriceYearly;
        plan.MaxUsers = model.MaxUsers;
        plan.MaxRecords = model.MaxRecords;
        plan.MaxStorageMb = model.MaxStorageMb;
        plan.Features = model.Features.Trim();
        plan.IsActive = model.IsActive;
        plan.IsFeatured = model.IsFeatured;
        plan.SortOrder = model.SortOrder;

        await _db.SaveChangesAsync();
        TempData["Success"] = $"پلن «{plan.Name}» ذخیره شد.";
        return RedirectToAction(nameof(Index));
    }
}
