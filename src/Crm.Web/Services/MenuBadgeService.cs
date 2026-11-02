using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Crm.Web.Services;

public sealed class MenuBadgeCounts
{
    public int Approvals { get; init; }
    public int Tasks { get; init; }
    public int Tickets { get; init; }
    public int ProjectTasks { get; init; }

    public int Work => Tasks + ProjectTasks;
}

public class MenuBadgeService
{
    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IMemoryCache _cache;

    public MenuBadgeService(CrmDbContext db, ITenantContext tenant, IMemoryCache cache)
    {
        _db = db;
        _tenant = tenant;
        _cache = cache;
    }

    public Task<MenuBadgeCounts> GetAsync()
    {
        if (_tenant.TenantId is not int tenantId || _tenant.UserId is not int userId)
            return Task.FromResult(new MenuBadgeCounts());

        var key = $"menu-badges:{tenantId}:{userId}:{_tenant.RoleId}:{_tenant.IsTenantAdmin}";
        return _cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
            return await CountAsync(userId);
        })!;
    }

    private async Task<MenuBadgeCounts> CountAsync(int userId)
    {
        var approvals = _db.ApprovalRequests.AsNoTracking()
            .Where(a => a.Status == ApprovalStatus.Pending);
        if (!_tenant.IsTenantAdmin)
        {
            if (_tenant.RoleId is int roleId)
            {
                var ruleIds = _db.ApprovalRules.AsNoTracking()
                    .Where(r => r.ApproverRoleId == roleId)
                    .Select(r => r.Id);
                approvals = approvals.Where(a => ruleIds.Contains(a.RuleId));
            }
            else
            {
                approvals = approvals.Where(_ => false);
            }
        }

        var approvalCount = await approvals.CountAsync();
        var taskCount = await OwnedCount("tasks", userId, "todo", "inprogress");
        var ticketCount = await OwnedCount("tickets", userId, "open", "pending");
        var projectTaskCount = await OwnedCount("project_tasks", userId, "todo", "doing");

        return new MenuBadgeCounts
        {
            Approvals = approvalCount,
            Tasks = taskCount,
            Tickets = ticketCount,
            ProjectTasks = projectTaskCount
        };
    }

    private Task<int> OwnedCount(string module, int userId, string a, string b)
    {
        var ja = "{\"status\":\"" + a + "\"}";
        var jb = "{\"status\":\"" + b + "\"}";
        return _db.Records.AsNoTracking().CountAsync(r =>
            r.Module.Name == module &&
            r.OwnerUserId == userId &&
            (EF.Functions.JsonContains(r.CustomData, ja) || EF.Functions.JsonContains(r.CustomData, jb)));
    }
}
