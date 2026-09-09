namespace HitPan.Application.Services;

/// <summary>빈칸 판정 — 컬럼 타입에 따라 "비어 있다" 의 모양이 다르다 (DESCRIBE 근거는 <see cref="MdbMasterMergeClause"/> 주석).</summary>
public enum MdbMergeBlank
{
    /// <summary>문자 컬럼 — NULL 또는 '' 가 빈칸.</summary>
    Text = 0,

    /// <summary>금액·율 컬럼(decimal NOT NULL DEFAULT 0.00 계열) — NULL 또는 0 이 빈칸. 0 은 "안 정함" 의 표현이다.</summary>
    Number = 1,

    /// <summary>날짜·플래그·이진 컬럼 — NULL 만 빈칸. 0/'' 비교가 의미가 없거나 유효값과 겹친다.</summary>
    NullOnly = 2,
}

/// <summary>갱신 대상 컬럼 하나 — 이름 + 빈칸 판정 방식.</summary>
public readonly record struct MdbMergeColumn(string Column, MdbMergeBlank Blank);

/// <summary>
/// 🔴 <b>마스터 UPSERT 갱신 절 조립 — 작22 (2026-09-09) A2 · 설계 별지 §1-2</b>
///
/// <para>
/// 종전 <c>ON DUPLICATE KEY UPDATE col = VALUES(col)</c> 는 <b>레거시가 ERP 를 덮는다</b>(G-MH 반증).
/// 병합 모드에선 <b>빈칸만 채운다</b> — <c>col = COALESCE(NULLIF(col, ''), VALUES(col))</c>.
/// 덮어쓰기·모드 미지정은 종전 절 그대로.
/// SQL 문자열을 두 벌 복사하지 않고 <b>갱신 절만</b> 모드에 따라 조립한다(작업지시서 갈래 A2).
/// </para>
///
/// <para>
/// 🔴 <b>왜 순수 static 인가</b> — 게이트(W16)가 값으로 불러 병합/덮어쓰기 절이 정말 갈리는지 본다(글자검사 금지).
/// </para>
/// </summary>
public static class MdbMasterMergeClause
{
    /// <summary>
    /// 컬럼 하나의 갱신식.
    /// </summary>
    public static string Assignment(string? mode, MdbMergeColumn column)
    {
        if (!MdbMigrationModes.IsValid(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "이관 모드는 merge · overwrite · 미지정 중 하나다.");
        }

        var c = column.Column;
        if (!MdbMigrationModes.IsMerge(mode))
        {
            // 덮어쓰기 · 미지정(종전 동작): 레거시 값으로 갱신 — 5/14 사장님 지시 "같은 MDB 재마이그 시 덮어쓰기" 그대로.
            return $"{c} = VALUES({c})";
        }

        // 병합: ERP 에 값이 있으면 두고, 빈칸일 때만 레거시 값을 채운다.
        return column.Blank switch
        {
            MdbMergeBlank.Text => $"{c} = COALESCE(NULLIF({c}, ''), VALUES({c}))",
            MdbMergeBlank.Number => $"{c} = COALESCE(NULLIF({c}, 0), VALUES({c}))",
            MdbMergeBlank.NullOnly => $"{c} = COALESCE({c}, VALUES({c}))",
            _ => throw new ArgumentOutOfRangeException(nameof(column), column.Blank, "빈칸 판정 종류가 아니다."),
        };
    }

    /// <summary>
    /// <c>ON DUPLICATE KEY UPDATE</c> 뒤에 붙는 전체 절.
    /// </summary>
    /// <param name="mode">"merge" 면 빈칸채움 · 그 밖(null/"overwrite")은 종전 덮어쓰기.</param>
    /// <param name="userColumns">사용자가 ERP 에서 고칠 수 있는 컬럼 — 모드를 탄다.</param>
    /// <param name="alwaysColumns">모드와 무관하게 항상 최신값으로 갱신하는 컬럼(<c>updated_at</c> · <c>migrated_source_hash</c>).</param>
    public static string Build(string? mode, IReadOnlyList<MdbMergeColumn> userColumns, IReadOnlyList<string> alwaysColumns)
    {
        var parts = new List<string>(userColumns.Count + alwaysColumns.Count);
        foreach (var c in userColumns) parts.Add(Assignment(mode, c));
        foreach (var a in alwaysColumns) parts.Add($"{a} = VALUES({a})");
        return string.Join(",\n              ", parts);
    }
}
