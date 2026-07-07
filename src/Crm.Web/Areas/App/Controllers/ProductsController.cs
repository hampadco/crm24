using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Services;

namespace Crm.Web.Areas.App.Controllers;

public class ProductFormModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "نام محصول الزامی است"), MaxLength(200)]
    [Display(Name = "نام محصول/سرویس")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50), Display(Name = "کد محصول (SKU)")]
    public string? Sku { get; set; }

    [Required, MaxLength(30), Display(Name = "واحد")]
    public string Unit { get; set; } = "عدد";

    [Range(0, 999999999999), Display(Name = "قیمت فروش (تومان)")]
    public decimal SalePrice { get; set; }

    [Range(0, 100), Display(Name = "درصد مالیات")]
    public decimal TaxPercent { get; set; }

    [Display(Name = "سرویس (بدون موجودی)")]
    public bool IsService { get; set; }

    [Display(Name = "کنترل موجودی")]
    public bool TrackInventory { get; set; } = true;

    [Display(Name = "موجودی فعلی")]
    public decimal StockQty { get; set; }

    [Display(Name = "نقطه سفارش")]
    public decimal ReorderPoint { get; set; }

    [Display(Name = "فعال")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "توضیحات")]
    public string? Description { get; set; }
}

/// <summary>کاتالوگ محصولات و سرویس‌ها + انبار.</summary>
public class ProductsController : AppControllerBase
{
    private readonly CrmDbContext _db;
    private readonly AuditService _audit;

    public ProductsController(CrmDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet("/App/products")]
    public async Task<IActionResult> Index(string? q)
    {
        var query = _db.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(p => p.Name.Contains(q) || (p.Sku != null && p.Sku.Contains(q)));

        var products = await query.OrderBy(p => p.Name).Take(500).ToListAsync();
        ViewData["Title"] = "محصولات و سرویس‌ها";
        ViewBag.Query = q;
        return View(products);
    }

    [HttpGet("/App/products/create")]
    public IActionResult Create()
    {
        ViewData["Title"] = "محصول جدید";
        return View("Form", new ProductFormModel());
    }

    [HttpGet("/App/products/{id:int}/edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var product = await _db.Products.FindAsync(id);
        if (product is null)
            return NotFound();

        ViewData["Title"] = $"ویرایش {product.Name}";
        return View("Form", new ProductFormModel
        {
            Id = product.Id,
            Name = product.Name,
            Sku = product.Sku,
            Unit = product.Unit,
            SalePrice = product.SalePrice,
            TaxPercent = product.TaxPercent,
            IsService = product.IsService,
            TrackInventory = product.TrackInventory,
            StockQty = product.StockQty,
            ReorderPoint = product.ReorderPoint,
            IsActive = product.IsActive,
            Description = product.Description
        });
    }

    [HttpPost("/App/products/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(ProductFormModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Title"] = model.Id == 0 ? "محصول جدید" : "ویرایش محصول";
            return View("Form", model);
        }

        Product product;
        if (model.Id == 0)
        {
            product = new Product();
            _db.Products.Add(product);
        }
        else
        {
            product = await _db.Products.FindAsync(model.Id) ?? throw new InvalidOperationException();
        }

        product.Name = model.Name.Trim();
        product.Sku = model.Sku?.Trim();
        product.Unit = model.Unit.Trim();
        product.SalePrice = model.SalePrice;
        product.TaxPercent = model.TaxPercent;
        product.IsService = model.IsService;
        product.TrackInventory = model.TrackInventory && !model.IsService;
        product.StockQty = model.StockQty;
        product.ReorderPoint = model.ReorderPoint;
        product.IsActive = model.IsActive;
        product.Description = model.Description?.Trim();

        await _db.SaveChangesAsync();
        _audit.Log("products", product.Id, model.Id == 0 ? "Create" : "Update");
        await _db.SaveChangesAsync();

        TempData["Success"] = "محصول ذخیره شد.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/App/products/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var product = await _db.Products.FindAsync(id);
        if (product is not null)
        {
            product.IsDeleted = true;
            product.DeletedAtUtc = DateTime.UtcNow;
            _audit.Log("products", product.Id, "Delete");
            await _db.SaveChangesAsync();
            TempData["Success"] = "محصول حذف شد.";
        }
        return RedirectToAction(nameof(Index));
    }
}
