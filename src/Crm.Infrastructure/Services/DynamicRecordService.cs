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
    public IReadOnlyList<DuplicateHit> Duplicates { get; }

    public RecordValidationException(
        IReadOnlyDictionary<string, string> errors,
        IReadOnlyList<DuplicateHit>? duplicates = null)
        : base("Record validation failed.")
    {
        Errors = errors;
        Duplicates = duplicates ?? [];
    }
}

public sealed class DuplicateHit
{
    public string FieldName { get; init; } = "";
    public string FieldLabel { get; init; } = "";
    public string? Value { get; init; }
    public int ModuleId { get; init; }
    public string ModuleName { get; init; } = "";
    public string ModuleLabel { get; init; } = "";
    public int RecordId { get; init; }
    public string RecordTitle { get; init; } = "";
    public bool IsGlobal { get; init; }

    public string Message =>
        $"مقدار «{FieldLabel}» در {ModuleLabel} «{RecordTitle}» (#{RecordId}) استفاده شده است.";

    public string DetailsUrl => $"/App/m/{ModuleName}/{RecordId}";
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
    private readonly PartyLinkService _partyLinks;

    public DynamicRecordService(
        CrmDbContext db,
        ITenantContext tenant,
        MetadataService metadata,
        RecordAccessService access,
        AuditService audit,
        IBackgroundJobClient jobs,
        PartyLinkService partyLinks)
    {
        _db = db;
        _tenant = tenant;
        _metadata = metadata;
        _access = access;
        _audit = audit;
        _jobs = jobs;
        _partyLinks = partyLinks;
    }

    /// <summary>اجرای async قوانین گردش‌کار پس از ایجاد/ویرایش رکورد.</summary>
    private void EnqueueWorkflows(int moduleId, int recordId, WorkflowTrigger trigger)
    {
        if (_tenant.TenantId is int tenantId)
            _jobs.Enqueue<WorkflowEngine>(engine =>
                engine.ExecuteForRecordAsync(tenantId, moduleId, recordId, trigger));
    }

    public Task<(IReadOnlyList<DynamicRecord> Items, int TotalCount)> ListAsync(
        int moduleId, string? search, int page, int pageSize, bool includeTotal = true) =>
        ListAsync(moduleId, new RecordListQuery
        {
            Search = search,
            Page = page,
            PageSize = pageSize
        }, includeTotal);

    public async Task<(IReadOnlyList<DynamicRecord> Items, int TotalCount)> ListAsync(
        int moduleId, RecordListQuery listQuery, bool includeTotal = true)
    {
        var page = Math.Max(1, listQuery.Page);
        var pageSize = Math.Clamp(listQuery.PageSize <= 0 ? 20 : listQuery.PageSize, 1, 10_000);
        var asc = string.Equals(listQuery.SortDir, "asc", StringComparison.OrdinalIgnoreCase);

        var query = _db.Records.AsNoTracking().Where(r => r.ModuleId == moduleId);
        query = await _access.ApplyVisibilityAsync(query, moduleId);

        if (!string.IsNullOrWhiteSpace(listQuery.Search))
        {
            var term = listQuery.Search.Trim();
            var like = $"%{EscapeLike(term)}%";
            // جستجوی سراسری: عنوان + کل jsonb (ایندکس GIN روی CustomData کمک می‌کند)
            var tenantId = _tenant.TenantId;
            var matched = await _db.Database.SqlQuery<IdRow>($"""
                SELECT r."Id" AS "Id"
                FROM "Records" r
                WHERE r."ModuleId" = {moduleId}
                  AND r."TenantId" = {tenantId}
                  AND r."IsDeleted" = FALSE
                  AND (
                    r."Title" ILIKE {like} ESCAPE '\'
                    OR COALESCE(r."CustomData"::text, '') ILIKE {like} ESCAPE '\'
                  )
                """).ToListAsync();
            var searchIds = matched.Select(x => x.Id).ToHashSet();
            if (searchIds.Count == 0)
                return ([], 0);
            query = query.Where(r => searchIds.Contains(r.Id));
        }

        var filters = listQuery.Filters
            .Where(f => IsSafeFieldName(f.Field) && !string.IsNullOrWhiteSpace(f.Value))
            .ToList();

        HashSet<int>? filteredIds = null;
        if (filters.Count > 0)
        {
            filteredIds = await FilterIdsByColumnsAsync(moduleId, filters);
            if (filteredIds.Count == 0)
                return ([], 0);
            query = query.Where(r => filteredIds.Contains(r.Id));
        }

        var sortField = listQuery.SortField?.Trim();
        var useJsonSort = IsSafeFieldName(sortField) && !IsTitleField(sortField!);

        if (useJsonSort)
        {
            var fields = await _metadata.GetFieldsAsync(moduleId);
            var sortFieldDef = fields.FirstOrDefault(f =>
                string.Equals(f.Name, sortField, StringComparison.OrdinalIgnoreCase));
            var sortKind = ClassifySortType(sortFieldDef?.Type);

            var total = includeTotal ? await query.CountAsync() : 0;
            if (total == 0)
                return ([], 0);

            var skip = (page - 1) * pageSize;
            List<IdRow> pageIds;

            async Task<List<IdRow>> SortPageAsync(int[] idArray)
            {
                if (sortKind == "numeric")
                {
                    return asc
                        ? await _db.Database.SqlQuery<IdRow>($"""
                            SELECT r."Id" AS "Id"
                            FROM "Records" r
                            WHERE r."Id" = ANY({idArray})
                            ORDER BY NULLIF(r."CustomData" ->> {sortField}, '')::numeric ASC NULLS LAST, r."Id" ASC
                            OFFSET {skip} LIMIT {pageSize}
                            """).ToListAsync()
                        : await _db.Database.SqlQuery<IdRow>($"""
                            SELECT r."Id" AS "Id"
                            FROM "Records" r
                            WHERE r."Id" = ANY({idArray})
                            ORDER BY NULLIF(r."CustomData" ->> {sortField}, '')::numeric DESC NULLS LAST, r."Id" DESC
                            OFFSET {skip} LIMIT {pageSize}
                            """).ToListAsync();
                }

                if (sortKind == "date")
                {
                    return asc
                        ? await _db.Database.SqlQuery<IdRow>($"""
                            SELECT r."Id" AS "Id"
                            FROM "Records" r
                            WHERE r."Id" = ANY({idArray})
                            ORDER BY NULLIF(r."CustomData" ->> {sortField}, '')::timestamptz ASC NULLS LAST, r."Id" ASC
                            OFFSET {skip} LIMIT {pageSize}
                            """).ToListAsync()
                        : await _db.Database.SqlQuery<IdRow>($"""
                            SELECT r."Id" AS "Id"
                            FROM "Records" r
                            WHERE r."Id" = ANY({idArray})
                            ORDER BY NULLIF(r."CustomData" ->> {sortField}, '')::timestamptz DESC NULLS LAST, r."Id" DESC
                            OFFSET {skip} LIMIT {pageSize}
                            """).ToListAsync();
                }

                return asc
                    ? await _db.Database.SqlQuery<IdRow>($"""
                        SELECT r."Id" AS "Id"
                        FROM "Records" r
                        WHERE r."Id" = ANY({idArray})
                        ORDER BY COALESCE(r."CustomData" ->> {sortField}, '') ASC, r."Id" ASC
                        OFFSET {skip} LIMIT {pageSize}
                        """).ToListAsync()
                    : await _db.Database.SqlQuery<IdRow>($"""
                        SELECT r."Id" AS "Id"
                        FROM "Records" r
                        WHERE r."Id" = ANY({idArray})
                        ORDER BY COALESCE(r."CustomData" ->> {sortField}, '') DESC, r."Id" DESC
                        OFFSET {skip} LIMIT {pageSize}
                        """).ToListAsync();
            }

            if (filteredIds is { Count: > 0 })
            {
                pageIds = await SortPageAsync(filteredIds.ToArray());
            }
            else
            {
                var idArray = await query.Select(r => r.Id).ToArrayAsync();
                if (idArray.Length == 0)
                    return ([], 0);
                if (!includeTotal)
                    total = idArray.Length;
                pageIds = await SortPageAsync(idArray);
            }

            var orderedIds = pageIds.Select(x => x.Id).ToList();
            if (orderedIds.Count == 0)
                return ([], total);

            var map = await _db.Records.AsNoTracking()
                .Where(r => orderedIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id);
            var items = orderedIds.Where(map.ContainsKey).Select(id => map[id]).ToList();
            return (items, total);
        }

        var totalCount = includeTotal ? await query.CountAsync() : 0;

        IOrderedQueryable<DynamicRecord> ordered;
        if (string.IsNullOrWhiteSpace(sortField) ||
            string.Equals(sortField, "id", StringComparison.OrdinalIgnoreCase))
        {
            ordered = asc ? query.OrderBy(r => r.Id) : query.OrderByDescending(r => r.Id);
        }
        else if (IsTitleField(sortField))
        {
            ordered = asc ? query.OrderBy(r => r.Title) : query.OrderByDescending(r => r.Title);
        }
        else
        {
            ordered = query.OrderByDescending(r => r.Id);
        }

        var pageItems = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (pageItems, totalCount);
    }

