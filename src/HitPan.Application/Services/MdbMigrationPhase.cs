namespace HitPan.Application.Services;

/// <summary>
/// 🔴 <b>이관 단계 — 작22 (2026-09-09) A3</b>
///
/// <para>
/// 5/16 사장님 UX: <i>"1단계 이관 끝나면 '1단계자료 이관완료. 2단계 자료 이관 계속 하시겠습니까?' 라는 메시지가 뜨면 좋을 것 같아."</i><br/>
/// 그래서 한 번의 이관을 <b>마스터(1단계)</b> 와 <b>거래(2단계)</b> 로 가른다. 종전 호출자(baseline 도구 · 동기 엔드포인트)는
/// <see cref="All"/> 로 종전과 똑같이 한 번에 돈다.
/// </para>
/// </summary>
public enum MdbMigrationPhase
{
    /// <summary>종전 동작 — 창고 → 마스터 → 거래 → 재고 리빌드를 한 번에.</summary>
    All = 0,

    /// <summary>1단계만 — 기본창고 확보 + PYOJUN 마스터(업체·상품·BOM·사원·계정과목). 끝나면 잡은 <c>paused</c>.</summary>
    MasterOnly = 1,

    /// <summary>
    /// 2단계 — PANDATA 병렬 + POTHER + item_stock 리빌드.
    /// ⚠️ 거래 잡은 1단계가 메모리에 채운 partnerMap/itemMap/employeeMap 을 읽어야 FK 를 잇는다.
    /// 그래서 이 단계도 창고·마스터 스텝을 <b>지나가되</b>, 마스터는 <b>쓰지 않고 읽어서 매핑만 되살린다</b>(map-only).
    /// </summary>
    TransactionsOnly = 2,
}

/// <summary>
/// 🔴 <b>이관 모드 어휘 — 작22 (2026-09-09) A2</b> · 9/4 사장님 오더 6 <i>"마이그레이션 옵션이 두개가 되어야 함. 1덮어쓰기, 2병합"</i>.
///
/// <para>
/// 서비스 안에서 <c>null</c> 은 <b>"모드 미지정 = 종전 동작"</b> 이다(baseline 도구 · 동기 엔드포인트가 그렇게 부른다).
/// 화면 요청 DTO 의 기본값은 <see cref="Merge"/>.
/// ⛔ <see cref="Overwrite"/> 의 본체(백업→초기화→뼈대 재시드→이관)는 사장님 Q1 답 전이라 **받기만 하고 400** 으로 막는다.
/// </para>
/// </summary>
public static class MdbMigrationModes
{
    public const string Merge = "merge";
    public const string Overwrite = "overwrite";

    /// <summary>화면·API 가 보낼 수 있는 값인가. 비어 있으면 미지정으로 본다(허용).</summary>
    public static bool IsValid(string? mode)
        => string.IsNullOrWhiteSpace(mode)
           || string.Equals(mode, Merge, StringComparison.OrdinalIgnoreCase)
           || string.Equals(mode, Overwrite, StringComparison.OrdinalIgnoreCase);

    public static bool IsMerge(string? mode)
        => string.Equals(mode, Merge, StringComparison.OrdinalIgnoreCase);

    public static bool IsOverwrite(string? mode)
        => string.Equals(mode, Overwrite, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 잡 세션의 <c>unique_checks</c> 값 — 선행검증 §2-8: <c>unique_checks=0</c> 은 InnoDB 가 보조 UNIQUE 검사를 미룰 수 있어
    /// <c>INSERT IGNORE</c> 멱등과 이론상 충돌한다. <b>병합(기존 자료 위에 얹는 모드)만 1</b>, 덮어쓰기(빈 표)·미지정(종전)은 0 유지.
    /// </summary>
    public static int UniqueChecksFor(string? mode) => IsMerge(mode) ? 1 : 0;
}

/// <summary>
/// 🔴 <b>체크포인트 규칙 — 작22 (2026-09-09) A3 · 설계 별지 §1-4</b>
///
/// <para>
/// <c>RunTableStepAsync</c> 는 jobId 가 있을 때만 <c>migration_checkpoints</c> 를 쓰고, 이미 <c>done</c> 인 표는
/// 콜백 <c>skipped</c> 를 보내고 건너뛴다. 단 아래 표는 <b>항상 다시 돈다</b>:
/// <list type="bullet">
/// <item><c>item_stock_rebuild</c> — 원장 합산이라 마지막에 항상 다시 계산해야 맞는다(별지 §1-4 명시 예외).</item>
/// <item><c>warehouse_migration</c> — 결과가 메모리 값(defaultWarehouseId)이라 건너뛰면 2단계가 창고를 모른다. 멱등(있으면 찾기만).</item>
/// <item><c>pyojun_master</c> — partnerMap/itemMap/employeeMap 을 메모리에 채우는 자리. 2단계(<see cref="MdbMigrationPhase.TransactionsOnly"/>)에선
///   쓰지 않고 읽기만 하는 map-only 로 지나간다.</item>
/// </list>
/// </para>
/// </summary>
public static class MdbMigrationCheckpointPolicy
{
    private static readonly HashSet<string> AlwaysRerunTables = new(StringComparer.Ordinal)
    {
        "item_stock_rebuild",
        "warehouse_migration",
        "pyojun_master",
    };

    /// <summary>done 이어도 다시 도는 표인가.</summary>
    public static bool AlwaysRerun(string tableName) => AlwaysRerunTables.Contains(tableName);
}
