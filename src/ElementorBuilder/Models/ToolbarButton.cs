namespace ElementorBuilder.Models;

public class ToolbarButton
{
    public string Label { get; set; } = string.Empty;

    public string Icon { get; set; } = "fas fa-circle";

    public string? OnClick { get; set; }

    public string? Href { get; set; }

    public bool Primary { get; set; }

    public string? Title { get; set; }
}
