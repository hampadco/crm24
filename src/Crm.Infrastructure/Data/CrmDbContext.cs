using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Identity;

namespace Crm.Infrastructure.Data;

public class CrmDbContext : IdentityDbContext<CrmUser, IdentityRole<int>, int>
{
    private readonly ITenantContext _tenant;

    public CrmDbContext(DbContextOptions<CrmDbContext> options, ITenantContext tenant)
        : base(options)
    {
        _tenant = tenant;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionPayment> SubscriptionPayments => Set<SubscriptionPayment>();

    public DbSet<ModuleDef> Modules => Set<ModuleDef>();
    public DbSet<FieldDef> Fields => Set<FieldDef>();
    public DbSet<PicklistValue> PicklistValues => Set<PicklistValue>();
    public DbSet<RelationDef> Relations => Set<RelationDef>();
    public DbSet<DynamicRecord> Records => Set<DynamicRecord>();

    public DbSet<Role> CrmRoles => Set<Role>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<ProfileModulePermission> ProfileModulePermissions => Set<ProfileModulePermission>();
    public DbSet<ProfileFieldPermission> ProfileFieldPermissions => Set<ProfileFieldPermission>();
    public DbSet<SharingRule> SharingRules => Set<SharingRule>();
    public DbSet<UserGroup> UserGroups => Set<UserGroup>();
    public DbSet<UserGroupMember> UserGroupMembers => Set<UserGroupMember>();

    public DbSet<Product> Products => Set<Product>();
    public DbSet<PriceBook> PriceBooks => Set<PriceBook>();
    public DbSet<PriceBookEntry> PriceBookEntries => Set<PriceBookEntry>();
    public DbSet<SalesDocument> SalesDocuments => Set<SalesDocument>();
    public DbSet<SalesDocumentLine> SalesDocumentLines => Set<SalesDocumentLine>();
    public DbSet<PaymentRecord> PaymentRecords => Set<PaymentRecord>();
    public DbSet<Installment> Installments => Set<Installment>();
    public DbSet<CommissionRule> CommissionRules => Set<CommissionRule>();
    public DbSet<CommissionEntry> CommissionEntries => Set<CommissionEntry>();

    public DbSet<WorkflowRule> WorkflowRules => Set<WorkflowRule>();
    public DbSet<WorkflowAction> WorkflowActions => Set<WorkflowAction>();
    public DbSet<WorkflowLog> WorkflowLogs => Set<WorkflowLog>();
    public DbSet<DashboardWidget> DashboardWidgets => Set<DashboardWidget>();
    public DbSet<ReportDef> Reports => Set<ReportDef>();

    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketMessage> TicketMessages => Set<TicketMessage>();
    public DbSet<SlaPolicy> SlaPolicies => Set<SlaPolicy>();
    public DbSet<ServiceContract> ServiceContracts => Set<ServiceContract>();
    public DbSet<Warranty> Warranties => Set<Warranty>();
    public DbSet<KbArticle> KbArticles => Set<KbArticle>();
    public DbSet<PortalUser> PortalUsers => Set<PortalUser>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectPhase> ProjectPhases => Set<ProjectPhase>();
    public DbSet<ProjectTask> ProjectTasks => Set<ProjectTask>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<VendorPayment> VendorPayments => Set<VendorPayment>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignMember> CampaignMembers => Set<CampaignMember>();
    public DbSet<WebForm> WebForms => Set<WebForm>();
    public DbSet<Survey> Surveys => Set<Survey>();
    public DbSet<SurveyQuestion> SurveyQuestions => Set<SurveyQuestion>();
    public DbSet<SurveyResponse> SurveyResponses => Set<SurveyResponse>();
    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<TagLink> TagLinks => Set<TagLink>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<SavedListView> SavedListViews => Set<SavedListView>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Tenant>(e =>
        {
            e.HasIndex(t => t.Slug).IsUnique();
            e.Property(t => t.Settings).HasColumnType("jsonb");
        });

        builder.Entity<CrmUser>(e =>
        {
            e.HasIndex(u => u.TenantId);
        });

        builder.Entity<Plan>(e =>
        {
            e.Property(p => p.PriceMonthly).HasPrecision(18, 0);
            e.Property(p => p.PriceYearly).HasPrecision(18, 0);
        });

        builder.Entity<Subscription>(e =>
        {
            e.Property(s => s.Amount).HasPrecision(18, 0);
            e.HasIndex(s => new { s.TenantId, s.Status });
            e.HasOne(s => s.Tenant).WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.Plan).WithMany().HasForeignKey(s => s.PlanId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SubscriptionPayment>(e =>
        {
            e.Property(p => p.Amount).HasPrecision(18, 0);
            e.HasOne(p => p.Subscription).WithMany(s => s.Payments).HasForeignKey(p => p.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ModuleDef>(e =>
        {
            e.HasIndex(m => new { m.TenantId, m.Name }).IsUnique();
        });

        builder.Entity<FieldDef>(e =>
        {
            e.HasIndex(f => new { f.TenantId, f.ModuleId, f.Name }).IsUnique();
            e.HasOne(f => f.Module).WithMany(m => m.Fields).HasForeignKey(f => f.ModuleId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PicklistValue>(e =>
        {
            e.HasOne(p => p.Field).WithMany(f => f.PicklistValues).HasForeignKey(p => p.FieldId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<DynamicRecord>(e =>
        {
            e.Property(r => r.CustomData).HasColumnType("jsonb");
            e.HasIndex(r => new { r.TenantId, r.ModuleId });
            e.HasOne(r => r.Module).WithMany().HasForeignKey(r => r.ModuleId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Role>(e =>
        {
            e.ToTable("CrmRoles");
            e.HasOne(r => r.ParentRole).WithMany(r => r.Children).HasForeignKey(r => r.ParentRoleId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ProfileModulePermission>(e =>
        {
            e.HasIndex(p => new { p.TenantId, p.ProfileId, p.ModuleId }).IsUnique();
            e.HasOne(p => p.Profile).WithMany(pr => pr.ModulePermissions).HasForeignKey(p => p.ProfileId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ProfileFieldPermission>(e =>
        {
            e.HasIndex(p => new { p.TenantId, p.ProfileId, p.FieldId }).IsUnique();
            e.HasOne(p => p.Profile).WithMany(pr => pr.FieldPermissions).HasForeignKey(p => p.ProfileId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SharingRule>(e =>
        {
            e.HasIndex(s => new { s.TenantId, s.ModuleId }).IsUnique();
        });

        builder.Entity<UserGroupMember>(e =>
        {
            e.HasIndex(m => new { m.TenantId, m.GroupId, m.UserId }).IsUnique();
            e.HasOne(m => m.Group).WithMany(g => g.Members).HasForeignKey(m => m.GroupId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Product>(e =>
        {
            e.HasIndex(p => new { p.TenantId, p.Name });
            foreach (var prop in new[] { "SalePrice", "TaxPercent", "StockQty", "ReorderPoint" })
                e.Property(prop).HasPrecision(18, 2);
        });

        builder.Entity<PriceBookEntry>(e =>
        {
            e.Property(p => p.Price).HasPrecision(18, 2);
            e.HasIndex(p => new { p.TenantId, p.PriceBookId, p.ProductId }).IsUnique();
            e.HasOne(p => p.PriceBook).WithMany(b => b.Entries).HasForeignKey(p => p.PriceBookId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.Product).WithMany().HasForeignKey(p => p.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SalesDocument>(e =>
        {
            e.HasIndex(d => new { d.TenantId, d.Kind, d.Number }).IsUnique();
            foreach (var prop in new[] { "SubTotal", "DiscountPercent", "DiscountAmount", "TaxTotal", "GrandTotal" })
                e.Property(prop).HasPrecision(18, 2);
        });

        builder.Entity<SalesDocumentLine>(e =>
        {
            e.HasOne(l => l.Document).WithMany(d => d.Lines).HasForeignKey(l => l.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.SetNull);
            foreach (var prop in new[] { "Quantity", "UnitPrice", "DiscountPercent", "TaxPercent", "LineTotal" })
                e.Property(prop).HasPrecision(18, 2);
        });

        builder.Entity<PaymentRecord>(e =>
        {
            e.Property(p => p.Amount).HasPrecision(18, 2);
            e.HasOne(p => p.Document).WithMany(d => d.Payments).HasForeignKey(p => p.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Installment>(e =>
        {
            e.Property(i => i.Amount).HasPrecision(18, 2);
            e.HasOne(i => i.Document).WithMany(d => d.Installments).HasForeignKey(i => i.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CommissionRule>(e =>
        {
            foreach (var prop in new[] { "Percent", "FixedAmount", "MinInvoiceAmount" })
                e.Property(prop).HasPrecision(18, 2);
            e.HasOne(r => r.Product).WithMany().HasForeignKey(r => r.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CommissionEntry>(e =>
        {
            e.Property(c => c.Amount).HasPrecision(18, 2);
            e.HasIndex(c => new { c.TenantId, c.UserId });
            e.HasOne(c => c.Document).WithMany().HasForeignKey(c => c.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WorkflowRule>(e =>
        {
            e.Property(r => r.ConditionsJson).HasColumnType("jsonb");
            e.HasOne(r => r.Module).WithMany().HasForeignKey(r => r.ModuleId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(r => new { r.TenantId, r.ModuleId, r.Trigger });
        });

        builder.Entity<WorkflowAction>(e =>
        {
            e.Property(a => a.ConfigJson).HasColumnType("jsonb");
            e.HasOne(a => a.Rule).WithMany(r => r.Actions).HasForeignKey(a => a.RuleId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WorkflowLog>(e => e.HasIndex(l => new { l.TenantId, l.RuleId }));

        builder.Entity<DashboardWidget>(e => e.HasIndex(w => new { w.TenantId, w.UserId }));

        builder.Entity<ReportDef>(e =>
        {
            e.Property(r => r.ColumnsJson).HasColumnType("jsonb");
            e.Property(r => r.FiltersJson).HasColumnType("jsonb");
            e.HasOne(r => r.Module).WithMany().HasForeignKey(r => r.ModuleId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Ticket>(e =>
        {
            e.HasIndex(t => new { t.TenantId, t.Number }).IsUnique();
            e.HasIndex(t => new { t.TenantId, t.Status });
            e.HasOne(t => t.PortalUser).WithMany().HasForeignKey(t => t.PortalUserId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.ServiceContract).WithMany().HasForeignKey(t => t.ServiceContractId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TicketMessage>(e =>
            e.HasOne(m => m.Ticket).WithMany(t => t.Messages).HasForeignKey(m => m.TicketId).OnDelete(DeleteBehavior.Cascade));

        builder.Entity<SlaPolicy>(e => e.HasIndex(p => new { p.TenantId, p.Priority }).IsUnique());

        builder.Entity<Warranty>(e =>
        {
            e.HasIndex(w => new { w.TenantId, w.SerialNumber });
            e.HasOne(w => w.Product).WithMany().HasForeignKey(w => w.ProductId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<PortalUser>(e => e.HasIndex(u => new { u.TenantId, u.Email }).IsUnique());

        builder.Entity<LeaveRequest>(e => e.HasIndex(l => new { l.TenantId, l.UserId }));

        builder.Entity<Project>(e =>
        {
            e.HasIndex(p => new { p.TenantId, p.Status });
            e.Property(p => p.Budget).HasPrecision(18, 0);
        });

        builder.Entity<ProjectPhase>(e =>
            e.HasOne(p => p.Project).WithMany(pr => pr.Phases).HasForeignKey(p => p.ProjectId).OnDelete(DeleteBehavior.Cascade));

        builder.Entity<ProjectTask>(e =>
        {
            e.HasOne(t => t.Project).WithMany(p => p.Tasks).HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(t => t.Phase).WithMany().HasForeignKey(t => t.PhaseId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Vendor>(e => e.HasIndex(v => new { v.TenantId, v.Name }));

        builder.Entity<PurchaseOrder>(e =>
        {
            e.HasIndex(p => new { p.TenantId, p.Number }).IsUnique();
            e.Property(p => p.Total).HasPrecision(18, 0);
            e.HasOne(p => p.Vendor).WithMany().HasForeignKey(p => p.VendorId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PurchaseOrderLine>(e =>
        {
            e.Property(l => l.Quantity).HasPrecision(18, 2);
            e.Property(l => l.UnitCost).HasPrecision(18, 0);
            e.Property(l => l.LineTotal).HasPrecision(18, 0);
            e.HasOne(l => l.PurchaseOrder).WithMany(p => p.Lines).HasForeignKey(l => l.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<VendorPayment>(e =>
        {
            e.Property(p => p.Amount).HasPrecision(18, 0);
            e.HasOne(p => p.PurchaseOrder).WithMany(po => po.Payments).HasForeignKey(p => p.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Campaign>(e =>
        {
            e.HasIndex(c => new { c.TenantId, c.Status });
            e.Property(c => c.Budget).HasPrecision(18, 0);
            e.Property(c => c.ActualCost).HasPrecision(18, 0);
        });

        builder.Entity<CampaignMember>(e =>
        {
            e.HasIndex(m => new { m.TenantId, m.ModuleName, m.RecordId });
            e.HasIndex(m => new { m.CampaignId, m.ModuleName, m.RecordId }).IsUnique();
            e.HasOne(m => m.Campaign).WithMany(c => c.Members).HasForeignKey(m => m.CampaignId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WebForm>(e =>
        {
            e.HasIndex(f => f.PublicKey).IsUnique();
            e.Property(f => f.FieldsJson).HasColumnType("jsonb");
            e.HasOne(f => f.Module).WithMany().HasForeignKey(f => f.ModuleId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Survey>(e => e.HasIndex(s => s.PublicKey).IsUnique());

        builder.Entity<ApiKey>(e => e.HasIndex(k => k.Key).IsUnique());

        builder.Entity<PaymentTransaction>(e =>
        {
            e.HasIndex(t => t.Token).IsUnique();
            e.Property(t => t.Amount).HasPrecision(18, 0);
        });

        builder.Entity<SurveyQuestion>(e =>
        {
            e.Property(q => q.OptionsJson).HasColumnType("jsonb");
            e.HasOne(q => q.Survey).WithMany(s => s.Questions).HasForeignKey(q => q.SurveyId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SurveyResponse>(e =>
        {
            e.Property(r => r.AnswersJson).HasColumnType("jsonb");
            e.HasOne(r => r.Survey).WithMany(s => s.Responses).HasForeignKey(r => r.SurveyId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AuditLog>(e =>
        {
            e.Property(a => a.Changes).HasColumnType("jsonb");
            e.HasIndex(a => new { a.TenantId, a.ModuleName, a.RecordId });
        });

        builder.Entity<TagLink>(e =>
        {
            e.HasIndex(t => new { t.TenantId, t.ModuleName, t.RecordId });
            e.HasOne(t => t.Tag).WithMany().HasForeignKey(t => t.TagId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Note>(e => e.HasIndex(n => new { n.TenantId, n.ModuleName, n.RecordId }));
        builder.Entity<Attachment>(e => e.HasIndex(a => new { a.TenantId, a.ModuleName, a.RecordId }));
        builder.Entity<Notification>(e => e.HasIndex(n => new { n.TenantId, n.UserId, n.IsRead }));
        builder.Entity<SavedListView>(e =>
        {
            e.Property(v => v.Definition).HasColumnType("jsonb");
            e.HasIndex(v => new { v.TenantId, v.ModuleId });
        });

        ApplyTenantAndSoftDeleteFilters(builder);
    }

    /// <summary>
    /// فیلتر سراسری Tenant + Soft Delete برای همه موجودیت‌های TenantEntity —
    /// اصل غیرقابل مذاکره: تفکیک داده در لایه Query نه UI.
    /// </summary>
    private void ApplyTenantAndSoftDeleteFilters(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (!typeof(TenantEntity).IsAssignableFrom(entityType.ClrType) || entityType.BaseType is not null)
                continue;

            var method = typeof(CrmDbContext)
                .GetMethod(nameof(SetTenantFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .MakeGenericMethod(entityType.ClrType);
            method.Invoke(this, new object[] { builder });
        }
    }

    private void SetTenantFilter<TEntity>(ModelBuilder builder) where TEntity : TenantEntity
    {
        builder.Entity<TEntity>().HasQueryFilter(e =>
            !e.IsDeleted && (_tenant.TenantId == null || e.TenantId == _tenant.TenantId));
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditAndTenantStamps();
        return await base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyAuditAndTenantStamps();
        return base.SaveChanges();
    }

    private void ApplyAuditAndTenantStamps()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<TenantEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.TenantId == 0 && _tenant.TenantId is int tid)
                        entry.Entity.TenantId = tid;
                    entry.Entity.CreatedAtUtc = now;
                    entry.Entity.CreatedByUserId ??= _tenant.UserId;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAtUtc = now;
                    entry.Entity.UpdatedByUserId = _tenant.UserId;
                    break;

                case EntityState.Deleted:
                    // حذف فیزیکی به حذف نرم تبدیل می‌شود؛ سطل بازیابی از همین داده تغذیه می‌کند
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                    entry.Entity.DeletedAtUtc = now;
                    entry.Entity.DeletedByUserId = _tenant.UserId;
                    break;
            }
        }
    }
}
