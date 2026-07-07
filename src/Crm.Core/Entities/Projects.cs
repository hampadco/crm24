namespace Crm.Core.Entities;

public enum ProjectStatus
{
    Active = 0,
    OnHold = 1,
    Completed = 2,
    Cancelled = 3
}

/// <summary>پروژه — قابل ساخت مستقیم یا از تبدیل فرصت برنده.</summary>
public class Project : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.Active;
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }

    public decimal Budget { get; set; }

    public int? ContactRecordId { get; set; }
    public string? CustomerName { get; set; }

    /// <summary>فرصت مبدأ (در تبدیل فرصت برنده به پروژه).</summary>
    public int? OpportunityRecordId { get; set; }

    /// <summary>نمایش وضعیت پروژه در پورتال مشتری نهایی.</summary>
    public bool ShowInPortal { get; set; }

    public ICollection<ProjectPhase> Phases { get; set; } = new List<ProjectPhase>();
    public ICollection<ProjectTask> Tasks { get; set; } = new List<ProjectTask>();
}

/// <summary>فاز پروژه — گروه‌بندی وظایف.</summary>
public class ProjectPhase : TenantEntity
{
    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public enum ProjectTaskStatus
{
    Todo = 0,
    InProgress = 1,
    Done = 2
}

/// <summary>وظیفه پروژه — با بازه زمانی برای نمای گانت.</summary>
public class ProjectTask : TenantEntity
{
    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public int? PhaseId { get; set; }
    public ProjectPhase? Phase { get; set; }

    public string Name { get; set; } = string.Empty;
    public int? AssignedUserId { get; set; }

    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }

    public ProjectTaskStatus Status { get; set; } = ProjectTaskStatus.Todo;
    public int ProgressPercent { get; set; }
    public int SortOrder { get; set; }
}
