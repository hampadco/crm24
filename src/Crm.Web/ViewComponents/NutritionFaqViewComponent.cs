using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Web.Data;
using Crm.Web.Models;

namespace Crm.Web.ViewComponents;

public class NutritionFaqViewComponent : ViewComponent
{
    private readonly SiteDbContext _db;

    public NutritionFaqViewComponent(SiteDbContext db)
    {
        _db = db;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var faqs = await _db.FaqItems
            .AsNoTracking()
            .OrderBy(f => f.SortOrder)
            .Take(3)
            .ToListAsync();

        return View(faqs);
    }
}
