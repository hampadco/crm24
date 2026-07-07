using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Areas.Owner.Controllers;

public class DashboardController : OwnerControllerBase
{
    public IActionResult Index() => RedirectToAction("Index", "Dashboard", new { area = "Admin" });
}
