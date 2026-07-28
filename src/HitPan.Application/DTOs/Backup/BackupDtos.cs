namespace HitPan.Application.DTOs.Backup;

/// <summary>자료 백업 운영 설정 DTO.</summary>
public sealed class BackupSettingsDto
{
    public string PrimaryPath { get; set; } = "";
    public string? MirrorPath { get; set; }
    public string ScheduleMode { get; set; } = "manual"; // manual / hourly / every_6h / daily_03
    public int RetentionCount { get; set; } = 30;
    public DateTime? LastRunAt { get; set; }
    public string? LastStatus { get; set; }
    public string? LastError { get; set; }
}

/// <summary>백업 설정 변경 요청.</summary>
public sealed class UpdateBackupSettingsRequest
{
    public string PrimaryPath { get; set; } = "";
    public string? MirrorPath { get; set; }
    public string ScheduleMode { get; set; } = "manual";
    public int RetentionCount { get; set; } = 30;
}

/// <summary>백업 이력 1행.</summary>
public sealed class BackupHistoryDto
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

/// <summary>백업 실행 응답.</summary>
public sealed class RunBackupResponse
{
    public bool Success { get; set; }
    public string BackupId { get; set; } = "";
    public string? PrimaryFile { get; set; }
    public string? MirrorFile { get; set; }
    public long FileSizeBytes { get; set; }
    public string? Error { get; set; }
}

/// <summary>복원 요청 (백업 이력에서 선택 또는 직접 경로).</summary>
public sealed class RestoreRequest
{
    /// <summary>백업 이력 ID (선택). 우선순위 1.</summary>
    public string? BackupId { get; set; }
    /// <summary>외부 .sql 파일 경로 (선택). BackupId 없을 때 사용.</summary>
    public string? ExternalFilePath { get; set; }
    /// <summary>안전 확인 — 사용자가 입력한 회사명 (DB 회사명과 일치해야 진행).</summary>
    public string ConfirmCompanyName { get; set; } = "";
}

/// <summary>복원 응답.</summary>
public sealed class RestoreResponse
{
    public bool Success { get; set; }
    public string RestoreId { get; set; } = "";
    public string? PreRestoreBackup { get; set; }
    public string? Error { get; set; }
}

/// <summary>복원 이력.</summary>
public sealed class RestoreHistoryDto
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
