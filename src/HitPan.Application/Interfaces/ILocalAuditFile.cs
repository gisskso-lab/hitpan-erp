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
///   대표 user_id 를 서버가 찾지 못하면(비번 되찾기 · 로그인 전) <see cref="LocalAuditWords.UnknownUser"/> 하나만 쓴다(입력값 그대로 적지 않음).
///
/// 🔴 <b>개인정보 최소</b> — 비번 · 토큰 · 주민번호 · 이메일은 넣지 않는다. 동작 이름과 id(+ 사전 백업 파일 이름 같은 짧은 덧말)만.
///   ⬛ (고침 1 전 문구) 덧말은 글자 · 숫자 · <c>. _ - : + =</c> 만 받는다(빈칸 · 줄바꿈 · 탭 · <c>@</c> 거절) — 문장이나 이메일이 들어갈 자리를 막는다.
///   🆕 고침 1(3-V F6) — 사실대로: 덧말은 글자 · 숫자 · <c>. _ - : + =</c> 만 · 200자 이하(빈칸 · 줄바꿈 · 탭 · <c>@</c> 거절).
///   더해서 JWT 꼴(<c>eyJ</c>) · 대소문자 섞인 32자 넘는 덩어리(base64 꼴) · 주민번호 꼴 · 휴대전화 꼴을 거절한다.
///   ⚠️ <b>이것으로 비밀을 다 막지는 못한다</b>(영숫자 1회용 표 · 짧은 토큰 조각 · 소문자 16진 비밀은 통과) — 비밀을 넣지 않는 것은 <b>호출자 책임</b>이고,
///   A · C 는 덧말을 고정 낱말(까닭 코드 · committed/rolledback · 사전 백업 파일 이름 꼴)에서만 고른다.
///
/// ⚠️ <b>지문의 한계</b> — 비밀 열쇠 없는 사슬이라 「고친 뒤 뒤 줄 지문을 모두 다시 계산」 하는 사람은 못 막는다.
///   또 맨 끝 줄들을 통째로 잘라내면 사슬만으로는 모른다 → DB 보조 기록(<c>restore_history</c> · <c>audit_logs</c>)과 맞대어 본다(설계 §6 보조).
///
/// 🆕 고침 1(3-V F4 · 4 발견 4) — <b>알아보는 것 / 못 알아보는 것</b>(사실대로):
///   <list type="bullet">
///   <item>✅ 알아봄 — 가운데 줄 고치기(<c>hash_mismatch</c>) · 가운데 줄 지우기 · 끼워 넣기(<c>chain_break</c> · <c>seq_gap</c>) ·
///     앞 줄 지우기 · 가운데 쓰레기 줄(<c>torn_unexplained</c>) · 끝 쓰레기 줄(<c>tail_garbage</c> — 줄바꿈이 붙어도 · 다음 쓰기가 덮어 주지 않음) ·
///     파일 없음 · 빈 파일(<see cref="LocalAuditFileState.Missing"/> · <see cref="LocalAuditFileState.Empty"/> — 초록 아님).</item>
///   <item>❌ 못 알아봄 — <b>맨 끝 줄 잘라내기</b>(끝 줄을 앞부분만 남기고 자르는 것 포함 — 전원 끊김 흔적과 같은 꼴이라 다음 쓰기가
///     <see cref="LocalAuditAction.TornTailNoted"/> 로 닫는다) · <b>옛 사본으로 통째 되돌리기</b> · <b>고친 뒤 지문 다시 계산 · 새 사슬</b> ·
///     <b>통째 지우고 다시 쓰기</b>. → <b>DB 보조와 맞대기 = 갈래 A 몫</b>(무엇을 같이 적고 무엇과 대 보는지 = A 설계 ·
///     <c>TornTailNoted</c> 줄 수 · 순번 재사용도 대 볼 것 · 개발명세서 §A 인계).</item>
///   <item>폴더는 SYSTEM · Administrators(· 실행 계정)만 쓰는 보호 권한으로 만든다(X1) — 일반 사용자는 위 꼴을 손댈 수 없다. 관리자 손은 막지 못한다.</item>
///   </list>
///
/// 🆕 고침 1(3-V F5 · X7) — 쓰기 실패는 <see cref="LocalAuditWriteException"/> 하나로만 던진다. 멈출지(fail-closed) 지날지는 A · C 지시서가 정한다
///   (PM 추천 = 복원 · 되찾기 둘 다 멈춤 + 쉬운 안내). 글자 규칙 위반(<see cref="ArgumentException"/>)은 호출자 코드 잘못이라 따로다.
/// </remarks>
public interface ILocalAuditFile
{
    /// <summary>감사 파일의 전체 경로(고객 PC 앱 자료 폴더 안 · 복원 대상 DB 밖).</summary>
    string FilePath { get; }

