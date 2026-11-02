namespace Crm.Web.Models;

public sealed class NxPageHeadModel
{
    public string Title { get; init; } = "";
    public string? Parent { get; init; }
    public string? ParentUrl { get; init; }
    public string? ActionText { get; init; }
    public string? ActionUrl { get; init; }
}

public sealed class NxStatModel
{
    public string Label { get; init; } = "";
    public string Value { get; init; } = "";
    public string Icon { get; init; } = "fi fi-rr-chart-histogram";
    public string? Href { get; init; }
    public string? HrefText { get; init; }
}
