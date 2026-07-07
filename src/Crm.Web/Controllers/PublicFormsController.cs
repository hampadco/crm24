using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Services;

namespace Crm.Web.Controllers;

/// <summary>
/// endpoint عمومی وب‌فرم‌ها و نظرسنجی‌ها — بدون Auth، با rate limit و کپچا.
/// </summary>
public class PublicFormsController : Controller
{
    /// <summary>rate limit ساده: حداکثر ۱۰ ارسال در دقیقه per-IP per-فرم.</summary>
    private static readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> RateLimits = new();

    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IDataProtector _captchaProtector;

    public PublicFormsController(CrmDbContext db, ITenantContext tenant, IDataProtectionProvider dataProtection)
    {
        _db = db;
        _tenant = tenant;
        _captchaProtector = dataProtection.CreateProtector("public-captcha");
    }

    private static bool IsRateLimited(string key)
    {
        var now = DateTime.UtcNow;
        var entry = RateLimits.AddOrUpdate(key,
            _ => (1, now),
            (_, current) => now - current.WindowStart > TimeSpan.FromMinutes(1)
                ? (1, now)
                : (current.Count + 1, current.WindowStart));
        return entry.Count > 10;
    }

    private (string Question, string Token) BuildCaptcha()
    {
        var random = Random.Shared;
        var a = random.Next(1, 10);
        var b = random.Next(1, 10);
        return ($"{a} + {b} = ?", _captchaProtector.Protect((a + b).ToString()));
    }

