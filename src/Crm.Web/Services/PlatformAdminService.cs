using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;
using Crm.Web.Models.Admin;

namespace Crm.Web.Services;

public class PlatformAdminService
{
    public const string GiftNotePrefix = "[هدیه]";

    private readonly CrmDbContext _db;
    private readonly UserManager<CrmUser> _userManager;

    public PlatformAdminService(CrmDbContext db, UserManager<CrmUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public static bool IsGiftSubscription(Subscription subscription) =>
        subscription.Note?.StartsWith(GiftNotePrefix, StringComparison.Ordinal) == true;

    public async Task<PlatformDashboardStats> GetDashboardStatsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var last30 = now.AddDays(-30);

        var tenants = await _db.Tenants.AsNoTracking().ToListAsync(ct);
        var payments = await _db.SubscriptionPayments.AsNoTracking().ToListAsync(ct);
        var activeSubs = await _db.Subscriptions.AsNoTracking()
            .CountAsync(s => s.Status == SubscriptionStatus.Active && s.EndsAtUtc > now, ct);

        return new PlatformDashboardStats
        {
            TotalTenants = tenants.Count,
            ActiveTenants = tenants.Count(t => t.Status == TenantStatus.Active),
            TrialTenants = tenants.Count(t => t.Status == TenantStatus.Trial),
            TotalRevenue = payments.Sum(p => p.Amount),
            RevenueLast30Days = payments.Where(p => p.PaidAtUtc >= last30).Sum(p => p.Amount),
            ActiveSubscriptions = activeSubs
        };
    }

    public async Task<List<TenantListItem>> GetTenantsAsync(string? q, TenantStatus? status, int take = 200, CancellationToken ct = default)
    {
        var query = _db.Tenants.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(t => EF.Functions.ILike(t.Name, $"%{term}%") || EF.Functions.ILike(t.Slug, $"%{term}%"));
        }

        if (status is not null)
            query = query.Where(t => t.Status == status);

        var tenants = await query.OrderByDescending(t => t.CreatedAtUtc).Take(take).ToListAsync(ct);
        var ids = tenants.Select(t => t.Id).ToList();

