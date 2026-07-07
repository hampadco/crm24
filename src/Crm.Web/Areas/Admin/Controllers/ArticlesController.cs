using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Web.Data;
using Crm.Web.Models;
using Crm.Web.Models.Admin;
using Crm.Web.Services;

namespace Crm.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class ArticlesController : Controller
{
    private readonly SiteDbContext _db;
    private readonly ContentTaxonomyService _taxonomy;
    private readonly ContentMediaService _contentMedia;

    public ArticlesController(SiteDbContext db, ContentTaxonomyService taxonomy, ContentMediaService contentMedia)
    {
        _db = db;
        _taxonomy = taxonomy;
        _contentMedia = contentMedia;
    }

    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        var query = new ContentListQuery { Search = search, Page = page };

        var articlesQuery = _db.Articles
            .Include(a => a.Category)
            .Include(a => a.Tags)
            .ThenInclude(t => t.Tag)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            articlesQuery = articlesQuery.Where(a =>
                a.Title.Contains(term) || a.Slug.Contains(term));
        }

        var model = await articlesQuery
            .OrderByDescending(a => a.PublishedAt)
            .ToPagedListAsync(query);

        ViewBag.ListQuery = query;
        return View(model);
    }

    public async Task<IActionResult> Create()
    {
        await PopulateFormAsync();
        return View(new Article { PublishedAt = DateTime.Now });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Article article, string? tagNames)
    {
        if (string.IsNullOrWhiteSpace(article.Slug))
            article.Slug = SlugHelper.From(article.Title);

        if (string.IsNullOrWhiteSpace(article.Title))
            ModelState.AddModelError(nameof(Article.Title), "عنوان الزامی است.");

        if (!ModelState.IsValid)
        {
            await PopulateFormAsync(tagNames);
            return View(article);
        }

        _db.Articles.Add(article);
        await _db.SaveChangesAsync();
        await _taxonomy.SyncArticleTagsAsync(article.Id, tagNames);
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var article = await _db.Articles.FindAsync(id);
        if (article is null)
            return NotFound();

        await PopulateFormAsync(articleId: id);
        return View(article);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Article article, string? tagNames)
    {
        if (id != article.Id)
            return NotFound();

        if (string.IsNullOrWhiteSpace(article.Slug))
            article.Slug = SlugHelper.From(article.Title);

        if (string.IsNullOrWhiteSpace(article.Title))
            ModelState.AddModelError(nameof(Article.Title), "عنوان الزامی است.");

        if (!ModelState.IsValid)
        {
            await PopulateFormAsync(tagNames, id);
            return View(article);
        }

        var existing = await _db.Articles.FindAsync(id);
        if (existing is null)
            return NotFound();

        var previousMedia = _contentMedia.CollectArticleMedia(existing);

        existing.Title = article.Title;
        existing.Slug = article.Slug;
        existing.Summary = article.Summary;
        existing.ThumbnailUrl = article.ThumbnailUrl;
        existing.CategoryId = article.CategoryId;
        existing.PublishedAt = article.PublishedAt;
        existing.Content = article.Content;

        await _db.SaveChangesAsync();
        await _taxonomy.SyncArticleTagsAsync(id, tagNames);
        await _contentMedia.CleanupRemovedMediaAsync(previousMedia, _contentMedia.CollectArticleMedia(existing));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var article = await _db.Articles.FindAsync(id);
        if (article is null)
            return NotFound();

        var mediaUrls = _contentMedia.CollectArticleMedia(article);
        _db.Articles.Remove(article);
        await _db.SaveChangesAsync();
        await _contentMedia.DeleteEntityMediaAsync(mediaUrls);
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateFormAsync(string? tagNames = null, int? articleId = null)
    {
        ViewBag.Categories = await _taxonomy.GetCategoriesAsync(ContentCategoryType.Article);
        ViewBag.CategoryType = ContentCategoryType.Article;
        ViewBag.TagNames = tagNames ?? (articleId.HasValue
            ? await _taxonomy.GetArticleTagNamesAsync(articleId.Value)
            : string.Empty);
    }
}
