using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Web.Areas.App.Controllers;

public class CommissionRuleFormModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "نام قانون الزامی است"), MaxLength(200)]
    [Display(Name = "نام قانون")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "محصول خاص (خالی = کل فاکتور)")]
    public int? ProductId { get; set; }

    [Range(0, 100), Display(Name = "درصد پورسانت")]
    public decimal Percent { get; set; }

    [Range(0, 999999999999), Display(Name = "مبلغ ثابت (تومان)")]
    public decimal FixedAmount { get; set; }

    [Range(0, 999999999999), Display(Name = "حداقل مبلغ فاکتور (پلکان)")]
    public decimal MinInvoiceAmount { get; set; }

    [Display(Name = "فعال")]
    public bool IsActive { get; set; } = true;
}

public class CommissionReportRow
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public int DealCount { get; set; }
    public decimal Total { get; set; }
}

/// <summary>مشارکت در فروش: قوانین پورسانت + گزارش هر کارشناس.</summary>
public class CommissionsController : AppControllerBase
{
    private readonly CrmDbContext _db;

    public CommissionsController(CrmDbContext db) => _db = db;

    [HttpGet("/App/commissions")]
    public async Task<IActionResult> Index()
    {
        var rules = await _db.CommissionRules.AsNoTracking()
            .Include(r => r.Product)
            .OrderBy(r => r.Name).ToListAsync();

        var entries = await _db.CommissionEntries.AsNoTracking().ToListAsync();
        var userIds = entries.Select(e => e.UserId).Distinct().ToList();
        var users = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        var report = entries
            .GroupBy(e => e.UserId)
            .Select(g => new CommissionReportRow
            {
                UserId = g.Key,
                UserName = users.GetValueOrDefault(g.Key, $"کاربر {g.Key}"),
                DealCount = g.Select(e => e.DocumentId).Distinct().Count(),
                Total = g.Sum(e => e.Amount)
            })
            .OrderByDescending(r => r.Total)
            .ToList();

        ViewData["Title"] = "مشارکت در فروش (پورسانت)";
        ViewBag.Report = report;
        return View(rules);
    }

    [HttpGet("/App/commissions/create")]
    public async Task<IActionResult> Create()
    {
        await FillProductsAsync();
        ViewData["Title"] = "قانون پورسانت جدید";
        return View("Form", new CommissionRuleFormModel());
    }

    [HttpGet("/App/commissions/{id:int}/edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var rule = await _db.CommissionRules.FindAsync(id);
        if (rule is null)
            return NotFound();

        await FillProductsAsync();
        ViewData["Title"] = $"ویرایش {rule.Name}";
        return View("Form", new CommissionRuleFormModel
        {
            Id = rule.Id,
            Name = rule.Name,
            ProductId = rule.ProductId,
            Percent = rule.Percent,
            FixedAmount = rule.FixedAmount,
            MinInvoiceAmount = rule.MinInvoiceAmount,
            IsActive = rule.IsActive
        });
    }

    [HttpPost("/App/commissions/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(CommissionRuleFormModel model)
    {
        if (!ModelState.IsValid)
        {
            await FillProductsAsync();
            ViewData["Title"] = "قانون پورسانت";
            return View("Form", model);
        }

        CommissionRule rule;
        if (model.Id == 0)
        {
            rule = new CommissionRule();
            _db.CommissionRules.Add(rule);
        }
        else
        {
            rule = await _db.CommissionRules.FindAsync(model.Id) ?? throw new InvalidOperationException();
        }

        rule.Name = model.Name.Trim();
        rule.ProductId = model.ProductId;
        rule.Percent = model.Percent;
        rule.FixedAmount = model.FixedAmount;
        rule.MinInvoiceAmount = model.MinInvoiceAmount;
        rule.IsActive = model.IsActive;

        await _db.SaveChangesAsync();
        TempData["Success"] = "قانون پورسانت ذخیره شد.";
        return RedirectToAction(nameof(Index));
    }

    private async Task FillProductsAsync()
    {
        ViewBag.Products = await _db.Products.AsNoTracking()
            .Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync();
    }
}
