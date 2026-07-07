using Microsoft.AspNetCore.Mvc;
using Crm.Core.Entities;

namespace Crm.Web.Areas.Owner.Controllers;

public class TenantsController : OwnerControllerBase
{
    public IActionResult Index(string? q, TenantStatus? status) =>
        RedirectToAction("Index", "Tenants", new { area = "Admin", q, status });

    public IActionResult Details(int id) =>
        RedirectToAction("Details", "Tenants", new { area = "Admin", id });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SetStatus(int id, TenantStatus status) =>
        RedirectToAction("SetStatus", "Tenants", new { area = "Admin", id, status });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Impersonate(int id) =>
        RedirectToAction("Impersonate", "Tenants", new { area = "Admin", id });
}
