using ElementorBuilder.Models;
using ElementorBuilder.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ElementorBuilder.ViewComponents;

public class ElementorEditorViewComponent : ViewComponent
{
    private readonly ElementorBuilderOptions _options;

    public ElementorEditorViewComponent(IOptions<ElementorBuilderOptions> options)
    {
        _options = options.Value;
    }

    public IViewComponentResult Invoke(ElementorEditorViewModel model)
    {
        model.ContentFieldId ??= _options.ContentFieldId;
        model.DraftStorageKey ??= _options.DraftStorageKey;
        ViewBag.Options = _options;
        return View(model);
    }
}
