using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Microsoft.Extensions.Caching.Memory;

namespace Crm.Infrastructure.Services;

/// <summary>
/// مجوز ماژول روی نقش: مهاجرت از پروفایل، سقف والد، و همگام‌سازی زیردستان.
/// </summary>
public class RolePermissionService
{
    private readonly CrmDbContext _db;
    private readonly IMemoryCache _cache;

    public RolePermissionService(CrmDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    /// <summary>یک‌بار برای Tenant: کپی ProfileModulePermission → RoleModulePermission.</summary>
    public async Task EnsureMigratedAsync(int tenantId)
    {
        var cacheKey = $"role-perms-migrated:{tenantId}";
        if (_cache.TryGetValue(cacheKey, out bool ok) && ok)
            return;

        var roles = await _db.CrmRoles.Where(r => r.TenantId == tenantId).ToListAsync();
        if (roles.Count == 0)
        {
            _cache.Set(cacheKey, true, TimeSpan.FromHours(12));
            return;
        }

        var roleIds = roles.Select(r => r.Id).ToList();
        var existingRolePerms = await _db.RoleModulePermissions
            .Where(p => p.TenantId == tenantId && roleIds.Contains(p.RoleId))
            .Select(p => p.RoleId)
            .Distinct()
            .ToListAsync();
        var rolesNeeding = roles.Where(r => !existingRolePerms.Contains(r.Id)).ToList();
        if (rolesNeeding.Count == 0)
        {
            _cache.Set(cacheKey, true, TimeSpan.FromHours(12));
            return;
        }

        var adminProfileId = await _db.Profiles.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.IsAdmin)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync();
        var userProfileId = await _db.Profiles.AsNoTracking()
            .Where(p => p.TenantId == tenantId && !p.IsAdmin)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync();

        var profilePerms = await _db.ProfileModulePermissions.AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .ToListAsync();
        var byProfile = profilePerms.GroupBy(p => p.ProfileId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var users = await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.CrmRoleId != null)
            .Select(u => new { u.CrmRoleId, u.ProfileId })
            .ToListAsync();

        var modules = await _db.Modules.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.IsActive)
            .Select(m => m.Id)
            .ToListAsync();

        foreach (var role in rolesNeeding)
        {
            if (role.IsAdmin || role.ParentRoleId is null && role.Name.Contains("مدیر"))
                role.IsAdmin = role.IsAdmin || role.ParentRoleId is null;

            List<ProfileModulePermission>? source = null;
            var userProfile = users.FirstOrDefault(u => u.CrmRoleId == role.Id && u.ProfileId != null)?.ProfileId;
            if (userProfile is int pid && byProfile.TryGetValue(pid, out var fromUser))
                source = fromUser;
            else if (role.IsAdmin && adminProfileId is int ap && byProfile.TryGetValue(ap, out var fromAdmin))
                source = fromAdmin;
            else if (userProfileId is int up && byProfile.TryGetValue(up, out var fromStd))
                source = fromStd;

            if (source is { Count: > 0 })
            {
                foreach (var p in source)
                {
                    _db.RoleModulePermissions.Add(new RoleModulePermission
                    {
                        TenantId = tenantId,
                        RoleId = role.Id,
                        ModuleId = p.ModuleId,
                        CanView = p.CanView,
                        CanCreate = p.CanCreate,
                        CanEdit = p.CanEdit,
                        CanDelete = p.CanDelete
                    });
                }
            }
            else
            {
                foreach (var moduleId in modules)
                {
                    _db.RoleModulePermissions.Add(new RoleModulePermission
                    {
                        TenantId = tenantId,
                        RoleId = role.Id,
                        ModuleId = moduleId,
                        CanView = true,
                        CanCreate = true,
                        CanEdit = true,
                        CanDelete = role.IsAdmin || role.ParentRoleId is null
                    });
                }
            }
        }

        // Root بدون والد را IsAdmin کن اگر فقط یکی است و هنوز نیست
        var roots = roles.Where(r => r.ParentRoleId is null).ToList();
        if (roots.Count == 1 && !roots[0].IsAdmin)
            roots[0].IsAdmin = true;

