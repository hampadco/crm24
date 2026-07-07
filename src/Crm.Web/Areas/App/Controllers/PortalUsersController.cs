using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Web.Areas.App.Models;

namespace Crm.Web.Areas.App.Controllers;

/// <summary>مدیریت کاربران پورتال مشتریان نهایی.</summary>
public class PortalUsersController : AppControllerBase
{
    private static readonly PasswordHasher<PortalUser> Hasher = new();

    private readonly CrmDbContext _db;

    public PortalUsersController(CrmDbContext db)
    {
        _db = db;
    }

    [HttpGet("/App/portal-users")]
    public async Task<IActionResult> Index()
    {
        var users = await _db.PortalUsers.AsNoTracking()
            .OrderByDescending(u => u.Id).Take(300).ToListAsync();

        ViewBag.Contacts = await LoadContactsAsync();
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

        TempData["Success"] = "کاربر پورتال ساخته شد. ورود: /Portal/Account/Login";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("/App/portal-users/{id:int}/edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var user = await _db.PortalUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return NotFound();

        ViewBag.Contacts = await LoadContactsAsync();
        ViewData["Title"] = $"ویرایش کاربر پورتال — {user.FullName}";
        return View(new PortalUserEditModel
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            ContactRecordId = user.ContactRecordId
        });
    }

    [HttpPost("/App/portal-users/{id:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, PortalUserEditModel model)
    {
        if (id != model.Id)
            return BadRequest();

        var user = await _db.PortalUsers.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return NotFound();

        if (!ModelState.IsValid)
        {
            ViewBag.Contacts = await LoadContactsAsync();
            ViewData["Title"] = $"ویرایش کاربر پورتال — {user.FullName}";
            return View(model);
        }

        var email = model.Email.Trim().ToLowerInvariant();
        if (await _db.PortalUsers.AnyAsync(u => u.Email == email && u.Id != id))
        {
            TempData["Error"] = "ایمیل تکراری است.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        user.FullName = model.FullName.Trim();
        user.Email = email;
        user.ContactRecordId = model.ContactRecordId;

        if (!string.IsNullOrWhiteSpace(model.NewPassword))
            user.PasswordHash = Hasher.HashPassword(user, model.NewPassword);

        await _db.SaveChangesAsync();
        TempData["Success"] = "کاربر پورتال به‌روزرسانی شد.";
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

    private async Task<Dictionary<int, string>> LoadContactsAsync()
    {
        var contactsModule = await _db.Modules.AsNoTracking().FirstOrDefaultAsync(m => m.Name == "contacts");
        if (contactsModule is null)
            return new Dictionary<int, string>();

        return await _db.Records.AsNoTracking()
            .Where(r => r.ModuleId == contactsModule.Id)
            .OrderByDescending(r => r.Id).Take(300)
            .ToDictionaryAsync(r => r.Id, r => r.Title);
    }
}
