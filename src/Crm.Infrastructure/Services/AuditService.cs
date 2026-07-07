using System.Text.Json;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Microsoft.AspNetCore.Http;

namespace Crm.Infrastructure.Services;

public class AuditService
{
    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IHttpContextAccessor _http;

    public AuditService(CrmDbContext db, ITenantContext tenant, IHttpContextAccessor http)
    {
        _db = db;
        _tenant = tenant;
        _http = http;
    }

    public void Log(string moduleName, int recordId, string action, object? changes = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = _tenant.TenantId ?? 0,
            UserId = _tenant.UserId,
            ModuleName = moduleName,
            RecordId = recordId,
            Action = action,
            Changes = changes is null ? "{}" : JsonSerializer.Serialize(changes),
            AtUtc = DateTime.UtcNow,
            Ip = _http.HttpContext?.Connection.RemoteIpAddress?.ToString()
        });
    }
}
