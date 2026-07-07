using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Web.Data;
using Crm.Web.Models;
using Crm.Web.Services;

namespace Crm.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class CategoriesController : Controller
{
    private readonly SiteDbContext _db;

    public CategoriesController(SiteDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(ContentCategoryType? type)
    {
        var selectedType = type ?? ContentCategoryType.Article;
        var categories = await _db.ContentCategories
            .Where(c => c.Type == selectedType)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();

        ViewBag.SelectedType = selectedType;
        return View(categories);
    }

    public IActionResult Create(ContentCategoryType? type)
    {
        return View(new ContentCategory
        {
            Type = type ?? ContentCategoryType.Article,
            SortOrder = 0
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ContentCategory category)
    {
        if (string.IsNullOrWhiteSpace(category.Name))
            ModelState.AddModelError(nameof(ContentCategory.Name), "نام دسته الزامی است.");

        if (string.IsNullOrWhiteSpace(category.Slug))
            category.Slug = SlugHelper.From(category.Name);

        if (!ModelState.IsValid)
            return View(category);

        _db.ContentCategories.Add(category);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { type = category.Type });
    }

    public async Task<IActionResult> Edit(int id)
    {
        var category = await _db.ContentCategories.FindAsync(id);
        if (category is null)
            return NotFound();

        return View(category);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ContentCategory category)
    {
        if (id != category.Id)
            return NotFound();

        if (string.IsNullOrWhiteSpace(category.Name))
            ModelState.AddModelError(nameof(ContentCategory.Name), "نام دسته الزامی است.");

        if (string.IsNullOrWhiteSpace(category.Slug))
            category.Slug = SlugHelper.From(category.Name);

        if (!ModelState.IsValid)
            return View(category);

        var existing = await _db.ContentCategories.FindAsync(id);
        if (existing is null)
            return NotFound();

        existing.Name = category.Name;
        existing.Slug = category.Slug;
        existing.Type = category.Type;
        existing.SortOrder = category.SortOrder;

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { type = category.Type });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var category = await _db.ContentCategories.FindAsync(id);
        if (category is null)
            return NotFound();

        var type = category.Type;
        _db.ContentCategories.Remove(category);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { type });
    }

    [HttpGet]
    public async Task<IActionResult> Search(ContentCategoryType type, string? q)
    {
        var query = _db.ContentCategories.Where(c => c.Type == type);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(c => c.Name.Contains(term) || c.Slug.Contains(term));
        }

        var items = await query
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .Take(30)
            .Select(c => new { c.Id, c.Name, c.Slug })
            .ToListAsync();

        return Json(items);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickCreate(ContentCategoryType type, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { success = false, message = "نام دسته الزامی است." });

        var trimmed = name.Trim();
        var slug = SlugHelper.From(trimmed);

        var existing = await _db.ContentCategories
            .FirstOrDefaultAsync(c => c.Type == type && c.Slug == slug);

        if (existing is not null)
        {
            return Json(new
            {
                success = true,
                id = existing.Id,
                name = existing.Name,
                slug = existing.Slug,
                existed = true
            });
        }

        var category = new ContentCategory
        {
            Name = trimmed,
            Slug = slug,
            Type = type,
            SortOrder = 0
        };

        _db.ContentCategories.Add(category);
        await _db.SaveChangesAsync();

        return Json(new
        {
            success = true,
            id = category.Id,
            name = category.Name,
            slug = category.Slug,
            existed = false
        });
    }
}
