using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;

namespace Crm.Web.Areas.Portal.Controllers;

/// <summary>ورود/خروج کاربران پورتال مشتریان نهایی.</summary>
[Area("Portal")]
public class AccountController : Controller
{
    private static readonly PasswordHasher<PortalUser> Hasher = new();

    private readonly CrmDbContext _db;

    public AccountController(CrmDbContext db) => _db = db;

    [HttpGet("/Portal/Account/Login")]
    public IActionResult Login()
    {
        ViewData["Title"] = "ورود به پورتال مشتریان";
        return View();
    }

    [HttpPost("/Portal/Account/Login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string email, string password)
    {
        email = email?.Trim().ToLowerInvariant() ?? "";

        var user = await _db.PortalUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email && u.IsActive && !u.IsDeleted);

        if (user is null ||
            Hasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError("", "ایمیل یا رمز عبور اشتباه است.");
            ViewData["Title"] = "ورود به پورتال مشتریان";
            return View();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Email),
            new(CrmClaimTypes.TenantId, user.TenantId.ToString()),
            new(CrmClaimTypes.FullName, user.FullName)
        };
        if (user.ContactRecordId is int contactId)
            claims.Add(new Claim("portal:contact", contactId.ToString()));

        await HttpContext.SignInAsync("Portal",
            new ClaimsPrincipal(new ClaimsIdentity(claims, "Portal")));

        return RedirectToAction("Index", "Dashboard", new { area = "Portal" });
    }

    [HttpPost("/Portal/Account/Logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("Portal");
        return RedirectToAction("Login");
    }
}
