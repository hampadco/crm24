using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Infrastructure.Data;

namespace Crm.Web.Areas.Portal.Controllers;

/// <summary>مشاهده وضعیت پروژه‌ها توسط مشتری نهایی.</summary>
public class ProjectsController : PortalControllerBase
{
    private readonly CrmDbContext _db;

    public ProjectsController(CrmDbContext db) => _db = db;

    [HttpGet("/Portal/projects")]
    public async Task<IActionResult> Index()
    {
        var projects = PortalContactRecordId is int contactId
            ? await _db.Projects.AsNoTracking()
                .Include(p => p.Tasks)
                .Where(p => p.ShowInPortal && p.ContactRecordId == contactId)
                .OrderByDescending(p => p.Id)
                .ToListAsync()
            : [];

        ViewData["Title"] = "پروژه‌های من";
        return View(projects);
    }
}
