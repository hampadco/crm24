using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Web.Data;
using Crm.Web.Models;

namespace Crm.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class SitePagesController : Controller
{
    private const string AboutKey = "about";
    private readonly SiteDbContext _db;

    public SitePagesController(SiteDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> EditAbout()
    {
        var page = await GetOrCreateAboutPageAsync();
        return View(page);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditAbout(SitePage model)
    {
        var page = await _db.SitePages.FirstOrDefaultAsync(p => p.Key == AboutKey);
        if (page is null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(model.Title))
            ModelState.AddModelError(nameof(SitePage.Title), "عنوان الزامی است.");

        if (!ModelState.IsValid)
            return View(model);

        page.Title = model.Title.Trim();
        page.Subtitle = model.Subtitle?.Trim() ?? string.Empty;
        page.HeroImageUrl = model.HeroImageUrl?.Trim() ?? string.Empty;
        page.Content = model.Content ?? string.Empty;

        await _db.SaveChangesAsync();
        TempData["AdminSuccess"] = "صفحه درباره ما ذخیره شد.";
        return RedirectToAction(nameof(EditAbout));
    }

    private async Task<SitePage> GetOrCreateAboutPageAsync()
    {
        var page = await _db.SitePages.FirstOrDefaultAsync(p => p.Key == AboutKey);
        if (page is not null)
            return page;

        page = new SitePage
        {
            Key = AboutKey,
            Title = "درباره ما",
            Subtitle = "بستری برای رشد کسب‌وکار شما",
            HeroImageUrl = "",
            Content = "<p></p>"
        };
        _db.SitePages.Add(page);
        await _db.SaveChangesAsync();
        return page;
    }
}
