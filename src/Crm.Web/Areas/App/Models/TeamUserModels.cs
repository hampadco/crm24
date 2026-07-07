using System.ComponentModel.DataAnnotations;

namespace Crm.Web.Areas.App.Models;

public class TeamUserCreateModel
{
    [Required(ErrorMessage = "نام الزامی است.")]
    [Display(Name = "نام و نام خانوادگی")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "ایمیل الزامی است.")]
    [EmailAddress]
    [Display(Name = "ایمیل")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "رمز عبور الزامی است.")]
    [MinLength(6, ErrorMessage = "رمز عبور حداقل ۶ کاراکتر.")]
    [Display(Name = "رمز عبور")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "پروفایل دسترسی")]
    public int? ProfileId { get; set; }

    [Display(Name = "نقش سازمانی")]
    public int? CrmRoleId { get; set; }

    [Display(Name = "مدیر Tenant (دسترسی کامل)")]
    public bool IsTenantAdmin { get; set; }
}

public class TeamUserEditModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "نام الزامی است.")]
    [Display(Name = "نام و نام خانوادگی")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "ایمیل الزامی است.")]
    [EmailAddress]
    [Display(Name = "ایمیل")]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "پروفایل دسترسی")]
    public int? ProfileId { get; set; }

    [Display(Name = "نقش سازمانی")]
    public int? CrmRoleId { get; set; }

    [Display(Name = "مدیر Tenant (دسترسی کامل)")]
    public bool IsTenantAdmin { get; set; }

    [Display(Name = "فعال")]
    public bool IsActive { get; set; } = true;

    [MinLength(6, ErrorMessage = "رمز عبور حداقل ۶ کاراکتر.")]
    [Display(Name = "رمز عبور جدید (اختیاری)")]
    public string? NewPassword { get; set; }
}

public class PortalUserEditModel
{
    public int Id { get; set; }

    [Required]
    [Display(Name = "نام و نام خانوادگی")]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [Display(Name = "ایمیل")]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "مخاطب مرتبط")]
    public int? ContactRecordId { get; set; }

    [MinLength(6)]
    [Display(Name = "رمز عبور جدید (اختیاری)")]
    public string? NewPassword { get; set; }
}

public class TeamUserListItem
{
    public Crm.Infrastructure.Identity.CrmUser User { get; set; } = null!;
    public string? ProfileName { get; set; }
    public string? RoleName { get; set; }
}