    /// <summary>
    /// 줄 하나를 끝에 더한다. 줄 끝에서 디스크까지 내려 쓴다(Flush to disk).
    /// 앞서 전원이 끊겨 마지막 줄이 깨져 있으면 그 조각을 닫고 <see cref="LocalAuditAction.TornTailNoted"/> 줄을 먼저 남긴 뒤 이어 쓴다.
    /// 🆕 고침 1(X5) — 끊긴 조각이 여럿(전원이 두 번 이상 끊김)이어도 조각마다 표시 줄을 남겨 사슬이 스스로 초록으로 돌아온다.
    ///   단 「기록기가 쓰다 끊긴 꼴」(맞는 순번 · 앞 줄 지문의 앞부분)이 아닌 쓰레기 줄은 표시하지 않는다(빨강 그대로).
    /// </summary>
    /// <returns>실제로 적힌 줄(순번 · 시각 · 지문 포함).</returns>
    /// <exception cref="ArgumentException">id · 덧말 글자가 규칙 밖이면(호출자 코드 잘못 · 쓰기 실패 아님).</exception>
    /// <exception cref="LocalAuditWriteException">🆕 고침 1(X7) — 쓰지 못함(폴더 · 파일 안전 검사 거절 · 다른 쪽이 오래 쥠 · 디스크 오류).
    ///   <see cref="LocalAuditWriteException.LineWritten"/> = 줄이 파일에 남았는지(디스크 내려쓰기 실패 뒤 다시 읽어 확인). 호출자는 삼키지 않는다.</exception>
    /// <remarks>⬛ (고침 1 전) <c>&lt;exception cref="IOException"&gt;</c> 다른 프로세스가 오래 쥐고 있어 쓰지 못하면 — 고침 1 부터 <see cref="LocalAuditWriteException"/> 로 감싼다.</remarks>
    Task<LocalAuditEntry> AppendAsync(LocalAuditRecord record, CancellationToken ct = default);

    // ⬛ 고침 1 전 선언(3-V F2 · 기본값 null = 모든 회사 → 다른 회사의 미완 교체를 닫을 수 있어 바꿈 · #1 기록으로 남김):
    // /// <summary>
    // /// 다음 기동 복구용(설계 §7 「⑤ 뒤 감사 · 끊기 전」) — SwapStarted 줄 중
    // /// 같은 OpId 로 끝 줄(SwapCompleted · SwapFailed · RecoveryResolved)이 아직 없는 것을 순번 차례로 돌려준다.
    // /// 깨진 줄은 「안 적힌 줄」 로 본다. 파일이 없으면 빈 목록.
    // /// </summary>
    // /// <param name="tenantId">null 이면 모든 회사. 값은 #2 대로 서버가 확정한 것만.</param>
    // Task<IReadOnlyList<LocalAuditEntry>> ReadUnfinishedSwapsAsync(string? tenantId = null, CancellationToken ct = default);

