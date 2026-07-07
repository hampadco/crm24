using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Services;
using Crm.Web.Hubs;

namespace Crm.Web.Controllers.Api;

/// <summary>
/// REST API عمومی /api/v1 — احراز با X-Api-Key و Scope خواندن/نوشتن،
/// الگوی CRUD یکسان برای همه ماژول‌های متادیتا + وب‌سرویس VoIP.
/// </summary>
[ApiController]
[Route("api/v1")]
public class ApiV1Controller : ControllerBase
{
    private const int RateLimitPerMinute = 120;
    private static readonly ConcurrentDictionary<int, (int Count, DateTime WindowStart)> RateLimits = new();

    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;

    public ApiV1Controller(CrmDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    /// <summary>احراز کلید API؛ در موفقیت زمینه Tenant را ست می‌کند.</summary>
    private async Task<(ApiKey? Key, IActionResult? Error)> AuthenticateAsync(bool requireWrite)
    {
        var rawKey = Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(rawKey))
            return (null, Unauthorized(new { error = "X-Api-Key header is required." }));

        var apiKey = await _db.ApiKeys.IgnoreQueryFilters()
            .FirstOrDefaultAsync(k => k.Key == rawKey && k.IsActive && !k.IsDeleted);
        if (apiKey is null)
            return (null, Unauthorized(new { error = "Invalid API key." }));

        if (requireWrite && !apiKey.CanWrite)
            return (null, StatusCode(403, new { error = "This key does not have write scope." }));
        if (!requireWrite && !apiKey.CanRead)
            return (null, StatusCode(403, new { error = "This key does not have read scope." }));

        var now = DateTime.UtcNow;
        var window = RateLimits.AddOrUpdate(apiKey.Id,
            _ => (1, now),
            (_, current) => now - current.WindowStart > TimeSpan.FromMinutes(1)
                ? (1, now)
                : (current.Count + 1, current.WindowStart));
        if (window.Count > RateLimitPerMinute)
            return (null, StatusCode(429, new { error = "Rate limit exceeded." }));

        // زمینه Tenant + دسترسی کامل در سطح Tenant (Scope در همین متد کنترل شد)
        if (_tenant is TenantContext mutable)
        {
            mutable.TenantId = apiKey.TenantId;
            mutable.IsTenantAdmin = true;
        }

        apiKey.LastUsedAtUtc = now;
        apiKey.RequestCount++;
        await _db.SaveChangesAsync();

        return (apiKey, null);
    }

