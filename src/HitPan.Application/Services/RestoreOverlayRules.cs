using HitPan.Application.DTOs.Backup;

namespace HitPan.Application.Services;

/// <summary>
/// 🔴 W-G1-52 갈래 A-2③ — 걸음 ④-끝 「권한 칸 덧씌움」 · ⓑ′ · C1′ 이 함께 쓰는 <b>상수 한 곳</b>
/// (작업지시서 <c>20261128작_W-G1-52_갈래A2</c> §4-1 A-2③ 줄 · 88-89행 N1-b · 보1 §2-2 · 덧1-1 D1 · 덧2-1 · 덧2-2 · 덧2-3 · §11 D1).
/// </summary>
/// <remarks>
/// <para><b>글자 복사 0</b>: ④ 의 「ⓑ 후보」 · ④-끝 ③ 의 ⓑ′ 확정 · ⑤ C1′ ⓓ 는 모두 <see cref="LiveOnlyActiveLinkedWhere"/> 와
/// <see cref="BPrimeExemption"/> 을 부른다(어긋나면 ③ 통과 · ⑤ 멈춤 = 변이로 잼). <see cref="ActiveAccount"/> 도 같은 상수 하나
/// (본 §15-5 ③ ⓑ 「활성 계정」 = 덧2-4 ② 「계정 살아 있음 조각(셋)」).</para>
/// <para>칸 이름 = 출하 DDL(<c>installer/hitpan_db_clean.sql</c> · #13) — <c>employees</c> :1149 · <c>chat_room_members</c> :4863 ·
/// <c>users</c> :4380. 실재 확인 = G13 덧(DB 게이트 <c>information_schema</c>) · 비DB 정적 대조(<c>SafeRestoreStaticGateTests</c>).</para>
/// <para>모든 비교는 <c>&lt;=&gt;</c>(C6) — 이 파일 · 덧씌움 SQL 안 <c>!=</c> · <c>&lt;&gt;</c> 0(정적 게이트).</para>
/// </remarks>
public static class RestoreOverlayRules
{
    /// <summary>사원 표(백업 값 · 덧씌움 대상).</summary>
    public const string EmployeesTable = "employees";

    /// <summary>메신저 방 참여 표(백업 값 · 덧씌움 대상).</summary>
    public const string ChatMembersTable = "chat_room_members";

    /// <summary>사원 맞대는 열쇠(C6 — <c>tenant_id</c> 함께 · 보1 §2-5 ⓑ 판정 열쇠 = <c>employee_id</c>).</summary>
    public static readonly string[] EmployeeKey = ["tenant_id", "employee_id"];

    /// <summary>
    /// 사원 덧씌움 7칸(D1 · 보1 덧1-1) — 이 칸만 「지금 값」 · 나머지(이름 · 부서 · 급여 · 연차 …) = 백업 값.
    /// <c>delegate_id</c> 는 넣지 않음(C5 · 덧씌움 0).
    /// </summary>
    public static readonly string[] EmployeeOverlayColumns =
        ["role", "is_active", "user_id", "login_id", "is_resigned", "resign_date", "resign_reason"];

    /// <summary>방 참여 맞대는 열쇠(C6 · 유일 키 <c>uq_chat_member(room_id, employee_id)</c> + <c>tenant_id</c>).</summary>
    public static readonly string[] ChatMemberKey = ["tenant_id", "room_id", "employee_id"];

    /// <summary>방 참여 덧씌움 1칸(D1).</summary>
    public static readonly string[] ChatMemberOverlayColumns = ["left_at"];

    /// <summary>퇴사 계정 표식(<c>EmployeeService</c> 퇴사 · <c>EmployeeLoginIdSync</c> :25-47 와 같은 글자).</summary>
    public const string ResignedMarker = "resigned+";

    /// <summary>폐기 계정 표식(<c>UserService</c> :606-617 과 같은 글자).</summary>
    public const string RetiredMarker = "retired+";

    /// <summary>덧씌움 칸 수 = 8(사원 7 + 방 참여 1 · Z1).</summary>
    public static int OverlayColumnCount => EmployeeOverlayColumns.Length + ChatMemberOverlayColumns.Length;

    /// <summary>
    /// <c>&lt;활성 계정 조각&gt;</c> — 「계정 살아 있음」 셋(지우지 않음 · 켜짐 · 퇴사 · 폐기 표식 아님 · 덧2-4 ② · 덧2-9 ②).
    /// 별칭 <paramref name="u"/> = <c>users</c>. 퇴사 · 폐기는 셋을 한 문장에 함께 쓰므로(<c>EmployeeService.cs</c> :733-748) 하나만 빼면 변이가 살아남는다 → 셋 다.
    /// </summary>
    public static string ActiveAccount(string u) =>
        $"{u}.is_deleted = 0 AND {u}.is_active = 1 AND {u}.email NOT LIKE '{ResignedMarker}%' AND {u}.email NOT LIKE '{RetiredMarker}%'";

