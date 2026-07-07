using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Services;

namespace Crm.Web.Areas.App.Controllers;

public class SalesDocFormModel
{
    public int Id { get; set; }
    public SalesDocumentKind Kind { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int? ContactRecordId { get; set; }
    public int? OrganizationRecordId { get; set; }
    public decimal DiscountPercent { get; set; }
    public string? Note { get; set; }
    public DateTime? ValidUntil { get; set; }
    public List<LineInput> Lines { get; set; } = [];

    public List<Product> Products { get; set; } = [];
    public Dictionary<int, string> ContactOptions { get; set; } = [];
}

/// <summary>چرخه مالی: پیش‌فاکتور / سفارش فروش / فاکتور + پرداخت و اقساط.</summary>
public class FinanceController : AppControllerBase
{
    private static readonly Dictionary<string, SalesDocumentKind> KindSlugs = new()
    {
        ["quotes"] = SalesDocumentKind.Quote,
        ["orders"] = SalesDocumentKind.Order,
        ["invoices"] = SalesDocumentKind.Invoice
    };

    public static string KindLabel(SalesDocumentKind kind) => kind switch
    {
        SalesDocumentKind.Quote => "پیش‌فاکتور",
        SalesDocumentKind.Order => "سفارش فروش",
        _ => "فاکتور"
    };

    public static string KindSlug(SalesDocumentKind kind) => kind switch
    {
        SalesDocumentKind.Quote => "quotes",
        SalesDocumentKind.Order => "orders",
        _ => "invoices"
    };

    public static string StatusLabel(SalesDocumentStatus status) => status switch
    {
        SalesDocumentStatus.Draft => "پیش‌نویس",
        SalesDocumentStatus.Confirmed => "تأییدشده",
        SalesDocumentStatus.Converted => "تبدیل‌شده",
        SalesDocumentStatus.PartiallyPaid => "پرداخت ناقص",
        SalesDocumentStatus.Paid => "تسویه‌شده",
        _ => "لغوشده"
    };

    private readonly CrmDbContext _db;
    private readonly FinanceService _finance;

    public FinanceController(CrmDbContext db, FinanceService finance)
    {
        _db = db;
        _finance = finance;
    }

    [HttpGet("/App/finance/{kindSlug:regex(^quotes|orders|invoices$)}")]
    public async Task<IActionResult> Index(string kindSlug, string? q)
    {
        var kind = KindSlugs[kindSlug];
        var query = _db.SalesDocuments.AsNoTracking().Where(d => d.Kind == kind);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(d => d.CustomerName.Contains(q));

        var docs = await query.OrderByDescending(d => d.Id).Take(300).ToListAsync();
        ViewData["Title"] = $"{KindLabel(kind)}ها";
        ViewBag.Kind = kind;
        ViewBag.KindSlug = kindSlug;
        ViewBag.Query = q;
        return View(docs);
    }

    [HttpGet("/App/finance/{kindSlug:regex(^quotes|orders|invoices$)}/create")]
    public async Task<IActionResult> Create(string kindSlug)
    {
        var kind = KindSlugs[kindSlug];
        var model = new SalesDocFormModel { Kind = kind };
        await FillFormListsAsync(model);
        ViewData["Title"] = $"{KindLabel(kind)} جدید";
        return View("Form", model);
    }

