using ElementorBuilder.Abstractions;
using ElementorBuilder.Helpers;
using ElementorBuilder.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Crm.Web.Data;

namespace Crm.Web.Services;

public class SiteElementorMediaUsageChecker : IElementorMediaUsageChecker
{
    private readonly SiteDbContext _db;
    private readonly ElementorBuilderOptions _options;

    public SiteElementorMediaUsageChecker(SiteDbContext db, IOptions<ElementorBuilderOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<bool> IsUrlInUseAsync(string url, CancellationToken cancellationToken = default)
    {
        var normalized = ElementorMediaUrlHelper.NormalizeMediaUrl(url);
        if (normalized is null)
            return false;

        var referenced = await GetAllReferencedUrlsAsync(cancellationToken);
        return referenced.Contains(normalized);
    }

    private async Task<HashSet<string>> GetAllReferencedUrlsAsync(CancellationToken cancellationToken)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var articles = await _db.Articles
            .AsNoTracking()
            .Select(a => new { a.ThumbnailUrl, a.Content })
            .ToListAsync(cancellationToken);

        foreach (var article in articles)
        {
            AddDirect(urls, article.ThumbnailUrl);
            urls.UnionWith(ElementorMediaUrlHelper.ExtractMediaUrlsFromHtml(article.Content, _options));
        }

        var pages = await _db.SitePages
            .AsNoTracking()
            .Select(p => new { p.HeroImageUrl, p.Content })
            .ToListAsync(cancellationToken);

        foreach (var page in pages)
        {
            AddDirect(urls, page.HeroImageUrl);
            urls.UnionWith(ElementorMediaUrlHelper.ExtractMediaUrlsFromHtml(page.Content, _options));
        }

        return urls;
    }

    private void AddDirect(HashSet<string> urls, string? directUrl)
    {
        var normalized = ElementorMediaUrlHelper.NormalizeMediaUrl(directUrl);
        if (normalized is not null && ElementorMediaUrlHelper.IsManagedUploadUrl(normalized, _options))
            urls.Add(normalized);
    }
}
