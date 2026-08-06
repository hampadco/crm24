using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Web.Models;

namespace Crm.Web.Areas.App.Controllers;

/// <summary>پایگاه دانش داخلی — جدا از FAQ سایت عمومی.</summary>
public class KbController : AppControllerBase
{
    private readonly CrmDbContext _db;

    public KbController(CrmDbContext db) => _db = db;

    [HttpGet("/App/kb")]
    public async Task<IActionResult> Index(string? q, int page = 1)
    {
        var query = _db.KbArticles.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(a => a.Title.Contains(q) || a.Body.Contains(q));

        var (articles, total, p, pageSize) = await AppPaging.ToPageAsync(query.OrderByDescending(a => a.Id), page);
        ViewData["Title"] = "پایگاه دانش";
        ViewBag.Query = q;
        AppPaging.SetViewBag(ViewBag, total, p, pageSize, new Dictionary<string, string?> { ["q"] = q });
        return View(articles);
    }

    [HttpGet("/App/kb/create")]
    public IActionResult Create()
    {
        ViewData["Title"] = "مقاله جدید";
        return View("Form", new KbArticle());
    }

    [HttpGet("/App/kb/{id:int}/edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var article = await _db.KbArticles.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (article is null)
            return NotFound();
        ViewData["Title"] = $"ویرایش {article.Title}";
        return View("Form", article);
    }

    [HttpPost("/App/kb/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int id, string title, string body, string? category, bool isPublishedToPortal)
    {
        if (string.IsNullOrWhiteSpace(title) || IsBodyEmpty(body))
        {
            TempData["Error"] = "عنوان و محتوای مقاله الزامی است.";
            if (id > 0)
                return RedirectToAction(nameof(Edit), new { id });
            return RedirectToAction(nameof(Create));
        }

        KbArticle article;
        if (id == 0)
        {
            article = new KbArticle();
            _db.KbArticles.Add(article);
        }
        else
        {
            article = await _db.KbArticles.FirstAsync(a => a.Id == id);
        }

        article.Title = title.Trim();
        article.Body = body.Trim();
        article.Category = category?.Trim();
        article.IsPublishedToPortal = isPublishedToPortal;

        await _db.SaveChangesAsync();
        TempData["Success"] = "مقاله ذخیره شد.";
        return RedirectToAction(nameof(Edit), new { id = article.Id });
    }

    [HttpPost("/App/kb/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var article = await _db.KbArticles.FindAsync(id);
        if (article is not null)
        {
            article.IsDeleted = true;
            article.DeletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            TempData["Success"] = "مقاله حذف شد.";
        }
        return RedirectToAction(nameof(Index));
    }

    /// <summary>HTML خالی / فقط empty-state المنتور را محتوا حساب نمی‌کند.</summary>
    internal static bool IsBodyEmpty(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return true;

        if (body.Contains("elementor-empty-state", StringComparison.OrdinalIgnoreCase)
            && !body.Contains("elementor-section", StringComparison.OrdinalIgnoreCase)
            && !body.Contains("elementor-widget", StringComparison.OrdinalIgnoreCase))
            return true;

        var text = Regex.Replace(body, "<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        if (string.IsNullOrWhiteSpace(text)
            || text.Contains("صفحه خالی است", StringComparison.Ordinal)
            || text.Contains("برای شروع، یک بخش جدید اضافه کنید", StringComparison.Ordinal))
            return true;

        return text.Length < 2;
    }
}
