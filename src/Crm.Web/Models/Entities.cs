namespace Crm.Web.Models;

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

public class Article
{
    public int Id { get; set; }

    [Display(Name = "عنوان")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Slug")]
    public string Slug { get; set; } = string.Empty;

    [Display(Name = "خلاصه")]
    public string Summary { get; set; } = string.Empty;

    [Display(Name = "تصویر")]
    public string ThumbnailUrl { get; set; } = string.Empty;

    [Display(Name = "دسته‌بندی")]
    public int? CategoryId { get; set; }

    [ValidateNever]
    public ContentCategory? Category { get; set; }

    [Display(Name = "تاریخ انتشار")]
    public DateTime PublishedAt { get; set; }

    [Display(Name = "محتوا")]
    public string Content { get; set; } = string.Empty;

    [ValidateNever]
    public ICollection<ArticleTag> Tags { get; set; } = new List<ArticleTag>();
}

public class FaqItem
{
    public int Id { get; set; }

    [Display(Name = "سؤال")]
    public string Question { get; set; } = string.Empty;

    [Display(Name = "پاسخ")]
    public string Answer { get; set; } = string.Empty;

    [Display(Name = "ترتیب")]
    public int SortOrder { get; set; }
}

public class NewsletterSubscriber
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public DateTime SubscribedAt { get; set; }
}

public class SitePage
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;

    [Display(Name = "عنوان")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "زیرعنوان")]
    public string Subtitle { get; set; } = string.Empty;

    [Display(Name = "تصویر هدر")]
    public string HeroImageUrl { get; set; } = string.Empty;

    [Display(Name = "محتوا")]
    public string Content { get; set; } = string.Empty;
}

public class AdminAccount
{
    public int Id { get; set; } = 1;
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
}
