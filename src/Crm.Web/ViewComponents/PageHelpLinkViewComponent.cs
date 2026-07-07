using Crm.Web.Services.Help;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.ViewComponents;

public class PageHelpLinkViewComponent : ViewComponent
{
    public IViewComponentResult Invoke()
    {
        var area = ViewContext.RouteData.Values["area"]?.ToString();
        var path = HttpContext.Request.Path.Value ?? "/";
        var match = HelpRouteMatcher.FindForPath(area, path);

        if (match is null)
            return Content(string.Empty);

        return View(match);
    }
}
