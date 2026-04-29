namespace HitPan.Application.DTOs.ApprovalLine;

public sealed class ApprovalLineListDto
{
    public string ApprovalLineId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public int StepCount { get; set; }
}

public sealed class ApprovalLineDetailDto
{
    public string ApprovalLineId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public List<ApprovalLineStepDto> Steps { get; set; } = new();
}

public sealed class ApprovalLineStepDto
{
    public string StepId { get; set; } = string.Empty;
    public int StepOrder { get; set; }
    public string PositionId { get; set; } = string.Empty;
    public string PositionName { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
}

public sealed class SaveApprovalLineRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public List<SaveApprovalLineStepRequest> Steps { get; set; } = new();
}

public sealed class SaveApprovalLineStepRequest
{
    public int StepOrder { get; set; }
    public string PositionId { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
}