    /// <summary>
    /// 다음 기동 복구용(설계 §7 「⑤ 뒤 감사 · 끊기 전」) — <see cref="LocalAuditAction.SwapStarted"/> 줄 중
    /// 같은 (회사, <see cref="LocalAuditRecord.OpId"/>) 로 끝 줄(<see cref="LocalAuditAction.SwapCompleted"/> ·
    /// <see cref="LocalAuditAction.SwapFailed"/> · <see cref="LocalAuditAction.RecoveryResolved"/>)이 아직 없는 것을 순번 차례로 돌려준다
    /// (<see cref="LocalAuditOpenReport.Entries"/> = 그 묶음의 <c>SwapStarted</c> 줄).
    /// 🆕 고침 1 — ① 회사 열쇠 <b>필수</b>(X2 · 줄마다 대조) ② <b>지문 맞고 사슬 이어진 줄만</b> 믿는다(X3) — 못 믿는 줄은 목록에 넣지 않고
    /// <see cref="LocalAuditOpenReport.UntrustedLines"/> 로 센다 → 0 이 아니면 호출자는 「확인 필요」 로 다룬다(자동 회복 금지 · A 지시서).
    /// 깨진 조각은 「안 적힌 줄」. 파일 없음 · 빈 파일은 <see cref="LocalAuditOpenReport.FileState"/> 로 알린다.
    /// </summary>
    /// <param name="tenantId">🔴 필수 — #2 대로 서버가 확정한 회사 id. 비면 <see cref="ArgumentException"/>.</param>
    /// <exception cref="ArgumentException">🆕 고침 1 덧(짧은 재확인) — 회사 id 가 비었거나 id 글자 규칙 밖이면(호출자 코드 잘못).</exception>
    /// <exception cref="IOException">파일을 읽지 못함(다른 쪽이 공유 없이 오래 쥠 — 잠깐씩 기다려 다시 해도 안 됨 · 디스크 오류 ·
    ///   있음 확인과 열기 사이에 지워짐 = <see cref="FileNotFoundException"/> 포함). <b>읽기는 <see cref="LocalAuditWriteException"/> 로 감싸지 않는다</b>(쓰기만 감쌈).</exception>
    /// <exception cref="UnauthorizedAccessException">읽을 권한이 없음(보호 폴더 = SYSTEM · Administrators(· 폴더를 만든 실행 계정)만 — 그 밖 계정으로 돌면 여기서 걸림).</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> 가 취소됨(차례 기다리는 중 · 다시 읽기 전 대기 중).</exception>
    /// <remarks>🆕 고침 1 덧 — 파일이 처음부터 없으면 던지지 않고 <see cref="LocalAuditOpenReport.FileState"/> 로 알린다.
    ///   읽기는 폴더 · 파일 문지기(재분석 · 소유자 · 권한 줄 검사)를 돌리지 <b>않는다</b>(쓰기 걸음에서만 돎). 위 넷은 호출자가 삼키지 않는다.</remarks>
    Task<LocalAuditOpenReport> ReadUnfinishedSwapsAsync(string tenantId, CancellationToken ct = default);

    /// <summary>
    /// 🆕 고침 1(X4 · 설계 §7 첫 줄 「① ~ ④ 중 끊김 → 다음 기동 때 감사 「중단」 줄」) — 복원 묶음 중 <b>시작은 있고 끝이 없는</b> 것 전부.
    /// 시작 쪽 줄 = <see cref="LocalAuditAction.RestoreStarted"/> · <see cref="LocalAuditAction.PreBackupCreated"/> ·
    /// <see cref="LocalAuditAction.VerifyFailed"/> · <see cref="LocalAuditAction.SwapStarted"/>.
    /// 끝 줄 = <see cref="LocalAuditAction.SwapCompleted"/> · <see cref="LocalAuditAction.SwapFailed"/> · <see cref="LocalAuditAction.RecoveryResolved"/> ·
    /// <see cref="LocalAuditAction.RestoreAborted"/> · <see cref="LocalAuditAction.RestoreInterrupted"/>.
    /// (<c>VerifyFailed</c> 는 끝이 아니다 — 대조 실패 뒤에는 <c>RestoreAborted</c> 를 적어 닫는다.) 비번 되찾기 줄 · 표시 줄은 묶음이 아니다.
    /// <see cref="LocalAuditOpenReport.Entries"/> = 묶음마다 처음 줄. 교체가 시작됐는지는 <see cref="ReadUnfinishedSwapsAsync"/> 로 가른다
    /// (교체 시작 묶음 = <c>RecoveryResolved</c> 로 닫음 · 교체 전 끊김 = <c>RestoreInterrupted</c> 로 닫음).
    /// 회사 열쇠 · 믿는 줄 규칙은 <see cref="ReadUnfinishedSwapsAsync"/> 와 같다.
    /// </summary>
    /// <param name="tenantId">🔴 필수 — #2 대로 서버가 확정한 회사 id.</param>
    /// <exception cref="ArgumentException">🆕 고침 1 덧(짧은 재확인) — 회사 id 가 비었거나 id 글자 규칙 밖이면(호출자 코드 잘못).</exception>
    /// <exception cref="IOException">파일을 읽지 못함 — <see cref="ReadUnfinishedSwapsAsync"/> 와 같음(<see cref="LocalAuditWriteException"/> 로 감싸지 않음).</exception>
    /// <exception cref="UnauthorizedAccessException">읽을 권한이 없음 — <see cref="ReadUnfinishedSwapsAsync"/> 와 같음.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> 가 취소됨.</exception>
    Task<LocalAuditOpenReport> ReadOpenOperationsAsync(string tenantId, CancellationToken ct = default);

