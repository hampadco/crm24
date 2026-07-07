using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Web.Areas.App.Controllers;

/// <summary>نظرسنجی‌ها: سوالات متنی/طیفی/انتخابی + لینک عمومی + مشاهده پاسخ‌ها.</summary>
public class SurveysController : AppControllerBase
{
    public static string TypeLabel(SurveyQuestionType type) => type switch
    {
        SurveyQuestionType.Text => "متنی",
        SurveyQuestionType.Scale => "طیفی (۱ تا ۵)",
        SurveyQuestionType.SingleChoice => "تک‌انتخابی",
        _ => "چندانتخابی"
    };

    public class QuestionInput
    {
        public string Text { get; set; } = string.Empty;
        public SurveyQuestionType Type { get; set; }
        public string? Options { get; set; }
    }

    private readonly CrmDbContext _db;

    public SurveysController(CrmDbContext db) => _db = db;

    [HttpGet("/App/surveys")]
    public async Task<IActionResult> Index()
    {
        var surveys = await _db.Surveys.AsNoTracking()
            .Include(s => s.Questions)
            .Include(s => s.Responses)
            .OrderByDescending(s => s.Id).Take(300).ToListAsync();
        ViewData["Title"] = "نظرسنجی‌ها";
        return View(surveys);
    }

    [HttpGet("/App/surveys/create")]
    public IActionResult Create()
    {
        ViewData["Title"] = "نظرسنجی جدید";
        return View("Form", new Survey());
    }

    [HttpGet("/App/surveys/{id:int}/edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var survey = await _db.Surveys.AsNoTracking()
            .Include(s => s.Questions.OrderBy(q => q.SortOrder))
            .FirstOrDefaultAsync(s => s.Id == id);
        if (survey is null)
            return NotFound();
        ViewData["Title"] = $"ویرایش {survey.Title}";
        return View("Form", survey);
    }

    [HttpPost("/App/surveys/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int id, string title, string? description,
        bool isActive, bool convertToLead, bool isTicketSurvey, List<QuestionInput> questions)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            TempData["Error"] = "عنوان نظرسنجی الزامی است.";
            return RedirectToAction(nameof(Index));
        }

        Survey survey;
        if (id == 0)
        {
            survey = new Survey { PublicKey = Guid.NewGuid().ToString("N")[..12] };
            _db.Surveys.Add(survey);
        }
        else
        {
            survey = await _db.Surveys.Include(s => s.Questions).FirstAsync(s => s.Id == id);
            _db.SurveyQuestions.RemoveRange(survey.Questions);
            survey.Questions.Clear();
        }

        survey.Title = title.Trim();
        survey.Description = description?.Trim();
        survey.IsActive = isActive;
        survey.ConvertToLead = convertToLead;
        survey.IsTicketSurvey = isTicketSurvey;

        var sortOrder = 0;
        foreach (var input in questions.Where(q => !string.IsNullOrWhiteSpace(q.Text)))
        {
            var options = (input.Options ?? "")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            survey.Questions.Add(new SurveyQuestion
            {
                Text = input.Text.Trim(),
                Type = input.Type,
                OptionsJson = JsonSerializer.Serialize(options),
                SortOrder = ++sortOrder
            });
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = "نظرسنجی ذخیره شد.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("/App/surveys/{id:int}/responses")]
    public async Task<IActionResult> Responses(int id)
    {
        var survey = await _db.Surveys.AsNoTracking()
            .Include(s => s.Questions.OrderBy(q => q.SortOrder))
            .Include(s => s.Responses.OrderByDescending(r => r.Id))
            .FirstOrDefaultAsync(s => s.Id == id);
        if (survey is null)
            return NotFound();

        ViewData["Title"] = $"پاسخ‌های {survey.Title}";
        return View(survey);
    }
}
