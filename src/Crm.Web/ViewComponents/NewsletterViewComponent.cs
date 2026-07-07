using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.ViewComponents;

public class NewsletterViewComponent : ViewComponent
{
    public Task<IViewComponentResult> InvokeAsync()
    {
        return Task.FromResult<IViewComponentResult>(View());
    }
}
