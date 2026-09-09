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
    public IReadOnlyList<DuplicateHit> Duplicates { get; init; } = [];
}

/// <summary>
/// تبدیل سرنخ به مخاطب (موجود یا جدید) و در صورت نیاز فرصت فروش.
/// سرنخ حذف نمی‌شود؛ به مخاطب وصل و وضعیت آن «تبدیل‌شده» می‌شود.
/// </summary>
public class LeadConversionService
{
    private readonly CrmDbContext _db;
    private readonly MetadataService _metadata;
    private readonly DynamicRecordService _records;
    private readonly ContactDirectory _directory;
    private readonly AuditService _audit;

    public LeadConversionService(
        CrmDbContext db,
        MetadataService metadata,
        DynamicRecordService records,
        ContactDirectory directory,
        AuditService audit)
    {
        _db = db;
        _metadata = metadata;
        _records = records;
        _directory = directory;
        _audit = audit;
    }

    public async Task<LeadConversionResult> ConvertAsync(
        int leadRecordId,
        int? existingContactId = null,
        bool createOpportunity = true,
        bool skipDuplicateCheck = false)
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
        var phone = PhoneIdentity.FromRecord(data);

        if (existingContactId is null && !string.IsNullOrWhiteSpace(phone))
        {
            var matches = await _directory.FindContactsByPhoneAsync(phone);
            if (matches.Count == 1)
                existingContactId = matches[0].Id;
            else if (matches.Count > 1)
            {
                return new LeadConversionResult
                {
                    Success = false,
                    Error = "چند مخاطب با همین شماره پیدا شد. یکی را در صفحه تبدیل انتخاب کنید."
                };
            }
        }

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
                try
                {
                    var org = await _records.CreateAsync(organizationsModule.Id, new Dictionary<string, string?>
                    {
                        ["name"] = company.Trim(),
                        ["phone"] = data.GetValueOrDefault("phone"),
                        ["city"] = data.GetValueOrDefault("city")
                    }, excludeDuplicateRecordId: leadRecordId, skipDuplicateCheck: skipDuplicateCheck);
                    organizationId = org.Id;
                }
                catch (RecordValidationException ex)
                {
                    return new LeadConversionResult
                    {
                        Success = false,
                        Error = "سازمان ساخته نشد: " + string.Join(" — ", ex.Errors.Values),
                        Duplicates = ex.Duplicates
                    };
                }
            }
        }

        DynamicRecord contact;
        try
        {
            contact = await _directory.EnsureContactAsync(
                "leads", lead, data, existingContactId, organizationId,
                excludeDuplicateRecordId: leadRecordId, skipDuplicateCheck: skipDuplicateCheck);
        }
        catch (RecordValidationException ex)
        {
            return new LeadConversionResult
            {
                Success = false,
                Error = "تبدیل ناموفق: " + string.Join(" — ", ex.Errors.Values),
                Duplicates = ex.Duplicates
            };
        }
        catch (InvalidOperationException ex)
        {
            return new LeadConversionResult { Success = false, Error = ex.Message };
        }

        int? opportunityId = null;
        if (createOpportunity && opportunitiesModule is not null)
        {
            try
            {
                var opp = await _records.CreateAsync(opportunitiesModule.Id, new Dictionary<string, string?>
                {
                    ["name"] = $"فرصت فروش {name}",
                    ["contact"] = contact.Id.ToString(),
                    ["organization"] = organizationId?.ToString(),
                    ["stage"] = "new",
                    ["description"] = data.GetValueOrDefault("description")
                });
                opportunityId = opp.Id;
                await _directory.CopyNotesAndAttachmentsAsync("leads", lead.Id, "opportunities", opp.Id);
            }
            catch (RecordValidationException ex)
            {
                return new LeadConversionResult
                {
                    Success = false,
                    Error = "فرصت ساخته نشد: " + string.Join(" — ", ex.Errors.Values),
                    ContactId = contact.Id,
                    OrganizationId = organizationId,
                    Duplicates = ex.Duplicates
                };
            }
        }

        data["contact"] = contact.Id.ToString();
        data["status"] = "converted";
        try
        {
            await _records.UpdateAsync(leadsModule.Id, leadRecordId, data);
        }
        catch (RecordValidationException ex)
        {
            return new LeadConversionResult
            {
                Success = false,
                Error = "سرنخ به‌روز نشد: " + string.Join(" — ", ex.Errors.Values),
                ContactId = contact.Id,
                OrganizationId = organizationId,
                OpportunityId = opportunityId
            };
        }

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
