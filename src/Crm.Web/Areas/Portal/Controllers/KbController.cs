using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Infrastructure.Data;

namespace Crm.Web.Areas.Portal.Controllers;

/// <summary>پایگاه دانش منتشرشده برای مشتریان نهایی.</summary>
public class KbController : PortalControllerBase
{
    private readonly CrmDbContext _db;

    public KbController(CrmDbContext db) => _db = db;

    [HttpGet("/Portal/kb")]
    public async Task<IActionResult> Index(string? q)
    {
        var query = _db.KbArticles.AsNoTracking().Where(a => a.IsPublishedToPortal);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(a => a.Title.Contains(q) || a.Body.Contains(q));

        var articles = await query.OrderByDescending(a => a.Id).Take(200).ToListAsync();
        ViewData["Title"] = "پایگاه دانش";
        ViewBag.Query = q;
        return View(articles);
    }

    [HttpGet("/Portal/kb/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var article = await _db.KbArticles.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.IsPublishedToPortal);
        if (article is null)
            return NotFound();

        ViewData["Title"] = article.Title;
        return View(article);
    }
}
