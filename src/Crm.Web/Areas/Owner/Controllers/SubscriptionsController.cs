using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Areas.Owner.Controllers;

public class SubscriptionsController : OwnerControllerBase
{
    public IActionResult Index() =>
        RedirectToAction("Index", "Subscriptions", new { area = "Admin" });

    public IActionResult Create(int tenantId) =>
        RedirectToAction("Create", "Subscriptions", new { area = "Admin", tenantId });
}
