using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Infrastructure.Services;

/// <summary>
/// وقتی روی یک رکورد Lookup طرف‌حساب پر شود، همان مقدار را روی همه ماژول‌هایی
/// که به این رکورد اشاره می‌کنند — اگر خالی باشد — کپی می‌کند.
/// </summary>
public class PartyLinkService
{
    public static readonly HashSet<string> PartyModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "leads", "contacts", "organizations"
    };

    private static readonly string[] PartyOrder = ["leads", "contacts", "organizations"];

    private readonly CrmDbContext _db;
    private readonly MetadataService _metadata;
    private readonly AuditService _audit;

    public PartyLinkService(CrmDbContext db, MetadataService metadata, AuditService audit)
    {
        _db = db;
        _metadata = metadata;
        _audit = audit;
    }

    public async Task PropagateFromAsync(string moduleName, int recordId, CancellationToken ct = default)
    {
        var hubModule = await _metadata.GetModuleByNameAsync(moduleName);
        if (hubModule is null)
            return;

        var partyValues = await CollectPartyGraphAsync(moduleName, recordId, ct);
        if (partyValues.Count == 0)
            return;

        var inbound = await _db.Fields.AsNoTracking()
            .Include(f => f.Module)
            .Where(f => f.Type == FieldType.Lookup
                        && f.LookupModule == moduleName
                        && f.ModuleId != hubModule.Id
                        && (f.Module == null || !f.Module.Name.EndsWith("_lines")))
            .ToListAsync(ct);

        var idStr = recordId.ToString();
        var seen = new HashSet<(int ModuleId, int Id)>();
        var childFieldsCache = new Dictionary<int, IReadOnlyList<FieldDef>>();
        var moduleNames = new Dictionary<int, string>();
        var pending = new List<DynamicRecord>();

        foreach (var field in inbound.Where(f => IsSafeJsonKey(f.Name) && f.Module is not null))
        {
            var rows = await _db.Records
                .FromSqlInterpolated($"""
                    SELECT * FROM "Records"
                    WHERE "ModuleId" = {field.ModuleId}
                      AND "CustomData" ->> {field.Name} = {idStr}
                    """)
                .AsNoTracking()
                .ToListAsync(ct);

            foreach (var row in rows)
            {
                if (!seen.Add((row.ModuleId, row.Id)))
                    continue;
                pending.Add(row);
                moduleNames.TryAdd(row.ModuleId, field.Module!.Name);
            }
        }

        if (pending.Count > 0)
        {
            var ids = pending.Select(r => r.Id).ToList();
            var tracked = await _db.Records.Where(r => ids.Contains(r.Id)).ToListAsync(ct);
            var trackedMap = tracked.ToDictionary(r => r.Id);
            var changed = false;

            foreach (var row in pending)
            {
                if (!trackedMap.TryGetValue(row.Id, out var entity))
                    continue;
                if (!childFieldsCache.TryGetValue(entity.ModuleId, out var childFields))
                {
                    childFields = await _metadata.GetFieldsAsync(entity.ModuleId);
                    childFieldsCache[entity.ModuleId] = childFields;
                }

                var data = DynamicRecordService.ParseData(entity);
                if (!FillEmptyPartyLookups(data, childFields, partyValues))
                    continue;

                entity.CustomData = JsonSerializer.Serialize(data);
                var moduleNameForAudit = moduleNames.GetValueOrDefault(entity.ModuleId, "records");
                _audit.Log(moduleNameForAudit, entity.Id, "Update", new { partyLink = partyValues });
                changed = true;
            }

            if (changed)
                await _db.SaveChangesAsync(ct);
        }

        if (string.Equals(moduleName, "leads", StringComparison.OrdinalIgnoreCase)
            && partyValues.TryGetValue("contacts", out var contactIdRaw)
            && int.TryParse(contactIdRaw, out var contactId)
            && contactId > 0)
        {
            await CopyCampaignMembersAsync(recordId, contactId, ct);
            await CopyManyToManyLinksAsync(hubModule.Id, recordId, contactId, ct);
        }
    }

    /// <summary>مخاطب/سازمان خالی قطب را پر می‌کند و به فرزندان پخش می‌کند.</summary>
    public async Task FillHubAndPropagateAsync(
        string moduleName, int recordId, int? contactId, int? organizationId, CancellationToken ct = default)
    {
        var hubModule = await _metadata.GetModuleByNameAsync(moduleName);
        if (hubModule is null)
            return;

        var hub = await _db.Records.FirstOrDefaultAsync(r => r.ModuleId == hubModule.Id && r.Id == recordId, ct);
        if (hub is null)
            return;

        var fields = await _metadata.GetFieldsAsync(hubModule.Id);
        var data = DynamicRecordService.ParseData(hub);
        var incoming = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (contactId is int cid && cid > 0)
            incoming["contacts"] = cid.ToString();
        if (organizationId is int oid && oid > 0)
            incoming["organizations"] = oid.ToString();
        if (incoming.Count == 0)
        {
            await PropagateFromAsync(moduleName, recordId, ct);
            return;
        }

        if (FillEmptyPartyLookups(data, fields, incoming))
        {
            hub.CustomData = JsonSerializer.Serialize(data);
            _audit.Log(moduleName, recordId, "Update", new { partyLink = incoming });
            await _db.SaveChangesAsync(ct);
        }

        await PropagateFromAsync(moduleName, recordId, ct);
    }

    /// <summary>
    /// فیلدهای خالی طرف‌حساب همین رکورد را از سرنخ/مخاطب متصل پر می‌کند (قبل از ذخیره).
    /// </summary>
    public async Task EnrichFromLinkedPartiesAsync(
        IReadOnlyList<FieldDef> fields, Dictionary<string, string?> data, CancellationToken ct = default)
    {
        if (!HasFillableEmptyPartyLookup(fields, data))
            return;

        foreach (var dest in PartyOrder)
        {
            var linked = ReadPartyValues(fields, data);
            if (!linked.TryGetValue(dest, out var idRaw)
                || !int.TryParse(idRaw, out var linkedId)
                || linkedId <= 0)
                continue;

            var extra = await CollectPartyGraphAsync(dest, linkedId, ct);
            if (extra.Count == 0)
                continue;
            FillEmptyPartyLookups(data, fields, extra);
        }
    }

    private async Task<Dictionary<string, string>> CollectPartyGraphAsync(
        string moduleName, int recordId, CancellationToken ct)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var record = await _db.Records.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ModuleId == module.Id && r.Id == recordId, ct);
        if (record is null)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var fields = await _metadata.GetFieldsAsync(module.Id);
        var values = ReadPartyValues(fields, DynamicRecordService.ParseData(record));

        foreach (var dest in PartyOrder)
        {
            if (!values.TryGetValue(dest, out var idRaw)
                || !int.TryParse(idRaw, out var hopId)
                || hopId <= 0)
                continue;
            if (dest.Equals(moduleName, StringComparison.OrdinalIgnoreCase) && hopId == recordId)
                continue;

            var hopModule = await _metadata.GetModuleByNameAsync(dest);
            if (hopModule is null)
                continue;
            var hop = await _db.Records.AsNoTracking()
                .FirstOrDefaultAsync(r => r.ModuleId == hopModule.Id && r.Id == hopId, ct);
            if (hop is null)
                continue;
            var hopFields = await _metadata.GetFieldsAsync(hopModule.Id);
            foreach (var (key, val) in ReadPartyValues(hopFields, DynamicRecordService.ParseData(hop)))
                values.TryAdd(key, val);
        }

        return values;
    }

    private static Dictionary<string, string> ReadPartyValues(
        IReadOnlyList<FieldDef> fields, IReadOnlyDictionary<string, string?> data)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dest in PartyModules)
        {
            var named = dest switch
            {
                "contacts" => "contact",
                "organizations" => "organization",
                "leads" => "lead",
                _ => dest.TrimEnd('s')
            };
            var field = fields.FirstOrDefault(f =>
                            f.Type == FieldType.Lookup
                            && dest.Equals(f.LookupModule, StringComparison.OrdinalIgnoreCase)
                            && f.Name.Equals(named, StringComparison.OrdinalIgnoreCase))
                        ?? fields.FirstOrDefault(f =>
                            f.Type == FieldType.Lookup
                            && dest.Equals(f.LookupModule, StringComparison.OrdinalIgnoreCase));
            if (field is null)
                continue;
            var raw = data.GetValueOrDefault(field.Name);
            if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var id) && id > 0)
                values[dest] = raw.Trim();
        }

        return values;
    }

    private static bool FillEmptyPartyLookups(
        Dictionary<string, string?> data,
        IReadOnlyList<FieldDef> fields,
        IReadOnlyDictionary<string, string> partyValues)
    {
        var changed = false;
        foreach (var (dest, id) in partyValues)
        {
            foreach (var field in fields.Where(f =>
                         f.Type == FieldType.Lookup
                         && dest.Equals(f.LookupModule, StringComparison.OrdinalIgnoreCase)))
            {
                if (!string.IsNullOrWhiteSpace(data.GetValueOrDefault(field.Name)))
                    continue;
                data[field.Name] = id;
                changed = true;
            }
        }

        return changed;
    }

    private async Task CopyCampaignMembersAsync(int leadId, int contactId, CancellationToken ct)
    {
        var leadMembers = await _db.CampaignMembers
            .Where(m => m.ModuleName == "leads" && m.RecordId == leadId)
            .ToListAsync(ct);
        if (leadMembers.Count == 0)
            return;

        var existing = await _db.CampaignMembers
            .Where(m => m.ModuleName == "contacts" && m.RecordId == contactId)
            .Select(m => m.CampaignId)
            .ToListAsync(ct);
        var have = existing.ToHashSet();
        foreach (var member in leadMembers)
        {
            if (!have.Add(member.CampaignId))
                continue;
            _db.CampaignMembers.Add(new CampaignMember
            {
                CampaignId = member.CampaignId,
                ModuleName = "contacts",
                RecordId = contactId
            });
        }

        if (_db.ChangeTracker.HasChanges())
            await _db.SaveChangesAsync(ct);
    }

    private async Task CopyManyToManyLinksAsync(int leadModuleId, int leadId, int contactId, CancellationToken ct)
    {
        var contactsModule = await _metadata.GetModuleByNameAsync("contacts");
        if (contactsModule is null)
            return;

        var relations = await _db.Relations.AsNoTracking()
            .Where(r => r.IsManyToMany || r.Kind == RelationKind.ManyToMany)
            .ToListAsync(ct);

        var leadRels = relations.Where(r =>
            r.SourceModuleId == leadModuleId || r.TargetModuleId == leadModuleId).ToList();

        foreach (var leadRel in leadRels)
        {
            var otherModuleId = leadRel.SourceModuleId == leadModuleId
                ? leadRel.TargetModuleId
                : leadRel.SourceModuleId;
            var contactRel = relations.FirstOrDefault(r =>
                r.Id != leadRel.Id
                && (r.SourceModuleId == contactsModule.Id || r.TargetModuleId == contactsModule.Id)
                && (r.SourceModuleId == otherModuleId || r.TargetModuleId == otherModuleId));
            if (contactRel is null)
                continue;

            var links = await _db.RecordLinks
                .Where(l => l.RelationId == leadRel.Id && (l.LeftRecordId == leadId || l.RightRecordId == leadId))
                .ToListAsync(ct);

            foreach (var link in links)
            {
                var other = link.LeftRecordId == leadId ? link.RightRecordId : link.LeftRecordId;
                var left = Math.Min(contactId, other);
                var right = Math.Max(contactId, other);
                var exists = await _db.RecordLinks.AnyAsync(l =>
                    l.RelationId == contactRel.Id && l.LeftRecordId == left && l.RightRecordId == right, ct);
                if (exists)
                    continue;
                _db.RecordLinks.Add(new RecordLink
                {
                    RelationId = contactRel.Id,
                    LeftRecordId = left,
                    RightRecordId = right
                });
            }
        }

        if (_db.ChangeTracker.HasChanges())
            await _db.SaveChangesAsync(ct);
    }

    public static bool PartyLookupsChanged(
        IReadOnlyList<FieldDef> fields,
        IReadOnlyDictionary<string, string?> oldData,
        IReadOnlyDictionary<string, string?> newData)
    {
        foreach (var field in fields.Where(f =>
                     f.Type == FieldType.Lookup
                     && PartyModules.Contains(f.LookupModule ?? "")))
        {
            var oldVal = oldData.GetValueOrDefault(field.Name);
            var newVal = newData.GetValueOrDefault(field.Name);
            if (!string.Equals(oldVal, newVal, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool HasFillableEmptyPartyLookup(
        IReadOnlyList<FieldDef> fields, IReadOnlyDictionary<string, string?> data)
    {
        var hasLinked = false;
        var hasEmpty = false;
        foreach (var field in fields.Where(f =>
                     f.Type == FieldType.Lookup
                     && PartyModules.Contains(f.LookupModule ?? "")))
        {
            if (string.IsNullOrWhiteSpace(data.GetValueOrDefault(field.Name)))
                hasEmpty = true;
            else
                hasLinked = true;
        }

        return hasLinked && hasEmpty;
    }

    public static bool HasPartyLookupValue(
        IReadOnlyList<FieldDef> fields, IReadOnlyDictionary<string, string?> data)
    {
        foreach (var field in fields.Where(f =>
                     f.Type == FieldType.Lookup
                     && PartyModules.Contains(f.LookupModule ?? "")))
        {
            if (!string.IsNullOrWhiteSpace(data.GetValueOrDefault(field.Name)))
                return true;
        }

        return false;
    }

    private static bool IsSafeJsonKey(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length <= 64
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}
