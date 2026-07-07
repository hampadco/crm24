using Crm.Web.Models.Help;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Areas.Portal.Controllers;

public class HelpController : PortalControllerBase
{
    [HttpGet("/Portal/help")]
    public IActionResult Index()
    {
        ViewData["PanelTitle"] = "راهنمای پورتال";
        return View(new HelpIndexViewModel
        {
            Title = "راهنمای پورتال مشتری",
            Description = "توضیح صفحات پورتال و اینکه هر بخش (تیکت، فاکتور، پروژه) چگونه از CRM و فیلد ContactRecordId به حساب شما وصل می‌شود.",
            BaseUrl = "/Portal/help",
            Topics = Services.Help.PortalHelpContent.Topics
        });
    }

    [HttpGet("/Portal/help/{slug}")]
    public IActionResult Topic(string slug)
    {
        var topic = Services.Help.PortalHelpContent.Find(slug);
        if (topic is null)
            return NotFound();

        ViewData["PanelTitle"] = $"آموزش: {topic.Title}";
        return View(new HelpTopicViewModel
        {
            Topic = topic,
            BaseUrl = "/Portal/help",
            AllTopics = Services.Help.PortalHelpContent.Topics
        });
    }
}
