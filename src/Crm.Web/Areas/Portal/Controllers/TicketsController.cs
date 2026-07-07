using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;
using Crm.Infrastructure.Services;

namespace Crm.Web.Areas.Portal.Controllers;

/// <summary>ثبت و پیگیری تیکت توسط مشتری نهایی.</summary>
public class TicketsController : PortalControllerBase
{
    private readonly CrmDbContext _db;
    private readonly SupportService _support;

    public TicketsController(CrmDbContext db, SupportService support)
    {
        _db = db;
        _support = support;
    }

    [HttpGet("/Portal/tickets")]
    public async Task<IActionResult> Index()
    {
        var tickets = await _db.Tickets.AsNoTracking()
            .Where(t => t.PortalUserId == PortalUserId)
            .OrderByDescending(t => t.Id)
            .ToListAsync();

        ViewData["Title"] = "تیکت‌های من";
        return View(tickets);
    }

    [HttpGet("/Portal/tickets/create")]
    public IActionResult Create()
    {
        ViewData["Title"] = "تیکت جدید";
        return View();
    }

    [HttpPost("/Portal/tickets/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string subject, string body, TicketPriority priority)
    {
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(body))
        {
            TempData["Error"] = "موضوع و متن تیکت الزامی است.";
            return RedirectToAction(nameof(Create));
        }

        var author = User.FindFirst(CrmClaimTypes.FullName)?.Value ?? "مشتری";
        var ticket = await _support.CreateTicketAsync(
            subject, body, priority, category: null, author, isFromCustomer: true,
            portalUserId: PortalUserId, contactRecordId: PortalContactRecordId);

        TempData["Success"] = $"تیکت شماره {ticket.Number} ثبت شد.";
        return RedirectToAction(nameof(Details), new { id = ticket.Id });
    }

    [HttpGet("/Portal/tickets/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var ticket = await _db.Tickets.AsNoTracking()
            .Include(t => t.Messages.OrderBy(m => m.Id))
            .FirstOrDefaultAsync(t => t.Id == id && t.PortalUserId == PortalUserId);
        if (ticket is null)
            return NotFound();

        // نظرسنجی پس از بستن تیکت (hook پلن ۸)
        if (ticket.Status is TicketStatus.Closed or TicketStatus.Resolved)
        {
            ViewBag.TicketSurveyKey = await _db.Surveys.AsNoTracking()
                .Where(s => s.IsTicketSurvey && s.IsActive)
                .Select(s => s.PublicKey)
                .FirstOrDefaultAsync();
        }

        ViewData["Title"] = $"تیکت #{ticket.Number}";
        return View(ticket);
    }

    [HttpPost("/Portal/tickets/{id:int}/reply")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reply(int id, string body)
    {
        var ticket = await _db.Tickets.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id && t.PortalUserId == PortalUserId);
        if (ticket is null)
            return NotFound();

        if (!string.IsNullOrWhiteSpace(body))
        {
            var author = User.FindFirst(CrmClaimTypes.FullName)?.Value ?? "مشتری";
            try
            {
                await _support.ReplyAsync(id, body, author, isFromCustomer: true);
                TempData["Success"] = "پیام شما ثبت شد.";
            }
            catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        }
        return RedirectToAction(nameof(Details), new { id });
    }
}
