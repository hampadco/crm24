using Crm.Web.Models.Help;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class HelpController : Controller
{
    [HttpGet("/Admin/help")]
    public IActionResult Index()
    {
        ViewData["PanelTitle"] = "مرکز آموزش";
        return View(new HelpIndexViewModel
        {
            Title = "آموزش پنل مدیریت BamaCRM",
            Description = "راهنمای مدیریت مشتریان SaaS، اشتراک‌ها، تراکنش‌ها و محتوای سایت — با توضیح روابط Tenant، Plan، Subscription و فیلدهای هر صفحه.",
            BaseUrl = "/Admin/help",
            Topics = Services.Help.AdminHelpContent.Topics
        });
    }

    [HttpGet("/Admin/help/{slug}")]
    public IActionResult Topic(string slug)
    {
        var topic = Services.Help.AdminHelpContent.Find(slug);
        if (topic is null)
            return NotFound();

        ViewData["PanelTitle"] = $"آموزش: {topic.Title}";
        return View(new HelpTopicViewModel
        {
            Topic = topic,
            BaseUrl = "/Admin/help",
            AllTopics = Services.Help.AdminHelpContent.Topics
        });
    }
}
