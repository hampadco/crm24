using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Infrastructure.Services;

public class LeadConversionResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public int? ContactId { get; init; }
    public int? OrganizationId { get; init; }
    public int? OpportunityId { get; init; }
}

/// <summary>
/// تبدیل یک‌کلیکی سرنخ به مخاطب + سازمان + فرصت فروش با نگاشت فیلدها.
/// سرنخ پس از تبدیل به سطل بازیابی می‌رود (Audit با اکشن Convert).
/// </summary>
public class LeadConversionService
{
    private readonly CrmDbContext _db;
    private readonly MetadataService _metadata;
    private readonly DynamicRecordService _records;
    private readonly AuditService _audit;

    public LeadConversionService(
        CrmDbContext db, MetadataService metadata, DynamicRecordService records, AuditService audit)
    {
        _db = db;
        _metadata = metadata;
        _records = records;
        _audit = audit;
    }

    public async Task<LeadConversionResult> ConvertAsync(int leadRecordId, bool createOpportunity = true)
    {
        var leadsModule = await _metadata.GetModuleByNameAsync("leads");
        var contactsModule = await _metadata.GetModuleByNameAsync("contacts");
        var organizationsModule = await _metadata.GetModuleByNameAsync("organizations");
        var opportunitiesModule = await _metadata.GetModuleByNameAsync("opportunities");

        if (leadsModule is null || contactsModule is null || organizationsModule is null)
            return new LeadConversionResult { Success = false, Error = "ماژول‌های لازم برای تبدیل وجود ندارند." };

        var lead = await _records.GetAsync(leadsModule.Id, leadRecordId);
        if (lead is null)
            return new LeadConversionResult { Success = false, Error = "سرنخ یافت نشد." };

        var data = DynamicRecordService.ParseData(lead);
        var name = data.GetValueOrDefault("name") ?? lead.Title;
        var company = data.GetValueOrDefault("company");

        // ۱) سازمان (اگر نام شرکت داشت) — سازمان همنام موجود دوباره ساخته نمی‌شود
        int? organizationId = null;
        if (!string.IsNullOrWhiteSpace(company))
        {
            var existingOrg = await _db.Records
                .AsNoTracking()
                .Where(r => r.ModuleId == organizationsModule.Id && r.Title == company.Trim())
                .Select(r => (int?)r.Id)
                .FirstOrDefaultAsync();

            if (existingOrg is not null)
            {
                organizationId = existingOrg;
            }
            else
            {
                var org = await _records.CreateAsync(organizationsModule.Id, new Dictionary<string, string?>
                {
                    ["name"] = company.Trim(),
                    ["phone"] = data.GetValueOrDefault("phone"),
                    ["city"] = data.GetValueOrDefault("city")
                });
                organizationId = org.Id;
            }
        }

        // ۲) مخاطب — نگاشت phone سرنخ به mobile مخاطب
        DynamicRecord contact;
        try
        {
            contact = await _records.CreateAsync(contactsModule.Id, new Dictionary<string, string?>
            {
                ["name"] = name,
                ["organization"] = organizationId?.ToString(),
                ["mobile"] = data.GetValueOrDefault("phone"),
                ["email"] = data.GetValueOrDefault("email"),
                ["description"] = data.GetValueOrDefault("description")
            });
        }
        catch (RecordValidationException ex)
        {
            return new LeadConversionResult
            {
                Success = false,
                Error = "تبدیل ناموفق: " + string.Join(" — ", ex.Errors.Values)
            };
        }

        // ۳) فرصت فروش
        int? opportunityId = null;
        if (createOpportunity && opportunitiesModule is not null)
        {
            var opp = await _records.CreateAsync(opportunitiesModule.Id, new Dictionary<string, string?>
            {
                ["name"] = $"فرصت فروش {name}",
                ["contact"] = contact.Id.ToString(),
                ["organization"] = organizationId?.ToString(),
                ["stage"] = "new"
            });
            opportunityId = opp.Id;
        }

        // ۴) بستن سرنخ (حذف نرم + Audit)
        await _records.DeleteAsync(leadsModule.Id, leadRecordId);
        _audit.Log("leads", leadRecordId, "Convert", new
        {
            ContactId = contact.Id,
            OrganizationId = organizationId,
            OpportunityId = opportunityId
        });
        await _db.SaveChangesAsync();

        return new LeadConversionResult
        {
            Success = true,
            ContactId = contact.Id,
            OrganizationId = organizationId,
            OpportunityId = opportunityId
        };
    }
}
