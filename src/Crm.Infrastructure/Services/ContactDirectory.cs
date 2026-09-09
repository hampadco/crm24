using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Infrastructure.Services;

public sealed class DirectoryPerson
{
    public int Id { get; init; }
    public string Title { get; init; } = "";
    public string? Phone { get; init; }
    public string? Email { get; init; }
}

/// <summary>
/// پیدا کردن / ساخت / ادغام مخاطب از روی شماره و نام، بدون از دست رفتن داده.
/// </summary>
public class ContactDirectory
{
    private readonly CrmDbContext _db;
    private readonly MetadataService _metadata;
    private readonly DynamicRecordService _records;

    public ContactDirectory(CrmDbContext db, MetadataService metadata, DynamicRecordService records)
    {
        _db = db;
        _metadata = metadata;
        _records = records;
    }

    public async Task<IReadOnlyList<DirectoryPerson>> FindContactsByPhoneAsync(string? phone, CancellationToken ct = default)
    {
        var module = await _metadata.GetModuleByNameAsync("contacts");
        if (module is null)
            return [];
        return await FindByPhoneAsync(module.Id, ["mobile", "phone"], phone, excludeId: null, ct);
    }

    public async Task<IReadOnlyList<DirectoryPerson>> FindLeadsByPhoneAsync(
        string? phone, int? excludeId = null, CancellationToken ct = default)
    {
        var module = await _metadata.GetModuleByNameAsync("leads");
        if (module is null)
            return [];
        return await FindByPhoneAsync(module.Id, ["phone"], phone, excludeId, ct);
    }

    public async Task<DynamicRecord> EnsureContactAsync(
        string sourceModuleName,
        DynamicRecord source,
        IReadOnlyDictionary<string, string?> sourceData,
        int? existingContactId,
        int? organizationId,
        int? excludeDuplicateRecordId,
        CancellationToken ct = default,
        bool skipDuplicateCheck = false)
    {
        var contactsModule = await _metadata.GetModuleByNameAsync("contacts")
            ?? throw new InvalidOperationException("ماژول مخاطب وجود ندارد.");

        var payload = BuildContactPayload(sourceModuleName, source, sourceData, organizationId);

        if (existingContactId is int id)
        {
            var existing = await _records.GetAsync(contactsModule.Id, id)
                ?? throw new InvalidOperationException("مخاطب انتخاب‌شده یافت نشد.");
            await MergeIntoContactAsync(contactsModule.Id, existing, payload);
            await CopyNotesAndAttachmentsAsync(sourceModuleName, source.Id, "contacts", existing.Id, ct);
            return await _records.GetAsync(contactsModule.Id, existing.Id) ?? existing;
        }

        var created = await _records.CreateAsync(contactsModule.Id, payload, excludeDuplicateRecordId, skipDuplicateCheck);
        await CopyNotesAndAttachmentsAsync(sourceModuleName, source.Id, "contacts", created.Id, ct);
        return created;
    }

    public async Task CopyNotesAndAttachmentsAsync(
        string fromModule, int fromId, string toModule, int toId, CancellationToken ct = default)
    {
        var notes = await _db.Notes.AsNoTracking()
            .Where(n => n.ModuleName == fromModule && n.RecordId == fromId)
            .ToListAsync(ct);
        var destBodies = await _db.Notes.AsNoTracking()
            .Where(n => n.ModuleName == toModule && n.RecordId == toId)
            .Select(n => n.Body)
            .ToListAsync(ct);
        var destBodySet = destBodies.ToHashSet(StringComparer.Ordinal);
        foreach (var note in notes)
        {
            if (destBodySet.Contains(note.Body))
                continue;
            _db.Notes.Add(new Note
            {
                ModuleName = toModule,
                RecordId = toId,
                Body = note.Body
            });
            destBodySet.Add(note.Body);
        }

        var files = await _db.Attachments.AsNoTracking()
            .Where(a => a.ModuleName == fromModule && a.RecordId == fromId)
            .ToListAsync(ct);
        var destFiles = await _db.Attachments.AsNoTracking()
            .Where(a => a.ModuleName == toModule && a.RecordId == toId)
            .Select(a => a.StoredPath)
            .ToListAsync(ct);
        var destPathSet = destFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (destPathSet.Contains(file.StoredPath))
                continue;
            _db.Attachments.Add(new Attachment
            {
                ModuleName = toModule,
                RecordId = toId,
                FileName = file.FileName,
                StoredPath = file.StoredPath,
                ContentType = file.ContentType,
                SizeBytes = file.SizeBytes
            });
            destPathSet.Add(file.StoredPath);
        }

