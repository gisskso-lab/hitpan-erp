using Dapper;
using HitPan.Application.Services;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G13 분류 전수 게이트 — DB 쪽</b>(W-G1-52 갈래 A-1 · 설계 §15-4 ⓐ ~ ⓔ · ⓖ · §15-9 G13 · CI db-gate 전용).
/// </summary>
/// <remarks>
/// <para>출하 DDL 로 신규설치본을 세우고(<see cref="MigrationGateDb"/> · #36 진실원 위에서 잰다) <b>실제</b> <c>information_schema</c> 를 읽어
/// 제품 판정 함수 <see cref="RestoreTableClassification.FindUnclassified"/> · <see cref="RestoreTableClassification.Classify"/> 로 대조한다
/// — 글자 검사가 아니라 엔진이 실제로 만든 표 · 외래키 · 트리거를 본다.</para>
/// <para>🔴 이 PC 3306 은 실물 DB 라 로컬에서 돌리지 않는다 — CI db-gate(HITPAN_REQUIRE_DB)에서만.
/// 선언 없는 로컬 건너뛰기는 실패(<see cref="DbGateEnvironment.SkipOrFailStrict"/> · <see cref="MigrationGateDb.ReadyAsync"/>).</para>
/// <para>뷰(<c>v_*</c> 9)는 표가 아니라 분류하지 않는다 — 복원은 뷰 · 트리거 · 루틴 손 0(설계 §4 ⑤ · K13).</para>
/// </remarks>
public sealed class RestoreTableClassificationGateDbTests : IDisposable
{
    private readonly MigrationGateDb _db = new("HITPAN_G13_ERP_DB", "g13cls");

    public void Dispose() => _db.Dispose();

    private static async Task<List<string>> BaseTablesAsync(MySqlConnection db) =>
        (await db.QueryAsync<string>(
            "SELECT TABLE_NAME FROM information_schema.TABLES "
          + "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME")).ToList();

    private static async Task<List<(string Child, string Parent)>> ForeignKeysAsync(MySqlConnection db) =>
        (await db.QueryAsync<(string, string)>(
            "SELECT TABLE_NAME, REFERENCED_TABLE_NAME FROM information_schema.KEY_COLUMN_USAGE "
          + "WHERE TABLE_SCHEMA = DATABASE() AND REFERENCED_TABLE_NAME IS NOT NULL")).ToList();

    private static string Report(string title, IEnumerable<string> names) =>
        $"{title}({names.Count()}개): {string.Join(", ", names)}";

    [Fact(DisplayName = "G13-D1 🔴 실제 BASE TABLE 전부가 두 목록 중 정확히 하나 (ⓐ · 제품 판정 함수로)")]
    public async Task G13_D1_실제표_전수()
    {
        const string gate = "G13-D1 실제 표 전수";
        if (!await _db.ReadyAsync(gate)) return;
        await using var db = await _db.OpenAsync();

        var tables = await BaseTablesAsync(db);
        Assert.True(tables.Count >= 100, $"[{gate}] 실제 표가 {tables.Count}개뿐 — 출하 DDL 적재가 깨졌다(0건 no-op 초록 막음).");

        var unclassified = RestoreTableClassification.FindUnclassified(tables);
        Assert.True(unclassified.Count == 0,
            Report($"[{gate}] 🔴 분류 안 된 실제 표 — RestoreTableClassification 에 더하라(기본값 없음 · 복원 걸음 ① 이 멈춘다)", unclassified));

        // 분류 쪽 수 = 실제 수(한 표가 두 쪽에 세어지지 않음)
        var keep = tables.Count(t => RestoreTableClassification.Classify(t) == RestoreTableClass.KeepCurrent);
        var backup = tables.Count(t => RestoreTableClassification.Classify(t) == RestoreTableClass.FromBackup);
        Assert.Equal(tables.Count, keep + backup);
        Console.Out.WriteLine($"[{gate}] 실제 BASE TABLE {tables.Count} = 지금 값 {keep} + 백업 값 {backup}");
    }

