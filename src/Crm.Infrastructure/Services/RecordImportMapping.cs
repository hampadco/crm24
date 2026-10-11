using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Crm.Core.Entities;

namespace Crm.Infrastructure.Services;

public sealed class ImportTable
{
    public List<ImportColumn> Columns { get; init; } = [];
    public List<string[]> Rows { get; init; } = [];
    public bool Truncated { get; init; }
    public int RowCount => Rows.Count;
}

public sealed class ImportColumn
{
    public int Index { get; init; }
    public string Header { get; init; } = "";
    public string? SuggestedField { get; set; }
    /// <summary>filled = سلول پر یعنی فعال؛ words = بله/خیر و ۱/۰.</summary>
    public string? FlagMode { get; set; }
    public List<string> Samples { get; init; } = [];
}

public sealed class ImportColumnBinding
{
    public int Index { get; init; }
    public string? FieldName { get; init; }
    public string? FlagMode { get; init; }
}

public sealed class ImportRunCursor
{
    public int Total { get; set; }
    public int Processed { get; set; }
    public int Imported { get; set; }
    public int LastRow { get; set; } = 1;
    public List<string> Errors { get; set; } = [];

    public ImportRunCursor()
    {
    }

    public ImportRunCursor(int total, int processed, int imported, int lastRow, List<string> errors)
    {
        Total = total;
        Processed = processed;
        Imported = imported;
        LastRow = lastRow;
        Errors = errors;
    }
}

public partial class RecordImportExportService
{
    public const int MaxImportRows = 5000;

