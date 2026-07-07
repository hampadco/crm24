using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Areas.Owner.Controllers;

public class PlansController : OwnerControllerBase
{
    public IActionResult Index() =>
        RedirectToAction("Index", "Plans", new { area = "Admin" });

    public IActionResult Create() =>
        RedirectToAction("Create", "Plans", new { area = "Admin" });

    public IActionResult Edit(int id) =>
        RedirectToAction("Edit", "Plans", new { area = "Admin", id });
}
