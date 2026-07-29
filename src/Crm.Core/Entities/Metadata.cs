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
    Lookup = 13,
    Percent = 14
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

    /// <summary>or = هر فیلد Unique جداگانه؛ and = همه فیلدهای Unique با هم یک کلید تکراری</summary>
    public string DuplicateMatchMode { get; set; } = "or";

    public ICollection<FieldDef> Fields { get; set; } = new List<FieldDef>();
    public ICollection<FieldBlock> Blocks { get; set; } = new List<FieldBlock>();
}

/// <summary>بلاک چیدمان فیلدهای یک ماژول (استودیوی سفارشی‌سازی).</summary>
public class FieldBlock : TenantEntity
{
    public int ModuleId { get; set; }
    public ModuleDef Module { get; set; } = null!;
    public string Name { get; set; } = string.Empty; // system name
    public string Label { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsCollapsed { get; set; }
    /// <summary>json dependency: { "field":"stage","op":"eq","value":"Closed Won" }</summary>
    public string? VisibilityRuleJson { get; set; }
    public ICollection<FieldDef> Fields { get; set; } = new List<FieldDef>();
}

/// <summary>تعریف فیلد یک ماژول (استاندارد یا سفارشی).</summary>
public class FieldDef : TenantEntity
{
    public int ModuleId { get; set; }
    public ModuleDef Module { get; set; } = null!;

    public int? BlockId { get; set; }
    public FieldBlock? Block { get; set; }

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
    public int? MaxLength { get; set; }
    /// <summary>تعداد ارقام صحیح مجاز (عدد اعشار / درصد).</summary>
    public int? IntegerDigits { get; set; }
    /// <summary>تعداد ارقام اعشاری مجاز.</summary>
    public int? DecimalDigits { get; set; }
    public bool IsVisible { get; set; } = true;
    /// <summary>json dependency: { "field":"stage","op":"eq","value":"Closed Won" }</summary>
    public string? VisibilityRuleJson { get; set; }
    /// <summary>عبارت فرمول ذخیره‌شده (موتور محاسبه اختیاری است).</summary>
    public string? FormulaExpression { get; set; }
    /// <summary>json array قوانین اعتبارسنجی: [{ "rule":"minLength","value":"2" }, ...]</summary>
    public string? ValidationRulesJson { get; set; }

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

/// <summary>نوع cardinality رابطه بین دو ماژول.</summary>
public enum RelationKind
{
    OneToOne = 0,
    OneToMany = 1,
    ManyToOne = 2,
    ManyToMany = 3
}

/// <summary>رابطه بین دو ماژول (یک-به-یک / یک-به-چند / چند-به-یک / چند-به-چند).</summary>
public class RelationDef : TenantEntity
{
    public int SourceModuleId { get; set; }
    public int TargetModuleId { get; set; }
    /// <summary>نام زبانه در رکورد ماژول مبدأ.</summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>نام فیلد Lookup در رکورد مقابل (برچسب نمایشی).</summary>
    public string? RelatedFieldLabel { get; set; }
    public RelationKind Kind { get; set; } = RelationKind.OneToMany;
    public bool IsManyToMany { get; set; }
    /// <summary>نام فیلد Lookup روی سمت «چند» که به طرف مقابل اشاره می‌کند.</summary>
    public string? LinkFieldName { get; set; }
}
