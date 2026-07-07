namespace Crm.Core.Entities;

public enum CampaignStatus
{
    Planned = 0,
    Active = 1,
    Finished = 2,
    Cancelled = 3
}

/// <summary>کمپین تبلیغاتی با بودجه و محاسبه ROI.</summary>
public class Campaign : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Channel { get; set; }
    public string? Description { get; set; }

    public CampaignStatus Status { get; set; } = CampaignStatus.Planned;
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }

    public decimal Budget { get; set; }
    public decimal ActualCost { get; set; }

    public ICollection<CampaignMember> Members { get; set; } = new List<CampaignMember>();
}

/// <summary>اتصال رکورد (سرنخ/مخاطب/فرصت) به کمپین.</summary>
public class CampaignMember : TenantEntity
{
    public int CampaignId { get; set; }
    public Campaign Campaign { get; set; } = null!;

    public string ModuleName { get; set; } = string.Empty;
    public int RecordId { get; set; }
}

/// <summary>وب‌فرم عمومی متصل به یک ماژول متادیتا — ثبت رکورد بدون Auth.</summary>
public class WebForm : TenantEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>کلید عمومی در URL فرم.</summary>
    public string PublicKey { get; set; } = string.Empty;

    public int ModuleId { get; set; }
    public ModuleDef Module { get; set; } = null!;

    /// <summary>پیکربندی فیلدها: [{"name","hidden","defaultValue"}].</summary>
    public string FieldsJson { get; set; } = "[]";

    public string SuccessMessage { get; set; } = string.Empty;
    public bool UseCaptcha { get; set; } = true;
    public int? AssignToUserId { get; set; }
    public bool IsActive { get; set; } = true;

    public int SubmissionCount { get; set; }
}

public enum SurveyQuestionType
{
    Text = 0,
    Scale = 1,
    SingleChoice = 2,
    MultiChoice = 3
}

/// <summary>نظرسنجی با لینک عمومی.</summary>
public class Survey : TenantEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    public string PublicKey { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    /// <summary>تبدیل شرکت‌کننده جدید به سرنخ.</summary>
    public bool ConvertToLead { get; set; }

    /// <summary>ارسال خودکار پس از بستن تیکت (hook پلن ۷).</summary>
    public bool IsTicketSurvey { get; set; }

    public ICollection<SurveyQuestion> Questions { get; set; } = new List<SurveyQuestion>();
    public ICollection<SurveyResponse> Responses { get; set; } = new List<SurveyResponse>();
}

public class SurveyQuestion : TenantEntity
{
    public int SurveyId { get; set; }
    public Survey Survey { get; set; } = null!;

    public string Text { get; set; } = string.Empty;
    public SurveyQuestionType Type { get; set; }

    /// <summary>گزینه‌ها برای تک/چندانتخابی (JSON آرایه رشته).</summary>
    public string OptionsJson { get; set; } = "[]";
    public int SortOrder { get; set; }
}

public class SurveyResponse : TenantEntity
{
    public int SurveyId { get; set; }
    public Survey Survey { get; set; } = null!;

    public string? RespondentName { get; set; }
    public string? RespondentPhone { get; set; }

    /// <summary>پاسخ‌ها: {questionId: answer}.</summary>
    public string AnswersJson { get; set; } = "{}";
}

/// <summary>قالب پیام پرکاربرد — عمومی یا خصوصی سازنده.</summary>
public class MessageTemplate : TenantEntity
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    /// <summary>عمومی برای همه کاربران Tenant یا خصوصی سازنده.</summary>
    public bool IsPublic { get; set; } = true;
}
