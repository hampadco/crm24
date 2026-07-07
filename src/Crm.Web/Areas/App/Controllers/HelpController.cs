using Crm.Web.Models.Help;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Areas.App.Controllers;

public class HelpController : AppControllerBase
{
    [HttpGet("/App/help")]
    public IActionResult Index()
    {
        ViewData["PanelTitle"] = "مرکز آموزش";
        return View(new HelpIndexViewModel
        {
            Title = "مرکز آموزش BaMaCRM",
            Description = "آموزش تک‌تک صفحات، فیلدها و روابط بین بخش‌ها — از سرنخ تا فاکتور، پروژه، تیکت و پورتال. برای شروع، «نقشه روابط CRM» را ببینید.",
            BaseUrl = "/App/help",
            Topics = Services.Help.AppHelpContent.Topics
        });
    }

    [HttpGet("/App/help/{slug}")]
    public IActionResult Topic(string slug)
    {
        var topic = Services.Help.AppHelpContent.Find(slug);
        if (topic is null)
            return NotFound();

        ViewData["PanelTitle"] = $"آموزش: {topic.Title}";
        return View(new HelpTopicViewModel
        {
            Topic = topic,
            BaseUrl = "/App/help",
            AllTopics = Services.Help.AppHelpContent.Topics
        });
    }
}
