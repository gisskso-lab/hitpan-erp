namespace HitPan.Application.DTOs.DataReset;

/// <summary>모든데이터 초기화 요청. 부모계정 패스워드 재입력을 강제한다.</summary>
public sealed class DataResetRequest
{
    /// <summary>부모계정(tenant_admin) 패스워드 재입력 — 본인 확인용.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>사용자가 경고를 읽고 입력한 확인 문구("초기화" 등) — 오조작 방지.</summary>
    public string ConfirmText { get; set; } = string.Empty;
}

/// <summary>모든데이터 초기화 결과.</summary>
public sealed class DataResetResponse
{
    public bool Success { get; set; }

    /// <summary>강제 백업 ID(초기화 직전 백업).</summary>
    public string? BackupId { get; set; }

    /// <summary>비운 테이블 수.</summary>
    public int ClearedTableCount { get; set; }

    /// <summary>실패 시 사유.</summary>
    public string? Error { get; set; }
}
