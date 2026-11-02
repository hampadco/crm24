using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Services;
using Crm.Web.Security;

namespace Crm.Web.Controllers.Api;

/// <summary>CRUD پویا روی ماژول‌های متادیتا. مسیرهای قبلی /api/v1/{module} همان‌جا مانده‌اند.</summary>
[ApiController]
[IgnoreAntiforgeryToken]
[EnableRateLimiting("api")]
[ServiceFilter(typeof(ApiScopeFilter))]
[Route("api/v1")]
public class ApiRecordsController : ControllerBase
{
    private static readonly Regex FilterKey = new(@"^filter\[([A-Za-z0-9_]+)\]\[([A-Za-z]+)\]$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly MetadataService _metadata;
    private readonly DynamicRecordService _records;
    private readonly LineItemsService _lines;
    private readonly SalesDocumentService _sales;
    private readonly LeadConversionService _conversion;

    public ApiRecordsController(
        CrmDbContext db,
        ITenantContext tenant,
        MetadataService metadata,
        DynamicRecordService records,
        LineItemsService lines,
        SalesDocumentService sales,
        LeadConversionService conversion)
    {
        _db = db;
        _tenant = tenant;
        _metadata = metadata;
        _records = records;
        _lines = lines;
        _sales = sales;
        _conversion = conversion;
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        if (!int.TryParse(User.FindFirst("crm:api_key_id")?.Value, out var keyId))
            return Unauthorized();

        var key = await _db.ApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Id == keyId);
        if (key is null)
            return Unauthorized();

        return Ok(new
        {
            name = key.Name,
            kind = key.Kind,
            prefix = key.KeyPrefix,
            scopes = key.ScopesJson,
            canRead = key.CanRead,
            canWrite = key.CanWrite,
            expiresAtUtc = key.ExpiresAtUtc,
            actAsUserId = key.ActAsUserId
        });
    }

    [HttpGet("modules")]
    public async Task<IActionResult> Modules()
    {
        var key = await CurrentKeyAsync();
        if (key is null)
            return Unauthorized();

        var modules = await _metadata.GetActiveModulesAsync();
        var visible = modules
            .Where(m => ApiKeyService.Allows(key, m.Name, write: false) || ApiKeyService.Allows(key, m.Name, write: true))
            .Select(m => new
            {
                m.Name,
                m.SingularLabel,
                m.PluralLabel,
                read = ApiKeyService.Allows(key, m.Name, false),
                write = ApiKeyService.Allows(key, m.Name, true)
            });
        return Ok(visible);
    }

    [HttpGet("modules/{moduleName}/schema")]
    public async Task<IActionResult> Schema(string moduleName)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound(new { error = "Module not found." });

