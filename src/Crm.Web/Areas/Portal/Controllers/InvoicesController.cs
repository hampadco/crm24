using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Web.Areas.Portal.Controllers;

/// <summary>مشاهده فاکتورها و پرداخت‌ها توسط مشتری نهایی.</summary>
public class InvoicesController : PortalControllerBase
{
    private readonly CrmDbContext _db;

    public InvoicesController(CrmDbContext db) => _db = db;

    [HttpGet("/Portal/invoices")]
    public async Task<IActionResult> Index()
    {
        var invoices = PortalContactRecordId is int contactId
            ? await _db.SalesDocuments.AsNoTracking()
                .Include(d => d.Payments)
                .Where(d => d.Kind == SalesDocumentKind.Invoice && d.ContactRecordId == contactId)
                .OrderByDescending(d => d.Id)
                .ToListAsync()
            : [];

        ViewData["Title"] = "فاکتورهای من";
        return View(invoices);
    }

    [HttpGet("/Portal/invoices/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        if (PortalContactRecordId is not int contactId)
            return NotFound();

        var invoice = await _db.SalesDocuments.AsNoTracking()
            .Include(d => d.Lines.OrderBy(l => l.SortOrder))
            .Include(d => d.Payments)
            .Include(d => d.Installments)
            .FirstOrDefaultAsync(d => d.Id == id && d.Kind == SalesDocumentKind.Invoice && d.ContactRecordId == contactId);
        if (invoice is null)
            return NotFound();

        ViewData["Title"] = $"فاکتور شماره {invoice.Number}";
        return View(invoice);
    }

    /// <summary>ساخت لینک پرداخت آنلاین برای مانده فاکتور.</summary>
    [HttpPost("/Portal/invoices/{id:int}/pay-online")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PayOnline(int id, [FromServices] IPaymentGateway gateway)
    {
        if (PortalContactRecordId is not int contactId)
            return NotFound();

        var invoice = await _db.SalesDocuments.AsNoTracking()
            .Include(d => d.Payments)
            .FirstOrDefaultAsync(d => d.Id == id && d.Kind == SalesDocumentKind.Invoice && d.ContactRecordId == contactId);
        if (invoice is null)
            return NotFound();

        var remaining = invoice.GrandTotal - invoice.Payments.Sum(p => p.Amount);
        if (remaining <= 0)
        {
            TempData["Error"] = "این فاکتور تسویه شده است.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var transaction = new PaymentTransaction
        {
            Token = Guid.NewGuid().ToString("N"),
            Kind = PaymentTransactionKind.Invoice,
            TargetId = invoice.Id,
            Amount = remaining,
            Description = $"پرداخت فاکتور شماره {invoice.Number}",
            ReturnUrl = $"/Portal/invoices/{invoice.Id}"
        };
        _db.PaymentTransactions.Add(transaction);
        await _db.SaveChangesAsync();

        return Redirect(await gateway.BeginAsync(transaction));
    }
}
