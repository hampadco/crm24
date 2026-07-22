using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Infrastructure.Services;

/// <summary>دسترسی به متادیتای ماژول‌ها و فیلدها (با کش per-tenant).</summary>
public class MetadataService
{
    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IMemoryCache _cache;

    public MetadataService(CrmDbContext db, ITenantContext tenant, IMemoryCache cache)
    {
        _db = db;
        _tenant = tenant;
        _cache = cache;
    }

    public async Task<IReadOnlyList<ModuleDef>> GetActiveModulesAsync()
    {
        var cacheKey = ModulesCacheKey();
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<ModuleDef>? cached) && cached is not null)
            return cached;

        var modules = await _db.Modules
            .AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Id)
            .ToListAsync();

        _cache.Set(cacheKey, (IReadOnlyList<ModuleDef>)modules, TimeSpan.FromMinutes(5));
        return modules;
    }

    public async Task<ModuleDef?> GetModuleByNameAsync(string name)
    {
        var modules = await GetActiveModulesAsync();
        return modules.FirstOrDefault(m => m.Name == name);
    }

    public async Task<IReadOnlyList<FieldDef>> GetFieldsAsync(int moduleId)
    {
        var cacheKey = $"fields:{_tenant.TenantId}:{moduleId}";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<FieldDef>? cached) && cached is not null)
            return cached;

        var fields = await _db.Fields
            .AsNoTracking()
            .Include(f => f.PicklistValues.Where(p => p.IsActive && !p.IsDeleted))
            .Where(f => f.ModuleId == moduleId)
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.Id)
            .ToListAsync();

        _cache.Set(cacheKey, (IReadOnlyList<FieldDef>)fields, TimeSpan.FromMinutes(5));
        return fields;
    }

    public void InvalidateFieldCache(int moduleId) =>
        _cache.Remove($"fields:{_tenant.TenantId}:{moduleId}");

    public void InvalidateModulesCache() =>
        _cache.Remove(ModulesCacheKey());

    private string ModulesCacheKey() => $"modules:{_tenant.TenantId}";
}
