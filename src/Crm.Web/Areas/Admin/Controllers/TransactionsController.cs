using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Crm.Web.Models.Admin;
using Crm.Web.Services;

namespace Crm.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class TransactionsController : Controller
{
    private readonly PlatformAdminService _platform;

    public TransactionsController(PlatformAdminService platform)
    {
        _platform = platform;
    }

    public async Task<IActionResult> Index(string? q, int page = 1)
    {
        var listQuery = new PlatformListQuery { Q = q, Page = page };
        var model = await _platform.GetTransactionsAsync(listQuery);
        ViewBag.ListQuery = listQuery;
        if (!string.IsNullOrWhiteSpace(listQuery.Q))
            ViewBag.PaginationRoutes = new Dictionary<string, object?> { ["q"] = listQuery.Q };
        return View(model);
    }
}
