using System.Text.RegularExpressions;

namespace HitPan.Application.Services;

/// <summary>교체 표 한 개 — 이름 · 붓는 칸(생성 칸 뺌) · 회사 표지 칸 있음.</summary>
public sealed record RestoreSwapTable(string Name, IReadOnlyList<string> Columns, bool HasTenantId);

/// <summary>⑤ 교체 차례 — 지우기(자식 먼저) · 붓기(부모 먼저). 순수 함수(DB 0) — 정적 게이트가 직접 잰다.</summary>
public sealed record RestoreSwapOrder(IReadOnlyList<string> DeleteStatements, IReadOnlyList<string> InsertStatements);

/// <summary>
/// 🔴 W-G1-52 갈래 A-2③ — ⑤ 교체 문장 만들기(본 §15-3 ⑤ · 외래키 그래프 순서 · <c>TRUNCATE</c> 금지 R12).
/// </summary>
public static class RestoreSwapPlan
{
    /// <summary>
    /// 외래키 그래프 차례로 문장을 만든다. <paramref name="edges"/> = (자식, 부모) — 둘 다 교체 표일 때만 차례에 씀.
    /// 고리(자기 참조 포함 · G13 ⓓ)가 있으면 <c>null</c>(차례가 서지 않음 → 멈춤).
    /// </summary>
    public static RestoreSwapOrder? Build(IReadOnlyList<RestoreSwapTable> tables, IEnumerable<(string Child, string Parent)> edges)
    {
        ArgumentNullException.ThrowIfNull(tables);
        var names = tables.Select(t => t.Name).ToList();
        foreach (var t in tables)
        {
            if (!RestoreSwapSql.PlainName.IsMatch(t.Name) || t.Columns.Count == 0 || t.Columns.Any(c => !RestoreSwapSql.PlainName.IsMatch(c)))
                return null;
        }
        var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var parents = names.ToDictionary(n => n, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        foreach (var (child, parent) in edges)
        {
            if (!set.Contains(child) || !set.Contains(parent)) continue;
            if (string.Equals(child, parent, StringComparison.OrdinalIgnoreCase)) return null;   // 자기 참조 = 고리(R10)
            parents[child].Add(parent);
        }

        // 부모 먼저(칸 · 이름 서수 차례로 늘 같은 결과)
        var order = new List<string>();
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = names.OrderBy(n => n, StringComparer.Ordinal).ToList();
        while (pending.Count > 0)
        {
            var ready = pending.Where(n => parents[n].All(done.Contains)).ToList();
            if (ready.Count == 0) return null;   // 고리
            foreach (var r in ready) { order.Add(r); done.Add(r); }
            pending = pending.Where(n => !done.Contains(n)).ToList();
        }

        var byName = tables.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        var deletes = order.AsEnumerable().Reverse().Select(n => DeleteSql(byName[n])).ToList();
        var inserts = order.Select(n => InsertSql(byName[n])).ToList();
        return new RestoreSwapOrder(deletes, inserts);
    }

    /// <summary>지우기 — 이 회사(+ 회사 표지 빈 줄) 만 · 회사 표지 칸이 없는 표는 통째(한 PC 한 회사 · ④ 에서 「라이브도 이 회사 표지뿐」 확인 뒤).</summary>
    public static string DeleteSql(RestoreSwapTable t) =>
        t.HasTenantId
            ? $"DELETE FROM `{t.Name}` WHERE (tenant_id = @t OR tenant_id IS NULL)"
            : $"DELETE FROM `{t.Name}`";

    /// <summary>붓기 — <c>INSERT INTO 표(칸) SELECT 칸 FROM rs_new__표</c>(자동 번호 그대로 · 생성 칸 뺌).</summary>
    public static string InsertSql(RestoreSwapTable t)
    {
        var cols = string.Join(", ", t.Columns.Select(c => $"`{c}`"));
        return $"INSERT INTO `{t.Name}` ({cols}) SELECT {cols} FROM `{RestoreTableClassification.StagingPrefix}{t.Name}`";
    }
}

/// <summary>⑤ 거래 안 문장이 허용 목록 밖이라 멈춤(되돌림).</summary>
public sealed class RestoreSwapRejectedException : Exception
{
    public RestoreSwapRejectedException(string message) : base(message) { }
}

/// <summary>
/// 🔴 W-G1-52 갈래 A-2③ — ⑤ 거래 안 <b>문장 허용 검사</b>(본 §15-3 ⑤ R5: 거래 안 = <c>DELETE</c> · <c>INSERT … SELECT</c> · <c>restore_history</c> 쓰기뿐 ·
/// DDL 한 줄이라도 끼면 암묵 커밋 → 반쪽 영구 · <c>TRUNCATE</c> 금지). C1′ 잠그며 읽기 · 격리 수준 읽기는 <b>이름으로</b> 안다(보1 덧1-3 C1).
/// </summary>
public static class RestoreSwapStatementCheck
{
    private static readonly Regex DeleteRx = new(
        @"^DELETE FROM `(?<t>[A-Za-z0-9_]+)`(?: WHERE \(tenant_id = @t OR tenant_id IS NULL\))?$", RegexOptions.CultureInvariant);