    public static ImportTable ReadTable(Stream stream, string fileName)
    {
        if (fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return ReadCsv(stream);
        if (fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return ReadXlsx(stream);
        throw new InvalidOperationException("فقط فایل اکسل (xlsx) یا CSV پذیرفته می‌شود.");
    }

    public static List<ImportColumn> Suggest(IReadOnlyList<FieldDef> fields, ImportTable table)
    {
        foreach (var column in table.Columns)
            column.SuggestedField = SuggestField(column.Header, fields);

        if (fields.Count == 0)
            return table.Columns;

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ranked = table.Columns
            .Select(column =>
            {
                var field = fields.FirstOrDefault(f =>
                    f.Name.Equals(column.SuggestedField ?? "", StringComparison.OrdinalIgnoreCase));
                return (column, field, score: field is null ? 0 : Score(column.Header, field));
            })
            .OrderByDescending(x => x.score);

        foreach (var (column, field, _) in ranked)
        {
            if (field is null || string.IsNullOrWhiteSpace(column.SuggestedField))
                continue;
            if (IsPersonName(field))
            {
                used.Add(field.Name);
                continue;
            }
            if (!used.Add(field.Name))
                column.SuggestedField = null;
        }

        foreach (var column in table.Columns)
        {
            var field = fields.FirstOrDefault(f =>
                f.Name.Equals(column.SuggestedField ?? "", StringComparison.OrdinalIgnoreCase));
            if (field?.Type == FieldType.Checkbox)
                column.FlagMode = LooksLikeYesNo(column.Samples) ? "words" : "filled";
        }

        return table.Columns;
    }

    public static bool CanImport(FieldDef field) =>
        field.IsVisible
        && field.Type != FieldType.Lookup
        && string.IsNullOrWhiteSpace(field.FormulaExpression)
        && !TelefonchyCallService.IsInternalCallField(field.Name);

    private static bool LooksLikeYesNo(IEnumerable<string> samples)
    {
        var seen = 0;
        foreach (var sample in samples)
        {
            if (string.IsNullOrWhiteSpace(sample))
                continue;
            seen++;
            if (!IsYesWord(sample) && !IsNoWord(sample))
                return false;
        }
        return seen > 0;
    }

    private static bool CheckboxOn(string text, string? mode)
    {
        if (string.IsNullOrWhiteSpace(text) || IsNoWord(text))
            return false;
        if (string.Equals(mode, "words", StringComparison.OrdinalIgnoreCase))
            return IsYesWord(text);
        return true;
    }

    private static bool IsYesWord(string text)
    {
        var n = Norm(text);
        return n is "true" or "1" or "yes" or "y" or "بله" or "بلی" or "آره" or "اره" or "درست" or "فعال" or "دارد";
    }

    private static bool IsNoWord(string text)
    {
        var n = Norm(text);
        return n is "false" or "0" or "no" or "n" or "خیر" or "نه" or "غلط" or "غیرفعال" or "ندارد";
    }

    public static IReadOnlyList<FieldDef> MissingRequired(
        IReadOnlyList<FieldDef> fields, IReadOnlyList<ImportColumnBinding> bindings)
    {
        var mapped = bindings
            .Where(b => !string.IsNullOrWhiteSpace(b.FieldName))
            .Select(b => b.FieldName!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return fields.Where(f => f.IsRequired && !mapped.Contains(f.Name)).ToList();
    }

    public async Task<(int Imported, List<string> Errors)> ImportMappedAsync(
        int moduleId,
        ImportTable table,
        IReadOnlyList<ImportColumnBinding> bindings,
        ImportRunCursor? cursor = null,
        Func<ImportRunCursor, Task>? onRow = null)
    {
        var fields = await _metadata.GetFieldsAsync(moduleId);
        var allowed = fields.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        var groups = bindings
            .Where(b => !string.IsNullOrWhiteSpace(b.FieldName) && allowed.ContainsKey(b.FieldName))
            .GroupBy(b => b.FieldName!, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var errors = new List<string>(cursor?.Errors ?? []);
        var imported = cursor?.Imported ?? 0;
        var processed = cursor?.Processed ?? 0;
        var resumeAfterRow = cursor?.LastRow ?? 1;
        var total = table.Rows.Count(row => !row.All(string.IsNullOrWhiteSpace));
        var rowNumber = 1;
        foreach (var row in table.Rows)
        {
            rowNumber++;
            if (rowNumber <= resumeAfterRow)
                continue;

            if (row.All(string.IsNullOrWhiteSpace))
            {
                if (onRow is not null)
                    await onRow(new ImportRunCursor(total, processed, imported, rowNumber, errors));
                continue;
            }

            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
            {
                var field = allowed[group.Key];
                if (!CanImport(field))
                    continue;

                if (field.Type == FieldType.Checkbox)
                {
                    var decided = false;
                    var on = false;
                    foreach (var binding in group.OrderBy(b => b.Index))
                    {
                        var text = binding.Index >= 0 && binding.Index < row.Length
                            ? FoldDigits(row[binding.Index].Trim())
                            : "";
                        decided = true;
                        if (CheckboxOn(text, binding.FlagMode))
                            on = true;
                    }
                    if (decided)
                        values[field.Name] = on ? "true" : "false";
                    continue;
                }

                var parts = group
                    .OrderBy(b => NamePartOrder(table, b.Index))
                    .ThenBy(b => b.Index)
                    .Select(b => b.Index >= 0 && b.Index < row.Length ? FoldDigits(row[b.Index].Trim()) : "")
                    .Where(p => p.Length > 0)
                    .ToList();
                if (parts.Count == 0)
                    continue;
                values[field.Name] = field.Type == FieldType.Phone
                    ? string.Join('\n', parts)
                    : await ResolveChoicesAsync(field, string.Join(' ', parts));
            }

            if (values.Count == 0)
            {
                if (onRow is not null)
                    await onRow(new ImportRunCursor(total, processed, imported, rowNumber, errors));
                continue;
            }

            try
            {
                await _records.CreateAsync(moduleId, values);
                imported++;
            }
            catch (RecordValidationException ex)
            {
                var where = ex.Duplicates.Count > 0
                    ? string.Join(" — ", ex.Duplicates.Select(h => $"{h.Message} ← {h.DetailsUrl}"))
                    : string.Join(" — ", ex.Errors.Values);
                errors.Add($"ردیف {rowNumber}: {where}");
            }

            processed++;
            if (onRow is not null)
                await onRow(new ImportRunCursor(total, processed, imported, rowNumber, errors));
        }

        return (imported, errors);
    }

    private static int NamePartOrder(ImportTable table, int index)
    {
        var header = index >= 0 && index < table.Columns.Count ? table.Columns[index].Header : "";
        return IsFamilyName(Norm(header)) ? 1 : 0;
    }

    private static string? SuggestField(string header, IReadOnlyList<FieldDef> fields)
    {
        var bestScore = 0;
        var second = 0;
        FieldDef? best = null;
        foreach (var field in fields)
        {
            var score = Score(header, field);
            if (score > bestScore)
            {
                second = bestScore;
                bestScore = score;
                best = field;
            }
            else if (score > second)
            {
                second = score;
            }
        }

        return bestScore >= 70 && bestScore > second ? best?.Name : null;
    }

    private static int Score(string header, FieldDef field)
    {
        var raw = header.Trim();
        var norm = Norm(raw);
        if (norm is "شناسه" or "id")
            return 0;
        if (field.Name.Equals(raw, StringComparison.OrdinalIgnoreCase) || field.Label.Trim() == raw)
            return 100;
        if (Norm(field.Name) == norm || Norm(field.Label) == norm)
            return 95;

        if ((IsGivenName(norm) || IsFamilyName(norm)) && IsPersonName(field))
            return field.Name.Equals("name", StringComparison.OrdinalIgnoreCase) ? 90 : 85;

        if (field.Type == FieldType.Lookup)
            return 0;

        if (AliasHits(norm, field))
            return 80;

        if (norm.Length >= 4 && Norm(field.Label).Contains(norm, StringComparison.Ordinal))
            return 50;

        return 0;
    }

    private static bool AliasHits(string header, FieldDef field)
    {
        var name = field.Name;
        bool Named(params string[] names) => names.Contains(name, StringComparer.OrdinalIgnoreCase);
        if (header.Contains("موبایل", StringComparison.Ordinal) && Named("mobile", "phone", "phoneNumber"))
            return true;
        if ((header is "تلفن" or "phone" or "tel") && Named("phone", "mobile", "phoneNumber"))
            return true;
        if (header.Contains("ایمیل", StringComparison.Ordinal) && Named("email"))
            return true;
        if ((header is "شرکت" or "company") && Named("company"))
            return true;
        if ((header is "شهر" or "city") && Named("city"))
            return true;
        if (header.Contains("وب", StringComparison.Ordinal) && header.Contains("سایت", StringComparison.Ordinal) && Named("website"))
            return true;
        if (header.Contains("منبع", StringComparison.Ordinal) && Named("source"))
            return true;
        if (header.Contains("وضعیت", StringComparison.Ordinal) && Named("status"))
            return true;
        if (header.Contains("صنعت", StringComparison.Ordinal) && Named("industry"))
            return true;
        if ((header.Contains("توضیح", StringComparison.Ordinal) || header is "description" or "شرح") && Named("description"))
            return true;
        if ((header.Contains("آدرس", StringComparison.Ordinal) || header.Contains("نشانی", StringComparison.Ordinal) || header is "address") && Named("address"))
            return true;
        return false;
    }

    private static bool IsPersonName(FieldDef field)
    {
        var label = Norm(field.Label);
        if (label.Contains("سازمان", StringComparison.Ordinal) || label.Contains("شرکت", StringComparison.Ordinal))
            return false;
        return field.Name.Equals("name", StringComparison.OrdinalIgnoreCase)
               || label is "نام" or "نامونامخانوادگی";
    }

    private static bool IsGivenName(string norm) =>
        norm is "نام" or "firstname" or "fname" or "givenname";

    private static bool IsFamilyName(string norm) =>
        norm is "نامخانوادگی" or "فامیلی" or "lastname" or "lname" or "family" or "familyname" or "surname";

    private static string Norm(string value)
    {
        var text = value.Trim().Replace("\u200c", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal);
        return text.Replace('ي', 'ی').Replace('ك', 'ک').Replace('ة', 'ه').ToLowerInvariant();
    }

    private async Task<string> ResolveChoicesAsync(FieldDef field, string raw)
    {
        if (field.Type == FieldType.MultiPicklist)
        {
            var parts = raw.Split([',', '،', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var mapped = new List<string>();
            foreach (var part in parts)
                mapped.Add(await ResolvePicklistAsync(field, part));
            return string.Join(",", mapped);
        }

        if (field.Type == FieldType.Picklist)
            return await ResolvePicklistAsync(field, raw);

        return NormalizeScalar(field, raw);
    }

    private static string NormalizeScalar(FieldDef field, string raw)
    {
        var text = FoldDigits(raw.Trim());
        if (field.Type is FieldType.Number or FieldType.Decimal or FieldType.Currency or FieldType.Percent)
            return NormalizeNumber(text);
        if (field.Type is FieldType.Date or FieldType.DateTime)
            return NormalizeDate(text, field.Type == FieldType.DateTime);
        return text;
    }

    private static string FoldDigits(string value)
    {
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= '۰' and <= '۹')
                chars[i] = (char)('0' + (chars[i] - '۰'));
            else if (chars[i] is >= '٠' and <= '٩')
                chars[i] = (char)('0' + (chars[i] - '٠'));
        }
        return new string(chars);
    }

    private static string NormalizeNumber(string text)
    {
        return text.Replace('٫', '.').Replace("٬", "", StringComparison.Ordinal)
            .Replace(",", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal)
            .TrimEnd('%');
    }

    private static string NormalizeDate(string text, bool withTime)
    {
        var formats = new[] { "yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy/MM/dd", "yyyy/MM/dd HH:mm" };
        if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var greg)
            && greg.Year >= 1700)
        {
            return withTime && greg.TimeOfDay != TimeSpan.Zero
                ? greg.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                : greg.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        var match = Regex.Match(text, @"^(\d{4})[/\-\.](\d{1,2})[/\-\.](\d{1,2})(?:\s+(\d{1,2}):(\d{2}))?$");
        if (!match.Success)
            return text;

        var year = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var month = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var day = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        var hour = match.Groups[4].Success ? int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) : 0;
        var minute = match.Groups[5].Success ? int.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture) : 0;
        if (year is < 1200 or > 1600 || month is < 1 or > 12 || day is < 1 or > 31)
            return text;

        try
        {
            var date = new PersianCalendar().ToDateTime(year, month, day, hour, minute, 0, 0);
            return withTime && (hour != 0 || minute != 0)
                ? date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                : date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return text;
        }
    }

    /// <summary>
    /// مقدار اکسل را به گزینهٔ موجود می‌چسباند (با نادیده گرفتن فاصله و نیم‌فاصله).
    /// اگر در فهرست نباشد، همان متن به‌عنوان گزینهٔ جدید ذخیره می‌شود.
    /// </summary>
    private async Task<string> ResolvePicklistAsync(FieldDef field, string raw)
    {
        var value = raw.Trim();
        var norm = Norm(value);
        var exact = field.PicklistValues.FirstOrDefault(p => Norm(p.Value) == norm || Norm(p.Label) == norm);
        if (exact is not null)
            return exact.Value;

        var loose = field.PicklistValues.Where(p =>
        {
            var label = Norm(p.Label);
            return label.Length >= 3 && norm.Contains(label, StringComparison.Ordinal);
        }).ToList();
        if (loose.Count == 1)
            return loose[0].Value;

        var created = new PicklistValue
        {
            TenantId = field.TenantId,
            FieldId = field.Id,
            Value = value,
            Label = value,
            SortOrder = field.PicklistValues.Select(p => p.SortOrder).DefaultIfEmpty(0).Max() + 1,
            IsActive = true
        };
        _db.PicklistValues.Add(created);
        await _db.SaveChangesAsync();
        field.PicklistValues.Add(created);
        _metadata.InvalidateFieldCache(field.ModuleId);
        return created.Value;
    }

    private static ImportTable ReadCsv(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
            throw new InvalidOperationException("فایل خالی است.");

        var delimiter = headerLine.Count(c => c == ';') > headerLine.Count(c => c == ',') ? ';' : ',';
        var headers = SplitDelimited(headerLine, delimiter);
        var rows = new List<string[]>();
        var truncated = false;
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (rows.Count >= MaxImportRows)
            {
                truncated = true;
                break;
            }
            rows.Add(Fit(SplitDelimited(line, delimiter), headers.Count));
        }

        return Build(headers, rows, truncated);
    }

