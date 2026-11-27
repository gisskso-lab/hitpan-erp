namespace HitPan.Application.Interfaces;

/// <summary>
/// 🔴 W-G1-52 갈래 0 — <b>복원 대상 DB 밖</b> 로컬 감사 파일(설계 §6 정본 · K4 = 파일).
/// 복원 서버(갈래 A)와 부모 비번 되찾기(갈래 C)가 <b>같이 쓰는 계약</b>이다.
/// </summary>
/// <remarks>
/// 🔴 <b>줄 더하기만</b> — 지우기 · 고치기 API 는 없다(만들지 않는다). 한 줄 = 시각(UTC) · 회사 · 대표 user_id ·
///   동작 · 결과 · 묶음 id · 짧은 덧말 · <b>앞 줄 지문</b>(SHA-256 사슬). 가운데 줄을 지우거나 고치면
///   <see cref="VerifyChainAsync"/> 가 빨강을 낸다.
///
/// 🔴 <b>본사 전송 0</b>(#18 · #22) — 이 파일은 고객 PC 안에만 있다. 구현체는 네트워크 형식을 참조하지 않는다(G11 시험).
///
/// 🔴 <b>#2 — tenant_id · user_id 는 호출자가 JWT 클레임(또는 서버 안에서 확정한 값)에서 꺼내 넘긴다.</b>
///   이 기록기는 받은 값을 그대로 적을 뿐 출처를 확인하지 않는다. 요청 본문 · 쿼리에서 온 값을 넣으면 호출자 위반이다.
///
/// 🔴 <b>개인정보 최소</b> — 비번 · 토큰 · 주민번호 · 이메일은 넣지 않는다. 동작 이름과 id(+ 사전 백업 파일 이름 같은 짧은 덧말)만.
///   덧말은 글자 · 숫자 · <c>. _ - : + =</c> 만 받는다(빈칸 · 줄바꿈 · 탭 · <c>@</c> 거절) — 문장이나 이메일이 들어갈 자리를 막는다.
///
/// ⚠️ <b>지문의 한계</b> — 비밀 열쇠 없는 사슬이라 「고친 뒤 뒤 줄 지문을 모두 다시 계산」 하는 사람은 못 막는다.
///   또 맨 끝 줄들을 통째로 잘라내면 사슬만으로는 모른다 → DB 보조 기록(<c>restore_history</c> · <c>audit_logs</c>)과 맞대어 본다(설계 §6 보조).
/// </remarks>
public interface ILocalAuditFile
{
    /// <summary>감사 파일의 전체 경로(고객 PC 앱 자료 폴더 안 · 복원 대상 DB 밖).</summary>
    string FilePath { get; }

    /// <summary>
    /// 줄 하나를 끝에 더한다. 줄 끝에서 디스크까지 내려 쓴다(Flush to disk).
    /// 앞서 전원이 끊겨 마지막 줄이 깨져 있으면 그 조각을 닫고 <see cref="LocalAuditAction.TornTailNoted"/> 줄을 먼저 남긴 뒤 이어 쓴다.
    /// </summary>
    /// <returns>실제로 적힌 줄(순번 · 시각 · 지문 포함).</returns>
    /// <exception cref="ArgumentException">id · 덧말 글자가 규칙 밖이면(개인정보 · 줄 깨뜨리기 방지).</exception>
    /// <exception cref="IOException">다른 프로세스가 오래 쥐고 있어 쓰지 못하면. 호출자는 삼키지 말고 기록 실패로 다룬다.</exception>
    Task<LocalAuditEntry> AppendAsync(LocalAuditRecord record, CancellationToken ct = default);

    /// <summary>
    /// 다음 기동 복구용(설계 §7 「⑤ 뒤 감사 · 끊기 전」) — <see cref="LocalAuditAction.SwapStarted"/> 줄 중
    /// 같은 <see cref="LocalAuditRecord.OpId"/> 로 끝 줄(<see cref="LocalAuditAction.SwapCompleted"/> ·
    /// <see cref="LocalAuditAction.SwapFailed"/> · <see cref="LocalAuditAction.RecoveryResolved"/>)이 아직 없는 것을 순번 차례로 돌려준다.
    /// 깨진 줄은 「안 적힌 줄」 로 본다. 파일이 없으면 빈 목록.
    /// </summary>
    /// <param name="tenantId">null 이면 모든 회사. 값은 #2 대로 서버가 확정한 것만.</param>
    Task<IReadOnlyList<LocalAuditEntry>> ReadUnfinishedSwapsAsync(string? tenantId = null, CancellationToken ct = default);

    /// <summary>사슬 검사 — 처음부터 끝까지 지문을 다시 계산해 대 본다. 파일이 없으면 통과(줄 0).</summary>
    Task<LocalAuditChainReport> VerifyChainAsync(CancellationToken ct = default);
}

