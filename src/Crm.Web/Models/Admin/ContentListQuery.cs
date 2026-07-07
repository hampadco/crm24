namespace Crm.Web.Models.Admin;

public class ContentListQuery
{
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 15;

    public int NormalizedPage => Page < 1 ? 1 : Page;

    public int NormalizedPageSize => PageSize switch
    {
        < 5 => 15,
        > 100 => 100,
        _ => PageSize
    };
}
