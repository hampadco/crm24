using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Web.Areas.App.Controllers;

/// <summary>مدیریت کاربران پورتال مشتریان نهایی.</summary>
public class PortalUsersController : AppControllerBase
{
    private static readonly PasswordHasher<PortalUser> Hasher = new();

    private readonly CrmDbContext _db;

    public PortalUsersController(CrmDbContext db) => _db = db;

    [HttpGet("/App/portal-users")]
    public async Task<IActionResult> Index()
    {
        var users = await _db.PortalUsers.AsNoTracking()
            .OrderByDescending(u => u.Id).Take(300).ToListAsync();

        var contactsModule = await _db.Modules.AsNoTracking().FirstOrDefaultAsync(m => m.Name == "contacts");
        ViewBag.Contacts = contactsModule is null
            ? new Dictionary<int, string>()
            : await _db.Records.AsNoTracking()
                .Where(r => r.ModuleId == contactsModule.Id)
                .OrderByDescending(r => r.Id).Take(300)
                .ToDictionaryAsync(r => r.Id, r => r.Title);

        ViewData["Title"] = "کاربران پورتال";
        return View(users);
    }

    [HttpPost("/App/portal-users/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string email, string fullName, string password, int? contactRecordId)
    {
        email = email?.Trim().ToLowerInvariant() ?? "";
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || password.Length < 6)
        {
            TempData["Error"] = "ایمیل و رمز عبور (حداقل ۶ کاراکتر) الزامی است.";
            return RedirectToAction(nameof(Index));
        }

        if (await _db.PortalUsers.AnyAsync(u => u.Email == email))
        {
            TempData["Error"] = "کاربری با این ایمیل وجود دارد.";
            return RedirectToAction(nameof(Index));
        }

        var user = new PortalUser
        {
            Email = email,
            FullName = fullName?.Trim() ?? email,
            ContactRecordId = contactRecordId
        };
        user.PasswordHash = Hasher.HashPassword(user, password);

        _db.PortalUsers.Add(user);
        await _db.SaveChangesAsync();

        TempData["Success"] = "کاربر پورتال ساخته شد.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/App/portal-users/{id:int}/toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var user = await _db.PortalUsers.FirstOrDefaultAsync(u => u.Id == id);
        if (user is not null)
        {
            user.IsActive = !user.IsActive;
            await _db.SaveChangesAsync();
            TempData["Success"] = user.IsActive ? "کاربر فعال شد." : "کاربر غیرفعال شد.";
        }
        return RedirectToAction(nameof(Index));
    }
}
