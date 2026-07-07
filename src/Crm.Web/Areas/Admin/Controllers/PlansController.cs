using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Web.Models.Admin;

namespace Crm.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class PlansController : Controller
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

        return View("Form", MapPlan(plan));
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

    private static PlanFormModel MapPlan(Plan plan) => new()
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
    };
}
