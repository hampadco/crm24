using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Services;

namespace Crm.Web.Areas.App.Controllers;

/// <summary>کمپین‌های تبلیغاتی: بودجه، اعضا (سرنخ/مخاطب/فرصت) و محاسبه ROI.</summary>
public class CampaignsController : AppControllerBase
{
    public static string StatusLabel(CampaignStatus status) => status switch
    {
        CampaignStatus.Planned => "برنامه‌ریزی‌شده",
        CampaignStatus.Active => "فعال",
        CampaignStatus.Finished => "پایان‌یافته",
        _ => "لغوشده"
    };

    private static readonly string[] MemberModules = ["leads", "contacts", "opportunities"];

    private readonly CrmDbContext _db;
    private readonly MetadataService _metadata;

    public CampaignsController(CrmDbContext db, MetadataService metadata)
    {
        _db = db;
        _metadata = metadata;
    }

    [HttpGet("/App/campaigns")]
    public async Task<IActionResult> Index()
    {
        var campaigns = await _db.Campaigns.AsNoTracking()
            .Include(c => c.Members)
            .OrderByDescending(c => c.Id).Take(300).ToListAsync();
        ViewData["Title"] = "کمپین‌ها";
        return View(campaigns);
    }

    [HttpGet("/App/campaigns/create")]
    public IActionResult Create()
    {
        ViewData["Title"] = "کمپین جدید";
        return View("Form", new Campaign { StartUtc = DateTime.UtcNow, EndUtc = DateTime.UtcNow.AddMonths(1) });
    }

    [HttpGet("/App/campaigns/{id:int}/edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var campaign = await _db.Campaigns.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (campaign is null)
            return NotFound();
        ViewData["Title"] = $"ویرایش {campaign.Name}";
        return View("Form", campaign);
    }

    [HttpPost("/App/campaigns/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int id, string name, string? channel, string? description,
        DateTime startUtc, DateTime endUtc, decimal budget, decimal actualCost, CampaignStatus status)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "نام کمپین الزامی است.";
            return RedirectToAction(nameof(Index));
        }

        Campaign campaign;
        if (id == 0)
        {
            campaign = new Campaign();
            _db.Campaigns.Add(campaign);
        }
        else
        {
            campaign = await _db.Campaigns.FirstAsync(c => c.Id == id);
        }

        campaign.Name = name.Trim();
        campaign.Channel = channel?.Trim();
        campaign.Description = description?.Trim();
        campaign.StartUtc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        campaign.EndUtc = DateTime.SpecifyKind(endUtc, DateTimeKind.Utc);
        campaign.Budget = budget;
        campaign.ActualCost = actualCost;
        campaign.Status = status;

        await _db.SaveChangesAsync();
        TempData["Success"] = "کمپین ذخیره شد.";
        return RedirectToAction(nameof(Details), new { id = campaign.Id });
    }

    [HttpGet("/App/campaigns/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var campaign = await _db.Campaigns.AsNoTracking()
            .Include(c => c.Members)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (campaign is null)
            return NotFound();

        // عنوان رکوردهای عضو + محاسبه ROI از فرصت‌های برنده مرتبط
        var recordIds = campaign.Members.Select(m => m.RecordId).Distinct().ToList();
        var records = await _db.Records.AsNoTracking()
            .Where(r => recordIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id);
        ViewBag.MemberRecords = records;

        decimal wonAmount = 0;
        foreach (var member in campaign.Members.Where(m => m.ModuleName == "opportunities"))
        {
            if (!records.TryGetValue(member.RecordId, out var record))
                continue;
            var data = DynamicRecordService.ParseData(record);
            if (data.GetValueOrDefault("stage") == "won" &&
                decimal.TryParse(data.GetValueOrDefault("amount"), out var amount))
                wonAmount += amount;
        }
        ViewBag.WonAmount = wonAmount;

        // گزینه‌های افزودن عضو
        var options = new Dictionary<string, Dictionary<int, string>>();
        foreach (var moduleName in MemberModules)
        {
            var module = await _metadata.GetModuleByNameAsync(moduleName);
            if (module is null)
                continue;
            var existing = campaign.Members
                .Where(m => m.ModuleName == moduleName).Select(m => m.RecordId).ToHashSet();
            options[moduleName] = await _db.Records.AsNoTracking()
                .Where(r => r.ModuleId == module.Id && !existing.Contains(r.Id))
                .OrderByDescending(r => r.Id).Take(200)
                .ToDictionaryAsync(r => r.Id, r => r.Title);
        }
        ViewBag.MemberOptions = options;

        ViewData["Title"] = campaign.Name;
        return View(campaign);
    }

    [HttpPost("/App/campaigns/{id:int}/members")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMember(int id, string moduleName, int recordId)
    {
        if (!MemberModules.Contains(moduleName))
            return BadRequest();

        var exists = await _db.CampaignMembers
            .AnyAsync(m => m.CampaignId == id && m.ModuleName == moduleName && m.RecordId == recordId);
        if (!exists)
        {
            _db.CampaignMembers.Add(new CampaignMember { CampaignId = id, ModuleName = moduleName, RecordId = recordId });
            await _db.SaveChangesAsync();
            TempData["Success"] = "عضو به کمپین افزوده شد.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("/App/campaigns/members/{memberId:int}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveMember(int memberId)
    {
        var member = await _db.CampaignMembers.FirstOrDefaultAsync(m => m.Id == memberId);
        if (member is null)
            return NotFound();

        _db.CampaignMembers.Remove(member);
        await _db.SaveChangesAsync();
        TempData["Success"] = "عضو حذف شد.";
        return RedirectToAction(nameof(Details), new { id = member.CampaignId });
    }
}
