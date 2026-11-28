namespace HitPan.Application.Interfaces;

/// <summary>
/// 🔴 W-G1-52 갈래 A-2① — <b>복원 점검 상태 · 복원 시각 표지</b>(설계 <c>docs/설계/erp/W-G1-52_최소복구_설계_20261127.md</c> §15-5 ① ⓐ ~ ⓔ · K8 · CTO N1-a).
/// </summary>
/// <remarks>
/// <para><b>점검 상태</b> = API 프로세스 안 깃발 1개(서비스 다시 띄우기 0 · #29). 프로세스가 죽으면 깃발도 사라진다(설계 §7).
/// DB 에 쓰는 프로세스 = API 하나(워치독은 <c>local_update_*</c> 지금 값 표만 씀 — 개발명세서 4-0 ⑥ 실측).</para>
/// <para><b>부르는 쪽</b>(A-2② <c>RestoreSafePath</c>): 걸음 ② <b>앞</b>에서 <see cref="BeginAsync"/> → 실패면 바뀐 것 0 으로 멈춤 →
/// 걸음 ② ~ ⑤ → 성공 · 실패 모두 <c>finally</c> 에서 <see cref="EndAsync"/>. <see cref="EndAsync"/> 를 빠뜨리면 회사 전체가 503 에 갇힌다.</para>
/// <para><b>복원 시각 표지(N1-a)</b>: 갱신 토큰 폐기 · 세션 지움만으로는 access token 이 다 죽지 않는다(4-0 ① 실측 —
/// <c>sid</c> 없는 토큰 · 생존 확인 스위치 끔 · 10초 캐시 · 조회 실패 통과). 그래서 표지보다 <b>앞에 발급된 토큰 = 401</b>.
/// 표지 자리 = 「지금 값」 표 <c>restore_history</c>(새 표 0 · 재기동 뒤에도 남음) + 프로세스 안 값(<see cref="EndAsync"/> 가 즉시 올림).</para>
/// <para>🔴 계약(A-2② · A-2③): <c>restore_history.started_at</c> · <c>finished_at</c> 은 <b>서버 지역 시각</b>으로 쓴다
/// (옛 <c>BackupService.RestoreAsync</c> 의 <c>DateTime.Now</c> 와 같음). 표지 읽는 쪽이 지역 시각 → UTC 로 바꾼다.
/// UTC 로 쓰면 한국 시각에서 표지가 9시간 이르게 읽혀 그 사이 발급 토큰이 살아남는다.</para>
/// </remarks>
public interface IRestoreMaintenance
{
    /// <summary>점검 상태가 켜져 있나(프로세스 안 깃발).</summary>
    bool IsActive { get; }

    /// <summary>
    /// 요청 입장 — <b>도는 셈을 먼저 올리고</b> 깃발을 본다(설계 §15-5 ① ⓐ · 확인-뒤-행동 틈 막기).
    /// 깃발이 켜져 있으면 셈을 내리고 <c>false</c>. <c>true</c> 를 받은 쪽은 끝에 반드시 <see cref="ExitRequest"/>.
    /// </summary>
    bool TryEnterRequest();

    /// <summary>요청 퇴장 — 셈 내림.</summary>
    void ExitRequest();

    /// <summary>지금 도는(허용 목록 밖) 요청 수.</summary>
    int InFlight { get; }

    /// <summary>
    /// 점검 켬 — 깃발 켜기와 셈 읽기는 한 자물쇠. 도는 요청이 0 이 될 때까지 기다리고(<paramref name="drainTimeout"/> 넘으면 깃발 끄고 실패 · 바뀐 것 0),
    /// 이 프로세스의 배경 일꾼(<c>IHostedService</c> 전부를 세고 · 도는 것은 멈춤)을 쉬게 한다.
    /// </summary>
    Task<RestoreMaintenanceBeginResult> BeginAsync(TimeSpan drainTimeout, CancellationToken ct);

    /// <summary>
    /// 점검 끔(성공 · 실패 모두) — 프로세스 안 캐시 비움 → 복원 시각 표지 올림(이 회사) → 깃발 끔 → 쉬던 일꾼 다시.
    /// </summary>
    Task EndAsync(string tenantId, CancellationToken ct);

    /// <summary>프로세스 안 표지(UTC). <c>null</c> = 아직 DB 에서 안 읽음 · <see cref="DateTime.MinValue"/> = 복원 기록 없음.</summary>
    DateTime? GetStampUtc(string tenantId);

    /// <summary>DB(<c>restore_history</c>)에서 읽은 표지를 기억(더 늦은 값만 남김). <c>null</c> = 복원 기록 없음.</summary>
    void RememberStampUtc(string tenantId, DateTime? stampUtc);
}

/// <summary><see cref="IRestoreMaintenance.BeginAsync"/> 결과.</summary>
/// <param name="Started">점검 상태를 켰나. <c>false</c> 면 깃발 꺼짐 · 일꾼 그대로(바뀐 것 0).</param>
/// <param name="DenyReason">못 켠 까닭(<c>already_active</c> · <c>drain_timeout</c> · <c>worker_stop_timeout</c>) — 서버 기록용 · 화면 문구 아님.</param>
/// <param name="HostedServices">센 <c>IHostedService</c> 전부(형 이름).</param>
/// <param name="PausedServices">멈춘 일꾼(형 이름) — <see cref="IRestoreMaintenance.EndAsync"/> 가 다시 띄운다.</param>
public sealed record RestoreMaintenanceBeginResult(
    bool Started,
    string? DenyReason,
    IReadOnlyList<string> HostedServices,
    IReadOnlyList<string> PausedServices);
