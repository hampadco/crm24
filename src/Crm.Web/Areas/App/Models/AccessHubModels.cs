using Crm.Infrastructure.Services;

namespace Crm.Web.Areas.App.Models;

public class AccessHubModel
{
    public string Tab { get; set; } = "users";
    public string? Q { get; set; }
    public string Status { get; set; } = "all";
    public int? RoleId { get; set; }
    public bool? AdminOnly { get; set; }

    public TenantPlanLimits Limits { get; set; } = null!;
    public int ActiveUserCount { get; set; }
    public bool CanAddUser { get; set; }

    public List<AccessUserCard> Users { get; set; } = [];
    public List<AccessRoleTreeNode> RoleTree { get; set; } = [];
    public Dictionary<int, string> RoleOptions { get; set; } = new();
}

public class AccessUserCard
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? RoleName { get; set; }
    public bool IsTenantAdmin { get; set; }
    public bool IsActive { get; set; }
    public string Initials { get; set; } = "";
}

public class AccessRoleTreeNode
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsAdmin { get; set; }
    public int UserCount { get; set; }
    public List<AccessRoleTreeNode> Children { get; set; } = [];
}

public class AccessRoleFormModel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? ParentRoleId { get; set; }
    public bool IsAdmin { get; set; }
    public string? ParentName { get; set; }
    public List<AccessRoleOption> ParentCandidates { get; set; } = [];
    public List<AccessModuleRow> Modules { get; set; } = [];
    /// <summary>سقف والد برای غیرفعال کردن چک‌باکس‌های UI.</summary>
    public Dictionary<int, AccessModuleRow>? ParentCaps { get; set; }
}

public class AccessRoleOption
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class AccessModuleRow
{
    public int ModuleId { get; set; }
    public string ModuleLabel { get; set; } = "";
    public string ModuleName { get; set; } = "";
    public bool CanView { get; set; }
    public bool CanCreate { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
}