        var fields = await _metadata.GetFieldsAsync(module.Id);
        return Ok(new
        {
            module = module.Name,
            label = module.PluralLabel,
            fields = fields.Where(f => f.IsVisible).Select(f => new
            {
                f.Name,
                f.Label,
                type = f.Type.ToString(),
                required = f.IsRequired,
                unique = f.IsUniqueCheck,
                maxLength = f.MaxLength,
                lookupModule = f.LookupModule,
                readOnly = !string.IsNullOrWhiteSpace(f.FormulaExpression),
                options = f.PicklistValues
                    .OrderBy(p => p.SortOrder)
                    .Select(p => new { p.Value, p.Label })
            })
        });
    }

    [HttpGet("{moduleName}")]
    public async Task<IActionResult> List(string moduleName, string? search, string? sort, string? dir, string? updatedSince, string? expand, int page = 1, int pageSize = 20)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound(new { error = "Module not found." });

        DateTime? since = null;
        if (!string.IsNullOrWhiteSpace(updatedSince) && DateTime.TryParse(updatedSince, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            since = parsed;

        var query = new RecordListQuery
        {
            Search = search,
            Page = page,
            PageSize = Math.Clamp(pageSize, 1, 200),
            SortField = sort,
            SortDir = string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc",
            UpdatedSinceUtc = since,
            Filters = ReadFilters()
        };

        var (items, total) = await _records.ListAsync(module.Id, query);
        var fields = WantsExpand(expand) ? await _metadata.GetFieldsAsync(module.Id) : null;
        var dtos = new List<object>(items.Count);
        foreach (var item in items)
            dtos.Add(await ToDtoAsync(item, fields));

        return Ok(new { total, page = query.Page, pageSize = query.PageSize, items = dtos });
    }

    [HttpGet("{moduleName}/{id:int}")]
    public async Task<IActionResult> Get(string moduleName, int id, string? expand)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound(new { error = "Module not found." });

        var record = await _records.GetAsync(module.Id, id);
        if (record is null)
            return NotFound(new { error = "Record not found." });

        var fields = WantsExpand(expand) ? await _metadata.GetFieldsAsync(module.Id) : null;
        return Ok(await ToDtoAsync(record, fields));
    }

    [HttpPost("{moduleName}")]
    public async Task<IActionResult> Create(string moduleName, [FromBody] JsonElement body)
    {
        var replay = await BeginIdempotencyAsync();
        if (replay is not null)
            return replay;

        try
        {
            var module = await _metadata.GetModuleByNameAsync(moduleName);
            if (module is null)
                return await FinishAsync(404, new { error = "Module not found." });

            var (values, lines) = ReadBody(body);
            var fields = await _metadata.GetFieldsAsync(module.Id);
            var data = ApplyWritable(fields, values, existing: null, merge: false, out var fieldError);
            if (fieldError is not null)
                return await FinishAsync(422, new { errors = new Dictionary<string, string> { ["_"] = fieldError } });

            var record = await _records.CreateAsync(module.Id, data);
            await _sales.AssignNumberIfNeededAsync(module, record);
            if (lines is not null)
                await _lines.SaveApiLinesAsync(module.Id, record.Id, lines);

            record = await _records.GetAsync(module.Id, record.Id) ?? record;
            Response.Headers.Location = $"/api/v1/{module.Name}/{record.Id}";
            return await FinishAsync(201, await ToDtoAsync(record, null));
        }
        catch
        {
            await ForgetIdempotencyAsync();
            throw;
        }
    }

    [HttpPut("{moduleName}/{id:int}")]
    public Task<IActionResult> Update(string moduleName, int id, [FromBody] JsonElement body) =>
        SaveAsync(moduleName, id, body, merge: false);

    [HttpPatch("{moduleName}/{id:int}")]
    public Task<IActionResult> Patch(string moduleName, int id, [FromBody] JsonElement body) =>
        SaveAsync(moduleName, id, body, merge: true);

    [HttpDelete("{moduleName}/{id:int}")]
    public async Task<IActionResult> Delete(string moduleName, int id)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound(new { error = "Module not found." });

        await _records.DeleteAsync(module.Id, id);
        return NoContent();
    }

    [HttpPost("{moduleName}/upsert")]
    public async Task<IActionResult> Upsert(string moduleName, string match, [FromBody] JsonElement body)
    {
        var replay = await BeginIdempotencyAsync();
        if (replay is not null)
            return replay;

        try
        {
            var module = await _metadata.GetModuleByNameAsync(moduleName);
            if (module is null)
                return await FinishAsync(404, new { error = "Module not found." });

            var fields = await _metadata.GetFieldsAsync(module.Id);
            var field = fields.FirstOrDefault(f => f.Name.Equals(match, StringComparison.OrdinalIgnoreCase));
            if (field is null)
                return await FinishAsync(422, new { error = "match field was not found on this module." });

            var (values, lines) = ReadBody(body);
            if (!values.TryGetValue(field.Name, out var raw) || string.IsNullOrWhiteSpace(raw))
                return await FinishAsync(422, new { error = "The match field must have a value." });

            var found = await FindMatchesAsync(module.Id, field, raw);
            if (found.Count > 1)
                return await FinishAsync(409, new { error = "More than one record matches.", ids = found });

            if (found.Count == 1)
            {
                var existing = await _records.GetAsync(module.Id, found[0]);
                var data = ApplyWritable(fields, values, existing is null ? null : DynamicRecordService.ParseData(existing), merge: true, out var fieldError);
                if (fieldError is not null)
                    return await FinishAsync(422, new { errors = new Dictionary<string, string> { ["_"] = fieldError } });
                await _records.UpdateAsync(module.Id, found[0], data);
                if (lines is not null)
                    await _lines.SaveApiLinesAsync(module.Id, found[0], lines);
                var updated = await _records.GetAsync(module.Id, found[0]);
                return await FinishAsync(200, new { created = false, record = updated is null ? null : await ToDtoAsync(updated, null) });
            }

            var createdData = ApplyWritable(fields, values, existing: null, merge: false, out var createError);
            if (createError is not null)
                return await FinishAsync(422, new { errors = new Dictionary<string, string> { ["_"] = createError } });
            var record = await _records.CreateAsync(module.Id, createdData);
            await _sales.AssignNumberIfNeededAsync(module, record);
            if (lines is not null)
                await _lines.SaveApiLinesAsync(module.Id, record.Id, lines);
            record = await _records.GetAsync(module.Id, record.Id) ?? record;
            return await FinishAsync(201, new { created = true, record = await ToDtoAsync(record, null) });
        }
        catch
        {
            await ForgetIdempotencyAsync();
            throw;
        }
    }

    [HttpPost("{moduleName}/batch")]
    public async Task<IActionResult> Batch(string moduleName, [FromBody] JsonElement body)
    {
        var replay = await BeginIdempotencyAsync();
        if (replay is not null)
            return replay;

        try
        {
            var module = await _metadata.GetModuleByNameAsync(moduleName);
            if (module is null)
                return await FinishAsync(404, new { error = "Module not found." });

            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                return await FinishAsync(422, new { error = "Body must be { \"items\": [ ... ] }." });

            if (items.GetArrayLength() > 100)
                return await FinishAsync(422, new { error = "A batch can contain at most 100 items." });

            var fields = await _metadata.GetFieldsAsync(module.Id);
            var results = new List<object>();
            var index = 0;
            foreach (var item in items.EnumerateArray())
            {
                results.Add(await RunBatchItemAsync(module, fields, item, index));
                index++;
            }

            return await FinishAsync(200, new { results });
        }
        catch
        {
            await ForgetIdempotencyAsync();
            throw;
        }
    }

    [HttpPost("public/{moduleName}")]
    public async Task<IActionResult> PublicCreate(string moduleName, [FromBody] JsonElement body)
    {
        var replay = await BeginIdempotencyAsync();
        if (replay is not null)
            return replay;

        try
        {
            var (values, _) = ReadBody(body);
            if (values.TryGetValue("_hp", out var hp) && !string.IsNullOrWhiteSpace(hp))
                return await FinishAsync(201, new { id = 0 });

            values.Remove("_hp");

            var module = await _metadata.GetModuleByNameAsync(moduleName);
            if (module is null)
                return await FinishAsync(404, new { error = "Module not found." });

            var fields = await _metadata.GetFieldsAsync(module.Id);
            var data = ApplyWritable(fields, values, existing: null, merge: false, out var fieldError);
            if (fieldError is not null)
                return await FinishAsync(422, new { error = "Invalid value." });

            var record = await _records.CreateAsync(module.Id, data);
            return await FinishAsync(201, new { id = record.Id });
        }
        catch (RecordValidationException)
        {
            await ForgetIdempotencyAsync();
            return StatusCode(422, new { error = "Could not save the form." });
        }
        catch
        {
            await ForgetIdempotencyAsync();
            throw;
        }
    }

    [HttpPost("{moduleName}/{id:int}/convert")]
    public async Task<IActionResult> ConvertLead(string moduleName, int id, [FromBody] JsonElement? body)
    {
        if (!moduleName.Equals("leads", StringComparison.OrdinalIgnoreCase))
            return NotFound(new { error = "Convert is only available for leads." });

        int? contactId = null;
        var createOpportunity = true;
        if (body is JsonElement json && json.ValueKind == JsonValueKind.Object)
        {
            if (json.TryGetProperty("contactId", out var contact) && contact.TryGetInt32(out var parsed))
                contactId = parsed;
            if (json.TryGetProperty("createOpportunity", out var flag) && flag.ValueKind is JsonValueKind.True or JsonValueKind.False)
                createOpportunity = flag.GetBoolean();
        }

        var result = await _conversion.ConvertAsync(id, contactId, createOpportunity);
        if (!result.Success)
            return UnprocessableEntity(new { error = result.Error, duplicates = result.Duplicates.Select(d => d.RecordId) });

        return Ok(new
        {
            contactId = result.ContactId,
            organizationId = result.OrganizationId,
            opportunityId = result.OpportunityId
        });
    }

    [HttpPost("{moduleName}/{id:int}/notes")]
    public async Task<IActionResult> AddNote(string moduleName, int id, [FromBody] JsonElement body)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound(new { error = "Module not found." });

        var record = await _records.GetAsync(module.Id, id);
        if (record is null)
            return NotFound(new { error = "Record not found." });

        var text = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("body", out var node)
            ? JsonToString(node)
            : null;
        if (string.IsNullOrWhiteSpace(text))
            return UnprocessableEntity(new { error = "body is required." });

        var note = new Note { ModuleName = module.Name, RecordId = id, Body = text.Trim() };
        _db.Notes.Add(note);
        await _db.SaveChangesAsync();
        return Ok(new { id = note.Id });
    }

    private async Task<IActionResult> SaveAsync(string moduleName, int id, JsonElement body, bool merge)
    {
        var replay = await BeginIdempotencyAsync();
        if (replay is not null)
            return replay;

        try
        {
            var module = await _metadata.GetModuleByNameAsync(moduleName);
            if (module is null)
                return await FinishAsync(404, new { error = "Module not found." });

            var existing = await _records.GetAsync(module.Id, id);
            if (existing is null)
                return await FinishAsync(404, new { error = "Record not found." });

            var fields = await _metadata.GetFieldsAsync(module.Id);
            var (values, lines) = ReadBody(body);
            var data = ApplyWritable(fields, values, DynamicRecordService.ParseData(existing), merge, out var fieldError);
            if (fieldError is not null)
                return await FinishAsync(422, new { errors = new Dictionary<string, string> { ["_"] = fieldError } });

            await _records.UpdateAsync(module.Id, id, data);
            if (lines is not null)
                await _lines.SaveApiLinesAsync(module.Id, id, lines);

            var updated = await _records.GetAsync(module.Id, id) ?? existing;
            return await FinishAsync(200, await ToDtoAsync(updated, null));
        }
        catch
        {
            await ForgetIdempotencyAsync();
            throw;
        }
    }

    private async Task<object> RunBatchItemAsync(ModuleDef module, IReadOnlyList<FieldDef> fields, JsonElement item, int index)
    {
        try
        {
            if (item.ValueKind != JsonValueKind.Object)
                return new { index, ok = false, status = 422, error = "Item must be an object." };

            var match = item.TryGetProperty("match", out var matchNode) ? JsonToString(matchNode) : null;
            var (values, lines) = ReadBody(item);
            values.Remove("match");
            values.Remove("values");

            if (!string.IsNullOrWhiteSpace(match))
            {
                var field = fields.FirstOrDefault(f => f.Name.Equals(match, StringComparison.OrdinalIgnoreCase));
                if (field is null)
                    return new { index, ok = false, status = 422, error = "match field was not found." };
                if (!values.TryGetValue(field.Name, out var raw) || string.IsNullOrWhiteSpace(raw))
                    return new { index, ok = false, status = 422, error = "The match field must have a value." };

                var found = await FindMatchesAsync(module.Id, field, raw);
                if (found.Count > 1)
                    return new { index, ok = false, status = 409, error = "More than one record matches.", ids = found };
                if (found.Count == 1)
                {
                    var existing = await _records.GetAsync(module.Id, found[0]);
                    var data = ApplyWritable(fields, values, existing is null ? null : DynamicRecordService.ParseData(existing), merge: true, out var error);
                    if (error is not null)
                        return new { index, ok = false, status = 422, error };
                    await _records.UpdateAsync(module.Id, found[0], data);
                    if (lines is not null)
                        await _lines.SaveApiLinesAsync(module.Id, found[0], lines);
                    return new { index, ok = true, created = false, id = found[0] };
                }
            }

            var createdData = ApplyWritable(fields, values, existing: null, merge: false, out var createError);
            if (createError is not null)
                return new { index, ok = false, status = 422, error = createError };
            var record = await _records.CreateAsync(module.Id, createdData);
            await _sales.AssignNumberIfNeededAsync(module, record);
            if (lines is not null)
                await _lines.SaveApiLinesAsync(module.Id, record.Id, lines);
            return new { index, ok = true, created = true, id = record.Id };
        }
        catch (RecordValidationException ex)
        {
            return new
            {
                index,
                ok = false,
                status = ex.Duplicates.Count > 0 ? 409 : 422,
                errors = ex.Errors
            };
        }
        catch (UnauthorizedAccessException)
        {
            return new { index, ok = false, status = 403, error = "Forbidden." };
        }
    }

    private async Task<List<int>> FindMatchesAsync(int moduleId, FieldDef field, string raw)
    {
        var tenantId = _tenant.TenantId;
        var probes = field.Type == FieldType.Phone
            ? PhoneIdentity.SearchVariants(raw).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().Take(8).ToList()
            : [raw.Trim()];
        if (probes.Count == 0)
            probes.Add(raw.Trim());

        var ids = new HashSet<int>();
        foreach (var probe in probes)
        {
            var rows = await _db.Database.SqlQuery<MatchRow>($"""
                SELECT r."Id" AS "Id"
                FROM "Records" r
                WHERE r."ModuleId" = {moduleId}
                  AND r."TenantId" = {tenantId}
                  AND r."IsDeleted" = FALSE
                  AND COALESCE(r."CustomData" ->> {field.Name}, '') = {probe}
                LIMIT 2
                """).ToListAsync();
            foreach (var row in rows)
                ids.Add(row.Id);
            if (ids.Count > 1)
                break;
        }

        return ids.ToList();
    }

    private List<ColumnFilter> ReadFilters()
    {
        var filters = new List<ColumnFilter>();
        foreach (var pair in Request.Query)
        {
            var match = FilterKey.Match(pair.Key);
            if (!match.Success)
                continue;
            filters.Add(new ColumnFilter
            {
                Field = match.Groups[1].Value,
                Op = match.Groups[2].Value,
                Value = pair.Value.ToString()
            });
        }

        return filters;
    }

    private static bool WantsExpand(string? expand) =>
        !string.IsNullOrWhiteSpace(expand) && expand.Contains("lookup", StringComparison.OrdinalIgnoreCase);

    private async Task<object> ToDtoAsync(DynamicRecord record, IReadOnlyList<FieldDef>? expandFields)
    {
        var data = DynamicRecordService.ParseData(record);
        Dictionary<string, object?>? lookups = null;
        if (expandFields is not null)
        {
            lookups = new Dictionary<string, object?>();
            foreach (var field in expandFields.Where(f => f.Type == FieldType.Lookup))
            {
                if (!data.TryGetValue(field.Name, out var raw) || !int.TryParse(raw, out var id))
                    continue;
                var title = await _db.Records.AsNoTracking().Where(r => r.Id == id).Select(r => r.Title).FirstOrDefaultAsync();
                lookups[field.Name] = new { id, title };
            }
        }

        return new
        {
            id = record.Id,
            title = record.Title,
            ownerUserId = record.OwnerUserId,
            createdAtUtc = record.CreatedAtUtc,
            updatedAtUtc = record.UpdatedAtUtc,
            data,
            lookups
        };
    }

    private static (Dictionary<string, string?> Values, List<Dictionary<string, string?>>? Lines) ReadBody(JsonElement body)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        List<Dictionary<string, string?>>? lines = null;
        if (body.ValueKind != JsonValueKind.Object)
            return (values, null);

        foreach (var prop in body.EnumerateObject())
        {
            if (prop.Name.Equals("lines", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Array)
            {
                lines = ReadLines(prop.Value);
                continue;
            }

            if (prop.Name.Equals("values", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var inner in prop.Value.EnumerateObject())
                    values[inner.Name] = JsonToString(inner.Value);
                continue;
            }

            values[prop.Name] = JsonToString(prop.Value);
        }

        return (values, lines);
    }

    private static List<Dictionary<string, string?>> ReadLines(JsonElement array)
    {
        var lines = new List<Dictionary<string, string?>>();
        foreach (var row in array.EnumerateArray())
        {
            var line = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (row.ValueKind != JsonValueKind.Object)
                continue;
            foreach (var prop in row.EnumerateObject())
                line[prop.Name] = JsonToString(prop.Value);
            lines.Add(line);
        }

        return lines;
    }

    private static string? JsonToString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => value.GetString(),
        _ => value.ToString()
    };

    private static Dictionary<string, string?> ApplyWritable(
        IReadOnlyList<FieldDef> fields,
        Dictionary<string, string?> incoming,
        Dictionary<string, string?>? existing,
        bool merge,
        out string? error)
    {
        error = null;
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (merge && existing is not null)
        {
            foreach (var kv in existing)
                result[kv.Key] = kv.Value;
        }
        else if (existing is not null)
        {
            foreach (var field in fields)
            {
                if (field.IsVisible && string.IsNullOrWhiteSpace(field.FormulaExpression))
                    continue;
                if (existing.TryGetValue(field.Name, out var kept))
                    result[field.Name] = kept;
            }
        }

        foreach (var kv in incoming)
        {
            var field = fields.FirstOrDefault(f => f.Name.Equals(kv.Key, StringComparison.OrdinalIgnoreCase));
            if (field is null)
                continue;
            if (!field.IsVisible)
                continue;
            if (!string.IsNullOrWhiteSpace(field.FormulaExpression))
            {
                error = $"Field '{field.Name}' is read-only.";
                return result;
            }

            result[field.Name] = kv.Value;
        }

        return result;
    }

    private async Task<ApiKey?> CurrentKeyAsync()
    {
        if (!int.TryParse(User.FindFirst("crm:api_key_id")?.Value, out var keyId))
            return null;
        return await _db.ApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Id == keyId);
    }

    private async Task<IActionResult?> BeginIdempotencyAsync()
    {
        var header = Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(header))
            return null;
        if (!int.TryParse(User.FindFirst("crm:api_key_id")?.Value, out var keyId))
            return null;

        var token = header.Trim();
        if (token.Length > 200)
            token = token[..200];

        var cutoff = DateTime.UtcNow.AddHours(-24);
        await _db.ApiIdempotencies.Where(x => x.CreatedAtUtc < cutoff).ExecuteDeleteAsync();

        var existing = await _db.ApiIdempotencies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ApiKeyId == keyId && x.Key == token);
        if (existing is not null)
        {
            if (existing.StatusCode == 0)
                return Conflict(new { error = "A request with this Idempotency-Key is still running." });
            Response.Headers["Idempotency-Replayed"] = "true";
            return new ContentResult
            {
                StatusCode = existing.StatusCode,
                Content = existing.ResponseJson,
                ContentType = "application/json"
            };
        }

        var row = new ApiIdempotency
        {
            TenantId = _tenant.TenantId ?? 0,
            ApiKeyId = keyId,
            Key = token,
            StatusCode = 0,
            ResponseJson = "{}",
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.ApiIdempotencies.Add(row);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict(new { error = "A request with this Idempotency-Key is still running." });
        }

        HttpContext.Items["idem-row"] = row.Id;
        return null;
    }

    private async Task<IActionResult> FinishAsync(int status, object body)
    {
        var json = JsonSerializer.Serialize(body, Json);
        if (HttpContext.Items["idem-row"] is int rowId)
        {
            await _db.ApiIdempotencies.Where(x => x.Id == rowId).ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.StatusCode, status)
                .SetProperty(x => x.ResponseJson, json));
        }

        return new ContentResult { StatusCode = status, Content = json, ContentType = "application/json" };
    }

    private async Task ForgetIdempotencyAsync()
    {
        if (HttpContext.Items["idem-row"] is not int rowId)
            return;
        HttpContext.Items.Remove("idem-row");
        await _db.ApiIdempotencies.Where(x => x.Id == rowId).ExecuteDeleteAsync();
    }

    private sealed record MatchRow(int Id);
}