    private static ImportTable ReadXlsx(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.FirstOrDefault();
        if (sheet is null)
            throw new InvalidOperationException("فایل اکسل برگه‌ای ندارد.");

        var lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        if (lastCol == 0 || lastRow == 0)
            throw new InvalidOperationException("فایل خالی است.");

        var headers = new List<string>();
        for (var col = 1; col <= lastCol; col++)
        {
            var text = CellText(sheet.Cell(1, col));
            headers.Add(string.IsNullOrWhiteSpace(text) ? $"ستون {col}" : text);
        }

        var rows = new List<string[]>();
        var truncated = false;
        for (var row = 2; row <= lastRow; row++)
        {
            if (rows.Count >= MaxImportRows)
            {
                truncated = true;
                break;
            }
            var cells = new string[headers.Count];
            var any = false;
            for (var col = 1; col <= headers.Count; col++)
            {
                cells[col - 1] = CellText(sheet.Cell(row, col));
                if (cells[col - 1].Length > 0)
                    any = true;
            }
            if (any)
                rows.Add(cells);
        }

        return Build(headers, rows, truncated);
    }

    private static ImportTable Build(List<string> headers, List<string[]> rows, bool truncated)
    {
        if (headers.Count == 0 || headers.All(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("ردیف عنوان ستون‌ها پیدا نشد.");

        var columns = new List<ImportColumn>();
        for (var i = 0; i < headers.Count; i++)
        {
            columns.Add(new ImportColumn
            {
                Index = i,
                Header = headers[i].Trim(),
                Samples = rows.Select(r => i < r.Length ? r[i] : "").Where(v => v.Length > 0).Take(3).ToList()
            });
        }

        return new ImportTable { Columns = columns, Rows = rows, Truncated = truncated };
    }

    private static string[] Fit(List<string> cells, int width)
    {
        var row = new string[width];
        for (var i = 0; i < width; i++)
            row[i] = i < cells.Count ? cells[i].Trim() : "";
        return row;
    }

    private static string CellText(IXLCell cell)
    {
        if (cell.IsEmpty())
            return "";
        if (cell.DataType == XLDataType.DateTime)
        {
            var date = cell.GetDateTime();
            return date.TimeOfDay == TimeSpan.Zero
                ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }
        if (cell.DataType == XLDataType.Number)
        {
            var number = cell.GetDouble();
            if (Math.Abs(number - Math.Round(number)) < 0.0000001 && Math.Abs(number) < 1e15)
                return Math.Round(number).ToString("0", CultureInfo.InvariantCulture);
            return number.ToString(CultureInfo.InvariantCulture);
        }
        if (cell.DataType == XLDataType.Boolean)
            return cell.GetBoolean() ? "true" : "false";
        return cell.GetString().Trim();
    }

    private static List<string> SplitDelimited(string line, char delimiter)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                    inQuotes = false;
                else
                    current.Append(c);
            }
            else if (c == '"')
                inQuotes = true;
            else if (c == delimiter)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
                current.Append(c);
        }
        result.Add(current.ToString());
        return result;
    }
}