    private bool VerifyCaptcha(string? token, string? answer)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(answer))
            return false;
        try
        {
            return _captchaProtector.Unprotect(token) == answer.Trim();
        }
        catch
        {
            return false;
        }
    }

    private void SetTenant(int tenantId)
    {
        if (_tenant is TenantContext mutable)
            mutable.TenantId = tenantId;
    }

    // ---------- وب‌فرم ----------

    [HttpGet("/f/{key}")]
    public async Task<IActionResult> ShowForm(string key)
    {
        var form = await _db.WebForms.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(f => f.PublicKey == key && f.IsActive && !f.IsDeleted);
        if (form is null)
            return NotFound();

        SetTenant(form.TenantId);
        await LoadFormViewDataAsync(form);
        return View("WebForm", form);
    }

    [HttpPost("/f/{key}")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SubmitForm(string key)
    {
        var form = await _db.WebForms.IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.PublicKey == key && f.IsActive && !f.IsDeleted);
        if (form is null)
            return NotFound();

        SetTenant(form.TenantId);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
        if (IsRateLimited($"f:{key}:{ip}"))
        {
            ViewBag.Error = "تعداد درخواست‌ها بیش از حد مجاز است. کمی بعد دوباره تلاش کنید.";
            await LoadFormViewDataAsync(form);
            return View("WebForm", form);
        }

        if (form.UseCaptcha && !VerifyCaptcha(Request.Form["captchaToken"], Request.Form["captchaAnswer"]))
        {
            ViewBag.Error = "پاسخ سوال امنیتی درست نیست.";
            await LoadFormViewDataAsync(form);
            return View("WebForm", form);
        }

        var configs = ParseConfigs(form.FieldsJson);
        var values = new Dictionary<string, string?>();
        foreach (var config in configs)
        {
            values[config.Name] = config.Hidden
                ? config.DefaultValue
                : (string?)Request.Form[$"fld_{config.Name}"];
        }

        var recordService = HttpContext.RequestServices.GetRequiredService<DynamicRecordService>();
        try
        {
            var record = await recordService.CreateAsync(form.ModuleId, values);

            // ارجاع خودکار به کاربر تعیین‌شده
            if (form.AssignToUserId is int userId)
            {
                var saved = await _db.Records.FirstAsync(r => r.Id == record.Id);
                saved.OwnerUserId = userId;
            }
            form.SubmissionCount++;
            await _db.SaveChangesAsync();

            ViewBag.Success = string.IsNullOrEmpty(form.SuccessMessage)
                ? "اطلاعات شما ثبت شد."
                : form.SuccessMessage;
        }
        catch (RecordValidationException ex)
        {
            ViewBag.Error = string.Join(" — ", ex.Errors.Values);
        }

        await LoadFormViewDataAsync(form);
        return View("WebForm", form);
    }

    private async Task LoadFormViewDataAsync(WebForm form)
    {
        var metadata = HttpContext.RequestServices.GetRequiredService<MetadataService>();
        var fields = await metadata.GetFieldsAsync(form.ModuleId);
        var configs = ParseConfigs(form.FieldsJson);

        ViewBag.VisibleFields = configs
            .Where(c => !c.Hidden)
            .Select(c => new
            {
                Config = c,
                Field = fields.FirstOrDefault(f => f.Name == c.Name)
            })
            .Where(x => x.Field is not null)
            .Select(x => (x.Field!, x.Config.DefaultValue))
            .ToList();

        if (form.UseCaptcha)
        {
            var (question, token) = BuildCaptcha();
            ViewBag.CaptchaQuestion = question;
            ViewBag.CaptchaToken = token;
        }
    }

    private static List<Areas.App.Controllers.WebFormsController.WebFormFieldConfig> ParseConfigs(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<Areas.App.Controllers.WebFormsController.WebFormFieldConfig>>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch
        {
            return [];
        }
    }

    // ---------- نظرسنجی ----------

    [HttpGet("/s/{key}")]
    public async Task<IActionResult> ShowSurvey(string key)
    {
        var survey = await _db.Surveys.IgnoreQueryFilters().AsNoTracking()
            .Include(s => s.Questions.OrderBy(q => q.SortOrder))
            .FirstOrDefaultAsync(s => s.PublicKey == key && s.IsActive && !s.IsDeleted);
        if (survey is null)
            return NotFound();

        SetTenant(survey.TenantId);
        return View("Survey", survey);
    }

    [HttpPost("/s/{key}")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SubmitSurvey(string key)
    {
        var survey = await _db.Surveys.IgnoreQueryFilters()
            .Include(s => s.Questions.OrderBy(q => q.SortOrder))
            .FirstOrDefaultAsync(s => s.PublicKey == key && s.IsActive && !s.IsDeleted);
        if (survey is null)
            return NotFound();

        SetTenant(survey.TenantId);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
        if (IsRateLimited($"s:{key}:{ip}"))
        {
            ViewBag.Error = "تعداد درخواست‌ها بیش از حد مجاز است.";
            return View("Survey", survey);
        }

        var answers = new Dictionary<int, string?>();
        foreach (var question in survey.Questions)
        {
            answers[question.Id] = question.Type == SurveyQuestionType.MultiChoice
                ? string.Join("، ", Request.Form[$"q_{question.Id}"].Where(v => !string.IsNullOrEmpty(v)))
                : (string?)Request.Form[$"q_{question.Id}"];
        }

        var name = ((string?)Request.Form["respondentName"])?.Trim();
        var phone = ((string?)Request.Form["respondentPhone"])?.Trim();

        _db.SurveyResponses.Add(new SurveyResponse
        {
            SurveyId = survey.Id,
            TenantId = survey.TenantId,
            RespondentName = name,
            RespondentPhone = phone,
            AnswersJson = JsonSerializer.Serialize(answers)
        });
        await _db.SaveChangesAsync();

        // تبدیل شرکت‌کننده جدید به سرنخ
        if (survey.ConvertToLead && !string.IsNullOrEmpty(name))
        {
            var metadata = HttpContext.RequestServices.GetRequiredService<MetadataService>();
            var leadsModule = await metadata.GetModuleByNameAsync("leads");
            if (leadsModule is not null)
            {
                var recordService = HttpContext.RequestServices.GetRequiredService<DynamicRecordService>();
                try
                {
                    await recordService.CreateAsync(leadsModule.Id, new Dictionary<string, string?>
                    {
                        ["name"] = name,
                        ["phone"] = phone,
                        ["description"] = $"شرکت‌کننده نظرسنجی «{survey.Title}»"
                    });
                }
                catch (RecordValidationException)
                {
                    // سرنخ تکراری/نامعتبر — پاسخ نظرسنجی به هر حال ثبت شده است
                }
            }
        }

        ViewBag.Success = "از شرکت شما در نظرسنجی سپاسگزاریم.";
        return View("Survey", survey);
    }
}