        if (_db.ChangeTracker.HasChanges())
            await _db.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<DirectoryPerson>> FindByPhoneAsync(
        int moduleId, IReadOnlyList<string> fieldNames, string? phone, int? excludeId, CancellationToken ct)
    {
        var variants = PhoneIdentity.SearchVariants(phone);
        if (variants.Count == 0)
            return [];

        var matches = new Dictionary<int, DynamicRecord>();
        foreach (var fieldName in fieldNames)
        {
            foreach (var variant in variants)
            {
                var hits = await _db.Records
                    .FromSqlInterpolated($"""
                        SELECT * FROM "Records"
                        WHERE "ModuleId" = {moduleId} AND "CustomData" ->> {fieldName} = {variant}
                        """)
                    .AsNoTracking()
                    .Where(r => excludeId == null || r.Id != excludeId)
                    .ToListAsync(ct);
                foreach (var hit in hits)
                    matches.TryAdd(hit.Id, hit);
            }
        }

        return matches.Values
            .Select(r =>
            {
                var data = DynamicRecordService.ParseData(r);
                return new DirectoryPerson
                {
                    Id = r.Id,
                    Title = r.Title,
                    Phone = PhoneIdentity.FromRecord(data),
                    Email = data.GetValueOrDefault("email")
                };
            })
            .OrderBy(p => p.Title)
            .ToList();
    }

    private static Dictionary<string, string?> BuildContactPayload(
        string sourceModuleName,
        DynamicRecord source,
        IReadOnlyDictionary<string, string?> sourceData,
        int? organizationId)
    {
        var name = sourceData.GetValueOrDefault("name")
            ?? sourceData.GetValueOrDefault("title")
            ?? sourceData.GetValueOrDefault("subject")
            ?? source.Title;
        var payload = new Dictionary<string, string?>
        {
            ["name"] = name,
            ["mobile"] = PhoneIdentity.FromRecord(sourceData),
            ["email"] = sourceData.GetValueOrDefault("email"),
            ["description"] = sourceData.GetValueOrDefault("description"),
            ["organization"] = organizationId?.ToString()
                ?? sourceData.GetValueOrDefault("organization")
        };
        if (string.Equals(sourceModuleName, "leads", StringComparison.OrdinalIgnoreCase))
            payload["lead"] = source.Id.ToString();
        return payload;
    }

    private async Task MergeIntoContactAsync(
        int contactsModuleId, DynamicRecord contact, Dictionary<string, string?> incoming)
    {
        var data = DynamicRecordService.ParseData(contact);
        foreach (var (key, value) in incoming)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            if (string.Equals(key, "description", StringComparison.OrdinalIgnoreCase))
            {
                var current = data.GetValueOrDefault("description");
                if (string.IsNullOrWhiteSpace(current))
                    data["description"] = value;
                else if (!current.Contains(value, StringComparison.Ordinal))
                    data["description"] = current.TrimEnd() + "\n\n" + value;
                continue;
            }

            if (string.IsNullOrWhiteSpace(data.GetValueOrDefault(key)))
                data[key] = value;
        }

        await _records.UpdateAsync(contactsModuleId, contact.Id, data);
    }

    public async Task<DynamicRecord> CollectFromRecordAsync(
        string moduleName, DynamicRecord record, int? existingContactId, CancellationToken ct = default)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName)
            ?? throw new InvalidOperationException("ماژول یافت نشد.");
        var data = DynamicRecordService.ParseData(record);
        var phone = PhoneIdentity.FromRecord(data);
        if (existingContactId is null && !string.IsNullOrWhiteSpace(phone))
        {
            var matches = await FindContactsByPhoneAsync(phone, ct);
            if (matches.Count == 1)
                existingContactId = matches[0].Id;
            else if (matches.Count > 1)
                throw new InvalidOperationException("چند مخاطب با همین شماره پیدا شد. یکی را انتخاب کنید.");
        }

        int? organizationId = null;
        if (int.TryParse(data.GetValueOrDefault("organization"), out var orgId) && orgId > 0)
            organizationId = orgId;

        var contact = await EnsureContactAsync(
            moduleName, record, data, existingContactId, organizationId, excludeDuplicateRecordId: record.Id, ct);

        data["contact"] = contact.Id.ToString();
        await _records.UpdateAsync(module.Id, record.Id, data);
        return contact;
    }
}
