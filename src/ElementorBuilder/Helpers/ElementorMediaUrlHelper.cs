using System.Text.RegularExpressions;
using ElementorBuilder.Options;

namespace ElementorBuilder.Helpers;

public static partial class ElementorMediaUrlHelper
{
    public static string? NormalizeMediaUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var trimmed = url.Trim();
        if (trimmed.StartsWith("~/", StringComparison.Ordinal))
            trimmed = trimmed[1..];

        if (!trimmed.StartsWith('/'))
            return null;

        var queryIndex = trimmed.IndexOf('?');
        if (queryIndex >= 0)
            trimmed = trimmed[..queryIndex];

        var hashIndex = trimmed.IndexOf('#');
        if (hashIndex >= 0)
            trimmed = trimmed[..hashIndex];

        return trimmed;
    }

    public static IEnumerable<string> GetManagedUrlPrefixes(ElementorBuilderOptions options)
    {
        yield return ToUrlPrefix(options.UploadFolder);

        foreach (var folder in options.AdditionalManagedUploadFolders ?? [])
        {
            if (string.IsNullOrWhiteSpace(folder))
                continue;

            yield return ToUrlPrefix(folder);
        }
    }

    public static bool IsManagedUploadUrl(string? url, ElementorBuilderOptions options)
    {
        var normalized = NormalizeMediaUrl(url);
        if (normalized is null)
            return false;

        return GetManagedUrlPrefixes(options).Any(prefix =>
            normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public static HashSet<string> CollectMediaUrls(
        ElementorBuilderOptions options,
        string? htmlContent,
        params string?[] directUrls)
    {
        var urls = ExtractMediaUrlsFromHtml(htmlContent, options);
        foreach (var directUrl in directUrls)
        {
            var normalized = NormalizeMediaUrl(directUrl);
            if (normalized is not null && IsManagedUploadUrl(normalized, options))
                urls.Add(normalized);
        }

        return urls;
    }

    public static IEnumerable<string> GetRemovedUrls(
        IEnumerable<string> previousUrls,
        IEnumerable<string> currentUrls)
    {
        var current = new HashSet<string>(
            currentUrls.Select(NormalizeMediaUrl).Where(u => u is not null).Cast<string>(),
            StringComparer.OrdinalIgnoreCase);

        return previousUrls
            .Select(NormalizeMediaUrl)
            .Where(u => u is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(u => !current.Contains(u));
    }

    public static HashSet<string> ExtractMediaUrlsFromHtml(string? html, ElementorBuilderOptions options)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(html))
            return urls;

        foreach (Match match in SrcHrefRegex().Matches(html))
        {
            AddIfManaged(urls, match.Groups[1].Value, options);
        }

        foreach (Match match in CssUrlRegex().Matches(html))
        {
            AddIfManaged(urls, match.Groups[1].Value, options);
        }

        return urls;
    }

    private static void AddIfManaged(HashSet<string> urls, string? rawUrl, ElementorBuilderOptions options)
    {
        var normalized = NormalizeMediaUrl(rawUrl);
        if (normalized is not null && IsManagedUploadUrl(normalized, options))
            urls.Add(normalized);
    }

    private static string ToUrlPrefix(string folder) =>
        $"/{folder.Trim('/').Replace('\\', '/')}/";

    [GeneratedRegex(@"(?:src|href)\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SrcHrefRegex();

    [GeneratedRegex(@"url\(\s*[""']?([^""')\s]+)[""']?\s*\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CssUrlRegex();
}
