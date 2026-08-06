using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Services;
using Crm.Web.Areas.App.Models;

namespace Crm.Web.Areas.App.Controllers;

/// <summary>هاب مدیریت کاربر: کاربران + درخت نقش با مجوز — فقط ادمین Tenant.</summary>
public class AccessController : AppControllerBase
{
    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly MetadataService _metadata;
    private readonly TenantQuotaService _quota;
    private readonly RolePermissionService _rolePerms;

    public AccessController(
        CrmDbContext db,
        ITenantContext tenant,
        MetadataService metadata,
        TenantQuotaService quota,
        RolePermissionService rolePerms)
    {
        _db = db;
        _tenant = tenant;
        _metadata = metadata;
        _quota = quota;
        _rolePerms = rolePerms;
    }

    [HttpGet("/App/access")]
    public async Task<IActionResult> Index(
        string? tab = "users",
        string? q = null,
        string? status = "all",
        int? roleId = null,
        bool? admin = null)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var tenantId = _tenant.TenantId!.Value;
        await _rolePerms.EnsureMigratedAsync(tenantId);

        tab = NormalizeTab(tab);
        status = (status ?? "all").Trim().ToLowerInvariant();
        if (status is not ("all" or "active" or "inactive"))
            status = "all";

        var flatRoles = await LoadFlatRolesAsync(tenantId);
        var roleOptions = flatRoles.ToDictionary(r => r.Id, r => r.Name);
        var limits = await _quota.GetLimitsAsync(tenantId);
        var activeCount = await _db.Users.AsNoTracking()
            .CountAsync(u => u.TenantId == tenantId && u.IsActive);

        var model = new AccessHubModel
        {
            Tab = tab,
            Q = q?.Trim(),
            Status = status,
            RoleId = roleId,
            AdminOnly = admin,
            Limits = limits,
            ActiveUserCount = activeCount,
            CanAddUser = activeCount < limits.MaxUsers,
            RoleTree = BuildTree(flatRoles),
            RoleOptions = roleOptions
        };

        if (tab == "users")
            model.Users = await LoadUsersAsync(tenantId, model.Q, status, roleId, admin, roleOptions);

