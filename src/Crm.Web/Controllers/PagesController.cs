using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Web.Data;

namespace Crm.Web.Controllers;

public class PagesController : Controller
{
    private readonly SiteDbContext _db;

    public PagesController(SiteDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> About()
    {
        var page = await _db.SitePages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Key == "about");

        return View(page);
    }

    [HttpGet]
    public IActionResult Contact()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Contact(string name, string email, string message)
    {
        TempData["Message"] = "پیام شما با موفقیت ثبت شد. به زودی با شما تماس خواهیم گرفت.";
        return RedirectToAction(nameof(Contact));
    }
}
