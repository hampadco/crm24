using System.ComponentModel.DataAnnotations;

namespace Crm.Web.Models;

public class AdminChangePasswordViewModel
{
    [Required(ErrorMessage = "رمز فعلی الزامی است.")]
    [DataType(DataType.Password)]
    [Display(Name = "رمز عبور فعلی")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "رمز جدید الزامی است.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "رمز جدید باید حداقل ۶ کاراکتر باشد.")]
    [DataType(DataType.Password)]
    [Display(Name = "رمز عبور جدید")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "تکرار رمز جدید الزامی است.")]
    [Compare(nameof(NewPassword), ErrorMessage = "تکرار رمز با رمز جدید یکسان نیست.")]
    [DataType(DataType.Password)]
    [Display(Name = "تکرار رمز جدید")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
