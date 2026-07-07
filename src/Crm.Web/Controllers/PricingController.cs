using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Infrastructure.Data;

namespace Crm.Web.Controllers;

/// <summary>صفحه تعرفه سایت عمومی — از داده پلن‌های پنل مالک تغذیه می‌شود.</summary>
public class PricingController : Controller
{
    private readonly CrmDbContext _db;

    public PricingController(CrmDbContext db)
    {
        _db = db;
    }

    [HttpGet("/pricing")]
    public async Task<IActionResult> Index()
    {
        var plans = await _db.Plans.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder)
            .ToListAsync();

        return View(plans);
    }
}
