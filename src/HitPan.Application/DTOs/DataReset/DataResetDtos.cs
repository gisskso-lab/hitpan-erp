namespace HitPan.Application.DTOs.DataReset;

/// <summary>모든데이터 초기화 요청. 부모계정 패스워드 재입력을 강제한다.</summary>
public sealed class DataResetRequest
{
    /// <summary>부모계정(tenant_admin) 패스워드 재입력 — 본인 확인용.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>사용자가 경고를 읽고 입력한 확인 문구("초기화" 등) — 오조작 방지.</summary>
    public string ConfirmText { get; set; } = string.Empty;
}

/// <summary>
/// 20261125작 — 모든데이터 초기화를 부를 수 있는지 판정한 결과(대표 계정 = users.is_parent = 1 · 대표 결재 §146 「초기화만」 · 「막고 안내」).
/// 판정 순서: 대표 0명 → NoParent · 요청자 본인이 대표 아님 → NotParent · 대표 2명 이상 → MultipleParents · 그 밖 → Allowed.
/// </summary>
public enum DataResetGate
{
    Allowed,
    NotParent,
    NoParent,
    MultipleParents,

    /// <summary>20261127작 W-S-01 갈래 B — 이 판에서 초기화가 닫혀 있다(<c>IDataResetAvailability.IsOpen == false</c> · 대표 판정보다 앞 · 설계 §3-2 의 5).</summary>
    Closed,
}

/// <summary>20261125작 — <see cref="DataResetGate"/> 를 화면 · 시험이 함께 쓰는 글자로 바꾼다(화면 ↔ 서버 계약 · 화면 쪽 짝 = HitPan.Web DataResetMenuRule).</summary>
public static class DataResetGateCodes
{
    public const string Allowed = "allowed";
    public const string NotParent = "not_parent";
    public const string NoParent = "no_parent";
    public const string MultipleParents = "multiple_parents";

    /// <summary>20261127작 W-S-01 갈래 B — 이 판에서 초기화 닫힘(화면 짝 = HitPan.Web DataResetMenuRule.Closed · 메뉴 감춤).</summary>
    public const string Closed = "closed";

    public static string ToCode(DataResetGate gate) => gate switch
    {
        DataResetGate.Allowed => Allowed,
        DataResetGate.NoParent => NoParent,
        DataResetGate.MultipleParents => MultipleParents,
        // 20261127작 W-S-01 갈래 B [반증 S-2] — 드러내 적는다. 빠뜨려도 아래 기본 칸(NotParent)으로 떨어져 빌드는 초록이다(시험 G-B3 ⓐ 가 잡음).
        DataResetGate.Closed => Closed,
        _ => NotParent,
    };
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

    /// <summary>20261125작 — 대표 판정에서 막혔을 때의 까닭 글자(<see cref="DataResetGateCodes"/>). 막히지 않았으면 null.</summary>
    public string? DenyCode { get; set; }
}
