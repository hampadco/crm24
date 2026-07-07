using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Crm.Infrastructure.Identity;
using Crm.Infrastructure.Services;
using Crm.Web.Areas.App.Models;

namespace Crm.Web.Areas.App.Controllers;

/// <summary>ثبت‌نام (ساخت خودکار Tenant + تریال ۱۰ روزه) و ورود/خروج کاربران CRM.</summary>
[Area("App")]
public class AccountController : Controller
{
    private readonly SignInManager<CrmUser> _signInManager;
    private readonly UserManager<CrmUser> _userManager;
    private readonly TenantProvisioningService _provisioning;

    public AccountController(
        SignInManager<CrmUser> signInManager,
        UserManager<CrmUser> userManager,
        TenantProvisioningService provisioning)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _provisioning = provisioning;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var result = await _provisioning.RegisterAsync(model.CompanyName, model.FullName, model.Email, model.Password);
        if (!result.Success || result.AdminUser is null)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "خطا در ثبت‌نام.");
            return View(model);
        }

        await _signInManager.SignInAsync(result.AdminUser, isPersistent: true);
        TempData["Success"] = $"به CRM24 خوش آمدید! دوره آزمایشی ۱۰ روزه «{result.Tenant!.Name}» فعال شد.";
        return RedirectToAction("Index", "Dashboard", new { area = "App" });
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid)
            return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email.Trim().ToLowerInvariant());
        if (user is null || !user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "ایمیل یا رمز عبور اشتباه است.");
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(user, model.Password, model.RememberMe, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.IsLockedOut
                ? "حساب به دلیل تلاش‌های ناموفق موقتاً قفل شده است."
                : "ایمیل یا رمز عبور اشتباه است.");
            return View(model);
        }

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", "Dashboard", new { area = "App" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(AuthenticationSchemes = "Identity.Application")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        return RedirectToAction("Login");
    }

    /// <summary>پایان حالت پشتیبانی مالک و بازگشت به پنل مالک.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(AuthenticationSchemes = "Identity.Application")]
    public async Task<IActionResult> StopImpersonation()
    {
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        return Redirect("/Owner/Tenants");
    }

    /// <summary>صفحه اتمام دوره آزمایشی/اشتراک.</summary>
    [HttpGet]
    [Authorize(AuthenticationSchemes = "Identity.Application")]
    public IActionResult Expired() => View();
}
