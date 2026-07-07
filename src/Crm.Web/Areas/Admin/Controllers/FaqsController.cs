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
public class FaqsController : Controller
{
    private readonly SiteDbContext _db;

    public FaqsController(SiteDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        var query = new ContentListQuery { Search = search, Page = page };

        var itemsQuery = _db.FaqItems.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            itemsQuery = itemsQuery.Where(i =>
                i.Question.Contains(term) || i.Answer.Contains(term));
        }

        var model = await itemsQuery
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Id)
            .ToPagedListAsync(query);

        ViewBag.ListQuery = query;
        return View(model);
    }

    public IActionResult Create()
    {
        return View(new FaqItem());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(FaqItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Question))
            ModelState.AddModelError(nameof(FaqItem.Question), "سؤال الزامی است.");

        if (!ModelState.IsValid)
            return View(item);

        _db.FaqItems.Add(item);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var item = await _db.FaqItems.FindAsync(id);
        if (item is null)
            return NotFound();

        return View(item);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, FaqItem item)
    {
        if (id != item.Id)
            return NotFound();

        if (string.IsNullOrWhiteSpace(item.Question))
            ModelState.AddModelError(nameof(FaqItem.Question), "سؤال الزامی است.");

        if (!ModelState.IsValid)
            return View(item);

        var existing = await _db.FaqItems.FindAsync(id);
        if (existing is null)
            return NotFound();

        existing.Question = item.Question;
        existing.Answer = item.Answer;
        existing.SortOrder = item.SortOrder;

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _db.FaqItems.FindAsync(id);
        if (item is null)
            return NotFound();

        _db.FaqItems.Remove(item);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