    private async Task<ModuleDef?> ResolveModuleAsync(string moduleName) =>
        await _db.Modules.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Name == moduleName && m.IsActive);

    private static object ToDto(DynamicRecord record) => new
    {
        id = record.Id,
        title = record.Title,
        ownerUserId = record.OwnerUserId,
        createdAtUtc = record.CreatedAtUtc,
        updatedAtUtc = record.UpdatedAtUtc,
        data = JsonSerializer.Deserialize<Dictionary<string, string?>>(record.CustomData)
    };

    [HttpGet("modules")]
    public async Task<IActionResult> Modules()
    {
        var (_, error) = await AuthenticateAsync(requireWrite: false);
        if (error is not null)
            return error;

        var modules = await _db.Modules.AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.SortOrder)
            .Select(m => new { m.Name, m.SingularLabel, m.PluralLabel })
            .ToListAsync();
        return Ok(modules);
    }

    [HttpGet("{moduleName}")]
    public async Task<IActionResult> List(string moduleName, string? search, int page = 1, int pageSize = 50)
    {
        var (_, error) = await AuthenticateAsync(requireWrite: false);
        if (error is not null)
            return error;

        var module = await ResolveModuleAsync(moduleName);
        if (module is null)
            return NotFound(new { error = "Module not found." });

        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = _db.Records.AsNoTracking().Where(r => r.ModuleId == module.Id);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(r => EF.Functions.ILike(r.Title, $"%{search.Trim()}%"));

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        return Ok(new { total, page, pageSize, items = items.Select(ToDto) });
    }

    [HttpGet("{moduleName}/{id:int}")]
    public async Task<IActionResult> Get(string moduleName, int id)
    {
        var (_, error) = await AuthenticateAsync(requireWrite: false);
        if (error is not null)
            return error;

        var module = await ResolveModuleAsync(moduleName);
        if (module is null)
            return NotFound(new { error = "Module not found." });

        var record = await _db.Records.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ModuleId == module.Id && r.Id == id);
        return record is null ? NotFound(new { error = "Record not found." }) : Ok(ToDto(record));
    }

    [HttpPost("{moduleName}")]
    public async Task<IActionResult> Create(string moduleName, [FromBody] Dictionary<string, string?> values)
    {
        var (_, error) = await AuthenticateAsync(requireWrite: true);
        if (error is not null)
            return error;

        var module = await ResolveModuleAsync(moduleName);
        if (module is null)
            return NotFound(new { error = "Module not found." });

        var records = HttpContext.RequestServices.GetRequiredService<DynamicRecordService>();
        try
        {
            var record = await records.CreateAsync(module.Id, values);
            return CreatedAtAction(nameof(Get), new { moduleName, id = record.Id }, ToDto(record));
        }
        catch (RecordValidationException ex)
        {
            return UnprocessableEntity(new { errors = ex.Errors });
        }
    }

    [HttpPut("{moduleName}/{id:int}")]
    public async Task<IActionResult> Update(string moduleName, int id, [FromBody] Dictionary<string, string?> values)
    {
        var (_, error) = await AuthenticateAsync(requireWrite: true);
        if (error is not null)
            return error;

        var module = await ResolveModuleAsync(moduleName);
        if (module is null)
            return NotFound(new { error = "Module not found." });

        var records = HttpContext.RequestServices.GetRequiredService<DynamicRecordService>();
        try
        {
            await records.UpdateAsync(module.Id, id, values);
            var record = await _db.Records.AsNoTracking().FirstAsync(r => r.Id == id);
            return Ok(ToDto(record));
        }
        catch (RecordValidationException ex)
        {
            return UnprocessableEntity(new { errors = ex.Errors });
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { error = "Record not found." });
        }
    }

    [HttpDelete("{moduleName}/{id:int}")]
    public async Task<IActionResult> Delete(string moduleName, int id)
    {
        var (_, error) = await AuthenticateAsync(requireWrite: true);
        if (error is not null)
            return error;

        var module = await ResolveModuleAsync(moduleName);
        if (module is null)
            return NotFound(new { error = "Module not found." });

        var records = HttpContext.RequestServices.GetRequiredService<DynamicRecordService>();
        try
        {
            await records.DeleteAsync(module.Id, id);
            return NoContent();
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { error = "Record not found." });
        }
    }

    public record VoipIncomingModel(string Caller, string? Called);

    /// <summary>
    /// وب‌سرویس سانترال: تماس ورودی → ثبت خودکار تماس + پاپ‌آپ SignalR
    /// و لینک به مخاطبِ هم‌شماره.
    /// </summary>
    [HttpPost("voip/incoming")]
    public async Task<IActionResult> VoipIncoming([FromBody] VoipIncomingModel model)
    {
        var (apiKey, error) = await AuthenticateAsync(requireWrite: true);
        if (error is not null)
            return error;

        if (string.IsNullOrWhiteSpace(model.Caller))
            return BadRequest(new { error = "caller is required." });

        var callsModule = await ResolveModuleAsync("calls");
        if (callsModule is null)
            return NotFound(new { error = "Calls module not found." });

        // تطبیق شماره با مخاطبین (jsonb ->> mobile)
        var contactsModule = await ResolveModuleAsync("contacts");
        DynamicRecord? contact = null;
        if (contactsModule is not null)
        {
            contact = await _db.Records
                .FromSqlInterpolated($"""
                    SELECT * FROM "Records"
                    WHERE "ModuleId" = {contactsModule.Id} AND "CustomData" ->> 'mobile' = {model.Caller.Trim()}
                    """)
                .AsNoTracking()
                .FirstOrDefaultAsync();
        }

        var records = HttpContext.RequestServices.GetRequiredService<DynamicRecordService>();
        var callName = contact is null
            ? $"تماس ورودی از {model.Caller}"
            : $"تماس ورودی از {contact.Title}";
        var call = await records.CreateAsync(callsModule.Id, new Dictionary<string, string?>
        {
            ["name"] = callName,
            ["contact"] = contact?.Id.ToString(),
            ["direction"] = "incoming",
            ["callAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm")
        });

        // پاپ‌آپ لحظه‌ای + اعلان برای کاربران Tenant
        var tenantUsers = await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == apiKey!.TenantId && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync();

        var hub = HttpContext.RequestServices.GetRequiredService<IHubContext<NotificationHub>>();
        foreach (var userId in tenantUsers)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = userId,
                Title = "تماس ورودی",
                Body = callName,
                LinkUrl = $"/App/m/calls/{call.Id}/edit"
            });
            await hub.Clients.Group($"user-{userId}").SendAsync("incomingCall", new
            {
                caller = model.Caller,
                contactId = contact?.Id,
                contactTitle = contact?.Title,
                callRecordId = call.Id
            });
        }
        await _db.SaveChangesAsync();

        return Ok(new { callRecordId = call.Id, matchedContactId = contact?.Id });
    }
}