    /// <summary>
    /// 「라이브에만 있고 활성 계정과 이은 사원 행」(본 §15-5 ③ ⓑ · 보1 §2-5 ⓑ — 열쇠 <c>employee_id</c>). 별칭 <paramref name="l"/> = 라이브 <c>employees</c>.
    /// 회사 = <c>@t</c> 하나(#2 · CTO N1 §3 「<c>@t</c> 외 tenant 값 0」).
    /// </summary>
    public static string LiveOnlyActiveLinkedWhere(string l) =>
        $"{l}.tenant_id = @t AND {l}.user_id IS NOT NULL"
        + $" AND NOT EXISTS (SELECT 1 FROM rs_new__employees nx WHERE nx.tenant_id = {l}.tenant_id AND nx.employee_id = {l}.employee_id)"
        + $" AND EXISTS (SELECT 1 FROM users ux WHERE ux.tenant_id = {l}.tenant_id AND ux.user_id = {l}.user_id AND {ActiveAccount("ux")})";

    /// <summary>
    /// 🔴 <b>ⓑ′ 단서</b>(N1 · 보1 덧2-2 · CTO N1 재확인 §3) — 라이브에만 있는 행 L 의 계정 U 를 ⓐ″ 뒤 <c>rs_new__employees</c> 의 한 행이 가지면 멈춤 대상에서 뺀다.
    /// <c>n2.is_active = 1</c> 을 <b>더하지 않음</b>(CTO ⚪1 — 근거 = 「복원 뒤 U 의 연결 = 백업 때 연결」).
    /// 「같은 U」 = <c>n2.user_id = l.user_id</c> · 같은 회사 = <c>n2.tenant_id = l.tenant_id</c>(둘 중 하나라도 빠지면 반려).
    /// </summary>
    public static string BPrimeExemption(string l) =>
        $"NOT EXISTS (SELECT 1 FROM rs_new__employees n2 WHERE n2.tenant_id = {l}.tenant_id AND n2.user_id = {l}.user_id)";

    /// <summary>ⓑ′ 를 실은 멈춤 판정(④-끝 ③ 확정 · ⑤ C1′ ⓓ) — 같은 두 조각을 잇기만 한다.</summary>
    public static string LiveOnlyActiveLinkedAfterBPrime(string l) =>
        LiveOnlyActiveLinkedWhere(l) + " AND " + BPrimeExemption(l);

    // ══ P3-9 고정 문구 표 — 응답 Message 는 여기서만 채운다 ══
    //   restore_history.error_message · 예외 글 · LogDetail 을 Message 로 옮기지 않는다(개발 용어 · 경로 · 자료 값 0).
    //   글은 서버 응답 문구(화면은 B 갈래가 그대로 보여 줌) — 바꾸려면 B · 대표 몫.

    /// <summary>D1(대표 결재 11/28 13:49) — 남는 멈춤(초기화 뒤 새로 만든 계정 · N1-b) 안내. {0} = 계정 수 · {1} = 이름들.</summary>
    public const string NewerAccountsMissingTemplate =
        "초기화 뒤에 만든 직원 계정 {0}개({1})가 그 백업에 없습니다 — 그 계정을 [멈춤] 한 뒤 다시 되살리면 됩니다";

    /// <summary>안내에 이름을 몇 명까지 적나(넘으면 「외 n명」).</summary>
    public const int NewerAccountsNameLimit = 5;

    /// <summary>⑤ C1′ 재대조가 어긋남(보1 덧1-3 C1 글 그대로).</summary>
    public const string DataChangedMessage = "자료가 바뀌었습니다 — 다시 시도해 주세요";

    /// <summary>D4 · D13 — 자료이관 · 업데이트가 도는 중.</summary>
    public const string BusyMigrationMessage = "자료 옮기기가 끝난 뒤 다시 되살려 주세요";

    /// <summary>D13 — 업데이트가 도는 중.</summary>
    public const string BusyUpdateMessage = "업데이트가 끝난 뒤 다시 되살려 주세요";

    /// <summary>경계 2표(<c>ai_conversations</c> · <c>esign_records</c> → <c>users</c>) 고아(본 §15-3 「경계 외래키 2」 글 그대로).</summary>
    public const string MissingAccountDataMessage = "백업 뒤 지운 계정의 자료가 있어 되살릴 수 없습니다";

    /// <summary>D5 — 닫힘(옛 <c>BackupService.RestoreClosedNotice</c> 와 같은 글).</summary>
    public const string ClosedMessage = "복원은 안전장치를 보강한 다음 업데이트에서 열립니다.";

    /// <summary>A-2③ 고침 1 P3-2 — 되살리는 도중 프로그램이 다시 시작됨(기동 청소 · 자료 그대로 · 무엇을 하면 되는지 말함).</summary>
    public const string InterruptedMessage = "되살리는 도중 프로그램이 다시 시작되어 멈췄습니다 — 자료는 바뀌지 않았습니다. 다시 되살려 주세요";