    /// <summary>
    /// 사슬 검사 — 처음부터 끝까지 지문을 다시 계산해 대 본다.
    /// ⬛ (고침 1 전) 파일이 없으면 통과(줄 0).
    /// 🆕 고침 1(X6) — 파일 없음 · 빈 파일(줄 0)은 <b>초록이 아니다</b>: <c>Ok=false</c> · <see cref="LocalAuditChainReport.State"/> =
    /// <see cref="LocalAuditFileState.Missing"/> / <see cref="LocalAuditFileState.Empty"/> · 까닭 <c>file_missing</c> / <c>file_empty</c>.
    /// (처음 설치 뒤 아직 한 줄도 안 쓴 상태와 「통째 지움」 은 파일만으로 못 가른다 → DB 보조와 맞대기 = A.)
    /// </summary>
    /// <exception cref="IOException">🆕 고침 1 덧(짧은 재확인) — 파일을 읽지 못함 — <see cref="ReadUnfinishedSwapsAsync"/> 와 같음(<see cref="LocalAuditWriteException"/> 로 감싸지 않음).</exception>
    /// <exception cref="UnauthorizedAccessException">읽을 권한이 없음 — <see cref="ReadUnfinishedSwapsAsync"/> 와 같음.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> 가 취소됨.</exception>
    /// <remarks>🆕 고침 1 덧 — 사슬이 틀린 것 · 파일 없음 · 빈 파일은 예외가 아니라 <c>Ok=false</c> 보고다. 읽기는 문지기를 돌리지 않는다(쓰기 걸음에서만).</remarks>
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
    /// <summary>🆕 고침 1(X4) — 「중단」 줄: 다음 기동이 교체 <b>전</b>(① ~ ④)에 끊긴 묶음을 닫음 · 같은 OpId · 결과 = Noted · 덧말 = 까닭 코드(설계 §7 첫 줄).</summary>
    RestoreInterrupted = 11,
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

/// <summary>🆕 고침 1(4 P9 · X9) — A · C · 검증이 같이 쓰는 고정 낱말.</summary>
public static class LocalAuditWords
{
    /// <summary>대표 user_id 를 서버가 찾지 못함(비번 되찾기 · 로그인 전) — 이 낱말 하나만 쓴다. <c>-</c> · 빈 값은 거절된다.</summary>
    public const string UnknownUser = "unknown";
}

/// <summary>호출자가 넘기는 한 줄 내용. 시각 · 순번 · 지문은 기록기가 붙인다.</summary>
/// <param name="TenantId">회사 id — 🔴 JWT 클레임 값만(#2).</param>
/// <param name="ParentUserId">대표(부모) user_id — 🔴 서버가 확정한 값만(#2 · #40). 비번 되찾기처럼 로그인 전이면 서버가 찾아 낸 id · 못 찾으면 <see cref="LocalAuditWords.UnknownUser"/>.</param>
/// <param name="Action">동작.</param>
/// <param name="Result">결과.</param>
/// <param name="OpId">한 번의 복원 · 되찾기를 묶는 id(호출자가 만듦 · 예: Guid "N"). 시작 줄과 끝 줄이 같은 값을 쓴다.</param>
/// <param name="Detail">짧은 덧말(없으면 null) — 글자 · 숫자 · <c>. _ - : + =</c> 만 · 200자 이하.</param>
/// <remarks>id 3칸 = 영문 · 숫자 · <c>- _</c> 64자 이하 · 🆕 고침 1: 글자 · 숫자가 하나도 없는 값(<c>-</c> 등)은 거절(기록기 표시 줄 자리 값과 겹침).</remarks>
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

/// <summary>🆕 고침 1(X6) — 감사 파일 자체의 상태. 파일 없음 · 빈 파일은 「줄 0 초록」 과 다르다.</summary>
public enum LocalAuditFileState
{
    /// <summary>파일이 없다.</summary>
    Missing = 1,
    /// <summary>파일은 있으나 읽을 줄이 0(0 바이트 · 끊긴 조각뿐).</summary>
    Empty = 2,
    /// <summary>읽을 줄이 1 이상.</summary>
    Present = 3,
}

/// <summary>사슬 검사 결과.</summary>
/// <param name="Ok">사슬이 끊긴 곳 없음 · 🆕 고침 1: 그리고 읽을 줄이 1 이상(<see cref="State"/> = Present).</param>
/// <param name="ValidLines">⬛ (고침 1 전 주석) 지문이 맞은 줄 수. 🆕 고침 1(4 발견 6 · 구현과 같게) = <b>처음 틀린 줄 앞까지</b> 지문이 맞은 줄 수(표시 줄 포함 · 깨진 조각 제외). 통과면 전체 줄 수.</param>
/// <param name="FirstBadLineNumber">처음 틀린 줄 번호(파일 줄 기준 1부터) · 통과면 null.</param>
/// <param name="Reason">틀린 까닭(짧은 코드) · 통과면 null. 🆕 고침 1 더함: <c>tail_garbage</c> · <c>file_missing</c> · <c>file_empty</c>.</param>
/// <param name="TailTorn">맨 끝이 기록기가 쓰다 끊긴 꼴로 깨져 있음(전원 끊김 흔적 · 다음 쓰기가 닫음). 이것만으로는 빨강 아님.</param>
public sealed record LocalAuditChainReport(
    bool Ok,
    int ValidLines,
    int? FirstBadLineNumber,
    string? Reason,
    bool TailTorn)
{
    /// <summary>🆕 고침 1(X6) — 파일 상태. 기본 = Present(옛 생성 꼴 그대로 · #1 더하기만).</summary>
    public LocalAuditFileState State { get; init; } = LocalAuditFileState.Present;

    /// <summary>🆕 고침 1(X5) — 표시 줄로 설명된 끊긴 조각 수(전원 끊김 횟수 · DB 맞대기 때 대 볼 값).</summary>
    public int ExplainedTornLines { get; init; }
}

/// <summary>🆕 고침 1(X2 · X3 · X4) — 미완 묶음 읽기 결과.</summary>
/// <param name="FileState">파일 상태(없음 · 빈 파일이면 <paramref name="Entries"/> 0).</param>
/// <param name="Entries">믿는 줄로만 고른 미완 묶음(묶음마다 한 줄 · 순번 차례).</param>
/// <param name="UntrustedLines">못 믿어 목록에서 뺀 줄 수 — 지문 틀림 · 사슬 끊김 · 순번 틈 · 설명 없는 쓰레기 줄(회사 상관없이 파일 전체). 0 이 아니면 「확인 필요」.</param>
/// <param name="UntrustedReason">처음 못 믿은 까닭 코드 · 0 이면 null.</param>
public sealed record LocalAuditOpenReport(
    LocalAuditFileState FileState,
    IReadOnlyList<LocalAuditEntry> Entries,
    int UntrustedLines,
    string? UntrustedReason);

/// <summary>
/// 🆕 고침 1(X7 · 3-V F5) — 감사 줄을 쓰지 못함. 쓰기 실패는 이 예외 하나로만 던진다.
/// </summary>
/// <remarks>
/// 까닭 코드(<see cref="Reason"/>): <c>folder_reparse_point</c> · <c>folder_owner</c> · <c>folder_acl</c> · <c>file_reparse_point</c> · <c>file_owner</c> ·
/// <c>file_acl</c> · <c>busy</c>(다른 쪽이 오래 쥠) · <c>io</c> · <c>access_denied</c> · <c>write_failed</c> · <c>flush_failed_line_present</c>.
/// 문구에 경로 값을 넣지 않는다(고객 화면에 그대로 나갈 수 있음).
/// </remarks>
public sealed class LocalAuditWriteException : Exception
{
    /// <summary>만든다.</summary>
    public LocalAuditWriteException(string reason, string message, bool lineWritten = false, Exception? inner = null)
        : base(message, inner)
    {
        Reason = reason;
        LineWritten = lineWritten;
    }

    /// <summary>까닭 코드(짧은 영문 · 위 목록).</summary>
    public string Reason { get; }

    /// <summary>
    /// 줄이 파일에 남았는가(디스크 내려쓰기에서 실패한 뒤 다시 읽어 확인한 사실). true 면 다시 쓰지 말 것(같은 OpId 줄 두 개) ·
    /// 다만 디스크까지 내려갔다는 보장은 없다 → 호출자는 멈춤(fail-closed)으로 다룬다.
    /// </summary>
    public bool LineWritten { get; }
}
