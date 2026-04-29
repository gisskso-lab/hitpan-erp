namespace HitPan.Web.Models;

public sealed class BackupSettingsModel
{
    public string PrimaryPath { get; set; } = "";
    public string? MirrorPath { get; set; }
    public string ScheduleMode { get; set; } = "manual";
    public int RetentionCount { get; set; } = 30;
    public DateTime? LastRunAt { get; set; }
    public string? LastStatus { get; set; }
    public string? LastError { get; set; }
}

public sealed class BackupHistoryModel
{
    public string BackupId { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? PrimaryFile { get; set; }
    public string? MirrorFile { get; set; }
    public long? FileSizeBytes { get; set; }
    public string Status { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public string TriggeredBy { get; set; } = "manual";
}

public sealed class RunBackupResultModel
{
    public bool Success { get; set; }
    public string BackupId { get; set; } = "";
    public string? PrimaryFile { get; set; }
    public string? MirrorFile { get; set; }
    public long FileSizeBytes { get; set; }
    public string? Error { get; set; }
}

public sealed class RestoreRequestModel
{
    public string? BackupId { get; set; }
    public string? ExternalFilePath { get; set; }
    public string ConfirmCompanyName { get; set; } = "";
}

public sealed class RestoreResultModel
{
    public bool Success { get; set; }
    public string RestoreId { get; set; } = "";
    public string? PreRestoreBackup { get; set; }
    public string? Error { get; set; }
}

public sealed class RestoreHistoryModel
{
    public string RestoreId { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string SourceFile { get; set; } = "";
    public string? PreRestoreBackup { get; set; }
    public string Status { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public string? TriggeredByUser { get; set; }
}
