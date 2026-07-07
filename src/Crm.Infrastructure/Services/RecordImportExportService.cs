using System.Text;
using ClosedXML.Excel;
using Crm.Core.Abstractions;
using Crm.Core.Entities;

namespace Crm.Infrastructure.Services;

/// <summary>خروجی اکسل و ورود CSV برای ماژول‌های metadata-driven.</summary>
public class RecordImportExportService
{
    private readonly MetadataService _metadata;
    private readonly DynamicRecordService _records;

    public RecordImportExportService(MetadataService metadata, DynamicRecordService records)
    {
        _metadata = metadata;
        _records = records;
    }

    public async Task<byte[]> ExportToExcelAsync(ModuleDef module, IReadOnlyList<DynamicRecord> records)
    {
        var fields = await _metadata.GetFieldsAsync(module.Id);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(module.PluralLabel);
        sheet.RightToLeft = true;

        sheet.Cell(1, 1).Value = "شناسه";
        for (var i = 0; i < fields.Count; i++)
            sheet.Cell(1, i + 2).Value = fields[i].Label;

        sheet.Row(1).Style.Font.Bold = true;

        for (var r = 0; r < records.Count; r++)
        {
            var data = DynamicRecordService.ParseData(records[r]);
            sheet.Cell(r + 2, 1).Value = records[r].Id;
            for (var i = 0; i < fields.Count; i++)
                sheet.Cell(r + 2, i + 2).Value = data.GetValueOrDefault(fields[i].Name) ?? string.Empty;
        }

        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// ورود CSV: ستون‌ها با نام سیستمی یا برچسب فیلد تطبیق داده می‌شوند.
    /// خروجی: تعداد موفق و خطاهای ردیف‌ها.
    /// </summary>
    public async Task<(int Imported, List<string> Errors)> ImportCsvAsync(int moduleId, Stream csvStream)
    {
        var fields = await _metadata.GetFieldsAsync(moduleId);
        var errors = new List<string>();
        var imported = 0;

        using var reader = new StreamReader(csvStream, Encoding.UTF8);
        var headerLine = await reader.ReadLineAsync();
        if (headerLine is null)
            return (0, new List<string> { "فایل خالی است." });

        var headers = SplitCsvLine(headerLine);
        var columnFieldMap = new Dictionary<int, string>();
        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i].Trim();
            var field = fields.FirstOrDefault(f =>
                string.Equals(f.Name, header, StringComparison.OrdinalIgnoreCase) || f.Label == header);
            if (field is not null)
                columnFieldMap[i] = field.Name;
        }

        if (columnFieldMap.Count == 0)
            return (0, new List<string> { "هیچ ستونی با فیلدهای ماژول تطبیق نیافت." });

        var rowNumber = 1;
        while (await reader.ReadLineAsync() is { } line)
        {
            rowNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var cells = SplitCsvLine(line);
            var values = new Dictionary<string, string?>();
            foreach (var (index, fieldName) in columnFieldMap)
            {
                if (index < cells.Count)
                    values[fieldName] = cells[index];
            }

            try
            {
                await _records.CreateAsync(moduleId, values);
                imported++;
            }
            catch (RecordValidationException ex)
            {
                errors.Add($"ردیف {rowNumber}: {string.Join(" — ", ex.Errors.Values)}");
            }
        }

        return (imported, errors);
    }

    private static List<string> SplitCsvLine(string line)
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
                {
                    inQuotes = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        result.Add(current.ToString());
        return result;
    }
}
