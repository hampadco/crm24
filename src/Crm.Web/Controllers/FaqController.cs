using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Web.Data;
using Crm.Web.Models;

namespace Crm.Web.Controllers;

public class FaqController : Controller
{
    private readonly SiteDbContext _db;

    public FaqController(SiteDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var items = await _db.FaqItems
            .AsNoTracking()
            .OrderBy(f => f.SortOrder)
            .ToListAsync();

        return View(new FaqListingViewModel { Items = items });
    }
}