/// <summary>
/// 감사 동작. 🔴 <b>이름이 파일에 글자로 적힌다 — 이름을 바꾸거나 지우지 않는다</b>(옛 줄을 못 읽게 된다 · #1). 더하기만.
/// </summary>
public enum LocalAuditAction
{
    /// <summary>복원 시작(대표 · 메인PC 확인을 지난 직후).</summary>
    RestoreStarted = 1,
    /// <summary>사전 백업 파일을 만듦 — 덧말 = 파일 이름(경로 아님).</summary>
    PreBackupCreated = 2,
    /// <summary>대조 실패(백업 ↔ 지금 계정 · 사원 등) — 덧말 = 실패 칸 이름.</summary>
    VerifyFailed = 3,
    /// <summary>교체 시작 — 이 줄만 있고 끝 줄이 없으면 다음 기동이 알아본다(§7).</summary>
    SwapStarted = 4,
    /// <summary>교체 끝(트랜잭션 커밋 · 갱신 줄 수 확인 뒤).</summary>
    SwapCompleted = 5,
    /// <summary>교체 실패(되돌려짐).</summary>
    SwapFailed = 6,
    /// <summary>복원 중단(교체 전 걸음에서 멈춤 · 라이브 손 0).</summary>
    RestoreAborted = 7,
    /// <summary>다음 기동 복구가 미완 교체를 정리함 — 같은 OpId · 덧말 = 판정(예: committed · rolledback).</summary>
    RecoveryResolved = 8,
    /// <summary>부모 비번 다시 정함(성공).</summary>
    ParentPasswordReset = 9,
    /// <summary>부모 비번 되찾기 거절 · 실패 — 덧말 = 까닭 코드.</summary>
    ParentPasswordResetFailed = 10,
    /// <summary>🔴 기록기 자신이 남김 — 앞 줄이 깨져 있어 닫고 이어 씀. 덧말 = <c>torn-sha256:</c> + 깨진 조각 지문 앞 16자.</summary>
    TornTailNoted = 99,
}

/// <summary>감사 결과. 🔴 이름이 글자로 적힌다 — 바꾸지 않는다.</summary>
public enum LocalAuditResult
{
    /// <summary>걸음을 시작함(끝은 뒤 줄이 말한다).</summary>
    Started = 1,
    /// <summary>성공.</summary>
    Succeeded = 2,
    /// <summary>실패 · 거절.</summary>
    Failed = 3,
    /// <summary>기록 정보(성공 · 실패가 아닌 사실 — 예: 깨진 줄 표시).</summary>
    Noted = 4,
}

/// <summary>호출자가 넘기는 한 줄 내용. 시각 · 순번 · 지문은 기록기가 붙인다.</summary>
/// <param name="TenantId">회사 id — 🔴 JWT 클레임 값만(#2).</param>
/// <param name="ParentUserId">대표(부모) user_id — 🔴 서버가 확정한 값만(#2 · #40). 비번 되찾기처럼 로그인 전이면 서버가 찾아 낸 id.</param>
/// <param name="Action">동작.</param>
/// <param name="Result">결과.</param>
/// <param name="OpId">한 번의 복원 · 되찾기를 묶는 id(호출자가 만듦 · 예: Guid "N"). 시작 줄과 끝 줄이 같은 값을 쓴다.</param>
/// <param name="Detail">짧은 덧말(없으면 null) — 글자 · 숫자 · <c>. _ - : + =</c> 만 · 200자 이하.</param>
public sealed record LocalAuditRecord(
    string TenantId,
    string ParentUserId,
    LocalAuditAction Action,
    LocalAuditResult Result,
    string OpId,
    string? Detail = null);

/// <summary>파일에 적힌 한 줄.</summary>
/// <param name="Seq">1부터 오르는 순번(깨진 조각은 세지 않음).</param>
/// <param name="AtUtc">기록 시각(UTC).</param>
/// <param name="Record">호출자 내용.</param>
/// <param name="PrevHash">앞 줄 지문(첫 줄은 0 64개).</param>
/// <param name="Hash">이 줄 지문 = SHA-256(이 줄의 지문 칸 앞 글자 전부 · UTF-8) 소문자 16진.</param>
public sealed record LocalAuditEntry(
    long Seq,
    DateTime AtUtc,
    LocalAuditRecord Record,
    string PrevHash,
    string Hash);

/// <summary>사슬 검사 결과.</summary>
/// <param name="Ok">사슬이 끊긴 곳 없음.</param>
/// <param name="ValidLines">지문이 맞은 줄 수.</param>
/// <param name="FirstBadLineNumber">처음 틀린 줄 번호(파일 줄 기준 1부터) · 통과면 null.</param>
/// <param name="Reason">틀린 까닭(짧은 코드) · 통과면 null.</param>
/// <param name="TailTorn">맨 끝이 줄바꿈 없이 깨져 있음(전원 끊김 흔적 · 다음 쓰기가 닫음). 이것만으로는 빨강 아님.</param>
public sealed record LocalAuditChainReport(
    bool Ok,
    int ValidLines,
    int? FirstBadLineNumber,
    string? Reason,
    bool TailTorn);
