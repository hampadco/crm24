namespace Crm.Core.Entities;

/// <summary>
/// رکورد عمومی ماژول‌های metadata-driven.
/// تمام مقادیر فیلدها در CustomData (jsonb) نگهداری می‌شود؛ Title برای نمایش سریع denormalize شده.
/// </summary>
public class DynamicRecord : CrmRecordEntity
{
    public int ModuleId { get; set; }
    public ModuleDef Module { get; set; } = null!;

    /// <summary>مقدار فیلد عنوان ماژول برای لیست‌ها و لینک‌ها.</summary>
    public string Title { get; set; } = string.Empty;
}
