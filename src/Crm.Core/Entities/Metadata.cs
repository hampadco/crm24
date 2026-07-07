namespace Crm.Core.Entities;

public enum FieldType
{
    Text = 0,
    MultilineText = 1,
    Number = 2,
    Decimal = 3,
    Currency = 4,
    Date = 5,
    DateTime = 6,
    Checkbox = 7,
    Picklist = 8,
    MultiPicklist = 9,
    Email = 10,
    Phone = 11,
    Url = 12,
    Lookup = 13
}

/// <summary>تعریف ماژول (موجودیت) — هسته معماری Metadata-First.</summary>
public class ModuleDef : TenantEntity
{
    /// <summary>نام سیستمی لاتین (مثل leads).</summary>
    public string Name { get; set; } = string.Empty;

    public string SingularLabel { get; set; } = string.Empty;
    public string PluralLabel { get; set; } = string.Empty;
    public string Icon { get; set; } = "bx-grid-alt";

    /// <summary>ماژول‌های سیستمی توسط پلتفرم ساخته می‌شوند و حذف‌شدنی نیستند.</summary>
    public bool IsSystem { get; set; }

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public ICollection<FieldDef> Fields { get; set; } = new List<FieldDef>();
}

/// <summary>تعریف فیلد یک ماژول (استاندارد یا سفارشی).</summary>
public class FieldDef : TenantEntity
{
    public int ModuleId { get; set; }
    public ModuleDef Module { get; set; } = null!;

    /// <summary>نام سیستمی؛ برای فیلدهای سفارشی کلید داخل CustomData است.</summary>
    public string Name { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
    public FieldType Type { get; set; }

    /// <summary>فیلد استاندارد به ستون واقعی جدول نگاشت می‌شود؛ سفارشی داخل jsonb.</summary>
    public bool IsCustom { get; set; } = true;

    public bool IsRequired { get; set; }
    public bool ShowInList { get; set; } = true;
    public bool IsUniqueCheck { get; set; }
    public int SortOrder { get; set; }
    public string? DefaultValue { get; set; }

    /// <summary>برای Lookup: نام ماژول مقصد.</summary>
    public string? LookupModule { get; set; }

    public ICollection<PicklistValue> PicklistValues { get; set; } = new List<PicklistValue>();
}

public class PicklistValue : TenantEntity
{
    public int FieldId { get; set; }
    public FieldDef Field { get; set; } = null!;

    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Color { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>رابطه بین دو ماژول (یک-به-چند / چند-به-چند).</summary>
public class RelationDef : TenantEntity
{
    public int SourceModuleId { get; set; }
    public int TargetModuleId { get; set; }
    public string Label { get; set; } = string.Empty;
    public bool IsManyToMany { get; set; }
}
