namespace Crm.Core.Entities;

/// <summary>نقش درختی — لایه اول RBAC. مدیر رکوردهای زیردستان را می‌بیند.</summary>
public class Role : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public int? ParentRoleId { get; set; }
    public Role? ParentRole { get; set; }
    public ICollection<Role> Children { get; set; } = new List<Role>();
}

public enum ModulePermissionLevel
{
    None = 0,
    View = 1,
    Create = 2,
    Edit = 3,
    Delete = 4
}

public enum FieldAccess
{
    Editable = 0,
    ReadOnly = 1,
    Hidden = 2
}

/// <summary>پروفایل دسترسی — لایه دوم RBAC: مجوز per-module و per-field.</summary>
public class Profile : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }

    public ICollection<ProfileModulePermission> ModulePermissions { get; set; } = new List<ProfileModulePermission>();
    public ICollection<ProfileFieldPermission> FieldPermissions { get; set; } = new List<ProfileFieldPermission>();
}

public class ProfileModulePermission : TenantEntity
{
    public int ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;
    public int ModuleId { get; set; }

    public bool CanView { get; set; }
    public bool CanCreate { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
}

public class ProfileFieldPermission : TenantEntity
{
    public int ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;
    public int FieldId { get; set; }

    public FieldAccess Access { get; set; } = FieldAccess.Editable;
}

public enum SharingLevel
{
    Private = 0,
    PublicReadOnly = 1,
    PublicReadWrite = 2,
    PublicFull = 3
}

/// <summary>قانون اشتراک‌گذاری per-module — لایه سوم RBAC.</summary>
public class SharingRule : TenantEntity
{
    public int ModuleId { get; set; }
    public SharingLevel DefaultLevel { get; set; } = SharingLevel.Private;
}

/// <summary>گروه کاربری برای ارجاع کار و اشتراک‌گذاری سفارشی.</summary>
public class UserGroup : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public ICollection<UserGroupMember> Members { get; set; } = new List<UserGroupMember>();
}

public class UserGroupMember : TenantEntity
{
    public int GroupId { get; set; }
    public UserGroup Group { get; set; } = null!;
    public int UserId { get; set; }
}
