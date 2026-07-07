using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Web.Data;
using Crm.Web.Models;

namespace Crm.Web.ViewComponents;

public class LatestArticlesViewComponent : ViewComponent
{
    private readonly SiteDbContext _db;

    public LatestArticlesViewComponent(SiteDbContext db)
    {
        _db = db;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var articles = await _db.Articles
            .AsNoTracking()
            .OrderByDescending(a => a.PublishedAt)
            .Take(6)
            .ToListAsync();

        return View(articles);
    }
}
