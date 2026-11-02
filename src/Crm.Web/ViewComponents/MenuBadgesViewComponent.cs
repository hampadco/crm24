using Crm.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.ViewComponents;

public class MenuBadgesViewComponent : ViewComponent
{
    private readonly MenuBadgeService _badges;

    public MenuBadgesViewComponent(MenuBadgeService badges)
    {
        _badges = badges;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var area = ViewContext.RouteData.Values["area"]?.ToString();
        var counts = area == "App"
            ? await _badges.GetAsync()
            : new MenuBadgeCounts();
        HttpContext.Items["MenuBadges"] = counts;
        return Content(string.Empty);
    }
}