    /// <summary>표에 없는 글자가 왔을 때(받은 글자를 되풀이하지 않음).</summary>
    public const string UnknownMessage = "되살리지 못했습니다 — 자료는 바뀌지 않았습니다";

    /// <summary>
    /// DenyCode → 고정 문구(P3-9). 모든 <see cref="SafeRestoreDenyCodes"/> 글자가 한 줄씩 있어야 한다(정적 게이트가 반사로 셈).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Messages = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [SafeRestoreDenyCodes.NotParent] = "대표 계정만 할 수 있습니다",
        [SafeRestoreDenyCodes.WrongPassword] = "비밀번호가 맞지 않습니다",
        [SafeRestoreDenyCodes.LockedOut] = "비밀번호를 여러 번 틀려 잠시 막혔습니다 — 잠시 뒤 다시 해 주세요",
        [SafeRestoreDenyCodes.CompanyMismatch] = "회사 이름이 맞지 않습니다",
        [SafeRestoreDenyCodes.NotAcknowledged] = "위 내용을 확인했다는 표시를 해 주세요",
        [SafeRestoreDenyCodes.AlreadyRunning] = "이미 되살리는 중입니다 — 끝난 뒤 다시 해 주세요",
        [SafeRestoreDenyCodes.BackupNotInList] = "이 백업은 목록에 없어 되살릴 수 없습니다",
        [SafeRestoreDenyCodes.FingerprintMismatch] = "이 백업 파일은 만든 뒤 바뀌어 되살릴 수 없습니다",
        [SafeRestoreDenyCodes.VersionMismatch] = "지금 프로그램과 다른 판의 백업이라 되살릴 수 없습니다",
        [SafeRestoreDenyCodes.UnclassifiedTable] = "되살리지 못했습니다 — 자료는 바뀌지 않았습니다",
        [SafeRestoreDenyCodes.PreBackupFailed] = "지금 상태를 먼저 백업하지 못해 멈췄습니다 — 자료는 바뀌지 않았습니다",
        [SafeRestoreDenyCodes.MaintenanceDrainTimeout] = "다른 직원의 작업이 끝나지 않아 멈췄습니다 — 잠시 뒤 다시 해 주세요",
        [SafeRestoreDenyCodes.StageFailed] = "백업 파일을 읽지 못해 멈췄습니다 — 자료는 바뀌지 않았습니다",
        [SafeRestoreDenyCodes.OtherCompanyBackup] = "다른 회사의 백업이라 되살릴 수 없습니다",
        [SafeRestoreDenyCodes.VerifyFailed] = "백업을 확인하다 멈췄습니다 — 자료는 바뀌지 않았습니다",
        [SafeRestoreDenyCodes.NewerAccountsMissing] = NewerAccountsMissingTemplate,
        [SafeRestoreDenyCodes.SwapFailed] = "되살리지 못했습니다 — 자료는 바뀌지 않았습니다",
        [SafeRestoreDenyCodes.AuditWriteFailed] = "기록을 남기지 못해 멈췄습니다 — 자료는 바뀌지 않았습니다",
        [SafeRestoreDenyCodes.CheckNeeded] = "확인이 필요합니다 — 다시 로그인한 뒤 결과를 확인해 주세요",
        [SafeRestoreDenyCodes.RestoreClosed] = ClosedMessage,
        [SafeRestoreDenyCodes.MigrationRunning] = BusyMigrationMessage,
        [SafeRestoreDenyCodes.UpdateRunning] = BusyUpdateMessage,
        [SafeRestoreDenyCodes.RestoreInterrupted] = InterruptedMessage,   // A-2③ 고침 1 P3-2
    };

    /// <summary>
    /// 응답 문구 고르기(P3-9) — 표에서만 고른다. <paramref name="names"/> 는 <see cref="SafeRestoreDenyCodes.NewerAccountsMissing"/> 일 때만 쓰임(D1 · 로컬 계정 이름).
    /// <paramref name="dataChanged"/> = ⑤ C1′ 재대조 어긋남(같은 <see cref="SafeRestoreDenyCodes.VerifyFailed"/> 라도 「다시 시도」 글).
    /// <paramref name="boundaryOrphan"/> = 경계 2표 고아.
    /// </summary>
    public static string MessageFor(string? denyCode, IReadOnlyList<string>? names = null, bool dataChanged = false, bool boundaryOrphan = false)
    {
        if (denyCode is null) return UnknownMessage;
        if (dataChanged) return DataChangedMessage;
        if (boundaryOrphan) return MissingAccountDataMessage;
        if (string.Equals(denyCode, SafeRestoreDenyCodes.NewerAccountsMissing, StringComparison.Ordinal))
        {
            var list = names ?? Array.Empty<string>();
            var shown = string.Join(", ", list.Take(NewerAccountsNameLimit));
            if (list.Count > NewerAccountsNameLimit) shown += $" 외 {list.Count - NewerAccountsNameLimit}명";
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, NewerAccountsMissingTemplate, list.Count, shown);
        }
        return Messages.TryGetValue(denyCode, out var m) ? m : UnknownMessage;
    }
}
