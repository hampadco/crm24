using System.ComponentModel.DataAnnotations;
using Crm.Core.Entities;

namespace Crm.Web.Models.Admin;

public class TenantListItem
{
    public Tenant Tenant { get; set; } = null!;
    public int UserCount { get; set; }
    public int RecordCount { get; set; }
}

public class TenantListQuery
{
    public string? Q { get; set; }
    public TenantStatus? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public int NormalizedPage => Page < 1 ? 1 : Page;

    public int NormalizedPageSize => PageSize switch
    {
        < 5 => 20,
        > 100 => 100,
        _ => PageSize
    };
}

public class PlatformListQuery
{
    public string? Q { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public int NormalizedPage => Page < 1 ? 1 : Page;

    public int NormalizedPageSize => PageSize switch
    {
        < 5 => 20,
        > 100 => 100,
        _ => PageSize
    };
}

public class TenantDetailsViewModel
{
    public Tenant Tenant { get; set; } = null!;
    public int UserCount { get; set; }
    public int RecordCount { get; set; }
    public long StorageBytes { get; set; }
    public List<Crm.Infrastructure.Identity.CrmUser> Users { get; set; } = [];
    public List<Subscription> Subscriptions { get; set; } = [];
}

public class SubscriptionCreateModel
{
    [Required]
    public int TenantId { get; set; }

    [Required(ErrorMessage = "انتخاب پلن الزامی است.")]
    [Display(Name = "پلن")]
    public int PlanId { get; set; }

    [Display(Name = "مدت (ماه)")]
    [Range(1, 36)]
    public int Months { get; set; } = 12;

    [Display(Name = "مبلغ (تومان)")]
    [Range(0, 9_999_999_999)]
    public decimal Amount { get; set; }

    [Display(Name = "پرداخت همین حالا ثبت شود")]
    public bool RecordPayment { get; set; } = true;

    [Display(Name = "شماره پیگیری پرداخت")]
    public string? PaymentReference { get; set; }

    [Display(Name = "یادداشت")]
    public string? Note { get; set; }
}

public class GiftSubscriptionModel
{
    [Required]
    public int TenantId { get; set; }

    [Required(ErrorMessage = "انتخاب پلن الزامی است.")]
    [Display(Name = "پلن")]
    public int PlanId { get; set; }

    [Display(Name = "مدت (ماه)")]
    [Range(1, 36)]
    public int Months { get; set; } = 1;

    [Display(Name = "دلیل / یادداشت هدیه")]
    [Required(ErrorMessage = "توضیح کوتاه برای اشتراک هدیه الزامی است.")]
    public string? Reason { get; set; }
}

public class PlanFormModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "نام پلن الزامی است.")]
    [Display(Name = "نام پلن")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "توضیح کوتاه")]
    public string? Description { get; set; }

    [Display(Name = "قیمت ماهانه (تومان)")]
    [Range(0, 999_999_999)]
    public decimal PriceMonthly { get; set; }

    [Display(Name = "قیمت سالانه (تومان)")]
    [Range(0, 9_999_999_999)]
    public decimal PriceYearly { get; set; }

    [Display(Name = "حداکثر کاربر")]
    [Range(1, 10_000)]
    public int MaxUsers { get; set; } = 5;

    [Display(Name = "حداکثر رکورد")]
    [Range(100, 100_000_000)]
    public int MaxRecords { get; set; } = 10_000;

    [Display(Name = "فضای ذخیره‌سازی (مگابایت)")]
    [Range(100, 1_000_000)]
    public int MaxStorageMb { get; set; } = 1024;

    [Display(Name = "امکانات (هر خط یک مورد)")]
    public string Features { get; set; } = string.Empty;

    [Display(Name = "فعال")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "پلن پیشنهادی (نمایش ویژه)")]
    public bool IsFeatured { get; set; }

    [Display(Name = "ترتیب نمایش")]
    public int SortOrder { get; set; }
}

public class PlatformTransactionRow
{
    public string Source { get; set; } = string.Empty;
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime AtUtc { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string? Description { get; set; }
}

public class PlatformDashboardStats
{
    public int TotalTenants { get; set; }
    public int ActiveTenants { get; set; }
    public int TrialTenants { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal RevenueLast30Days { get; set; }
    public int ActiveSubscriptions { get; set; }
}
