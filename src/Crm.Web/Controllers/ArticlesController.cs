using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Web.Data;
using Crm.Web.Models;
using Crm.Web.Services;

namespace Crm.Web.Controllers;

public class ArticlesController : Controller
{
    private readonly SiteDbContext _db;
    private readonly ContentTaxonomyService _taxonomy;

    public ArticlesController(SiteDbContext db, ContentTaxonomyService taxonomy)
    {
        _db = db;
        _taxonomy = taxonomy;
    }

    public async Task<IActionResult> Index(string? search, string? category, string? tag, int page = 1)
    {
        var query = _db.Articles
            .AsNoTracking()
            .Include(a => a.Category)
            .Include(a => a.Tags)
            .ThenInclude(t => t.Tag)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(a => a.Title.Contains(term) || a.Summary.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(a => a.Category != null && a.Category.Slug == category);

        if (!string.IsNullOrWhiteSpace(tag))
            query = query.Where(a => a.Tags.Any(t => t.Tag.Slug == tag));

        var results = await query
            .OrderByDescending(a => a.PublishedAt)
            .ToPagedListAsync(page, ListingDefaults.PageSize);

        return View(new ArticleListingViewModel
        {
            Results = results,
            Filters = new ContentFilterBarViewModel
            {
                ControllerName = "Articles",
                Categories = await _taxonomy.GetCategoriesAsync(ContentCategoryType.Article),
                Tags = await _taxonomy.GetArticleTagsInUseAsync(),
                SelectedCategorySlug = category,
                SelectedTagSlug = tag,
                Search = search?.Trim(),
                Page = results.Page
            }
        });
    }

    public async Task<IActionResult> Detail(string slug)
    {
        var article = await _db.Articles
            .AsNoTracking()
            .Include(a => a.Category)
            .Include(a => a.Tags)
            .ThenInclude(t => t.Tag)
            .FirstOrDefaultAsync(a => a.Slug == slug);

        if (article is null)
            return NotFound();

        var tagIds = article.Tags.Select(t => t.TagId).ToList();
        var related = new List<Article>();
        var relatedIds = new HashSet<int>();

        if (article.CategoryId.HasValue)
        {
            var sameCategory = await _db.Articles
                .AsNoTracking()
                .Where(a => a.Id != article.Id && a.CategoryId == article.CategoryId)
                .OrderByDescending(a => a.PublishedAt)
                .Take(4)
                .ToListAsync();

            related.AddRange(sameCategory);
            foreach (var item in sameCategory)
                relatedIds.Add(item.Id);
        }

        if (related.Count < 4 && tagIds.Count > 0)
        {
            var byTags = await _db.Articles
                .AsNoTracking()
                .Where(a => a.Id != article.Id && !relatedIds.Contains(a.Id))
                .Where(a => a.Tags.Any(t => tagIds.Contains(t.TagId)))
                .OrderByDescending(a => a.PublishedAt)
                .Take(4 - related.Count)
                .ToListAsync();

            related.AddRange(byTags);
            foreach (var item in byTags)
                relatedIds.Add(item.Id);
        }

        var latest = await _db.Articles
            .AsNoTracking()
            .Where(a => a.Id != article.Id && !relatedIds.Contains(a.Id))
            .OrderByDescending(a => a.PublishedAt)
            .Take(5)
            .ToListAsync();

        return View(new ArticleDetailViewModel
        {
            Article = article,
            Related = related.Select(a => a.ToSidebar()).ToList(),
            Latest = latest.Select(a => a.ToSidebar()).ToList()
        });
    }
}
