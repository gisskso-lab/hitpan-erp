namespace HitPan.Backoffice.Components.Pages.Platform.Components;

/// <summary>
/// 🔴 20261102작 — 고객사 목록 · 상세가 읽는 「ERP 설치」 두 칸 (설계 §5).
/// <para>화면 모델 <c>AdminTenantsV2.TenantRow</c> · <c>AdminTenantDetailV2.TenantDetail</c> 이 상속한다
/// ⇒ 게이트가 <b>화면이 읽는 그 형</b>으로 역직렬화한다(10/13 사전 판독 사고 재발 방지).
/// 새 칸은 전부 널 허용 — 값 없는 고객 1건이 화면 전체를 가리지 않는다.</para>
/// </summary>
public class TenantInstallFields
{
    public string? InstallState { get; set; }
    // 최근 ERP 기동 보고 시각(UTC). 설치 시각이 아니다 — 재부팅마다 바뀐다.
    public DateTime? LastErpStartAt { get; set; }
}

/// <summary>
/// 🔴 20261102작 — 「ERP 설치」 칸 낱말 한 벌. Blazor 의존 0 순수 C#(<c>CsVocab.cs</c> 선례) · 시험 프로젝트에 소스 링크.
///
/// <para>🔴 <b>기본가지는 「확인 불가」</b>다. 값 없음 · 모르는 값을 「미설치」로 떨어뜨리면
/// 판정 실패가 거짓 미설치가 된다(설계 §3-4 · 대표 결정 4).</para>
///
/// <para>🔴 「완료」는 증명이 아니다. 지운 뒤 · 재설치 실패 뒤에도 남는다(대표 수용) ⇒ 「실행된 기록이 있습니다」로만 말한다.
/// 「설치 완료: 시각」 · 접속 상태 낱말은 쓰지 않는다 — 시각은 재부팅마다 바뀐다(설계 §5 쓰지 않는 말).</para>
/// </summary>
public static class TenantInstallText
{
    public const string Installed = "installed";
    public const string Started = "started";
    public const string None = "none";

    private const string RanRecord = "고객 PC에서 ERP가 실행된 기록이 있습니다.";
    private const string StaysAfterRemoval = "프로그램을 지워도 이 표시는 바뀌지 않습니다.";

    public static string Label(string? state) => state switch
    {
        Installed => "완료",
        Started => "설치 시작",
        None => "미설치",
        _ => "확인 불가"
    };

    /// <summary>목록 툴팁 = 상세 보조문. 「설치 시작」 은 시각을 쓰지 않는다 — 시리얼 확인 시각을 설치 시각으로 읽을 수 있다.</summary>
    public static string Help(string? state, DateTime? lastErpStartAt) => state switch
    {
        Installed => lastErpStartAt is DateTime at
            ? $"{RanRecord} 최근 ERP 기동: {FormatKst(at)} {StaysAfterRemoval}"
            : $"{RanRecord} 최근 ERP 기동 시각은 아직 없습니다. {StaysAfterRemoval}",
        // 끝 문장 = 대표 결정 5 — 옛 판 고객이 이 칸에 섞인다. 다음 업데이트 뒤 「완료」로 옮겨 간다.
        Started => "시리얼 번호 확인까지 진행됐습니다. ERP가 실행된 기록은 아직 없습니다. "
                 + "설치 중이거나 중간에 멈췄을 수 있습니다. "
                 + "이전 판 프로그램을 쓰는 고객은 설치가 끝났어도 업데이트 전까지 이렇게 보일 수 있습니다.",
        // 「설치되지 않았습니다」 단정 금지 — 본사가 모를 뿐인 고객이 있다.
        None => "본사에 설치 진행 기록이 없습니다.",
        _ => "설치 기록을 읽지 못했습니다. 잠시 뒤 새로고침해 주세요."
    };

    /// <summary>재료 시각은 UTC 다. <c>ToLocalTime()</c> 은 서버 시간대를 타므로 쓰지 않는다 — 한국은 서머타임이 없어 고정값이 맞다.</summary>
    public static string FormatKst(DateTime utc) => utc.AddHours(9).ToString("yyyy-MM-dd HH:mm");
}
