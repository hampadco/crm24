using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.ViewComponents;

public class HeroBannerViewComponent : ViewComponent
{
    public Task<IViewComponentResult> InvokeAsync()
    {
        var model = new HeroBannerViewModel
        {
            BackgroundUrl = "/images/home/hero.jpg",
            Headline = "نرم‌افزار مدیریت ارتباط با مشتری",
            Subtitle = "در هر مرحله از چرخه فروش، از سرنخ تا ایجاد مشتری وفادار، کنار شما خواهد بود."
        };

        return Task.FromResult<IViewComponentResult>(View(model));
    }
}

public class HeroBannerViewModel
{
    public string BackgroundUrl { get; set; } = string.Empty;
    public string Headline { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
}
