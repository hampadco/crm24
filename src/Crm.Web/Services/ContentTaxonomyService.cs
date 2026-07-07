using Microsoft.EntityFrameworkCore;
using Crm.Web.Data;
using Crm.Web.Models;

namespace Crm.Web.Services;

public class ContentTaxonomyService
{
    private readonly SiteDbContext _db;

    public ContentTaxonomyService(SiteDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ContentCategory>> GetCategoriesAsync(ContentCategoryType type) =>
        await _db.ContentCategories
            .Where(c => c.Type == type)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();

    public async Task<IReadOnlyList<ContentTag>> GetArticleTagsInUseAsync() =>
        await _db.ArticleTags
            .Select(t => t.Tag)
            .Distinct()
            .OrderBy(t => t.Name)
            .ToListAsync();

    public async Task<string> GetArticleTagNamesAsync(int articleId) =>
        string.Join("\n", await _db.ArticleTags
            .Where(t => t.ArticleId == articleId)
            .Select(t => t.Tag.Name)
            .OrderBy(n => n)
            .ToListAsync());

    public async Task SyncArticleTagsAsync(int articleId, string? tagNames)
    {
        var names = ParseTagNames(tagNames);
        var existing = await _db.ArticleTags
            .Where(t => t.ArticleId == articleId)
            .Include(t => t.Tag)
            .ToListAsync();

        var existingByName = existing.ToDictionary(
            t => t.Tag.Name,
            t => t,
            StringComparer.OrdinalIgnoreCase);

        foreach (var junction in existing)
        {
            if (!names.Contains(junction.Tag.Name, StringComparer.OrdinalIgnoreCase))
                _db.ArticleTags.Remove(junction);
        }

        foreach (var name in names)
        {
            if (existingByName.ContainsKey(name))
                continue;

            var tag = await GetOrCreateTagAsync(name);
            _db.ArticleTags.Add(new ArticleTag { ArticleId = articleId, TagId = tag.Id });
        }

        await _db.SaveChangesAsync();
    }

    public static IReadOnlyList<string> ParseTagNames(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Array.Empty<string>();

        return input
            .Split(new[] { ',', '،', ';', '|', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<ContentTag> GetOrCreateTagAsync(string name)
    {
        var slug = SlugHelper.From(name);
        var tag = await _db.ContentTags.FirstOrDefaultAsync(t => t.Slug == slug);
        if (tag is not null)
            return tag;

        tag = new ContentTag { Name = name.Trim(), Slug = slug };
        _db.ContentTags.Add(tag);
        await _db.SaveChangesAsync();
        return tag;
    }
}