    private static readonly Regex InsertRx = new(
        @"^INSERT INTO `(?<t>[A-Za-z0-9_]+)` \((?<c>`[A-Za-z0-9_]+`(?:, `[A-Za-z0-9_]+`)*)\) SELECT (?<c2>`[A-Za-z0-9_]+`(?:, `[A-Za-z0-9_]+`)*) FROM `rs_new__(?<s>[A-Za-z0-9_]+)`$",
        RegexOptions.CultureInvariant);

    /// <summary>이름으로 아는 읽기 문장(쓰기 아님 · DDL 아님).</summary>
    public static IReadOnlyList<string> KnownReads { get; } =
    [
        RestoreSwapSql.ReadIsolation,
        RestoreSwapSql.C1EmployeesDiff, RestoreSwapSql.C1EmployeesBoth, RestoreSwapSql.C1EmployeesLiveOnly, RestoreSwapSql.C1EmployeesNewerLinked,
        RestoreSwapSql.C1ChatDiff, RestoreSwapSql.C1ChatBoth, RestoreSwapSql.C1ChatLiveOnly,
    ];

    /// <summary>허용 목록 밖이면 <see cref="RestoreSwapRejectedException"/>. 지우기 · 붓기 대상은 「백업 값」 표만(지금 값 · 임시 표 0).</summary>
    public static void Verify(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        if (KnownReads.Contains(sql, StringComparer.Ordinal)) return;
        if (string.Equals(sql, RestoreSwapSql.MarkHistorySuccess, StringComparison.Ordinal)) return;
        if (sql.Contains(';') || sql.Contains("--", StringComparison.Ordinal) || sql.Contains("/*", StringComparison.Ordinal) || sql.Contains('#'))
            throw new RestoreSwapRejectedException("거래 안 문장에 둘째 문장 · 주석 자리");

        var d = DeleteRx.Match(sql);
        if (d.Success)
        {
            RequireBackupTable(d.Groups["t"].Value);
            return;
        }
        var i = InsertRx.Match(sql);
        if (i.Success)
        {
            var t = i.Groups["t"].Value;
            RequireBackupTable(t);
            if (!string.Equals(t, i.Groups["s"].Value, StringComparison.Ordinal))
                throw new RestoreSwapRejectedException("붓기 원본이 같은 이름 임시 표가 아님");
            if (!string.Equals(i.Groups["c"].Value, i.Groups["c2"].Value, StringComparison.Ordinal))
                throw new RestoreSwapRejectedException("붓기 칸 목록이 짝이 아님");
            return;
        }
        throw new RestoreSwapRejectedException("거래 안 허용 목록 밖 문장");
    }

    private static void RequireBackupTable(string name)
    {
        if (name.StartsWith(RestoreTableClassification.StagingPrefix, StringComparison.OrdinalIgnoreCase)
            || RestoreTableClassification.Classify(name) != RestoreTableClass.FromBackup)
            throw new RestoreSwapRejectedException("교체 대상이 「백업 값」 표가 아님");
    }
}
