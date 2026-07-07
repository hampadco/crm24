using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Crm.Web.Data;
using Crm.Web.Models;

namespace Crm.Web.Services;

public class AdminAuthService
{
    private readonly AdminSettings _settings;
    private readonly SiteDbContext _db;
    private readonly PasswordHasher<AdminAccount> _hasher = new();

    public AdminAuthService(IOptions<AdminSettings> options, SiteDbContext db)
    {
        _settings = options.Value;
        _db = db;
    }

    public async Task<bool> ValidateCredentialsAsync(string username, string password)
    {
        var account = await _db.AdminAccounts.AsNoTracking().FirstOrDefaultAsync();
        if (account is not null)
        {
            if (!string.Equals(username, account.Username, StringComparison.Ordinal))
                return false;

            return _hasher.VerifyHashedPassword(account, account.PasswordHash, password)
                != PasswordVerificationResult.Failed;
        }

        return string.Equals(username, _settings.Username, StringComparison.Ordinal)
            && string.Equals(password, _settings.Password, StringComparison.Ordinal);
    }

    public async Task SignInAsync(HttpContext httpContext, string username)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Role, "Admin")
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
            });
    }

    public Task SignOutAsync(HttpContext httpContext)
    {
        return httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public async Task<bool> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        var account = await _db.AdminAccounts.FirstOrDefaultAsync();
        if (account is null)
        {
            if (!string.Equals(currentPassword, _settings.Password, StringComparison.Ordinal))
                return false;

            account = new AdminAccount
            {
                Id = 1,
                Username = _settings.Username
            };
            account.PasswordHash = _hasher.HashPassword(account, newPassword);
            _db.AdminAccounts.Add(account);
        }
        else
        {
            if (_hasher.VerifyHashedPassword(account, account.PasswordHash, currentPassword)
                == PasswordVerificationResult.Failed)
                return false;

            account.PasswordHash = _hasher.HashPassword(account, newPassword);
        }

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<string> GetUsernameAsync()
    {
        var account = await _db.AdminAccounts.AsNoTracking().FirstOrDefaultAsync();
        return account?.Username ?? _settings.Username;
    }
}
