using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Crm.Web.Models;
using Crm.Web.Services;

namespace Crm.Web.Areas.Admin.Controllers;

[Area("Admin")]
public class AccountController : Controller
{
    private readonly AdminAuthService _auth;

    public AccountController(AdminAuthService auth)
    {
        _auth = auth;
    }

    [AllowAnonymous]
    public IActionResult Login(string? returnUrl)
    {
        return View(new AdminLoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(AdminLoginViewModel model)
    {
        if (!await _auth.ValidateCredentialsAsync(model.Username, model.Password))
        {
            ModelState.AddModelError(string.Empty, "نام کاربری یا رمز عبور اشتباه است.");
            return View(model);
        }

        await _auth.SignInAsync(HttpContext, model.Username);

        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            return Redirect(model.ReturnUrl);

        return RedirectToAction("Index", "Dashboard", new { area = "Admin" });
    }

    [Authorize]
    public IActionResult ChangePassword()
    {
        return View(new AdminChangePasswordViewModel());
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(AdminChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        if (!await _auth.ChangePasswordAsync(model.CurrentPassword, model.NewPassword))
        {
            ModelState.AddModelError(nameof(AdminChangePasswordViewModel.CurrentPassword), "رمز عبور فعلی اشتباه است.");
            return View(model);
        }

        TempData["AdminSuccess"] = "رمز عبور با موفقیت تغییر کرد.";
        return RedirectToAction(nameof(ChangePassword));
    }

    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await _auth.SignOutAsync(HttpContext);
        return RedirectToAction(nameof(Login));
    }
}
