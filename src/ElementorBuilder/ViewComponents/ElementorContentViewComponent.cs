using System.Text.RegularExpressions;
using ElementorBuilder.Models;
using ElementorBuilder.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ElementorBuilder.ViewComponents;

public class ElementorContentViewComponent : ViewComponent
{
    private readonly ElementorBuilderOptions _options;

    public ElementorContentViewComponent(IOptions<ElementorBuilderOptions> options)
    {
        _options = options.Value;
    }

    public IViewComponentResult Invoke(string html, string? wrapperClass = null)
    {
        ViewBag.Options = _options;
        return View(new ElementorContentViewModel
        {
            Html = PreparePublicHtml(html),
            WrapperClass = wrapperClass ?? "content"
        });
    }

    /// <summary>
    /// Removes inline desktop grid-template-columns so responsive CSS variables apply on public pages.
    /// </summary>
    private static string PreparePublicHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return html;

        return Regex.Replace(
            html,
            @"(class=""elementor-columns""[^>]*\sstyle="")([^""]*)("")",
            match =>
            {
                var style = Regex.Replace(
                    match.Groups[2].Value,
                    @"\s*grid-template-columns\s*:\s*[^;""']+\s*;?",
                    " ",
                    RegexOptions.IgnoreCase).Trim();
                return match.Groups[1].Value + style + match.Groups[3].Value;
            },
            RegexOptions.IgnoreCase);
    }
}
