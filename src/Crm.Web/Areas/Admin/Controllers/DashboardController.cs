using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Web.Data;
using Crm.Web.Models.Admin;
using Crm.Web.Services;

namespace Crm.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class DashboardController : Controller
{
    private readonly SiteDbContext _db;

    public DashboardController(SiteDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var recentItems = await _db.Articles
            .OrderByDescending(a => a.PublishedAt)
            .Take(8)
            .Select(a => new DashboardRecentItem
            {
                Type = "article",
                TypeLabel = "مقاله",
                Title = a.Title,
                DateLabel = PersianDateHelper.ToJalaliDate(a.PublishedAt),
                EditUrl = Url.Action("Edit", "Articles", new { area = "Admin", id = a.Id })!
            })
            .ToListAsync();

        var model = new DashboardViewModel
        {
            TodayJalali = PersianDateHelper.ToJalaliDate(DateTime.Now),
            ArticleCount = await _db.Articles.CountAsync(),
            FaqCount = await _db.FaqItems.CountAsync(),
            SitePageCount = await _db.SitePages.CountAsync(),
            SubscriberCount = await _db.NewsletterSubscribers.CountAsync(),
            RecentItems = recentItems
        };

        return View(model);
    }
}