        ViewData["Title"] = "مدیریت کاربر";
        ViewData["PanelTitle"] = "تنظیمات";
        return View(model);
    }

    [HttpGet("/App/access/roles/create")]
    public async Task<IActionResult> CreateRoleForm(int? parentId = null)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        ViewData["Title"] = "ایجاد نقش";
        ViewData["PanelTitle"] = "تنظیمات";
        return View("RoleForm", await BuildRoleFormAsync(0, "", parentId, isAdmin: false));
    }

    [HttpGet("/App/access/roles/{id:int}/edit")]
    public async Task<IActionResult> EditRoleForm(int id)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var tenantId = _tenant.TenantId!.Value;
        var role = await _db.CrmRoles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id);
        if (role is null) return NotFound();

        ViewData["Title"] = $"ویرایش نقش «{role.Name}»";
        ViewData["PanelTitle"] = "تنظیمات";
        return View("RoleForm", await BuildRoleFormAsync(role.Id, role.Name, role.ParentRoleId, role.IsAdmin));
    }

    [HttpPost("/App/access/roles/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRole(
        string name, int? parentRoleId, bool isAdmin,
        int[]? moduleIds, int[]? canView, int[]? canCreate, int[]? canEdit, int[]? canDelete)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var tenantId = _tenant.TenantId!.Value;
        name = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "نام نقش الزامی است.";
            return RedirectToAction(nameof(CreateRoleForm), new { parentId = parentRoleId });
        }

        parentRoleId = await SanitizeParentAsync(tenantId, parentRoleId, excludeId: null);
        if (parentRoleId is not null)
            isAdmin = false; // فقط ریشه می‌تواند ادمین نقش باشد

        var role = new Role
        {
            TenantId = tenantId,
            Name = name,
            ParentRoleId = parentRoleId,
            IsAdmin = isAdmin && parentRoleId is null
        };
        _db.CrmRoles.Add(role);
        await _db.SaveChangesAsync();

        await SavePermissionsFromFormAsync(tenantId, role.Id, role.ParentRoleId, role.IsAdmin,
            moduleIds, canView, canCreate, canEdit, canDelete);

        TempData["Success"] = "نقش ایجاد شد.";
        return Redirect("/App/access?tab=roles");
    }

    [HttpPost("/App/access/roles/{id:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditRole(
        int id, string name, int? parentRoleId, bool isAdmin,
        int[]? moduleIds, int[]? canView, int[]? canCreate, int[]? canEdit, int[]? canDelete)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var tenantId = _tenant.TenantId!.Value;
        var role = await _db.CrmRoles.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id);
        if (role is null) return NotFound();

        name = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "نام نقش الزامی است.";
            return RedirectToAction(nameof(EditRoleForm), new { id });
        }

        parentRoleId = await SanitizeParentAsync(tenantId, parentRoleId, excludeId: id);
        // جلوگیری از حلقه: والد نباید از نوادگان این نقش باشد
        if (parentRoleId is int pid && await IsDescendantAsync(tenantId, id, pid))
        {
            TempData["Error"] = "نمی‌توان نقش را زیر یکی از زیردستان خودش قرار داد.";
            return RedirectToAction(nameof(EditRoleForm), new { id });
        }

        if (parentRoleId is not null)
            isAdmin = false;

        role.Name = name;
        role.ParentRoleId = parentRoleId;
        role.IsAdmin = isAdmin && parentRoleId is null;
        await _db.SaveChangesAsync();

        await SavePermissionsFromFormAsync(tenantId, role.Id, role.ParentRoleId, role.IsAdmin,
            moduleIds, canView, canCreate, canEdit, canDelete);

        TempData["Success"] = "نقش و دسترسی‌ها به‌روز شد.";
        return Redirect("/App/access?tab=roles");
    }

    // مسیرهای قدیمی پروفایل → تب نقش‌ها
    [HttpGet("/App/access/profiles/create")]
    [HttpGet("/App/access/profiles/{id:int}/edit")]
    public IActionResult LegacyProfiles() => Redirect("/App/access?tab=roles");

    private async Task SavePermissionsFromFormAsync(
        int tenantId, int roleId, int? parentRoleId, bool roleIsAdmin,
        int[]? moduleIds, int[]? canView, int[]? canCreate, int[]? canEdit, int[]? canDelete)
    {
        var modules = await _metadata.GetActiveModulesAsync();
        var viewSet = (canView ?? []).ToHashSet();
        var createSet = (canCreate ?? []).ToHashSet();
        var editSet = (canEdit ?? []).ToHashSet();
        var deleteSet = (canDelete ?? []).ToHashSet();
        var idList = (moduleIds ?? modules.Select(m => m.Id).ToArray()).Distinct();

        var proposed = idList.Select(mid =>
        {
            if (roleIsAdmin)
                return (mid, true, true, true, true);
            return (
                mid,
                viewSet.Contains(mid) || createSet.Contains(mid) || editSet.Contains(mid) || deleteSet.Contains(mid),
                createSet.Contains(mid),
                editSet.Contains(mid),
                deleteSet.Contains(mid));
        });

        var clamped = await _rolePerms.ClampToParentAsync(tenantId, parentRoleId, proposed);
        foreach (var kv in clamped)
            kv.Value.RoleId = roleId;
        await _rolePerms.SaveRolePermissionsAsync(tenantId, roleId, clamped);
    }

    private static string NormalizeTab(string? tab)
    {
        tab = (tab ?? "users").Trim().ToLowerInvariant();
        return tab is "users" or "roles" ? tab : "users";
    }

    private async Task<List<(int Id, string Name, int? ParentRoleId, bool IsAdmin, int UserCount)>> LoadFlatRolesAsync(int tenantId)
    {
        var roles = await _db.CrmRoles.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.Name)
            .ToListAsync();
        var counts = await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.CrmRoleId != null)
            .GroupBy(u => u.CrmRoleId!.Value)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count);

        return roles.Select(r => (r.Id, r.Name, r.ParentRoleId, r.IsAdmin, counts.GetValueOrDefault(r.Id))).ToList();
    }

    private static List<AccessRoleTreeNode> BuildTree(
        List<(int Id, string Name, int? ParentRoleId, bool IsAdmin, int UserCount)> flat)
    {
        var nodes = flat.ToDictionary(
            r => r.Id,
            r => new AccessRoleTreeNode
            {
                Id = r.Id,
                Name = r.Name,
                IsAdmin = r.IsAdmin,
                UserCount = r.UserCount
            });

        var roots = new List<AccessRoleTreeNode>();
        foreach (var r in flat)
        {
            var node = nodes[r.Id];
            if (r.ParentRoleId is int pid && nodes.TryGetValue(pid, out var parent))
                parent.Children.Add(node);
            else
                roots.Add(node);
        }

        void Sort(List<AccessRoleTreeNode> list)
        {
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
            foreach (var n in list)
                Sort(n.Children);
        }

        Sort(roots);
        return roots;
    }

    private async Task<List<AccessUserCard>> LoadUsersAsync(
        int tenantId, string? q, string status, int? roleId, bool? adminOnly,
        Dictionary<int, string> roles)
    {
        var query = _db.Users.AsNoTracking().Where(u => u.TenantId == tenantId);

        if (status == "active") query = query.Where(u => u.IsActive);
        else if (status == "inactive") query = query.Where(u => !u.IsActive);
        if (roleId is int rid) query = query.Where(u => u.CrmRoleId == rid);
        if (adminOnly == true) query = query.Where(u => u.IsTenantAdmin);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(u =>
                u.FullName.Contains(term) || (u.Email != null && u.Email.Contains(term)));
        }

        var users = await query
            .OrderByDescending(u => u.IsTenantAdmin)
            .ThenByDescending(u => u.IsActive)
            .ThenBy(u => u.FullName)
            .Take(200)
            .ToListAsync();

        return users.Select(u => new AccessUserCard
        {
            Id = u.Id,
            FullName = u.FullName,
            Email = u.Email ?? "",
            RoleName = u.CrmRoleId is int r ? roles.GetValueOrDefault(r) : null,
            IsTenantAdmin = u.IsTenantAdmin,
            IsActive = u.IsActive,
            Initials = MakeInitials(u.FullName)
        }).ToList();
    }

    private async Task<AccessRoleFormModel> BuildRoleFormAsync(int id, string name, int? parentRoleId, bool isAdmin)
    {
        var tenantId = _tenant.TenantId!.Value;
        var flat = await LoadFlatRolesAsync(tenantId);
        var modules = await _metadata.GetActiveModulesAsync();

        var perms = id == 0
            ? new Dictionary<int, RoleModulePermission>()
            : await _db.RoleModulePermissions.AsNoTracking()
                .Where(p => p.RoleId == id)
                .ToDictionaryAsync(p => p.ModuleId);

        Dictionary<int, AccessModuleRow>? parentCaps = null;
        string? parentName = null;
        if (parentRoleId is int pid)
        {
            parentName = flat.FirstOrDefault(r => r.Id == pid).Name;
            var parentRole = await _db.CrmRoles.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == pid);
            if (parentRole is { IsAdmin: false })
            {
                var pp = await _db.RoleModulePermissions.AsNoTracking()
                    .Where(p => p.RoleId == pid)
                    .ToListAsync();
                parentCaps = pp.ToDictionary(p => p.ModuleId, p => new AccessModuleRow
                {
                    ModuleId = p.ModuleId,
                    CanView = p.CanView,
                    CanCreate = p.CanCreate,
                    CanEdit = p.CanEdit,
                    CanDelete = p.CanDelete
                });
            }
        }

        var defaultFull = isAdmin || parentRoleId is null;
        return new AccessRoleFormModel
        {
            Id = id,
            Name = name,
            ParentRoleId = parentRoleId,
            ParentName = parentName,
            IsAdmin = isAdmin,
            ParentCandidates = flat
                .Where(r => r.Id != id)
                .Select(r => new AccessRoleOption { Id = r.Id, Name = r.Name })
                .ToList(),
            ParentCaps = parentCaps,
            Modules = modules.Select(m =>
            {
                perms.TryGetValue(m.Id, out var p);
                var row = new AccessModuleRow
                {
                    ModuleId = m.Id,
                    ModuleName = m.Name,
                    ModuleLabel = m.PluralLabel,
                    CanView = p?.CanView ?? defaultFull,
                    CanCreate = p?.CanCreate ?? defaultFull,
                    CanEdit = p?.CanEdit ?? (defaultFull || id == 0),
                    CanDelete = p?.CanDelete ?? (isAdmin || (id == 0 && parentRoleId is null))
                };
                if (id == 0 && parentRoleId is not null && !defaultFull)
                {
                    row.CanView = true;
                    row.CanCreate = true;
                    row.CanEdit = true;
                    row.CanDelete = false;
                }

                if (parentCaps is not null)
                {
                    if (!parentCaps.TryGetValue(m.Id, out var cap))
                    {
                        row.CanView = row.CanCreate = row.CanEdit = row.CanDelete = false;
                    }
                    else
                    {
                        row.CanView &= cap.CanView;
                        row.CanCreate &= cap.CanCreate;
                        row.CanEdit &= cap.CanEdit;
                        row.CanDelete &= cap.CanDelete;
                    }
                }

                return row;
            }).ToList()
        };
    }

    private async Task<int?> SanitizeParentAsync(int tenantId, int? parentRoleId, int? excludeId)
    {
        if (parentRoleId is not int pid || pid == excludeId)
            return null;
        var ok = await _db.CrmRoles.AnyAsync(r => r.TenantId == tenantId && r.Id == pid);
        return ok ? pid : null;
    }

    private async Task<bool> IsDescendantAsync(int tenantId, int ancestorId, int candidateId)
    {
        var roles = await _db.CrmRoles.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .Select(r => new { r.Id, r.ParentRoleId })
            .ToListAsync();
        var children = roles.Where(r => r.ParentRoleId != null)
            .GroupBy(r => r.ParentRoleId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());
        var stack = new Stack<int>();
        stack.Push(ancestorId);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (!children.TryGetValue(cur, out var kids)) continue;
            foreach (var k in kids)
            {
                if (k == candidateId) return true;
                stack.Push(k);
            }
        }
        return false;
    }

    private static string MakeInitials(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return "?";
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return parts[0][..Math.Min(2, parts[0].Length)];
        return $"{parts[0][0]}{parts[^1][0]}";
    }
}
