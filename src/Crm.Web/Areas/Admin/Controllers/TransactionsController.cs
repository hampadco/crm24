using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    public async Task<IActionResult> Index(string? q)
    {
        var model = await _platform.GetTransactionsAsync(q);
        ViewData["Search"] = q;
        return View(model);
    }
}
