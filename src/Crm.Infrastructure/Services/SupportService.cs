using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Infrastructure.Services;

/// <summary>
/// تیکتینگ: شماره‌گذاری، SLA per-اولویت، اتصال قرارداد خدمات و Escalation.
/// </summary>
public class SupportService
{
    private static readonly (TicketPriority Priority, int Hours)[] DefaultSla =
    [
        (TicketPriority.Urgent, 2),
        (TicketPriority.High, 4),
        (TicketPriority.Normal, 8),
        (TicketPriority.Low, 24)
    ];

    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly AuditService _audit;

    public SupportService(CrmDbContext db, ITenantContext tenant, AuditService audit)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
    }

    public async Task EnsureSlaPoliciesAsync()
    {
        if (await _db.SlaPolicies.AnyAsync())
            return;

        foreach (var (priority, hours) in DefaultSla)
            _db.SlaPolicies.Add(new SlaPolicy { Priority = priority, ResponseHours = hours });
        await _db.SaveChangesAsync();
    }

    public async Task<Ticket> CreateTicketAsync(
        string subject, string body, TicketPriority priority, string? category,
        string authorName, bool isFromCustomer,
        int? portalUserId = null, int? contactRecordId = null, int? serviceContractId = null)
    {
        await EnsureSlaPoliciesAsync();

        // بررسی اعتبار قرارداد خدمات
        if (serviceContractId is int contractId)
        {
            var contract = await _db.ServiceContracts.FirstOrDefaultAsync(c => c.Id == contractId)
                ?? throw new InvalidOperationException("قرارداد خدمات یافت نشد.");

            var now = DateTime.UtcNow;
            if (!contract.IsActive || now < contract.StartUtc || now > contract.EndUtc)
                throw new InvalidOperationException("قرارداد خدمات معتبر نیست یا منقضی شده است.");
            if (contract.MaxTickets > 0 && contract.TicketsUsed >= contract.MaxTickets)
                throw new InvalidOperationException("سقف تیکت قرارداد پر شده است.");

            contract.TicketsUsed++;
        }

        var sla = await _db.SlaPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.Priority == priority);
        var maxNumber = await _db.Tickets.IgnoreQueryFilters()
            .Where(t => t.TenantId == _tenant.TenantId)
            .MaxAsync(t => (int?)t.Number);

        var ticket = new Ticket
        {
            Number = (maxNumber ?? 100) + 1,
            Subject = subject.Trim(),
            Category = category?.Trim(),
            Priority = priority,
            PortalUserId = portalUserId,
            ContactRecordId = contactRecordId,
            ServiceContractId = serviceContractId,
            DueAtUtc = sla is not null ? DateTime.UtcNow.AddHours(sla.ResponseHours) : null
        };

        ticket.Messages.Add(new TicketMessage
        {
            Body = body.Trim(),
            IsFromCustomer = isFromCustomer,
            AuthorName = authorName
        });

        _db.Tickets.Add(ticket);
        await _db.SaveChangesAsync();

        _audit.Log("tickets", ticket.Id, "Create", new { ticket.Number, priority = priority.ToString() });
        await _db.SaveChangesAsync();

        return ticket;
    }

    public async Task ReplyAsync(int ticketId, string body, string authorName, bool isFromCustomer)
    {
        var ticket = await _db.Tickets.Include(t => t.Messages).FirstOrDefaultAsync(t => t.Id == ticketId)
            ?? throw new InvalidOperationException("تیکت یافت نشد.");

        if (ticket.Status == TicketStatus.Closed)
            throw new InvalidOperationException("تیکت بسته شده است.");

        ticket.Messages.Add(new TicketMessage
        {
            TenantId = ticket.TenantId,
            Body = body.Trim(),
            IsFromCustomer = isFromCustomer,
            AuthorName = authorName
        });

        if (isFromCustomer)
        {
            ticket.Status = TicketStatus.Open;
        }
        else
        {
            ticket.FirstResponseAtUtc ??= DateTime.UtcNow;
            ticket.Status = TicketStatus.WaitingCustomer;
        }

        await _db.SaveChangesAsync();
    }

    public async Task SetStatusAsync(int ticketId, TicketStatus status)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId)
            ?? throw new InvalidOperationException("تیکت یافت نشد.");

        ticket.Status = status;
        if (status is TicketStatus.Closed or TicketStatus.Resolved)
            ticket.ClosedAtUtc = DateTime.UtcNow;

        _audit.Log("tickets", ticket.Id, "Status", new { status = status.ToString() });
        await _db.SaveChangesAsync();
    }

    /// <summary>جاب ساعتی: تیکت‌های گذشته از مهلت SLA بدون پاسخ اول → اعلان و Escalation.</summary>
    public async Task CheckSlaBreachesAsync()
    {
        var now = DateTime.UtcNow;
        var breached = await _db.Tickets
            .IgnoreQueryFilters()
            .Where(t => !t.IsDeleted &&
                        t.Status != TicketStatus.Closed && t.Status != TicketStatus.Resolved &&
                        t.FirstResponseAtUtc == null &&
                        t.EscalatedAtUtc == null &&
                        t.DueAtUtc != null && t.DueAtUtc < now)
            .ToListAsync();

        foreach (var ticket in breached)
        {
            ticket.EscalatedAtUtc = now;

            var targetUserId = ticket.AssignedUserId;
            if (targetUserId is null)
            {
                targetUserId = await _db.Users.AsNoTracking()
                    .Where(u => u.TenantId == ticket.TenantId && u.IsTenantAdmin)
                    .Select(u => (int?)u.Id)
                    .FirstOrDefaultAsync();
            }

            if (targetUserId is int userId)
            {
                _db.Notifications.Add(new Notification
                {
                    TenantId = ticket.TenantId,
                    UserId = userId,
                    Title = "نقض SLA",
                    Body = $"تیکت #{ticket.Number} «{ticket.Subject}» از مهلت پاسخ عبور کرده است.",
                    LinkUrl = $"/App/tickets/{ticket.Id}"
                });
            }
        }

        await _db.SaveChangesAsync();
    }
}