    [Fact(DisplayName = "G13-D2 🔴 예약 접두 rs_new__ 로 시작하는 실제 표 0 (ⓑ · LEFT 로 정확히 · LIKE 의 _ 와일드카드 안 씀)")]
    public async Task G13_D2_예약접두_0()
    {
        const string gate = "G13-D2 예약 접두";
        if (!await _db.ReadyAsync(gate)) return;
        await using var db = await _db.OpenAsync();

        var n = await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() "
          + "AND LEFT(TABLE_NAME, 8) = @P", new { P = RestoreTableClassification.StagingPrefix });
        Assert.True(n == 0, $"[{gate}] 🔴 신규설치본에 임시 접두 표가 {n}개 — 출하 DDL 에 들어가면 안 된다.");
    }

    [Fact(DisplayName = "G13-D3 🔴 실제 외래키 — 「지금 값」 자식 → 「백업 값」 부모 0 (ⓒ) · 백업 값 사이 고리 0(자기 참조 포함 · ⓓ)")]
    public async Task G13_D3_외래키_방향_고리()
    {
        const string gate = "G13-D3 외래키";
        if (!await _db.ReadyAsync(gate)) return;
        await using var db = await _db.OpenAsync();

        var fks = await ForeignKeysAsync(db);
        Assert.True(fks.Count >= 20, $"[{gate}] 실제 외래키가 {fks.Count}개뿐 — 적재가 깨졌다.");

        var wrongWay = fks.Where(e => RestoreTableClassification.Classify(e.Child) == RestoreTableClass.KeepCurrent
                                   && RestoreTableClassification.Classify(e.Parent) == RestoreTableClass.FromBackup)
                          .Select(e => $"{e.Child} → {e.Parent}").Distinct().ToList();
        Assert.True(wrongWay.Count == 0,
            Report($"[{gate}] 🔴 지금 값 표가 백업 값 표를 외래키로 가리킴 — 교체 때 끊긴다", wrongWay));

        var cycles = RestoreTableClassificationGateTests.FindCycles(
            fks.Where(e => RestoreTableClassification.Classify(e.Child) == RestoreTableClass.FromBackup
                        && RestoreTableClassification.Classify(e.Parent) == RestoreTableClass.FromBackup));
        Assert.True(cycles.Count == 0, Report($"[{gate}] 🔴 백업 값 외래키 고리 — 지우고 붓는 순서가 안 선다", cycles));
    }

    [Fact(DisplayName = "G13-D4 🔴 실제 트리거 걸린 표 전부가 아는 목록 안 (ⓔ)")]
    public async Task G13_D4_트리거표()
    {
        const string gate = "G13-D4 트리거 표";
        if (!await _db.ReadyAsync(gate)) return;
        await using var db = await _db.OpenAsync();

        var tables = (await db.QueryAsync<string>(
            "SELECT DISTINCT EVENT_OBJECT_TABLE FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA = DATABASE()")).ToList();
        Assert.True(tables.Count >= 1, $"[{gate}] 실제 트리거 표가 0개 — 출하 DDL 트리거(partner_special_prices · tax_invoices) 적재가 깨졌다.");

        var unknown = tables.Where(t => !RestoreTableClassification.KnownTriggerTables.Contains(t)).ToList();
        Assert.True(unknown.Count == 0, Report($"[{gate}] 🔴 아는 목록 밖 트리거 표 — 교체 걸음 설계를 다시 보라", unknown));
    }

    [Fact(DisplayName = "G13-D5 🔴 실제 BASE TABLE 이름 전부 ≤ 56자 (ⓖ · rs_new__ 를 붙여도 64자 안)")]
    public async Task G13_D5_이름길이()
    {
        const string gate = "G13-D5 이름 길이";
        if (!await _db.ReadyAsync(gate)) return;
        await using var db = await _db.OpenAsync();

        var tooLong = (await BaseTablesAsync(db))
            .Where(t => t.Length > RestoreTableClassification.MaxClassifiedNameLength).ToList();
        Assert.True(tooLong.Count == 0, Report($"[{gate}] 🔴 56자 넘는 실제 표", tooLong));
    }
}
