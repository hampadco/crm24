using Microsoft.EntityFrameworkCore;
using Crm.Web.Models;

namespace Crm.Web.Data;

public class SiteDbContext : DbContext
{
    public SiteDbContext(DbContextOptions<SiteDbContext> options)
        : base(options)
    {
    }

    public DbSet<Article> Articles => Set<Article>();
    public DbSet<FaqItem> FaqItems => Set<FaqItem>();
    public DbSet<NewsletterSubscriber> NewsletterSubscribers => Set<NewsletterSubscriber>();
    public DbSet<ContentCategory> ContentCategories => Set<ContentCategory>();
    public DbSet<ContentTag> ContentTags => Set<ContentTag>();
    public DbSet<ArticleTag> ArticleTags => Set<ArticleTag>();
    public DbSet<SitePage> SitePages => Set<SitePage>();
    public DbSet<AdminAccount> AdminAccounts => Set<AdminAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Article>()
            .HasIndex(a => a.Slug)
            .IsUnique();

        modelBuilder.Entity<SitePage>()
            .HasIndex(p => p.Key)
            .IsUnique();

        modelBuilder.Entity<AdminAccount>()
            .HasKey(a => a.Id);

        modelBuilder.Entity<ContentCategory>()
            .HasIndex(c => new { c.Slug, c.Type })
            .IsUnique();

        modelBuilder.Entity<ContentTag>()
            .HasIndex(t => t.Slug)
            .IsUnique();

        modelBuilder.Entity<ArticleTag>()
            .HasKey(t => new { t.ArticleId, t.TagId });

        modelBuilder.Entity<Article>()
            .HasOne(a => a.Category)
            .WithMany()
            .HasForeignKey(a => a.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<ArticleTag>()
            .HasOne(t => t.Article)
            .WithMany(a => a.Tags)
            .HasForeignKey(t => t.ArticleId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ArticleTag>()
            .HasOne(t => t.Tag)
            .WithMany()
            .HasForeignKey(t => t.TagId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
