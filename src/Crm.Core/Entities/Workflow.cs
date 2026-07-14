namespace Crm.Core.Entities;

public enum WorkflowTrigger
{
    RecordCreated = 0,
    RecordUpdated = 1,
    Scheduled = 2
}

public enum WorkflowSchedule
{
    Hourly = 0,
    Daily = 1,
    Monthly = 2
}

/// <summary>قانون گردش‌کار: محرک + شرط‌های ترکیبی و/یا + زنجیره اکشن‌ها.</summary>
public class WorkflowRule : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public int ModuleId { get; set; }
    public ModuleDef Module { get; set; } = null!;

    public WorkflowTrigger Trigger { get; set; }
    public WorkflowSchedule? Schedule { get; set; }

    /// <summary>شرط‌ها به‌صورت jsonb: { "logic": "and|or", "items": [{ "field", "op", "value" }] }</summary>
    public string ConditionsJson { get; set; } = """{"logic":"and","items":[]}""";

    public bool IsActive { get; set; } = true;

    public ICollection<WorkflowAction> Actions { get; set; } = new List<WorkflowAction>();
}

/// <summary>۱۱ اکشن گردش‌کار طبق تحلیل BamaCRM.</summary>
public enum WorkflowActionType
{
    SendEmail = 0,
    SendSms = 1,
    CreateTask = 2,
    CreateEvent = 3,
    UpdateField = 4,
    CreateRecord = 5,
    Notify = 6,
    ToggleTag = 7,
    SendToAccounting = 8,
    CallWebhook = 9,
    RunCustomFunction = 10
}

public class WorkflowAction : TenantEntity
{
    public int RuleId { get; set; }
    public WorkflowRule Rule { get; set; } = null!;

    public WorkflowActionType Type { get; set; }

    /// <summary>پیکربندی اکشن (گیرنده، متن با placeholder های {field}، فیلد هدف و ...) jsonb.</summary>
    public string ConfigJson { get; set; } = "{}";

    public int SortOrder { get; set; }
}

/// <summary>لاگ اجرای هر قانون برای رهگیری و خطایابی.</summary>
public class WorkflowLog : TenantEntity
{
    public int RuleId { get; set; }
    public int? RecordId { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>ویجت داشبورد per-user.</summary>
public class DashboardWidget : TenantEntity
{
    public int UserId { get; set; }

    /// <summary>counter | pie | monthly</summary>
    public string Type { get; set; } = "counter";

    public string Title { get; set; } = string.Empty;
    public int ModuleId { get; set; }

    /// <summary>نام فیلد گروه‌بندی برای نمودار pie.</summary>
    public string? FieldName { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>تعریف گزارش پویا: ماژول + ستون‌ها + فیلتر + گروه‌بندی/جمع.</summary>
public class ReportDef : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public int ModuleId { get; set; }
    public ModuleDef Module { get; set; } = null!;

    /// <summary>نام فیلدهای ستون به‌صورت jsonb آرایه.</summary>
    public string ColumnsJson { get; set; } = "[]";

    /// <summary>فیلترها با همان قالب شرط‌های گردش‌کار.</summary>
    public string FiltersJson { get; set; } = """{"logic":"and","items":[]}""";

    public string? GroupByField { get; set; }

    /// <summary>فیلد جمع (فقط برای فیلدهای عددی) — خالی یعنی شمارش.</summary>
    public string? SumField { get; set; }
}
