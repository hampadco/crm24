using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Web.Areas.App.Controllers;

/// <summary>قراردادهای خدمات: بازه، سقف تیکت و اتصال به تیکت.</summary>
public class ServiceContractsController : AppControllerBase
{
    private readonly CrmDbContext _db;

    public ServiceContractsController(CrmDbContext db) => _db = db;

    [HttpGet("/App/contracts")]
    public async Task<IActionResult> Index()
    {
        var contracts = await _db.ServiceContracts.AsNoTracking()
            .OrderByDescending(c => c.Id).Take(300).ToListAsync();
        ViewData["Title"] = "قراردادهای خدمات";
        return View(contracts);
    }

    [HttpGet("/App/contracts/create")]
    public IActionResult Create()
    {
        ViewData["Title"] = "قرارداد جدید";
        return View("Form", new ServiceContract
        {
            StartUtc = DateTime.UtcNow,
            EndUtc = DateTime.UtcNow.AddYears(1)
        });
    }

    [HttpGet("/App/contracts/{id:int}/edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var contract = await _db.ServiceContracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (contract is null)
            return NotFound();
        ViewData["Title"] = $"ویرایش {contract.Name}";
        return View("Form", contract);
    }

    [HttpPost("/App/contracts/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int id, string name, string customerName,
        DateTime startUtc, DateTime endUtc, int maxTickets, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "نام قرارداد الزامی است.";
            return RedirectToAction(nameof(Index));
        }

        ServiceContract contract;
        if (id == 0)
        {
            contract = new ServiceContract();
            _db.ServiceContracts.Add(contract);
        }
        else
        {
            contract = await _db.ServiceContracts.FirstAsync(c => c.Id == id);
        }

        contract.Name = name.Trim();
        contract.CustomerName = customerName?.Trim() ?? "";
        contract.StartUtc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        contract.EndUtc = DateTime.SpecifyKind(endUtc, DateTimeKind.Utc);
        contract.MaxTickets = Math.Max(0, maxTickets);
        contract.IsActive = isActive;

        await _db.SaveChangesAsync();
        TempData["Success"] = "قرارداد ذخیره شد.";
        return RedirectToAction(nameof(Index));
    }
}
