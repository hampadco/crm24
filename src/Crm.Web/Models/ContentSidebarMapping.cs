namespace Crm.Web.Models;

public static class ContentSidebarMapping
{
    public static ContentSidebarItem ToSidebar(this Article article) =>
        new(article.Title, article.Slug, article.ThumbnailUrl, article.PublishedAt);
}