    private async Task<HashSet<int>> FilterIdsByColumnsAsync(int moduleId, List<ColumnFilter> filters)
    {
        HashSet<int>? current = null;
        foreach (var filter in filters)
        {
            var ids = await ApplyOneColumnFilterAsync(moduleId, filter);
            if (ids.Count == 0)
                return [];

            current = current is null
                ? ids
                : current.Intersect(ids).ToHashSet();

            if (current.Count == 0)
                return [];
        }

        return current ?? [];
    }

    private async Task<HashSet<int>> ApplyOneColumnFilterAsync(int moduleId, ColumnFilter filter)
    {
        var tenantId = _tenant.TenantId;
        var field = filter.Field.Trim();
        var value = filter.Value.Trim();
        var op = NormalizeFilterOp(filter.Op);
        var isTitle = IsTitleField(field);

        List<IdRow> rows;
        if (op is "isempty" or "isnotempty")
        {
            var wantEmpty = op == "isempty";
            if (isTitle)
            {
                rows = wantEmpty
                    ? await _db.Database.SqlQuery<IdRow>($"""
                        SELECT r."Id" AS "Id"
                        FROM "Records" r
                        WHERE r."ModuleId" = {moduleId}
                          AND r."TenantId" = {tenantId}
                          AND r."IsDeleted" = FALSE
                          AND COALESCE(TRIM(r."Title"), '') = ''
                        """).ToListAsync()
                    : await _db.Database.SqlQuery<IdRow>($"""
                        SELECT r."Id" AS "Id"
                        FROM "Records" r
                        WHERE r."ModuleId" = {moduleId}
                          AND r."TenantId" = {tenantId}
                          AND r."IsDeleted" = FALSE
                          AND COALESCE(TRIM(r."Title"), '') <> ''
                        """).ToListAsync();
            }
            else
            {
                rows = wantEmpty
                    ? await _db.Database.SqlQuery<IdRow>($"""
                        SELECT r."Id" AS "Id"
                        FROM "Records" r
                        WHERE r."ModuleId" = {moduleId}
                          AND r."TenantId" = {tenantId}
                          AND r."IsDeleted" = FALSE
                          AND COALESCE(TRIM(r."CustomData" ->> {field}), '') = ''
                        """).ToListAsync()
                    : await _db.Database.SqlQuery<IdRow>($"""
                        SELECT r."Id" AS "Id"
                        FROM "Records" r
                        WHERE r."ModuleId" = {moduleId}
                          AND r."TenantId" = {tenantId}
                          AND r."IsDeleted" = FALSE
                          AND COALESCE(TRIM(r."CustomData" ->> {field}), '') <> ''
                        """).ToListAsync();
            }
        }
        else if (op is "equals" or "notequals")
        {
            var negate = op == "notequals";
            if (isTitle)
            {
                rows = negate
                    ? await _db.Database.SqlQuery<IdRow>($"""
                        SELECT r."Id" AS "Id"
                        FROM "Records" r
                        WHERE r."ModuleId" = {moduleId}
                          AND r."TenantId" = {tenantId}
                          AND r."IsDeleted" = FALSE
                          AND LOWER(COALESCE(r."Title", '')) <> LOWER({value})
                        """).ToListAsync()
                    : await _db.Database.SqlQuery<IdRow>($"""
                        SELECT r."Id" AS "Id"
                        FROM "Records" r
                        WHERE r."ModuleId" = {moduleId}
                          AND r."TenantId" = {tenantId}
                          AND r."IsDeleted" = FALSE
                          AND LOWER(COALESCE(r."Title", '')) = LOWER({value})
                        """).ToListAsync();
            }
            else
            {
                rows = negate
                    ? await _db.Database.SqlQuery<IdRow>($"""
                        SELECT r."Id" AS "Id"
                        FROM "Records" r
                        WHERE r."ModuleId" = {moduleId}
                          AND r."TenantId" = {tenantId}
                          AND r."IsDeleted" = FALSE
                          AND LOWER(COALESCE(r."CustomData" ->> {field}, '')) <> LOWER({value})
                        """).ToListAsync()
                    : await _db.Database.SqlQuery<IdRow>($"""
                        SELECT r."Id" AS "Id"
                        FROM "Records" r
                        WHERE r."ModuleId" = {moduleId}
                          AND r."TenantId" = {tenantId}
                          AND r."IsDeleted" = FALSE
                          AND LOWER(COALESCE(r."CustomData" ->> {field}, '')) = LOWER({value})
                        """).ToListAsync();
            }
        }
        else
        {
            var escaped = EscapeLike(value);
            var pattern = op switch
            {
                "startswith" => escaped + "%",
                "endswith" => "%" + escaped,
                _ => "%" + escaped + "%"
            };
            var negateLike = op == "notcontains";

            if (isTitle)
            {
                rows = negateLike
                    ? await _db.Database.SqlQuery<IdRow>($"""
                        SELECT r."Id" AS "Id"
                        FROM "Records" r
                        WHERE r."ModuleId" = {moduleId}
                          AND r."TenantId" = {tenantId}
                          AND r."IsDeleted" = FALSE
                          AND COALESCE(r."Title", '') NOT ILIKE {pattern} ESCAPE '\'
                        """).ToListAsync()
                    : await _db.Database.SqlQuery<IdRow>($"""
                        SELECT r."Id" AS "Id"
                        FROM "Records" r
                        WHERE r."ModuleId" = {moduleId}
                          AND r."TenantId" = {tenantId}
                          AND r."IsDeleted" = FALSE
                          AND r."Title" ILIKE {pattern} ESCAPE '\'
                        """).ToListAsync();
            }
            else
            {
                rows = negateLike
                    ? await _db.Database.SqlQuery<IdRow>($"""
                        SELECT r."Id" AS "Id"
                        FROM "Records" r
                        WHERE r."ModuleId" = {moduleId}
                          AND r."TenantId" = {tenantId}
                          AND r."IsDeleted" = FALSE
                          AND COALESCE(r."CustomData" ->> {field}, '') NOT ILIKE {pattern} ESCAPE '\'
                        """).ToListAsync()
                    : await _db.Database.SqlQuery<IdRow>($"""
                        SELECT r."Id" AS "Id"
                        FROM "Records" r
                        WHERE r."ModuleId" = {moduleId}
                          AND r."TenantId" = {tenantId}
                          AND r."IsDeleted" = FALSE
                          AND COALESCE(r."CustomData" ->> {field}, '') ILIKE {pattern} ESCAPE '\'
                        """).ToListAsync();
            }
        }

        return rows.Select(r => r.Id).ToHashSet();
    }

