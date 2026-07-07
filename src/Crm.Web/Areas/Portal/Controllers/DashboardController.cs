using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Web.Areas.Portal.Controllers;

public class PortalDashboardViewModel
{
    public string FullName { get; set; } = string.Empty;
    public int OpenTickets { get; set; }
    public int TotalTickets { get; set; }
    public int InvoiceCount { get; set; }
    public decimal UnpaidAmount { get; set; }
    public List<Ticket> RecentTickets { get; set; } = [];
}

public class DashboardController : PortalControllerBase
{
    private readonly CrmDbContext _db;

    public DashboardController(CrmDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var myTickets = _db.Tickets.AsNoTracking().Where(t => t.PortalUserId == PortalUserId);

        var model = new PortalDashboardViewModel
        {
            FullName = User.FindFirst(Crm.Infrastructure.Identity.CrmClaimTypes.FullName)?.Value ?? "",
            TotalTickets = await myTickets.CountAsync(),
            OpenTickets = await myTickets.CountAsync(t => t.Status != TicketStatus.Closed && t.Status != TicketStatus.Resolved),
            RecentTickets = await myTickets.OrderByDescending(t => t.Id).Take(5).ToListAsync()
        };

        if (PortalContactRecordId is int contactId)
        {
            var invoices = await _db.SalesDocuments.AsNoTracking()
                .Include(d => d.Payments)
                .Where(d => d.Kind == SalesDocumentKind.Invoice && d.ContactRecordId == contactId)
                .ToListAsync();
            model.InvoiceCount = invoices.Count;
            model.UnpaidAmount = invoices.Sum(d => d.GrandTotal - d.Payments.Sum(p => p.Amount));
        }

        ViewData["Title"] = "پورتال مشتری";
        return View(model);
    }
}
