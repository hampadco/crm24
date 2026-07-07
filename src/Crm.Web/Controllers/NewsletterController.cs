using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Web.Data;
using Crm.Web.Models;

namespace Crm.Web.Controllers;

public class NewsletterController : Controller
{
    private readonly SiteDbContext _db;

    public NewsletterController(SiteDbContext db)
    {
        _db = db;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Subscribe(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            TempData["NewsletterError"] = "لطفاً ایمیل معتبر وارد کنید.";
            return RedirectToAction("Index", "Home");
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var exists = await _db.NewsletterSubscribers
            .AnyAsync(s => s.Email == normalizedEmail);

        if (!exists)
        {
            _db.NewsletterSubscribers.Add(new NewsletterSubscriber
            {
                Email = normalizedEmail,
                SubscribedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }

        TempData["NewsletterSuccess"] = "عضویت شما در خبرنامه با موفقیت انجام شد.";
        return RedirectToAction("Index", "Home");
    }
}