    private static string NormalizeFilterOp(string? op) =>
        (op ?? "contains").Trim().ToLowerInvariant() switch
        {
            "startswith" => "startswith",
            "endswith" => "endswith",
            "equals" => "equals",
            "notequals" => "notequals",
            "notcontains" => "notcontains",
            "isempty" => "isempty",
            "isnotempty" => "isnotempty",
            _ => "contains"
        };

    private static bool IsTitleField(string field) =>
        string.Equals(field, "title", StringComparison.OrdinalIgnoreCase);

    private static string ClassifySortType(FieldType? type) => type switch
    {
        FieldType.Number or FieldType.Decimal or FieldType.Currency or FieldType.Percent => "numeric",
        FieldType.Date or FieldType.DateTime => "date",
        _ => "text"
    };

    private static bool IsSafeFieldName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.Length <= 64 &&
        name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    /// <summary>
    /// رکوردهایی که مقدار تاریخِ فیلد jsonb در بازهٔ [from, to] است (برای تقویم).
    /// </summary>
    public async Task<IReadOnlyList<DynamicRecord>> ListByJsonDateRangeAsync(
        int moduleId, string dateField, DateTime fromUtc, DateTime toUtc, int max = 400)
    {
        if (string.IsNullOrWhiteSpace(dateField) ||
            dateField.Length > 64 ||
            !dateField.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            return [];

        var tenantId = _tenant.TenantId;
        var fromIso = fromUtc.ToString("yyyy-MM-dd");
        var toIso = toUtc.ToString("yyyy-MM-dd");

        // مقایسه متنی ISO روی jsonb؛ برای فیلتر تقویم کافی و سریع است
        var ids = await _db.Database
            .SqlQuery<IdRow>($"""
                SELECT r."Id" AS "Id"
                FROM "Records" r
                WHERE r."ModuleId" = {moduleId}
                  AND r."TenantId" = {tenantId}
                  AND r."IsDeleted" = FALSE
                  AND COALESCE(r."CustomData" ->> {dateField}, '') >= {fromIso}
                  AND COALESCE(r."CustomData" ->> {dateField}, '') < {toIso}
                ORDER BY r."Id" DESC
                LIMIT {max}
                """)
            .ToListAsync();

        if (ids.Count == 0)
            return [];

        var idList = ids.Select(i => i.Id).ToList();
        var query = _db.Records.AsNoTracking().Where(r => r.ModuleId == moduleId && idList.Contains(r.Id));
        query = await _access.ApplyVisibilityAsync(query, moduleId);
        var map = await query.ToDictionaryAsync(r => r.Id);
        return idList.Where(map.ContainsKey).Select(id => map[id]).ToList();
    }

    private sealed class IdRow
    {
        public int Id { get; set; }
    }

    public async Task<DynamicRecord?> GetAsync(int moduleId, int id)
    {
        var query = _db.Records.AsNoTracking().Where(r => r.ModuleId == moduleId && r.Id == id);
        query = await _access.ApplyVisibilityAsync(query, moduleId);
        return await query.FirstOrDefaultAsync();
    }

    /// <summary>اگر یک فیلد تلفن چند شماره چسبیده داشته باشد، هر شماره را در خط جدا ذخیره می‌کند.</summary>
    public async Task<DynamicRecord> SeparatePhoneNumbersAsync(DynamicRecord record)
    {
        var fields = await _metadata.GetFieldsAsync(record.ModuleId);
        var data = ParseData(record);
        var changed = false;
        foreach (var field in fields.Where(f => f.Type == FieldType.Phone))
        {
            if (!data.TryGetValue(field.Name, out var raw) || string.IsNullOrWhiteSpace(raw))
                continue;
            var joined = PhoneIdentity.Join(PhoneIdentity.Split(raw));
            if (joined.Length == 0 || joined == raw.Trim())
                continue;
            data[field.Name] = joined;
            changed = true;
        }

        if (!changed)
            return record;

        var tracked = await _db.Records.FirstOrDefaultAsync(r => r.Id == record.Id && r.ModuleId == record.ModuleId);
        if (tracked is null)
            return record;

        tracked.CustomData = JsonSerializer.Serialize(data);
        await _db.SaveChangesAsync();
        record.CustomData = tracked.CustomData;
        return record;
    }

    public async Task<DynamicRecord> CreateAsync(
        int moduleId, Dictionary<string, string?> values, int? excludeDuplicateRecordId = null,
        bool skipDuplicateCheck = false, bool skipPartyPropagate = false)
    {
        var module = await _db.Modules.AsNoTracking().FirstAsync(m => m.Id == moduleId);
        var fields = await _metadata.GetFieldsAsync(moduleId);

        var data = ValidateAndBuildData(fields, values);
        KeepIntegrationExtras(values, oldData: new Dictionary<string, string?>(), data);
        if (!skipPartyPropagate)
            await _partyLinks.EnrichFromLinkedPartiesAsync(fields, data);
        await ValidateLookupsAsync(fields, data);
        if (!skipDuplicateCheck)
            await CheckDuplicatesAsync(moduleId, fields, data, excludeRecordId: excludeDuplicateRecordId);

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
        if (!skipPartyPropagate && PartyLinkService.HasPartyLookupValue(fields, data))
            await _partyLinks.PropagateFromAsync(module.Name, record.Id);
        return record;
    }

    public async Task UpdateAsync(
        int moduleId, int id, Dictionary<string, string?> values,
        bool skipDuplicateCheck = false, bool skipPartyPropagate = false)
    {
        var module = await _db.Modules.AsNoTracking().FirstAsync(m => m.Id == moduleId);
        var record = await _db.Records.FirstOrDefaultAsync(r => r.ModuleId == moduleId && r.Id == id)
            ?? throw new InvalidOperationException("Record not found.");

        if (!await _access.CanModifyRecordAsync(record))
            throw new UnauthorizedAccessException();

        var fields = await _metadata.GetFieldsAsync(moduleId);
        var oldData = JsonSerializer.Deserialize<Dictionary<string, string?>>(record.CustomData)
            ?? new Dictionary<string, string?>();
        var data = ValidateAndBuildData(fields, values);
        KeepIntegrationExtras(values, oldData, data);
        if (!skipPartyPropagate)
            await _partyLinks.EnrichFromLinkedPartiesAsync(fields, data);
        await ValidateLookupsAsync(fields, data);
        if (!skipDuplicateCheck)
            await CheckDuplicatesAsync(moduleId, fields, data, excludeRecordId: id);

        var changes = data
            .Where(kv => (oldData.TryGetValue(kv.Key, out var old) ? old : null) != kv.Value)
            .ToDictionary(
                kv => kv.Key,
                kv => (object)new Dictionary<string, string?>
                {
                    ["old"] = oldData.GetValueOrDefault(kv.Key),
                    ["new"] = kv.Value
                });

        record.CustomData = JsonSerializer.Serialize(data);
        record.Title = ResolveTitle(fields, data);

        _audit.Log(module.Name, record.Id, "Update", changes);
        await _db.SaveChangesAsync();

        if (changes.Count > 0)
            EnqueueWorkflows(moduleId, id, WorkflowTrigger.RecordUpdated);
        if (!skipPartyPropagate && PartyLinkService.PartyLookupsChanged(fields, oldData, data))
            await _partyLinks.PropagateFromAsync(module.Name, id);
    }

    /// <summary>فیلدهای خالی مقصد را از ورودی پر می‌کند؛ توضیح را اضافه می‌کند نه جایگزین.</summary>
    public async Task MergeMissingFieldsAsync(int moduleId, int targetId, Dictionary<string, string?> incoming)
    {
        var target = await GetAsync(moduleId, targetId)
            ?? throw new InvalidOperationException("Record not found.");
        var data = ParseData(target);
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

        await UpdateAsync(moduleId, targetId, data, skipDuplicateCheck: true);
    }

    /// <summary>کپی رکورد؛ عنوان با پسوند «(کپی)» و بدون تغییر مالک.</summary>
    public async Task<DynamicRecord> CloneAsync(int moduleId, int id)
    {
        var module = await _db.Modules.AsNoTracking().FirstAsync(m => m.Id == moduleId);
        if (!await _access.CanCreateAsync(moduleId))
            throw new UnauthorizedAccessException();

        var source = await GetAsync(moduleId, id)
            ?? throw new InvalidOperationException("Record not found.");

        var fields = await _metadata.GetFieldsAsync(moduleId);
        var data = ParseData(source);

        // ریست فیلدهای سند
        foreach (var key in new[] { "number", "status", "sourceRecordId" })
        {
            if (data.ContainsKey(key))
                data[key] = key == "status" ? "Draft" : null;
        }

        var titleField = fields.FirstOrDefault(f =>
            f.Name is "name" or "title" or "subject");
        if (titleField is not null && data.TryGetValue(titleField.Name, out var titleVal)
            && !string.IsNullOrWhiteSpace(titleVal))
        {
            data[titleField.Name] = titleVal.Trim() + " (کپی)";
        }

        return await CreateAsync(moduleId, data);
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
        var oldData = new Dictionary<string, string?>(data);
        data[fieldName] = value;
        await _partyLinks.EnrichFromLinkedPartiesAsync(fields, data);
        record.CustomData = JsonSerializer.Serialize(data);

        _audit.Log(module.Name, record.Id, "Update", new Dictionary<string, object?>
        {
            [fieldName] = new { Old = old, New = value }
        });
        await _db.SaveChangesAsync();

        if (old != value)
            EnqueueWorkflows(moduleId, id, WorkflowTrigger.RecordUpdated);
        if (PartyLinkService.PartyLookupsChanged(fields, oldData, data))
            await _partyLinks.PropagateFromAsync(module.Name, id);
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

    /// <summary>سطل بازیابی: رکوردهای حذف‌شده Tenant جاری، صفحه‌بندی‌شده.</summary>
    public async Task<(IReadOnlyList<DynamicRecord> Items, int Total)> ListDeletedAsync(int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.Records
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.IsDeleted && r.TenantId == _tenant.TenantId);
        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(r => r.DeletedAtUtc)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(r => r.Module)
            .ToListAsync();
        return (items, total);
    }

    /// <summary>حذف همیشگی رکوردهای داخل سطل. مسیر فایل پیوست‌ها برای پاک‌کردن از دیسک برمی‌گردد.</summary>
    public async Task<(int Count, IReadOnlyList<string> FilePaths)> PurgeDeletedAsync(IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0 || _tenant.TenantId is not int tenantId)
            return (0, []);

        var wanted = ids.Distinct().Take(200).ToArray();
        var records = await _db.Records
            .IgnoreQueryFilters()
            .Include(r => r.Module)
            .Where(r => wanted.Contains(r.Id) && r.IsDeleted && r.TenantId == tenantId)
            .ToListAsync();

        var allowed = new List<DynamicRecord>();
        foreach (var record in records)
        {
            if (await _access.CanDeleteAsync(record.ModuleId))
                allowed.Add(record);
        }
        if (allowed.Count == 0)
            return (0, []);

        var paths = new List<string>();
        foreach (var group in allowed.GroupBy(r => r.Module.Name))
        {
            var groupIds = group.Select(r => r.Id).ToArray();
            var stored = await _db.Attachments
                .IgnoreQueryFilters()
                .Where(a => a.TenantId == tenantId && a.ModuleName == group.Key && groupIds.Contains(a.RecordId))
                .Select(a => a.StoredPath)
                .ToListAsync();
            paths.AddRange(stored.Where(p => !string.IsNullOrWhiteSpace(p))!);

            await _db.Notes.IgnoreQueryFilters()
                .Where(n => n.TenantId == tenantId && n.ModuleName == group.Key && groupIds.Contains(n.RecordId))
                .ExecuteDeleteAsync();
            await _db.Attachments.IgnoreQueryFilters()
                .Where(a => a.TenantId == tenantId && a.ModuleName == group.Key && groupIds.Contains(a.RecordId))
                .ExecuteDeleteAsync();
            await _db.TagLinks.IgnoreQueryFilters()
                .Where(t => t.TenantId == tenantId && t.ModuleName == group.Key && groupIds.Contains(t.RecordId))
                .ExecuteDeleteAsync();
        }

        var allIds = allowed.Select(r => r.Id).ToArray();
        await _db.RecordLinks.IgnoreQueryFilters()
            .Where(l => l.TenantId == tenantId && (allIds.Contains(l.LeftRecordId) || allIds.Contains(l.RightRecordId)))
            .ExecuteDeleteAsync();

        foreach (var record in allowed)
            _audit.Log(record.Module.Name, record.Id, "Purge");
        await _db.SaveChangesAsync();

        await _db.Records.IgnoreQueryFilters()
            .Where(r => allIds.Contains(r.Id) && r.TenantId == tenantId)
            .ExecuteDeleteAsync();

        return (allowed.Count, paths);
    }

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

