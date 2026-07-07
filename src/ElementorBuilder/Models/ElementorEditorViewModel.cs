namespace ElementorBuilder.Models;

public class ElementorEditorViewModel
{
    public string? Content { get; set; }

    public string? ContentFieldId { get; set; }

    public string? DraftStorageKey { get; set; }

    public string CancelUrl { get; set; } = "/";

    public IList<ToolbarButton> ExtraToolbarButtons { get; set; } = new List<ToolbarButton>();
}
