using System.Text.RegularExpressions;

namespace Crm.Web.Services;

public static class SlugHelper
{
    public static string From(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return string.Empty;

        var slug = source.Trim().ToLowerInvariant();
        slug = Regex.Replace(slug, @"\s+", "-");
        slug = Regex.Replace(slug, @"[^\p{L}\p{N}\-]", "");
        slug = Regex.Replace(slug, @"-+", "-").Trim('-');
        return slug;
    }
}
