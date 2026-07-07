using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Security;

namespace Crm.Infrastructure.Services;

public class RecordValidationException : Exception
{
    public IReadOnlyDictionary<string, string> Errors { get; }

    public RecordValidationException(IReadOnlyDictionary<string, string> errors)
        : base("Record validation failed.")
    {
        Errors = errors;
    }
}

/// <summary>
/// CRUD عمومی رکوردهای metadata-driven: اعتبارسنجی از روی FieldDef ها،
/// تشخیص تکراری، ممیزی و اعمال دسترسی سه‌لایه.
/// </summary>
public class DynamicRecordService
{
    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly MetadataService _metadata;
    private readonly RecordAccessService _access;
    private readonly AuditService _audit;
    private readonly IBackgroundJobClient _jobs;

    public DynamicRecordService(
        CrmDbContext db,
        ITenantContext tenant,
        MetadataService metadata,
        RecordAccessService access,
        AuditService audit,
        IBackgroundJobClient jobs)
    {
        _db = db;
        _tenant = tenant;
        _metadata = metadata;
        _access = access;
        _audit = audit;
        _jobs = jobs;
    }

    /// <summary>اجرای async قوانین گردش‌کار پس از ایجاد/ویرایش رکورد.</summary>
    private void EnqueueWorkflows(int moduleId, int recordId, WorkflowTrigger trigger)
    {
        if (_tenant.TenantId is int tenantId)
            _jobs.Enqueue<WorkflowEngine>(engine =>
                engine.ExecuteForRecordAsync(tenantId, moduleId, recordId, trigger));
    }

