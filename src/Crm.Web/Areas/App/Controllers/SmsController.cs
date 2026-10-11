using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Services;
using Crm.Web.Areas.App.Models;

namespace Crm.Web.Areas.App.Controllers;

public class SmsController : AppControllerBase
{
    private readonly CrmDbContext _db;
    private readonly MetadataService _metadata;
    private readonly TenantIntegrationService _integrations;
    private readonly SmsService _sms;

    public SmsController(
        CrmDbContext db, MetadataService metadata, TenantIntegrationService integrations, SmsService sms)
    {
        _db = db;
        _metadata = metadata;
        _integrations = integrations;
        _sms = sms;
    }

    [HttpGet("/App/sms")]
    public async Task<IActionResult> Index()
    {
        var config = await _integrations.GetAsync();
        var modules = await _metadata.GetActiveModulesAsync();
        var model = new SmsIndexModel
        {
            Ready = config.HasIpPanel,
            KavenegarSelected = config.IsKavenegar,
            FromDisplay = config.HasIpPanel ? IpPanelSmsClient.NormalizeSender(config.SmsFrom) : null,
            Modules = modules
                .Where(m => !m.IsChildModule && !m.Name.EndsWith("_lines", StringComparison.OrdinalIgnoreCase))
                .Select(m => new SmsModuleOption { Name = m.Name, Label = m.PluralLabel })
                .ToList(),
            Templates = await _db.MessageTemplates.AsNoTracking().OrderBy(t => t.Title).Take(80).ToListAsync(),
            Recent = await _db.SmsDispatches.AsNoTracking().OrderByDescending(d => d.Id).Take(12).ToListAsync()
        };

        if (config.HasIpPanel)
        {
            var credit = await _sms.GetCreditAsync();
            model.CreditText = credit.Ok && credit.Credit is decimal amount
                ? $"{amount:N0} ریال"
                : null;
        }

        ViewData["Title"] = "پیامک";
        return View(model);
    }

    [HttpGet("/App/sms/audience")]
    public async Task<IActionResult> Audience(string module, string? q)
    {
        var config = await _integrations.GetAsync();
        if (!config.HasIpPanel)
            return Json(new { items = Array.Empty<object>(), error = "پنل پیامک فعال نیست." });

        var items = await _sms.SearchAsync(module, q);
        return Json(new
        {
            items = items.Select(t => new
            {
                id = t.RecordId,
                title = string.IsNullOrWhiteSpace(t.Name) ? t.Title : t.Name,
                phone = IpPanelSmsClient.Display(t.PhoneE164)
            })
        });
    }

    [HttpPost("/App/sms/send")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(
        string? module, int[]? recordIds, bool allInModule, string? q, string? manualPhones, string? message, string? schedule)
    {
        var outcome = await _sms.SendAsync(new SmsSendRequest
        {
            ModuleName = module,
            RecordIds = recordIds ?? [],
            AllInModule = allInModule,
            Query = q,
            ManualPhones = manualPhones,
            Message = message ?? "",
            ScheduledUtc = ParseIranLocal(schedule)
        });

        TempData[outcome.Ok ? "Success" : "Error"] = outcome.Message;
        return outcome.DispatchId is int id
            ? RedirectToAction(nameof(Details), new { id })
            : RedirectToAction(nameof(Index));
    }

    [HttpGet("/App/sms/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var dispatch = await _db.SmsDispatches
            .Include(d => d.Recipients.OrderBy(r => r.Id))
            .FirstOrDefaultAsync(d => d.Id == id);
        if (dispatch is null)
            return NotFound();

        ViewData["Title"] = "جزئیات پیامک";
        return View(dispatch);
    }

    [HttpPost("/App/sms/{id:int}/refresh")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Refresh(int id)
    {
        await _sms.RefreshAsync(id);
        TempData["Success"] = "وضعیت از آی‌پی‌پنل به‌روز شد.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("/App/sms/{id:int}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var outcome = await _sms.CancelAsync(id);
        TempData[outcome.Ok ? "Success" : "Error"] = outcome.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    private static DateTime? ParseIranLocal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
            && !DateTime.TryParse(raw, out local))
            return null;

        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "Iran Standard Time" : "Asia/Tehran");
        }
        catch (TimeZoneNotFoundException)
        {
            tz = TimeZoneInfo.CreateCustomTimeZone("Iran", TimeSpan.FromMinutes(210), "Iran", "Iran");
        }

        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, tz);
    }
}