        var userCounts = await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.TenantId))
            .GroupBy(u => u.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

        var recordCounts = await _db.Records.IgnoreQueryFilters().AsNoTracking()
            .Where(r => ids.Contains(r.TenantId) && !r.IsDeleted)
            .GroupBy(r => r.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

        return tenants.Select(t => new TenantListItem
        {
            Tenant = t,
            UserCount = userCounts.GetValueOrDefault(t.Id),
            RecordCount = recordCounts.GetValueOrDefault(t.Id)
        }).ToList();
    }

    public async Task<TenantDetailsViewModel?> GetTenantDetailsAsync(int id, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null)
            return null;

        return new TenantDetailsViewModel
        {
            Tenant = tenant,
            UserCount = await _db.Users.CountAsync(u => u.TenantId == id, ct),
            RecordCount = await _db.Records.IgnoreQueryFilters().CountAsync(r => r.TenantId == id && !r.IsDeleted, ct),
            StorageBytes = await _db.Attachments.IgnoreQueryFilters()
                .Where(a => a.TenantId == id && !a.IsDeleted)
                .SumAsync(a => (long?)a.SizeBytes, ct) ?? 0,
            Users = await _db.Users.AsNoTracking().Where(u => u.TenantId == id).OrderBy(u => u.Id).ToListAsync(ct),
            Subscriptions = await _db.Subscriptions.AsNoTracking()
                .IgnoreQueryFilters()
                .Include(s => s.Plan)
                .Include(s => s.Payments)
                .Where(s => s.TenantId == id)
                .OrderByDescending(s => s.EndsAtUtc)
                .ToListAsync(ct)
        };
    }

    public async Task<(bool Ok, string? Error)> SetTenantStatusAsync(int id, TenantStatus status, string? actor, string? ip, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null)
            return (false, "مشتری یافت نشد.");

        tenant.Status = status;
        AddAudit(tenant.Id, "tenants", tenant.Id, "SetStatus", $"{{\"status\":\"{status}\",\"by\":\"{actor}\"}}", ip);
        await _db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(Subscription? Subscription, string? Error)> CreateSubscriptionAsync(
        SubscriptionCreateModel model, string? actor, string? ip, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == model.TenantId, ct);
        var plan = await _db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == model.PlanId, ct);
        if (tenant is null || plan is null)
            return (null, "مشتری یا پلن یافت نشد.");

        var now = DateTime.UtcNow;
        var lastActiveEnd = await _db.Subscriptions
            .Where(s => s.TenantId == tenant.Id && s.Status == SubscriptionStatus.Active && s.EndsAtUtc > now)
            .MaxAsync(s => (DateTime?)s.EndsAtUtc, ct);

        var start = lastActiveEnd ?? now;
        var subscription = new Subscription
        {
            TenantId = tenant.Id,
            PlanId = plan.Id,
            StartsAtUtc = start,
            EndsAtUtc = start.AddMonths(model.Months),
            Status = SubscriptionStatus.Active,
            Amount = model.Amount,
            Note = model.Note?.Trim(),
            CreatedAtUtc = now
        };
        _db.Subscriptions.Add(subscription);

        if (model.RecordPayment && model.Amount > 0)
        {
            subscription.Payments.Add(new SubscriptionPayment
            {
                Amount = model.Amount,
                PaidAtUtc = now,
                Method = "manual",
                Reference = model.PaymentReference?.Trim(),
                Note = model.Note?.Trim()
            });
        }

        tenant.Status = TenantStatus.Active;
        AddAudit(tenant.Id, "subscriptions", tenant.Id, "Subscribe",
            $"{{\"plan\":\"{plan.Name}\",\"months\":{model.Months},\"amount\":{model.Amount},\"by\":\"{actor}\"}}", ip);
        await _db.SaveChangesAsync(ct);
        return (subscription, null);
    }

    public async Task<(Subscription? Subscription, string? Error)> CreateGiftSubscriptionAsync(
        GiftSubscriptionModel model, string? actor, string? ip, CancellationToken ct = default)
    {
        var reason = model.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
            return (null, "دلیل اشتراک هدیه الزامی است.");

        return await CreateSubscriptionAsync(new SubscriptionCreateModel
        {
            TenantId = model.TenantId,
            PlanId = model.PlanId,
            Months = model.Months,
            Amount = 0,
            RecordPayment = false,
            Note = $"{GiftNotePrefix} {reason}"
        }, actor, ip, ct);
    }

    public async Task<(bool Ok, string? Error)> CancelSubscriptionAsync(int subscriptionId, string? actor, string? ip, CancellationToken ct = default)
    {
        var sub = await _db.Subscriptions
            .IgnoreQueryFilters()
            .Include(s => s.Tenant)
            .FirstOrDefaultAsync(s => s.Id == subscriptionId, ct);
        if (sub is null)
            return (false, "اشتراک یافت نشد.");

        sub.Status = SubscriptionStatus.Canceled;
        AddAudit(sub.TenantId, "subscriptions", sub.Id, "Cancel", $"{{\"by\":\"{actor}\"}}", ip);
        await _db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<List<PlatformTransactionRow>> GetTransactionsAsync(string? q, int take = 300, CancellationToken ct = default)
    {
        var tenantQuery = _db.Tenants.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            tenantQuery = tenantQuery.Where(t => EF.Functions.ILike(t.Name, $"%{term}%") || EF.Functions.ILike(t.Slug, $"%{term}%"));
        }

        var tenantMap = await tenantQuery.ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var tenantIds = tenantMap.Keys.ToList();

        var subscriptionPayments = await _db.SubscriptionPayments.AsNoTracking()
            .Include(p => p.Subscription)
            .Where(p => tenantIds.Contains(p.Subscription.TenantId))
            .OrderByDescending(p => p.PaidAtUtc)
            .Take(take)
            .ToListAsync(ct);

        var gatewayPayments = await _db.PaymentTransactions.IgnoreQueryFilters().AsNoTracking()
            .Where(p => tenantIds.Contains(p.TenantId))
            .OrderByDescending(p => p.PaidAtUtc ?? DateTime.MinValue)
            .Take(take)
            .ToListAsync(ct);

        var rows = subscriptionPayments.Select(p => new PlatformTransactionRow
        {
            Source = "subscription",
            Id = p.Id,
            TenantId = p.Subscription.TenantId,
            TenantName = tenantMap.GetValueOrDefault(p.Subscription.TenantId, "—"),
            Amount = p.Amount,
            AtUtc = p.PaidAtUtc,
            Method = p.Method,
            Status = "paid",
            Reference = p.Reference,
            Description = p.Note
        }).ToList();

        rows.AddRange(gatewayPayments.Select(p => new PlatformTransactionRow
        {
            Source = "gateway",
            Id = p.Id,
            TenantId = p.TenantId,
            TenantName = tenantMap.GetValueOrDefault(p.TenantId, "—"),
            Amount = p.Amount,
            AtUtc = p.PaidAtUtc ?? p.CreatedAtUtc,
            Method = "gateway",
            Status = p.Status switch
            {
                PaymentTransactionStatus.Paid => "paid",
                PaymentTransactionStatus.Pending => "pending",
                PaymentTransactionStatus.Failed => "failed",
                _ => p.Status.ToString().ToLowerInvariant()
            },
            Reference = p.Reference ?? p.Token,
            Description = p.Description ?? p.Kind.ToString()
        }));

        return rows.OrderByDescending(r => r.AtUtc).Take(take).ToList();
    }

    public async Task<(bool Ok, string? Error)> DeleteTenantAsync(int tenantId, string confirmSlug, string? actor, string? ip, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null)
            return (false, "مشتری یافت نشد.");

        if (!string.Equals(tenant.Slug, confirmSlug.Trim(), StringComparison.OrdinalIgnoreCase))
            return (false, "برای حذف، باید شناسه (slug) مشتری را دقیقاً وارد کنید.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            await PurgeTenantDataAsync(tenantId, ct);

            var users = await _db.Users.Where(u => u.TenantId == tenantId).ToListAsync(ct);
            foreach (var user in users)
            {
                var result = await _userManager.DeleteAsync(user);
                if (!result.Succeeded)
                    throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
            }

            _db.Tenants.Remove(tenant);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return (true, null);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            return (false, $"حذف مشتری انجام نشد: {ex.Message}");
        }
    }

    private async Task PurgeTenantDataAsync(int tenantId, CancellationToken ct)
    {
        await _db.WorkflowActions.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.WorkflowLogs.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.WorkflowRules.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);

        await _db.SurveyResponses.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.SurveyQuestions.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.Surveys.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);

        await _db.TicketMessages.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.Tickets.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);

        await _db.CommissionEntries.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.PaymentRecords.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.Installments.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.SalesDocumentLines.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.SalesDocuments.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);

        await _db.VendorPayments.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.PurchaseOrderLines.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.PurchaseOrders.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);

        await _db.ProjectTasks.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.ProjectPhases.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.Projects.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);

        await _db.CampaignMembers.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.Campaigns.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);

        await _db.WebForms.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.MessageTemplates.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);

        await _db.PicklistValues.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.Records.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.Fields.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.Relations.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.Modules.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);

        await _db.ProfileFieldPermissions.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.ProfileModulePermissions.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.UserGroupMembers.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.UserGroups.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.Profiles.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.CrmRoles.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && x.ParentRoleId != null).ExecuteDeleteAsync(ct);
        await _db.CrmRoles.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.SharingRules.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);

        await _db.PriceBookEntries.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.PriceBooks.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.Products.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.CommissionRules.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);

        await _db.SlaPolicies.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.ServiceContracts.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.Warranties.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.KbArticles.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.PortalUsers.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.LeaveRequests.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.Vendors.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);

        await _db.ApiKeys.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.PaymentTransactions.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.DashboardWidgets.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.Reports.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);

        await _db.TagLinks.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.Tags.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.Notes.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.Attachments.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.Notifications.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.SavedListViews.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);

        await _db.AuditLogs.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await _db.Subscriptions.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
    }

    private void AddAudit(int tenantId, string module, int recordId, string action, string changes, string? ip)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            ModuleName = module,
            RecordId = recordId,
            Action = action,
            Changes = changes,
            AtUtc = DateTime.UtcNow,
            Ip = ip
        });
    }
}
