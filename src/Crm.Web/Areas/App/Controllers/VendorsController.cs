using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Web.Areas.App.Controllers;

/// <summary>تأمین‌کنندگان کالا و خدمات.</summary>
public class VendorsController : AppControllerBase
{
    private readonly CrmDbContext _db;

    public VendorsController(CrmDbContext db) => _db = db;

    [HttpGet("/App/vendors")]
    public async Task<IActionResult> Index()
    {
        var vendors = await _db.Vendors.AsNoTracking()
            .OrderByDescending(v => v.Id).Take(300).ToListAsync();
        ViewData["Title"] = "تأمین‌کنندگان";
        return View(vendors);
    }

    [HttpGet("/App/vendors/create")]
    public IActionResult Create()
    {
        ViewData["Title"] = "تأمین‌کننده جدید";
        return View("Form", new Vendor());
    }

    [HttpGet("/App/vendors/{id:int}/edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var vendor = await _db.Vendors.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id);
        if (vendor is null)
            return NotFound();
        ViewData["Title"] = $"ویرایش {vendor.Name}";
        return View("Form", vendor);
    }

    [HttpPost("/App/vendors/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int id, string name, string? phone, string? email,
        string? address, string? notes, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "نام تأمین‌کننده الزامی است.";
            return RedirectToAction(nameof(Index));
        }

        Vendor vendor;
        if (id == 0)
        {
            vendor = new Vendor();
            _db.Vendors.Add(vendor);
        }
        else
        {
            vendor = await _db.Vendors.FirstAsync(v => v.Id == id);
        }

        vendor.Name = name.Trim();
        vendor.Phone = phone?.Trim();
        vendor.Email = email?.Trim();
        vendor.Address = address?.Trim();
        vendor.Notes = notes?.Trim();
        vendor.IsActive = isActive;

        await _db.SaveChangesAsync();
        TempData["Success"] = "تأمین‌کننده ذخیره شد.";
        return RedirectToAction(nameof(Index));
    }
}
