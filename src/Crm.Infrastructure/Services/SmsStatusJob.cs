using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Infrastructure.Services;

public class SmsStatusJob
{
    private readonly CrmDbContext _db;
    private readonly TenantContext _tenant;
    private readonly SmsService _sms;
    private readonly ILogger<SmsStatusJob> _logger;

    public SmsStatusJob(CrmDbContext db, TenantContext tenant, SmsService sms, ILogger<SmsStatusJob> logger)
    {
        _db = db;
        _tenant = tenant;
        _sms = sms;
        _logger = logger;
    }

    public async Task RefreshOpenAsync()
    {
        var since = DateTime.UtcNow.AddDays(-2);
        var rows = await _db.SmsDispatches.IgnoreQueryFilters()
            .Where(d => !d.IsDeleted
                        && d.CreatedAtUtc >= since
                        && (d.Status == "queued" || d.Status == "sending" || d.Status == "sent" || d.Status == "partial"))
            .OrderByDescending(d => d.Id)
            .Select(d => new { d.Id, d.TenantId })
            .Take(80)
            .ToListAsync();

        foreach (var group in rows.GroupBy(r => r.TenantId))
        {
            _tenant.TenantId = group.Key;
            foreach (var row in group)
            {
                try
                {
                    await _sms.RefreshAsync(row.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SMS status refresh failed for dispatch {Id}", row.Id);
                }
            }
        }
    }
}
