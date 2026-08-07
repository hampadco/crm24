using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Infrastructure.Security;

/// <summary>
/// دسترسی: نقش درختی (دید زیردستان) + مجوز ماژول روی نقش + Sharing Rule.
/// </summary>
public class RecordAccessService
{
    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;

    public RecordAccessService(CrmDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<bool> CanViewModuleAsync(int moduleId) => await HasModulePermissionAsync(moduleId, p => p.CanView);
    public async Task<bool> CanCreateAsync(int moduleId) => await HasModulePermissionAsync(moduleId, p => p.CanCreate);
    public async Task<bool> CanEditAsync(int moduleId) => await HasModulePermissionAsync(moduleId, p => p.CanEdit);
    public async Task<bool> CanDeleteAsync(int moduleId) => await HasModulePermissionAsync(moduleId, p => p.CanDelete);

    private async Task<bool> HasModulePermissionAsync(int moduleId, Func<RoleModulePermission, bool> check)
    {
        if (_tenant.IsTenantAdmin)
            return true;

        if (_tenant.RoleId is not int roleId)
            return false;

        var role = await _db.CrmRoles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == roleId);
        if (role is null)
            return false;
        if (role.IsAdmin)
            return true;

        var perm = await _db.RoleModulePermissions
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.RoleId == roleId && p.ModuleId == moduleId);

        return perm is not null && check(perm);
    }

    public async Task<IQueryable<DynamicRecord>> ApplyVisibilityAsync(IQueryable<DynamicRecord> query, int moduleId)
    {
        if (_tenant.IsTenantAdmin)
            return query;

        var sharing = await _db.SharingRules
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ModuleId == moduleId);

        var level = sharing?.DefaultLevel ?? SharingLevel.Private;
        if (level != SharingLevel.Private)
            return query;

        var userId = _tenant.UserId ?? 0;
        var visibleUserIds = await GetSelfAndSubordinateUserIdsAsync(userId);

        return query.Where(r =>
            (r.OwnerUserId != null && visibleUserIds.Contains(r.OwnerUserId.Value)) ||
            r.CreatedByUserId == userId);
    }

    public async Task<bool> CanModifyRecordAsync(DynamicRecord record)
    {
        if (_tenant.IsTenantAdmin)
            return true;

        var sharing = await _db.SharingRules
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ModuleId == record.ModuleId);

        var level = sharing?.DefaultLevel ?? SharingLevel.Private;
        if (level is SharingLevel.PublicReadWrite or SharingLevel.PublicFull)
            return true;

        var userId = _tenant.UserId ?? 0;
        if (record.OwnerUserId == userId || record.CreatedByUserId == userId)
            return true;

        if (level == SharingLevel.PublicReadOnly)
            return false;

        var visibleUserIds = await GetSelfAndSubordinateUserIdsAsync(userId);
        return record.OwnerUserId is int owner && visibleUserIds.Contains(owner);
    }

    public async Task<Dictionary<int, FieldAccess>> GetFieldAccessMapAsync(int moduleId)
    {
        if (_tenant.IsTenantAdmin || _tenant.RoleId is not int roleId)
            return new Dictionary<int, FieldAccess>();

        var role = await _db.CrmRoles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roleId);
        if (role?.IsAdmin == true)
            return new Dictionary<int, FieldAccess>();

        var fieldIds = await _db.Fields
            .Where(f => f.ModuleId == moduleId)
            .Select(f => f.Id)
            .ToListAsync();

        return await _db.RoleFieldPermissions
            .AsNoTracking()
            .Where(p => p.RoleId == roleId && fieldIds.Contains(p.FieldId))
            .ToDictionaryAsync(p => p.FieldId, p => p.Access);
    }

    private async Task<HashSet<int>> GetSelfAndSubordinateUserIdsAsync(int userId)
    {
        var result = new HashSet<int> { userId };

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.CrmRoleId is not int roleId)
            return result;

        var roles = await _db.CrmRoles
            .AsNoTracking()
            .Select(r => new { r.Id, r.ParentRoleId })
            .ToListAsync();

        var childrenByParent = roles
            .Where(r => r.ParentRoleId is not null)
            .GroupBy(r => r.ParentRoleId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Id).ToList());

        var subordinateRoleIds = new HashSet<int>();
        var stack = new Stack<int>();
        stack.Push(roleId);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!childrenByParent.TryGetValue(current, out var children))
                continue;
            foreach (var child in children)
            {
                if (subordinateRoleIds.Add(child))
                    stack.Push(child);
            }
        }

        if (subordinateRoleIds.Count == 0)
            return result;

        var subordinateUsers = await _db.Users
            .AsNoTracking()
            .Where(u => u.TenantId == _tenant.TenantId && u.CrmRoleId != null && subordinateRoleIds.Contains(u.CrmRoleId.Value))
            .Select(u => u.Id)
            .ToListAsync();

        result.UnionWith(subordinateUsers);
        return result;
    }
}
