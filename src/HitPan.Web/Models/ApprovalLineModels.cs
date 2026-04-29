namespace HitPan.Web.Models;

public sealed class ApprovalLineListItemModel
{
    public string ApprovalLineId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public int StepCount { get; set; }
}

public sealed class ApprovalLineDetailModel
{
    public string ApprovalLineId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public List<ApprovalLineStepModel> Steps { get; set; } = new();
}

public sealed class ApprovalLineStepModel
{
    public string StepId { get; set; } = string.Empty;
    public int StepOrder { get; set; }
    public string PositionId { get; set; } = string.Empty;
    public string PositionName { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
}

public sealed class SaveApprovalLineModel
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public List<SaveApprovalLineStepModel> Steps { get; set; } = new();
}

public sealed class SaveApprovalLineStepModel
{
    public int StepOrder { get; set; }
    public string PositionId { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
}
