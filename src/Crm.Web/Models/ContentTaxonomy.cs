namespace Crm.Web.Models;

using System.ComponentModel.DataAnnotations;

public enum ContentCategoryType
{
    Article = 1
}

public class ContentCategory
{
    public int Id { get; set; }

    [Display(Name = "نام دسته")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Slug")]
    public string Slug { get; set; } = string.Empty;

    [Display(Name = "نوع")]
    public ContentCategoryType Type { get; set; }

    [Display(Name = "ترتیب")]
    public int SortOrder { get; set; }
}

public class ContentTag
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
}

public class ArticleTag
{
    public int ArticleId { get; set; }
    public Article Article { get; set; } = null!;
    public int TagId { get; set; }
    public ContentTag Tag { get; set; } = null!;
}

public static class ContentCategoryTypeLabels
{
    public static string GetLabel(ContentCategoryType type) => type switch
    {
        ContentCategoryType.Article => "مقالات",
        _ => type.ToString()
    };
}
