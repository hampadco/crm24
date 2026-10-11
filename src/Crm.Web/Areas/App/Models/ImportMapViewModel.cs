using Crm.Core.Entities;
using Crm.Infrastructure.Services;

namespace Crm.Web.Areas.App.Models;

public class ImportMapViewModel
{
    public ModuleDef Module { get; set; } = null!;
    public List<FieldDef> Fields { get; set; } = [];
    public string? Token { get; set; }
    public ImportTable? Table { get; set; }
    public string? Error { get; set; }
}

public class ImportRunViewModel
{
    public ModuleDef Module { get; set; } = null!;
    public string Token { get; set; } = "";
    public ImportRunStatus Status { get; set; } = new();
}
