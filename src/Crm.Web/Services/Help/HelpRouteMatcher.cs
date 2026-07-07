using System.Text.RegularExpressions;
using Crm.Web.Models.Help;

namespace Crm.Web.Services.Help;

/// <summary>یافتن موضوع آموزش متناظر با مسیر فعلی UI.</summary>
public static class HelpRouteMatcher
{
    private sealed record RouteCandidate(string Slug, string TopicTitle, string Label, string Route, int Score);

    public static PageHelpLinkModel? FindForPath(string? area, string path)
    {
        if (string.IsNullOrWhiteSpace(area))
            return null;

        var (topics, baseUrl) = area switch
        {
            "App" => (AppHelpContent.Topics, "/App/help"),
            "Admin" => (AdminHelpContent.Topics, "/Admin/help"),
            "Portal" => (PortalHelpContent.Topics, "/Portal/help"),
            _ => ((List<HelpTopic>?)null, (string?)null)
        };

        if (topics is null || string.IsNullOrEmpty(baseUrl))
            return null;

        var normalizedPath = NormalizePath(path);
        var normalizedBase = NormalizePath(baseUrl);

        if (normalizedPath.Equals(normalizedBase, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedBase + "/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        RouteCandidate? best = null;

        foreach (var topic in topics)
        {
            foreach (var page in topic.Pages)
                Consider(topic.Slug, topic.Title, page.Title, page.Route, normalizedPath, ref best);

            foreach (var section in topic.Sections)
            {
                if (!string.IsNullOrWhiteSpace(section.Route))
                    Consider(topic.Slug, topic.Title, section.Title, section.Route, normalizedPath, ref best);
            }

            foreach (var relation in topic.Relations)
            {
                if (string.IsNullOrWhiteSpace(relation.TargetRoute))
                    continue;

                var slug = string.IsNullOrWhiteSpace(relation.HelpSlug) ? topic.Slug : relation.HelpSlug;
                Consider(slug, topic.Title, relation.Target, relation.TargetRoute, normalizedPath, ref best);
            }
        }

        if (best is null)
            return null;

        return new PageHelpLinkModel
        {
            Url = $"{baseUrl}/{best.Slug}",
            TopicTitle = best.TopicTitle,
            MatchedLabel = best.Label
        };
    }

    private static void Consider(
        string slug,
        string topicTitle,
        string label,
        string route,
        string path,
        ref RouteCandidate? best)
    {
        var score = ScoreRoute(route, path);
        if (score <= 0)
            return;

        if (best is null || score > best.Score)
            best = new RouteCandidate(slug, topicTitle, label, route, score);
    }

    private static int ScoreRoute(string route, string path)
    {
        if (string.IsNullOrWhiteSpace(route))
            return 0;

        var routePath = NormalizePath(route.Split('?', '#')[0]);
        if (routePath.Length == 0)
            return 0;

        if (path.Equals(routePath, StringComparison.OrdinalIgnoreCase))
            return 10_000 + routePath.Length;

        if (routePath.Contains('{', StringComparison.Ordinal))
        {
            var regex = "^" + Regex.Escape(routePath)
                .Replace("\\{id\\}", "\\d+", StringComparison.Ordinal)
                .Replace("\\{module\\}", "[^/]+", StringComparison.Ordinal)
                .Replace("\\{slug\\}", "[^/]+", StringComparison.Ordinal) + "$";

            if (Regex.IsMatch(path, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return 9_000 + routePath.Length;
        }

        if (path.StartsWith(routePath + "/", StringComparison.OrdinalIgnoreCase))
            return routePath.Length;

        if (routePath.Equals("/admin", StringComparison.OrdinalIgnoreCase)
            && path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase)
            && (path.Length == 6 || path[6] == '/'))
        {
            return 80;
        }

        return 0;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        var value = path.Split('?', '#')[0].Trim();
        if (!value.StartsWith('/'))
            value = "/" + value;

        value = value.TrimEnd('/');
        return string.IsNullOrEmpty(value) ? "/" : value.ToLowerInvariant();
    }
}
