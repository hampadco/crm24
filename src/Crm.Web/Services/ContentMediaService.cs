using ElementorBuilder.Helpers;
using ElementorBuilder.Options;
using ElementorBuilder.Services;
using Microsoft.Extensions.Options;
using Crm.Web.Models;

namespace Crm.Web.Services;

public class ContentMediaService
{
    private readonly ElementorMediaFileService _media;
    private readonly ElementorBuilderOptions _options;

    public ContentMediaService(ElementorMediaFileService media, IOptions<ElementorBuilderOptions> options)
    {
        _media = media;
        _options = options.Value;
    }

    public HashSet<string> CollectArticleMedia(Article article) =>
        ElementorMediaUrlHelper.CollectMediaUrls(_options, article.Content, article.ThumbnailUrl);

    public HashSet<string> CollectSitePageMedia(SitePage page) =>
        ElementorMediaUrlHelper.CollectMediaUrls(_options, page.Content, page.HeroImageUrl);

    public Task CleanupRemovedMediaAsync(IEnumerable<string> previousUrls, IEnumerable<string> currentUrls) =>
        _media.CleanupRemovedMediaAsync(previousUrls, currentUrls);

    public Task DeleteEntityMediaAsync(IEnumerable<string> urls) =>
        _media.DeleteMediaUrlsAsync(urls);

    public Task<(bool Success, string Message)> TryDeleteIfUnreferencedAsync(string? url) =>
        _media.TryDeleteIfUnreferencedAsync(url);
}
