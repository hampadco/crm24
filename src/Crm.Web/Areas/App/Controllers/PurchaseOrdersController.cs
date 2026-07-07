using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Services;

namespace Crm.Web.Areas.App.Controllers;

/// <summary>سفارش خرید: پیش‌نویس ← ثبت ← دریافت (شارژ انبار) + پرداخت به تأمین‌کننده.</summary>
public class PurchaseOrdersController : AppControllerBase
{
    public static string StatusLabel(PurchaseOrderStatus status) => status switch
    {
        PurchaseOrderStatus.Draft => "پیش‌نویس",
        PurchaseOrderStatus.Ordered => "ثبت‌شده",
        PurchaseOrderStatus.Received => "دریافت‌شده",
        _ => "لغوشده"
    };

    public class PoLineInputModel
    {
        public int? ProductId { get; set; }
        public string Title { get; set; } = string.Empty;
        public decimal Quantity { get; set; } = 1;
        public decimal UnitCost { get; set; }
    }

    public class PoFormModel
    {
        public int Id { get; set; }
        public int VendorId { get; set; }
        public string? Note { get; set; }
        public List<PoLineInputModel> Lines { get; set; } = new();
    }

    private readonly CrmDbContext _db;
    private readonly PurchasingService _purchasing;

    public PurchaseOrdersController(CrmDbContext db, PurchasingService purchasing)
    {
        _db = db;
        _purchasing = purchasing;
    }

    [HttpGet("/App/purchase-orders")]
    public async Task<IActionResult> Index()
    {
        var orders = await _db.PurchaseOrders.AsNoTracking()
            .Include(p => p.Vendor)
            .Include(p => p.Payments)
            .OrderByDescending(p => p.Id).Take(300).ToListAsync();
        ViewData["Title"] = "سفارش‌های خرید";
        return View(orders);
    }

    [HttpGet("/App/purchase-orders/create")]
    public async Task<IActionResult> Create()
    {
        await FillListsAsync();
        ViewData["Title"] = "سفارش خرید جدید";
        return View("Form", new PoFormModel { Lines = { new PoLineInputModel() } });
    }

    [HttpGet("/App/purchase-orders/{id:int}/edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var order = await _db.PurchaseOrders.AsNoTracking()
            .Include(p => p.Lines.OrderBy(l => l.SortOrder))
            .FirstOrDefaultAsync(p => p.Id == id);
        if (order is null)
            return NotFound();
        if (order.Status != PurchaseOrderStatus.Draft)
        {
            TempData["Error"] = "فقط سفارش پیش‌نویس قابل ویرایش است.";
            return RedirectToAction(nameof(Details), new { id });
        }

        await FillListsAsync();
        ViewData["Title"] = $"ویرایش سفارش {order.Number}";
        return View("Form", new PoFormModel
        {
            Id = order.Id,
            VendorId = order.VendorId,
            Note = order.Note,
            Lines = order.Lines.Select(l => new PoLineInputModel
            {
                ProductId = l.ProductId,
                Title = l.Title,
                Quantity = l.Quantity,
                UnitCost = l.UnitCost
            }).ToList()
        });
    }

    [HttpPost("/App/purchase-orders/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(PoFormModel model)
    {
        var lines = model.Lines
            .Where(l => !string.IsNullOrWhiteSpace(l.Title))
            .Select(l => new PurchasingService.PoLineInput(l.ProductId, l.Title, l.Quantity, l.UnitCost))
            .ToList();

        try
        {
            if (model.Id == 0)
            {
                var order = await _purchasing.CreateAsync(model.VendorId, model.Note, lines);
                TempData["Success"] = $"سفارش خرید شماره {order.Number} ثبت شد.";
                return RedirectToAction(nameof(Details), new { id = order.Id });
            }

            await _purchasing.UpdateAsync(model.Id, model.VendorId, model.Note, lines);
            TempData["Success"] = "سفارش بروزرسانی شد.";
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            await FillListsAsync();
            return View("Form", model);
        }
    }

    [HttpGet("/App/purchase-orders/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var order = await _db.PurchaseOrders.AsNoTracking()
            .Include(p => p.Vendor)
            .Include(p => p.Lines.OrderBy(l => l.SortOrder))
            .Include(p => p.Payments)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (order is null)
            return NotFound();

        ViewData["Title"] = $"سفارش خرید {order.Number}";
        return View(order);
    }

    [HttpPost("/App/purchase-orders/{id:int}/order")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkOrdered(int id)
    {
        try
        {
            await _purchasing.MarkOrderedAsync(id);
            TempData["Success"] = "سفارش ثبت شد.";
        }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("/App/purchase-orders/{id:int}/receive")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Receive(int id)
    {
        try
        {
            await _purchasing.ReceiveAsync(id);
            TempData["Success"] = "کالا دریافت و انبار شارژ شد.";
        }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("/App/purchase-orders/{id:int}/pay")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pay(int id, decimal amount, string? method, string? reference)
    {
        try
        {
            await _purchasing.AddPaymentAsync(id, amount, method, reference);
            TempData["Success"] = "پرداخت ثبت شد.";
        }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task FillListsAsync()
    {
        ViewBag.Vendors = await _db.Vendors.AsNoTracking()
            .Where(v => v.IsActive).OrderBy(v => v.Name).ToListAsync();
        ViewBag.Products = await _db.Products.AsNoTracking()
            .Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync();
    }
}
