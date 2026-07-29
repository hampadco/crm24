using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Infrastructure.Services;

/// <summary>ترجیح ستون‌های لیست ماژول برای کاربر جاری (SavedListView).</summary>
public class ListColumnService
{
    public const string ViewName = "__list_columns__";
    public const int MaxColumns = 15;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly MetadataService _metadata;

    public ListColumnService(CrmDbContext db, ITenantContext tenant, MetadataService metadata)
    {
        _db = db;
        _tenant = tenant;
        _metadata = metadata;
    }

    public async Task<IReadOnlyList<FieldDef>> GetListFieldsAsync(int moduleId)
    {
        var all = await _metadata.GetFieldsAsync(moduleId);
        var visible = all.Where(f => f.IsVisible).ToList();
        var preferredIds = await GetPreferredColumnIdsAsync(moduleId);

        if (preferredIds.Count > 0)
        {
            var byId = visible.ToDictionary(f => f.Id);
            var ordered = new List<FieldDef>();
            foreach (var id in preferredIds)
            {
                if (byId.TryGetValue(id, out var field))
                    ordered.Add(field);
            }

            if (ordered.Count > 0)
                return ordered;
        }

        return visible.Where(f => f.ShowInList).OrderBy(f => f.SortOrder).ThenBy(f => f.Id).ToList();
    }

    public async Task SaveListColumnsAsync(int moduleId, IReadOnlyList<int> fieldIds)
    {
        if (_tenant.UserId is not int userId)
            throw new InvalidOperationException("کاربر وارد نشده است.");

        var moduleOk = await _db.Modules.AnyAsync(m => m.Id == moduleId);
        if (!moduleOk)
            throw new InvalidOperationException("ماژول یافت نشد.");

        var allowed = (await _metadata.GetFieldsAsync(moduleId))
            .Where(f => f.IsVisible)
            .Select(f => f.Id)
            .ToHashSet();

        var clean = fieldIds
            .Where(allowed.Contains)
            .Distinct()
            .Take(MaxColumns)
            .ToList();

        if (clean.Count == 0)
            throw new InvalidOperationException("حداقل یک ستون باید انتخاب شود.");

        var view = await _db.SavedListViews
            .FirstOrDefaultAsync(v =>
                v.ModuleId == moduleId
                && v.Name == ViewName
                && v.CreatedByUserId == userId);

        var definition = JsonSerializer.Serialize(new ColumnDefinition { Columns = clean }, JsonOptions);

        if (view is null)
        {
            _db.SavedListViews.Add(new SavedListView
            {
                ModuleId = moduleId,
                Name = ViewName,
                Definition = definition,
                IsShared = false,
                CreatedByUserId = userId
            });
        }
        else
        {
            view.Definition = definition;
            view.UpdatedAtUtc = DateTime.UtcNow;
            view.UpdatedByUserId = userId;
        }

        await _db.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<int>> GetPreferredColumnIdsAsync(int moduleId)
    {
        if (_tenant.UserId is not int userId)
            return [];

        var definition = await _db.SavedListViews.AsNoTracking()
            .Where(v => v.ModuleId == moduleId && v.Name == ViewName && v.CreatedByUserId == userId)
            .Select(v => v.Definition)
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(definition))
            return [];

        try
        {
            var parsed = JsonSerializer.Deserialize<ColumnDefinition>(definition, JsonOptions);
            return parsed?.Columns ?? [];
        }
        catch
        {
            return [];
        }
    }

    private sealed class ColumnDefinition
    {
        public List<int> Columns { get; set; } = [];
    }
}