    [HttpGet("/App/finance/doc/{id:int}/edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var document = await _finance.GetAsync(id);
        if (document is null)
            return NotFound();
        if (document.Status != SalesDocumentStatus.Draft)
        {
            TempData["Error"] = "فقط سند پیش‌نویس قابل ویرایش است.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var model = new SalesDocFormModel
        {
            Id = document.Id,
            Kind = document.Kind,
            CustomerName = document.CustomerName,
            ContactRecordId = document.ContactRecordId,
            OrganizationRecordId = document.OrganizationRecordId,
            DiscountPercent = document.DiscountPercent,
            Note = document.Note,
            Lines = document.Lines.OrderBy(l => l.SortOrder).Select(l => new LineInput
            {
                ProductId = l.ProductId,
                Title = l.Title,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                DiscountPercent = l.DiscountPercent,
                TaxPercent = l.TaxPercent
            }).ToList()
        };
        await FillFormListsAsync(model);
        ViewData["Title"] = $"ویرایش {KindLabel(document.Kind)} {document.Number}";
        return View("Form", model);
    }

    [HttpPost("/App/finance/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(SalesDocFormModel model)
    {
        model.Lines = model.Lines.Where(l => !string.IsNullOrWhiteSpace(l.Title)).ToList();
        if (string.IsNullOrWhiteSpace(model.CustomerName) || model.Lines.Count == 0)
        {
            TempData["Error"] = "نام مشتری و حداقل یک آیتم الزامی است.";
            await FillFormListsAsync(model);
            ViewData["Title"] = $"{KindLabel(model.Kind)} جدید";
            return View("Form", model);
        }

        try
        {
            if (model.Id == 0)
            {
                var document = await _finance.CreateAsync(
                    model.Kind, model.CustomerName, model.ContactRecordId, model.OrganizationRecordId,
                    model.DiscountPercent, model.Note, model.ValidUntil?.ToUniversalTime(), model.Lines);
                TempData["Success"] = $"{KindLabel(model.Kind)} شماره {document.Number} ثبت شد.";
                return RedirectToAction(nameof(Details), new { id = document.Id });
            }

            await _finance.UpdateAsync(model.Id, model.CustomerName, model.DiscountPercent, model.Note, model.Lines);
            TempData["Success"] = "سند بروزرسانی شد.";
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            await FillFormListsAsync(model);
            return View("Form", model);
        }
    }

    [HttpGet("/App/finance/doc/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var document = await _finance.GetAsync(id);
        if (document is null)
            return NotFound();

        ViewData["Title"] = $"{KindLabel(document.Kind)} شماره {document.Number}";
        return View(document);
    }

    [HttpGet("/App/finance/doc/{id:int}/print")]
    public async Task<IActionResult> Print(int id)
    {
        var document = await _finance.GetAsync(id);
        if (document is null)
            return NotFound();

        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == document.TenantId);
        ViewBag.TenantName = tenant?.Name ?? "";
        ViewData["Title"] = $"چاپ {KindLabel(document.Kind)} {document.Number}";
        return View(document);
    }

    [HttpPost("/App/finance/doc/{id:int}/confirm")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(int id)
    {
        try
        {
            await _finance.ConfirmAsync(id);
            TempData["Success"] = "سند تأیید شد.";
        }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("/App/finance/doc/{id:int}/convert")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Convert(int id)
    {
        try
        {
            var target = await _finance.ConvertAsync(id);
            TempData["Success"] = $"{KindLabel(target.Kind)} شماره {target.Number} ساخته شد.";
            return RedirectToAction(nameof(Details), new { id = target.Id });
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    [HttpPost("/App/finance/doc/{id:int}/pay")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPayment(int id, decimal amount, string method = "cash", string? reference = null)
    {
        try
        {
            await _finance.AddPaymentAsync(id, amount, method, reference, null);
            TempData["Success"] = "پرداخت ثبت شد.";
        }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("/App/finance/doc/{id:int}/installments")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateInstallments(int id, int count, DateTime firstDueDate)
    {
        try
        {
            await _finance.CreateInstallmentsAsync(id, count, DateTime.SpecifyKind(firstDueDate, DateTimeKind.Utc));
            TempData["Success"] = $"{count} قسط ایجاد شد.";
        }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("/App/finance/installment/{installmentId:int}/pay")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PayInstallment(int installmentId, int documentId)
    {
        try
        {
            await _finance.PayInstallmentAsync(installmentId);
            TempData["Success"] = "قسط پرداخت شد.";
        }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id = documentId });
    }

    private async Task FillFormListsAsync(SalesDocFormModel model)
    {
        model.Products = await _db.Products.AsNoTracking()
            .Where(p => p.IsActive).OrderBy(p => p.Name).Take(500).ToListAsync();

        var contactsModule = await _db.Modules.AsNoTracking().FirstOrDefaultAsync(m => m.Name == "contacts");
        if (contactsModule is not null)
        {
            model.ContactOptions = await _db.Records.AsNoTracking()
                .Where(r => r.ModuleId == contactsModule.Id)
                .OrderByDescending(r => r.Id).Take(300)
                .ToDictionaryAsync(r => r.Id, r => r.Title);
        }
    }
}