    /// <summary>
    /// تجمیع مقادیر یک فیلد jsonb در SQL (به‌جای لود هزاران رکورد به حافظه).
    /// </summary>
    public async Task<IReadOnlyList<(string Value, int Count)>> AggregateFieldAsync(int moduleId, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName) ||
            fieldName.Length > 64 ||
            !fieldName.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            return [];

        var tenantId = _tenant.TenantId;
        var rows = await _db.Database
            .SqlQuery<FieldAggregateRow>($"""
                SELECT COALESCE("CustomData" ->> {fieldName}, '(خالی)') AS "Value", COUNT(*)::int AS "Count"
                FROM "Records"
                WHERE "ModuleId" = {moduleId}
                  AND "TenantId" = {tenantId}
                  AND "IsDeleted" = FALSE
                GROUP BY 1
                ORDER BY 2 DESC
                """)
            .ToListAsync();

        return rows.Select(r => (r.Value ?? "(خالی)", r.Count)).ToList();
    }

    private sealed class FieldAggregateRow
    {
        public string? Value { get; set; }
        public int Count { get; set; }
    }

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
            {
                value = field.DefaultValue == "__TODAY__"
                    ? DateTime.Today.ToString("yyyy-MM-dd")
                    : field.DefaultValue;
            }

            if (field.IsRequired && value is null)
            {
                errors[field.Name] = $"فیلد «{field.Label}» الزامی است.";
                continue;
            }

            if (value is not null)
            {
                if (field.MaxLength is int maxLen && value.Length > maxLen)
                {
                    errors[field.Name] = $"طول «{field.Label}» نباید بیشتر از {maxLen} باشد.";
                    continue;
                }

                switch (field.Type)
                {
                    case FieldType.Number:
                        if (!long.TryParse(value, out var n))
                        {
                            errors[field.Name] = $"مقدار «{field.Label}» باید عدد صحیح باشد.";
                            continue;
                        }
                        if (field.MaxLength is int maxDigits && value.TrimStart('-').Length > maxDigits)
                        {
                            errors[field.Name] = $"«{field.Label}» حداکثر {maxDigits} رقم مجاز است.";
                            continue;
                        }
                        break;
                    case FieldType.Decimal:
                    case FieldType.Currency:
                    case FieldType.Percent:
                        if (!decimal.TryParse(value, System.Globalization.NumberStyles.Number,
                                System.Globalization.CultureInfo.InvariantCulture, out _) &&
                            !decimal.TryParse(value, out _))
                        {
                            errors[field.Name] = $"مقدار «{field.Label}» باید عددی باشد.";
                            continue;
                        }
                        if (!ValidateDecimalDigits(field, value, out var digitErr))
                        {
                            errors[field.Name] = digitErr!;
                            continue;
                        }
                        break;
                    case FieldType.Phone:
                        value = PhoneIdentity.Join(PhoneIdentity.Split(value));
                        if (string.IsNullOrEmpty(value))
                            value = null;
                        break;
                    case FieldType.Email when !value.Contains('@'):
                        errors[field.Name] = $"مقدار «{field.Label}» ایمیل معتبر نیست.";
                        continue;
                    case FieldType.Picklist when field.PicklistValues.Count > 0 &&
                                                 !field.PicklistValues.Any(p => p.Value == value):
                        errors[field.Name] = $"مقدار «{field.Label}» از میان گزینه‌های مجاز نیست.";
                        continue;
                    case FieldType.MultiPicklist when field.PicklistValues.Count > 0:
                    {
                        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        var allowed = field.PicklistValues.Select(p => p.Value).ToHashSet(StringComparer.Ordinal);
                        if (parts.Any(p => !allowed.Contains(p)))
                        {
                            errors[field.Name] = $"مقدار «{field.Label}» از میان گزینه‌های مجاز نیست.";
                            continue;
                        }
                        value = string.Join(",", parts);
                        break;
                    }
                }

                if (value is not null)
                    ApplyValidationRules(field, value, values, errors);
                if (errors.ContainsKey(field.Name))
                    continue;
            }

            data[field.Name] = value;
        }

        FormulaEvaluator.ApplyFormulas(fields, data);

        if (errors.Count > 0)
            throw new RecordValidationException(errors);

        return data;
    }

    /// <summary>
    /// کلیدهای ضبط/تلفنچی که ممکن است هنوز فیلد ماژول نباشند، یا در فرم آپدیت
    /// نیامده باشند، نباید از CustomData پاک شوند.
    /// </summary>
    private static void KeepIntegrationExtras(
        Dictionary<string, string?> values,
        IReadOnlyDictionary<string, string?> oldData,
        Dictionary<string, string?> data)
    {
        foreach (var key in oldData.Keys.Concat(values.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!IsIntegrationExtra(key))
                continue;

            var incoming = values.GetValueOrDefault(key);
            if (!string.IsNullOrWhiteSpace(incoming))
            {
                data[key] = incoming.Trim();
                continue;
            }

            var previous = oldData.GetValueOrDefault(key);
            if (!string.IsNullOrWhiteSpace(previous)
                && string.IsNullOrWhiteSpace(data.GetValueOrDefault(key)))
                data[key] = previous;
        }
    }

    private static bool IsIntegrationExtra(string key) =>
        key.StartsWith("recording", StringComparison.OrdinalIgnoreCase)
        || key.Equals("hasAudio", StringComparison.OrdinalIgnoreCase)
        || key.Equals("fileStatus", StringComparison.OrdinalIgnoreCase);

    private async Task ValidateLookupsAsync(
        IReadOnlyList<FieldDef> fields, Dictionary<string, string?> data)
    {
        var errors = new Dictionary<string, string>();

        foreach (var field in fields.Where(f => f.Type == FieldType.Lookup))
        {
            if (!data.TryGetValue(field.Name, out var raw) || string.IsNullOrWhiteSpace(raw))
                continue;

            if (!int.TryParse(raw, out var lookupId) || lookupId <= 0)
            {
                errors[field.Name] = $"مقدار «{field.Label}» نامعتبر است.";
                continue;
            }

            if (string.IsNullOrWhiteSpace(field.LookupModule))
                continue;

            var targetModule = await _metadata.GetModuleByNameAsync(field.LookupModule);
            if (targetModule is null)
            {
                errors[field.Name] = $"ماژول مقصد «{field.LookupModule}» یافت نشد.";
                continue;
            }

            if (!await _access.CanViewModuleAsync(targetModule.Id))
            {
                errors[field.Name] = $"دسترسی به ماژول «{field.Label}» ندارید.";
                continue;
            }

            var visible = await GetAsync(targetModule.Id, lookupId);
            if (visible is null)
                errors[field.Name] = $"رکورد انتخاب‌شده برای «{field.Label}» معتبر نیست.";
        }

        if (errors.Count > 0)
            throw new RecordValidationException(errors);
    }

    private static bool ValidateDecimalDigits(FieldDef field, string value, out string? error)
    {
        error = null;
        var normalized = value.Trim().TrimStart('-');
        var parts = normalized.Split('.', 2);
        var intPart = parts[0];
        var fracPart = parts.Length > 1 ? parts[1] : "";

        if (field.IntegerDigits is int id && intPart.Length > id)
        {
            error = $"تعداد ارقام صحیح «{field.Label}» نباید بیشتر از {id} باشد.";
            return false;
        }
        if (field.DecimalDigits is int dd && fracPart.Length > dd)
        {
            error = $"تعداد ارقام اعشاری «{field.Label}» نباید بیشتر از {dd} باشد.";
            return false;
        }
        return true;
    }

    private static void ApplyValidationRules(
        FieldDef field,
        string value,
        Dictionary<string, string?> allValues,
        Dictionary<string, string> errors)
    {
        if (string.IsNullOrWhiteSpace(field.ValidationRulesJson) || errors.ContainsKey(field.Name))
            return;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(field.ValidationRulesJson);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
                return;

            foreach (var ruleEl in doc.RootElement.EnumerateArray())
            {
                var ruleName = ruleEl.TryGetProperty("rule", out var r) ? r.GetString() : null;
                var ruleValue = ruleEl.TryGetProperty("value", out var v) && v.ValueKind != System.Text.Json.JsonValueKind.Null
                    ? v.GetString()
                    : null;
                if (string.IsNullOrEmpty(ruleName))
                    continue;

                switch (ruleName)
                {
                    case "englishOnly" when !System.Text.RegularExpressions.Regex.IsMatch(value, @"^[A-Za-z\s]+$"):
                        errors[field.Name] = $"«{field.Label}» فقط باید حروف انگلیسی باشد.";
                        return;
                    case "persianOnly" when !System.Text.RegularExpressions.Regex.IsMatch(value, @"^[\u0600-\u06FF\s]+$"):
                        errors[field.Name] = $"«{field.Label}» فقط باید حروف فارسی باشد.";
                        return;
                    case "minLength" when int.TryParse(ruleValue, out var minL) && value.Length < minL:
                        errors[field.Name] = $"حداقل تعداد کاراکتر «{field.Label}» باید {minL} باشد.";
                        return;
                    case "maxLength" when int.TryParse(ruleValue, out var maxL) && value.Length > maxL:
                        errors[field.Name] = $"حداکثر تعداد کاراکتر «{field.Label}» باید {maxL} باشد.";
                        return;
                    case "nationalId" when !IsValidIranianNationalId(value):
                        errors[field.Name] = $"«{field.Label}» کد ملی معتبر نیست.";
                        return;
                    case "contains" when !string.IsNullOrEmpty(ruleValue) && !value.Contains(ruleValue, StringComparison.Ordinal):
                        errors[field.Name] = $"«{field.Label}» باید شامل «{ruleValue}» باشد.";
                        return;
                    case "digitsOnly" when !System.Text.RegularExpressions.Regex.IsMatch(value, @"^-?\d+$"):
                        errors[field.Name] = $"«{field.Label}» فقط باید اعداد صحیح باشد.";
                        return;
                    case "min" when decimal.TryParse(value, out var num) && decimal.TryParse(ruleValue, out var minV) && num < minV:
                        errors[field.Name] = $"حداقل مقدار «{field.Label}» باید {minV} باشد.";
                        return;
                    case "max" when decimal.TryParse(value, out var num2) && decimal.TryParse(ruleValue, out var maxV) && num2 > maxV:
                        errors[field.Name] = $"حداکثر مقدار «{field.Label}» باید {maxV} باشد.";
                        return;
                    case "beforeToday":
                    case "beforeOrOnToday":
                    case "afterToday":
                    case "afterOrOnToday":
                    case "beforeField":
                    case "afterField":
                    case "afterNDays":
                        if (!TryParseDateValue(value, out var date))
                        {
                            errors[field.Name] = $"مقدار «{field.Label}» تاریخ معتبر نیست.";
                            return;
                        }
                        var today = DateTime.Today;
                        if (ruleName == "beforeToday" && date.Date >= today)
                        {
                            errors[field.Name] = $"«{field.Label}» باید قبل از امروز باشد.";
                            return;
                        }
                        if (ruleName == "beforeOrOnToday" && date.Date > today)
                        {
                            errors[field.Name] = $"«{field.Label}» باید قبل از یا مساوی امروز باشد.";
                            return;
                        }
                        if (ruleName == "afterToday" && date.Date <= today)
                        {
                            errors[field.Name] = $"«{field.Label}» باید بعد از امروز باشد.";
                            return;
                        }
                        if (ruleName == "afterOrOnToday" && date.Date < today)
                        {
                            errors[field.Name] = $"«{field.Label}» باید بعد از یا مساوی امروز باشد.";
                            return;
                        }
                        if (ruleName == "afterNDays" && int.TryParse(ruleValue, out var nDays))
                        {
                            var threshold = today.AddDays(nDays);
                            if (date.Date <= threshold)
                            {
                                errors[field.Name] = $"«{field.Label}» باید بعد از {nDays} روز آینده باشد.";
                                return;
                            }
                        }
                        if ((ruleName is "beforeField" or "afterField") && !string.IsNullOrWhiteSpace(ruleValue))
                        {
                            allValues.TryGetValue(ruleValue, out var otherRaw);
                            if (string.IsNullOrWhiteSpace(otherRaw) || !TryParseDateValue(otherRaw, out var otherDate))
                                break;
                            if (ruleName == "beforeField" && date.Date >= otherDate.Date)
                            {
                                errors[field.Name] = $"«{field.Label}» باید قبل از فیلد مرتبط باشد.";
                                return;
                            }
                            if (ruleName == "afterField" && date.Date <= otherDate.Date)
                            {
                                errors[field.Name] = $"«{field.Label}» باید بعد از فیلد مرتبط باشد.";
                                return;
                            }
                        }
                        break;
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // ignore invalid rule json
        }
    }

    private static bool TryParseDateValue(string value, out DateTime date)
    {
        if (DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeLocal, out date))
            return true;
        if (DateTime.TryParse(value, out date))
            return true;
        date = default;
        return false;
    }

    /// <summary>اعتبارسنجی کد ملی ایران (۱۰ رقم + رقم کنترل).</summary>
    private static bool IsValidIranianNationalId(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;
        var code = input.Trim();
        if (code.Length != 10 || !code.All(char.IsDigit))
            return false;
        if (code.Distinct().Count() == 1)
            return false;

        var check = code[9] - '0';
        var sum = 0;
        for (var i = 0; i < 9; i++)
            sum += (code[i] - '0') * (10 - i);
        var rem = sum % 11;
        return (rem < 2 && check == rem) || (rem >= 2 && check == 11 - rem);
    }

    /// <summary>تشخیص تکراری روی فیلدهای علامت‌خورده با IsUniqueCheck (حالت or / and) + سراسری.</summary>
    private async Task CheckDuplicatesAsync(
        int moduleId, IReadOnlyList<FieldDef> fields,
        Dictionary<string, string?> data, int? excludeRecordId)
    {
        var module = await _db.Modules.AsNoTracking()
            .Where(m => m.Id == moduleId)
            .Select(m => new
            {
                m.DuplicateCheckEnabled,
                m.DuplicateMatchMode,
                m.DuplicateIgnoreEmpty,
                m.GlobalDuplicateEnabled
            })
            .FirstOrDefaultAsync();

        if (module is null)
            return;

        var errors = new Dictionary<string, string>();
        var hits = new List<DuplicateHit>();
        var modules = await _db.Modules.AsNoTracking()
            .Select(m => new { m.Id, m.Name, m.SingularLabel })
            .ToDictionaryAsync(m => m.Id);
        var excludeIds = new HashSet<int>();
        if (excludeRecordId is int self)
            excludeIds.Add(self);
        foreach (var key in new[] { "contact", "lead" })
        {
            if (data.TryGetValue(key, out var linkedRaw)
                && int.TryParse(linkedRaw, out var linkedId)
                && linkedId > 0)
                excludeIds.Add(linkedId);
        }

        DuplicateHit? ToHit(FieldDef field, DynamicRecord record, int recordModuleId, bool global)
        {
            if (!modules.TryGetValue(recordModuleId, out var meta))
                return null;
            return new DuplicateHit
            {
                FieldName = field.Name,
                FieldLabel = field.Label,
                Value = data.GetValueOrDefault(field.Name),
                ModuleId = recordModuleId,
                ModuleName = meta.Name,
                ModuleLabel = meta.SingularLabel,
                RecordId = record.Id,
                RecordTitle = string.IsNullOrWhiteSpace(record.Title) ? $"#{record.Id}" : record.Title,
                IsGlobal = global
            };
        }

        void AddHit(DuplicateHit hit)
        {
            if (hits.Any(h => h.RecordId == hit.RecordId && h.FieldName == hit.FieldName))
                return;
            hits.Add(hit);
            errors[hit.FieldName] = hit.Message;
        }

        if (module.DuplicateCheckEnabled)
        {
            var uniqueFields = fields.Where(f => f.IsUniqueCheck).ToList();
            var matchMode = module.DuplicateMatchMode ?? "or";
            var ignoreEmpty = module.DuplicateIgnoreEmpty;

            if (uniqueFields.Count > 0)
            {
                if (string.Equals(matchMode, "and", StringComparison.OrdinalIgnoreCase))
                {
                    var valued = uniqueFields
                        .Select(f => (Field: f, Value: data.TryGetValue(f.Name, out var v) ? v : null))
                        .Where(x => !ignoreEmpty || !string.IsNullOrEmpty(x.Value))
                        .ToList();

                    var shouldCheck = ignoreEmpty
                        ? valued.Count == uniqueFields.Count && valued.Count > 0
                        : uniqueFields.Count > 0;

                    if (shouldCheck)
                    {
                        var pairs = ignoreEmpty
                            ? valued
                            : uniqueFields
                                .Select(f => (Field: f, Value: data.TryGetValue(f.Name, out var v) ? v : null))
                                .ToList();

                        if (pairs.Count > 0 && (ignoreEmpty || pairs.All(p => !string.IsNullOrEmpty(p.Value))))
                        {
                            var sb = new System.Text.StringBuilder("""SELECT * FROM "Records" WHERE "ModuleId" = {0}""");
                            var args = new List<object> { moduleId };
                            var i = 1;
                            foreach (var (field, value) in pairs)
                            {
                                if (string.IsNullOrEmpty(value))
                                {
                                    sb.Append($" AND (\"CustomData\" ->> {{{i}}} IS NULL OR \"CustomData\" ->> {{{i}}} = '')");
                                    args.Add(field.Name);
                                    i += 1;
                                }
                                else
                                {
                                    sb.Append($" AND \"CustomData\" ->> {{{i}}} = {{{i + 1}}}");
                                    args.Add(field.Name);
                                    args.Add(value);
                                    i += 2;
                                }
                            }

                            var found = await _db.Records
                                .FromSqlRaw(sb.ToString(), args.ToArray())
                                .Where(r => !excludeIds.Contains(r.Id))
                                .FirstOrDefaultAsync();

                            if (found is not null)
                            {
                                foreach (var (field, _) in pairs)
                                {
                                    var hit = ToHit(field, found, moduleId, global: false);
                                    if (hit is not null)
                                        AddHit(hit);
                                }
                            }
                        }
                    }
                }
                else
                {
                    foreach (var field in uniqueFields)
                    {
                        data.TryGetValue(field.Name, out var value);
                        if (ignoreEmpty && string.IsNullOrEmpty(value))
                            continue;

                        DynamicRecord? found;
                        if (string.IsNullOrEmpty(value))
                        {
                            found = await _db.Records
                                .FromSqlInterpolated($"""
                                    SELECT * FROM "Records"
                                    WHERE "ModuleId" = {moduleId}
                                      AND ("CustomData" ->> {field.Name} IS NULL OR "CustomData" ->> {field.Name} = '')
                                    """)
                                .Where(r => !excludeIds.Contains(r.Id))
                                .FirstOrDefaultAsync();
                        }
                        else if (field.Type == FieldType.Phone)
                        {
                            var variants = PhoneIdentity.Split(value).SelectMany(PhoneIdentity.SearchVariants).ToList();
                            found = await FindPhonesAsync([moduleId], [field.Name], variants, excludeIds);
                        }
                        else
                        {
                            found = await _db.Records
                                .FromSqlInterpolated($"""
                                    SELECT * FROM "Records"
                                    WHERE "ModuleId" = {moduleId} AND "CustomData" ->> {field.Name} = {value}
                                    """)
                                .Where(r => !excludeIds.Contains(r.Id))
                                .FirstOrDefaultAsync();
                        }

                        if (found is not null)
                        {
                            var hit = ToHit(field, found, moduleId, global: false);
                            if (hit is not null)
                                AddHit(hit);
                        }
                    }
                }
            }
        }

        if (module.GlobalDuplicateEnabled)
        {
            var globalFields = fields.Where(f => f.IsGlobalUniqueCheck && f.Type == FieldType.Phone).ToList();
            if (globalFields.Count > 0)
            {
                var phoneFields = await _db.Fields.AsNoTracking()
                    .Where(f => f.Type == FieldType.Phone && (f.IsGlobalUniqueCheck || f.ModuleId == moduleId))
                    .Select(f => new { f.ModuleId, f.Name })
                    .ToListAsync();
                var moduleIds = phoneFields.Select(f => f.ModuleId).Distinct().ToList();
                var names = phoneFields.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                foreach (var field in globalFields)
                {
                    if (!data.TryGetValue(field.Name, out var value) || string.IsNullOrWhiteSpace(value))
                        continue;

                    var variants = PhoneIdentity.Split(value).SelectMany(PhoneIdentity.SearchVariants).ToList();
                    var found = await FindPhonesAsync(moduleIds, names, variants, excludeIds);
                    if (found is null)
                        continue;
                    var hit = ToHit(field, found, found.ModuleId, global: true);
                    if (hit is not null)
                        AddHit(hit);
                }
            }
        }

        if (hits.Count > 0)
        {
            errors["_duplicate"] = string.Join(" ", hits.Select(h => h.Message).Distinct());
            throw new RecordValidationException(errors, hits);
        }

        if (errors.Count > 0)
            throw new RecordValidationException(errors);
    }

    /// <summary>همهٔ شکل‌های یک شماره را در یک رفت‌وبرگشت با دیتابیس جستجو می‌کند.</summary>
    private async Task<DynamicRecord?> FindPhonesAsync(
        IReadOnlyCollection<int> moduleIds,
        IReadOnlyCollection<string> fieldNames,
        IReadOnlyCollection<string> variants,
        IReadOnlySet<int> excludeIds)
    {
        var ids = moduleIds.Where(id => id > 0).Distinct().ToArray();
        var names = fieldNames
            .Where(name => name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var packed = string.Join("||", variants
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.Ordinal));
        if (ids.Length == 0 || names.Length == 0 || packed.Length == 0)
            return null;

        var idList = string.Join(",", ids);
        var nameList = string.Join(",", names.Select(name => $"'{name}'"));
        var sql = $@"
            SELECT * FROM ""Records""
            WHERE ""ModuleId"" IN ({idList})
              AND EXISTS (
                SELECT 1
                FROM unnest(ARRAY[{nameList}]::text[]) AS fname
                CROSS JOIN LATERAL unnest(regexp_split_to_array(COALESCE(""CustomData"" ->> fname, ''), E'[\n\r,،;؛|]+')) AS part
                WHERE btrim(part) = ANY(string_to_array({{0}}, '||'))
              )";

        return await _db.Records
            .FromSqlRaw(sql, packed)
            .Where(r => !excludeIds.Contains(r.Id))
            .FirstOrDefaultAsync();
    }

    private static string ResolveTitle(IReadOnlyList<FieldDef> fields, Dictionary<string, string?> data)
    {
        var titleField = fields.FirstOrDefault(f => f.Name is "name" or "title" or "subject")
            ?? fields.FirstOrDefault(f => f.Type is FieldType.Text);

        var title = titleField is not null ? data.GetValueOrDefault(titleField.Name) : null;
        return title ?? "(بدون عنوان)";
    }
}
