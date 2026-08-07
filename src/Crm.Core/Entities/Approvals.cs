namespace Crm.Core.Entities;

public enum ApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

/// <summary>قانون تأیید برای ماژول — شرط ساده روی یک فیلد.</summary>
public class ApprovalRule : TenantEntity
{
    public int ModuleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ConditionField { get; set; } = string.Empty;
    /// <summary>eq | ne | contains | isempty | isnotempty</summary>
    public string ConditionOp { get; set; } = "eq";
    public string? ConditionValue { get; set; }
    public int ApproverRoleId { get; set; }
}

/// <summary>درخواست تأیید یک رکورد بر اساس قانون.</summary>
public class ApprovalRequest : TenantEntity
{
    public string ModuleName { get; set; } = string.Empty;
    public int RecordId { get; set; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public int? RequestedByUserId { get; set; }
    public int? DecidedByUserId { get; set; }
    public string? Note { get; set; }
    public int RuleId { get; set; }
}