    public async Task<(IReadOnlyList<DynamicRecord> Items, int TotalCount)> ListAsync(
        int moduleId, string? search, int page, int pageSize)
    {
        var query = _db.Records.AsNoTracking().Where(r => r.ModuleId == moduleId);
        query = await _access.ApplyVisibilityAsync(query, moduleId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(r => EF.Functions.ILike(r.Title, $"%{term}%"));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<DynamicRecord?> GetAsync(int moduleId, int id)
    {
        var query = _db.Records.AsNoTracking().Where(r => r.ModuleId == moduleId && r.Id == id);
        query = await _access.ApplyVisibilityAsync(query, moduleId);
        return await query.FirstOrDefaultAsync();
    }

    public async Task<DynamicRecord> CreateAsync(int moduleId, Dictionary<string, string?> values)
    {
        var module = await _db.Modules.AsNoTracking().FirstAsync(m => m.Id == moduleId);
        var fields = await _metadata.GetFieldsAsync(moduleId);

        var data = ValidateAndBuildData(fields, values);
        await CheckDuplicatesAsync(moduleId, fields, data, excludeRecordId: null);

        var record = new DynamicRecord
        {
            ModuleId = moduleId,
            Title = ResolveTitle(fields, data),
            OwnerUserId = _tenant.UserId,
            CustomData = JsonSerializer.Serialize(data)
        };

        _db.Records.Add(record);
        await _db.SaveChangesAsync();

        _audit.Log(module.Name, record.Id, "Create", data);
        await _db.SaveChangesAsync();

        EnqueueWorkflows(moduleId, record.Id, WorkflowTrigger.RecordCreated);
        return record;
    }

    public async Task UpdateAsync(int moduleId, int id, Dictionary<string, string?> values)
    {
        var module = await _db.Modules.AsNoTracking().FirstAsync(m => m.Id == moduleId);
        var record = await _db.Records.FirstOrDefaultAsync(r => r.ModuleId == moduleId && r.Id == id)
            ?? throw new InvalidOperationException("Record not found.");

        if (!await _access.CanModifyRecordAsync(record))
            throw new UnauthorizedAccessException();

        var fields = await _metadata.GetFieldsAsync(moduleId);
        var data = ValidateAndBuildData(fields, values);
        await CheckDuplicatesAsync(moduleId, fields, data, excludeRecordId: id);

        var oldData = JsonSerializer.Deserialize<Dictionary<string, string?>>(record.CustomData) ?? new();
        var changes = data
            .Where(kv => (oldData.TryGetValue(kv.Key, out var old) ? old : null) != kv.Value)
            .ToDictionary(kv => kv.Key, kv => new { Old = oldData.GetValueOrDefault(kv.Key), New = kv.Value });

        record.CustomData = JsonSerializer.Serialize(data);
        record.Title = ResolveTitle(fields, data);

        _audit.Log(module.Name, record.Id, "Update", changes);
        await _db.SaveChangesAsync();

        if (changes.Count > 0)
            EnqueueWorkflows(moduleId, id, WorkflowTrigger.RecordUpdated);
    }

    /// <summary>بروزرسانی یک فیلد (برای drag & drop کاریز) بدون اعتبارسنجی کامل فرم.</summary>
    public async Task UpdateFieldAsync(int moduleId, int id, string fieldName, string? value)
    {
        var module = await _db.Modules.AsNoTracking().FirstAsync(m => m.Id == moduleId);
        var record = await _db.Records.FirstOrDefaultAsync(r => r.ModuleId == moduleId && r.Id == id)
            ?? throw new InvalidOperationException("Record not found.");

        if (!await _access.CanModifyRecordAsync(record))
            throw new UnauthorizedAccessException();

        var fields = await _metadata.GetFieldsAsync(moduleId);
        var field = fields.FirstOrDefault(f => f.Name == fieldName)
            ?? throw new InvalidOperationException("Field not found.");

        if (field.Type == FieldType.Picklist && value is not null &&
            field.PicklistValues.Count > 0 && !field.PicklistValues.Any(p => p.Value == value))
            throw new RecordValidationException(new Dictionary<string, string> { [fieldName] = "مقدار مجاز نیست." });

        var data = ParseData(record);
        var old = data.GetValueOrDefault(fieldName);
        data[fieldName] = value;
        record.CustomData = JsonSerializer.Serialize(data);

        _audit.Log(module.Name, record.Id, "Update", new Dictionary<string, object?>
        {
            [fieldName] = new { Old = old, New = value }
        });
        await _db.SaveChangesAsync();

        if (old != value)
            EnqueueWorkflows(moduleId, id, WorkflowTrigger.RecordUpdated);
    }

    public async Task DeleteAsync(int moduleId, int id)
    {
        var module = await _db.Modules.AsNoTracking().FirstAsync(m => m.Id == moduleId);
        var record = await _db.Records.FirstOrDefaultAsync(r => r.ModuleId == moduleId && r.Id == id)
            ?? throw new InvalidOperationException("Record not found.");

        if (!await _access.CanModifyRecordAsync(record))
            throw new UnauthorizedAccessException();

        _db.Records.Remove(record); // به حذف نرم تبدیل می‌شود
        _audit.Log(module.Name, record.Id, "Delete");
        await _db.SaveChangesAsync();
    }

    /// <summary>سطل بازیابی: رکوردهای حذف‌شده Tenant جاری.</summary>
    public async Task<IReadOnlyList<DynamicRecord>> ListDeletedAsync(int take = 100) =>
        await _db.Records
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.IsDeleted && r.TenantId == _tenant.TenantId)
            .OrderByDescending(r => r.DeletedAtUtc)
            .Take(take)
            .Include(r => r.Module)
            .ToListAsync();

    public async Task RestoreAsync(int id)
    {
        var record = await _db.Records
            .IgnoreQueryFilters()
            .Include(r => r.Module)
            .FirstOrDefaultAsync(r => r.Id == id && r.IsDeleted && r.TenantId == _tenant.TenantId)
            ?? throw new InvalidOperationException("Record not found.");

        record.IsDeleted = false;
        record.DeletedAtUtc = null;
        record.DeletedByUserId = null;

        _audit.Log(record.Module.Name, record.Id, "Restore");
        await _db.SaveChangesAsync();
    }

    public static Dictionary<string, string?> ParseData(DynamicRecord record) =>
        JsonSerializer.Deserialize<Dictionary<string, string?>>(record.CustomData) ?? new();

    private static Dictionary<string, string?> ValidateAndBuildData(
        IReadOnlyList<FieldDef> fields, Dictionary<string, string?> values)
    {
        var errors = new Dictionary<string, string>();
        var data = new Dictionary<string, string?>();

        foreach (var field in fields)
        {
            values.TryGetValue(field.Name, out var raw);
            var value = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();

            if (value is null && field.DefaultValue is not null)
                value = field.DefaultValue;

            if (field.IsRequired && value is null)
            {
                errors[field.Name] = $"فیلد «{field.Label}» الزامی است.";
                continue;
            }

            if (value is not null)
            {
                switch (field.Type)
                {
                    case FieldType.Number when !long.TryParse(value, out _):
                        errors[field.Name] = $"مقدار «{field.Label}» باید عدد صحیح باشد.";
                        continue;
                    case FieldType.Decimal or FieldType.Currency when !decimal.TryParse(value, out _):
                        errors[field.Name] = $"مقدار «{field.Label}» باید عددی باشد.";
                        continue;
                    case FieldType.Email when !value.Contains('@'):
                        errors[field.Name] = $"مقدار «{field.Label}» ایمیل معتبر نیست.";
                        continue;
                    case FieldType.Picklist when field.PicklistValues.Count > 0 &&
                                                 !field.PicklistValues.Any(p => p.Value == value):
                        errors[field.Name] = $"مقدار «{field.Label}» از میان گزینه‌های مجاز نیست.";
                        continue;
                }
            }

            data[field.Name] = value;
        }

        if (errors.Count > 0)
            throw new RecordValidationException(errors);

        return data;
    }

    /// <summary>تشخیص تکراری روی فیلدهای علامت‌خورده با IsUniqueCheck.</summary>
    private async Task CheckDuplicatesAsync(
        int moduleId, IReadOnlyList<FieldDef> fields,
        Dictionary<string, string?> data, int? excludeRecordId)
    {
        var errors = new Dictionary<string, string>();

        foreach (var field in fields.Where(f => f.IsUniqueCheck))
        {
            if (!data.TryGetValue(field.Name, out var value) || value is null)
                continue;

            // مقایسه مقدار jsonb با عملگر ->> در SQL خام (LINQ روی jsonb ترجمه نمی‌شود)
            var duplicate = await _db.Records
                .FromSqlInterpolated($"""
                    SELECT * FROM "Records"
                    WHERE "ModuleId" = {moduleId} AND "CustomData" ->> {field.Name} = {value}
                    """)
                .Where(r => excludeRecordId == null || r.Id != excludeRecordId)
                .AnyAsync();

            if (duplicate)
                errors[field.Name] = $"رکوردی با همین «{field.Label}» از قبل وجود دارد.";
        }

        if (errors.Count > 0)
            throw new RecordValidationException(errors);
    }

    private static string ResolveTitle(IReadOnlyList<FieldDef> fields, Dictionary<string, string?> data)
    {
        var titleField = fields.FirstOrDefault(f => f.Name is "name" or "title" or "subject")
            ?? fields.FirstOrDefault(f => f.Type is FieldType.Text);

        var title = titleField is not null ? data.GetValueOrDefault(titleField.Name) : null;
        return title ?? "(بدون عنوان)";
    }
}
