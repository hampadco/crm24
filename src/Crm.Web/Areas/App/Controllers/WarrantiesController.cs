using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Web.Areas.App.Controllers;

/// <summary>گارانتی و پرونده فروش محصول با سریال.</summary>
public class WarrantiesController : AppControllerBase
{
    private readonly CrmDbContext _db;

    public WarrantiesController(CrmDbContext db) => _db = db;

    [HttpGet("/App/warranties")]
    public async Task<IActionResult> Index(string? q)
    {
        var query = _db.Warranties.AsNoTracking().Include(w => w.Product).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(w => w.SerialNumber.Contains(q) || w.CustomerName.Contains(q));

        var warranties = await query.OrderByDescending(w => w.Id).Take(300).ToListAsync();
        ViewData["Title"] = "گارانتی‌ها";
        ViewBag.Query = q;
        return View(warranties);
    }

    [HttpGet("/App/warranties/create")]
    public async Task<IActionResult> Create()
    {
        ViewBag.Products = await _db.Products.AsNoTracking()
            .Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync();
        ViewData["Title"] = "گارانتی جدید";
        return View();
    }

    [HttpPost("/App/warranties/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string serialNumber, int? productId, string customerName,
        DateTime startUtc, DateTime endUtc, string? notes)
    {
        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            TempData["Error"] = "شماره سریال الزامی است.";
            return RedirectToAction(nameof(Create));
        }

        _db.Warranties.Add(new Warranty
        {
            SerialNumber = serialNumber.Trim(),
            ProductId = productId,
            CustomerName = customerName?.Trim() ?? "",
            StartUtc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc),
            EndUtc = DateTime.SpecifyKind(endUtc, DateTimeKind.Utc),
            Notes = notes?.Trim()
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = "گارانتی ثبت شد.";
        return RedirectToAction(nameof(Index));
    }
}
