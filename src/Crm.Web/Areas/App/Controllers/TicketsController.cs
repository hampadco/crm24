using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Services;

namespace Crm.Web.Areas.App.Controllers;

/// <summary>تیکتینگ پنل CRM: صف تیکت‌ها، مکالمه، SLA و ارجاع.</summary>
public class TicketsController : AppControllerBase
{
    public static string PriorityLabel(TicketPriority priority) => priority switch
    {
        TicketPriority.Low => "کم",
        TicketPriority.Normal => "عادی",
        TicketPriority.High => "زیاد",
        _ => "بحرانی"
    };

    public static string StatusLabel(TicketStatus status) => status switch
    {
        TicketStatus.Open => "باز",
        TicketStatus.InProgress => "در حال بررسی",
        TicketStatus.WaitingCustomer => "منتظر مشتری",
        TicketStatus.Resolved => "حل‌شده",
        _ => "بسته"
    };

    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly SupportService _support;

    public TicketsController(CrmDbContext db, ITenantContext tenant, SupportService support)
    {
        _db = db;
        _tenant = tenant;
        _support = support;
    }

    [HttpGet("/App/tickets")]
    public async Task<IActionResult> Index(string? status, string? q)
    {
        var query = _db.Tickets.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<TicketStatus>(status, out var st))
            query = query.Where(t => t.Status == st);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(t => t.Subject.Contains(q));

        var tickets = await query.OrderByDescending(t => t.Id).Take(300).ToListAsync();

        ViewData["Title"] = "تیکت‌ها";
        ViewBag.Status = status;
        ViewBag.Query = q;
        return View(tickets);
    }

    [HttpGet("/App/tickets/create")]
    public async Task<IActionResult> Create()
    {
        await FillFormListsAsync();
        ViewData["Title"] = "تیکت جدید";
        return View();
    }

    [HttpPost("/App/tickets/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        string subject, string body, TicketPriority priority, string? category,
        int? contactRecordId, int? serviceContractId)
    {
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(body))
        {
            TempData["Error"] = "موضوع و متن تیکت الزامی است.";
            return RedirectToAction(nameof(Create));
        }

        try
        {
            var author = User.FindFirst(Crm.Infrastructure.Identity.CrmClaimTypes.FullName)?.Value ?? "کارشناس";
            var ticket = await _support.CreateTicketAsync(
                subject, body, priority, category, author, isFromCustomer: false,
                contactRecordId: contactRecordId, serviceContractId: serviceContractId);

            TempData["Success"] = $"تیکت شماره {ticket.Number} ثبت شد.";
            return RedirectToAction(nameof(Details), new { id = ticket.Id });
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Create));
        }
    }

    [HttpGet("/App/tickets/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var ticket = await _db.Tickets.AsNoTracking()
            .Include(t => t.Messages.OrderBy(m => m.Id))
            .Include(t => t.ServiceContract)
            .Include(t => t.PortalUser)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null)
            return NotFound();

        ViewBag.Users = await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == _tenant.TenantId)
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        var warranty = ticket.ContactRecordId is not null
            ? await _db.Warranties.AsNoTracking()
                .Where(w => w.ContactRecordId == ticket.ContactRecordId)
                .OrderByDescending(w => w.EndUtc)
                .FirstOrDefaultAsync()
            : null;
        ViewBag.Warranty = warranty;

        ViewData["Title"] = $"تیکت #{ticket.Number}";
        return View(ticket);
    }

    [HttpPost("/App/tickets/{id:int}/reply")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reply(int id, string body)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            var author = User.FindFirst(Crm.Infrastructure.Identity.CrmClaimTypes.FullName)?.Value ?? "کارشناس";
            try
            {
                await _support.ReplyAsync(id, body, author, isFromCustomer: false);
                TempData["Success"] = "پاسخ ثبت شد.";
            }
            catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("/App/tickets/{id:int}/status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, TicketStatus status)
    {
        try
        {
            await _support.SetStatusAsync(id, status);
            TempData["Success"] = "وضعیت تیکت بروزرسانی شد.";
        }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("/App/tickets/{id:int}/assign")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(int id, int userId)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is not null)
        {
            ticket.AssignedUserId = userId;
            if (ticket.Status == TicketStatus.Open)
                ticket.Status = TicketStatus.InProgress;
            await _db.SaveChangesAsync();
            TempData["Success"] = "تیکت ارجاع شد.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("/App/tickets/sla")]
    public async Task<IActionResult> Sla()
    {
        await _support.EnsureSlaPoliciesAsync();
        var policies = await _db.SlaPolicies.OrderByDescending(p => p.Priority).ToListAsync();
        ViewData["Title"] = "تنظیمات SLA";
        return View(policies);
    }

    [HttpPost("/App/tickets/sla")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSla(Dictionary<int, int> hours)
    {
        var policies = await _db.SlaPolicies.ToListAsync();
        foreach (var policy in policies)
        {
            if (hours.TryGetValue((int)policy.Priority, out var h) && h > 0)
                policy.ResponseHours = h;
        }
        await _db.SaveChangesAsync();
        TempData["Success"] = "SLA بروزرسانی شد.";
        return RedirectToAction(nameof(Sla));
    }

    private async Task FillFormListsAsync()
    {
        var contactsModule = await _db.Modules.AsNoTracking().FirstOrDefaultAsync(m => m.Name == "contacts");
        ViewBag.Contacts = contactsModule is null
            ? new Dictionary<int, string>()
            : await _db.Records.AsNoTracking()
                .Where(r => r.ModuleId == contactsModule.Id)
                .OrderByDescending(r => r.Id).Take(300)
                .ToDictionaryAsync(r => r.Id, r => r.Title);

        ViewBag.Contracts = await _db.ServiceContracts.AsNoTracking()
            .Where(c => c.IsActive)
            .ToDictionaryAsync(c => c.Id, c => c.Name);
    }
}