        await _db.SaveChangesAsync();
        await ClampAllDescendantsAsync(tenantId);
        _cache.Set(cacheKey, true, TimeSpan.FromHours(12));
    }

    public async Task EnsureModulePermissionRowsAsync(int tenantId, int moduleId)
    {
        var roles = await _db.CrmRoles.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .Select(r => new { r.Id, r.IsAdmin, r.ParentRoleId })
            .ToListAsync();
        if (roles.Count == 0) return;

        var existing = await _db.RoleModulePermissions
            .Where(p => p.TenantId == tenantId && p.ModuleId == moduleId)
            .Select(p => p.RoleId)
            .ToListAsync();
        var existingSet = existing.ToHashSet();

        foreach (var role in roles)
        {
            if (existingSet.Contains(role.Id)) continue;
            var full = role.IsAdmin || role.ParentRoleId is null;
            _db.RoleModulePermissions.Add(new RoleModulePermission
            {
                TenantId = tenantId,
                RoleId = role.Id,
                ModuleId = moduleId,
                CanView = true,
                CanCreate = true,
                CanEdit = true,
                CanDelete = full
            });
        }

        await _db.SaveChangesAsync();
        await ClampAllDescendantsAsync(tenantId);
    }

    /// <summary>سقف والد روی ماتریس ارسالی اعمال می‌شود.</summary>
    public async Task<Dictionary<int, RoleModulePermission>> ClampToParentAsync(
        int tenantId, int? parentRoleId, IEnumerable<(int ModuleId, bool V, bool C, bool E, bool D)> proposed)
    {
        Dictionary<int, RoleModulePermission>? parentMap = null;
        if (parentRoleId is int pid)
        {
            var parentRole = await _db.CrmRoles.AsNoTracking()
                .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == pid);
            if (parentRole is not null && !parentRole.IsAdmin)
            {
                parentMap = await _db.RoleModulePermissions.AsNoTracking()
                    .Where(p => p.RoleId == pid)
                    .ToDictionaryAsync(p => p.ModuleId);
            }
        }

        var result = new Dictionary<int, RoleModulePermission>();
        foreach (var (moduleId, v, c, e, d) in proposed)
        {
            var canV = v;
            var canC = c;
            var canE = e;
            var canD = d;
            if (parentMap is not null)
            {
                if (!parentMap.TryGetValue(moduleId, out var parent))
                {
                    canV = canC = canE = canD = false;
                }
                else
                {
                    canV &= parent.CanView;
                    canC &= parent.CanCreate;
                    canE &= parent.CanEdit;
                    canD &= parent.CanDelete;
                }
            }

            if (canC || canE || canD)
                canV = true;

            result[moduleId] = new RoleModulePermission
            {
                TenantId = tenantId,
                ModuleId = moduleId,
                CanView = canV,
                CanCreate = canC,
                CanEdit = canE,
                CanDelete = canD
            };
        }

        return result;
    }

    public async Task SaveRolePermissionsAsync(
        int tenantId, int roleId, IReadOnlyDictionary<int, RoleModulePermission> clamped)
    {
        var existing = await _db.RoleModulePermissions
            .Where(p => p.RoleId == roleId)
            .ToListAsync();
        var byModule = existing.ToDictionary(p => p.ModuleId);

        foreach (var (moduleId, src) in clamped)
        {
            if (!byModule.TryGetValue(moduleId, out var row))
            {
                row = new RoleModulePermission
                {
                    TenantId = tenantId,
                    RoleId = roleId,
                    ModuleId = moduleId
                };
                _db.RoleModulePermissions.Add(row);
            }

            row.CanView = src.CanView;
            row.CanCreate = src.CanCreate;
            row.CanEdit = src.CanEdit;
            row.CanDelete = src.CanDelete;
        }

        await _db.SaveChangesAsync();
        await ClampDescendantsOfAsync(tenantId, roleId);
    }

    public async Task ClampAllDescendantsAsync(int tenantId)
    {
        var roles = await _db.CrmRoles.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .Select(r => new { r.Id, r.ParentRoleId })
            .ToListAsync();
        var roots = roles.Where(r => r.ParentRoleId is null).Select(r => r.Id).ToList();
        foreach (var rootId in roots)
            await ClampDescendantsOfAsync(tenantId, rootId);
    }

    public async Task ClampDescendantsOfAsync(int tenantId, int roleId)
    {
        var roles = await _db.CrmRoles
            .Where(r => r.TenantId == tenantId)
            .Select(r => new { r.Id, r.ParentRoleId, r.IsAdmin })
            .ToListAsync();

        var childrenByParent = roles
            .Where(r => r.ParentRoleId != null)
            .GroupBy(r => r.ParentRoleId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());

        var allPerms = await _db.RoleModulePermissions
            .Where(p => p.TenantId == tenantId)
            .ToListAsync();
        var permsByRole = allPerms.GroupBy(p => p.RoleId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(p => p.ModuleId));

        var queue = new Queue<int>();
        queue.Enqueue(roleId);
        while (queue.Count > 0)
        {
            var parentId = queue.Dequeue();
            if (!childrenByParent.TryGetValue(parentId, out var kids))
                continue;

            var parentIsAdmin = roles.FirstOrDefault(r => r.Id == parentId)?.IsAdmin == true;
            permsByRole.TryGetValue(parentId, out var parentPerms);

            foreach (var childId in kids)
            {
                if (!permsByRole.TryGetValue(childId, out var childPerms))
                {
                    queue.Enqueue(childId);
                    continue;
                }

                foreach (var (moduleId, childRow) in childPerms)
                {
                    if (parentIsAdmin)
                        continue;

                    if (parentPerms is null || !parentPerms.TryGetValue(moduleId, out var parentRow))
                    {
                        childRow.CanView = childRow.CanCreate = childRow.CanEdit = childRow.CanDelete = false;
                        continue;
                    }

                    childRow.CanView &= parentRow.CanView;
                    childRow.CanCreate &= parentRow.CanCreate;
                    childRow.CanEdit &= parentRow.CanEdit;
                    childRow.CanDelete &= parentRow.CanDelete;
                    if (childRow.CanCreate || childRow.CanEdit || childRow.CanDelete)
                        childRow.CanView = true;
                }

                queue.Enqueue(childId);
            }
        }

        await _db.SaveChangesAsync();
    }
}
