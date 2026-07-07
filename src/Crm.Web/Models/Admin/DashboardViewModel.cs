namespace Crm.Web.Models.Admin;

public class DashboardRecentItem
{
    public string Type { get; set; } = string.Empty;
    public string TypeLabel { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string DateLabel { get; set; } = string.Empty;
    public string EditUrl { get; set; } = string.Empty;
}

public class DashboardViewModel
{
    public string TodayJalali { get; set; } = string.Empty;
    public int ArticleCount { get; set; }
    public int FaqCount { get; set; }
    public int SitePageCount { get; set; }
    public int SubscriberCount { get; set; }
    public IReadOnlyList<DashboardRecentItem> RecentItems { get; set; } = Array.Empty<DashboardRecentItem>();
}
