using System.Text.RegularExpressions;
using Dapper;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261014작9 갈래B — <b>마이그 적용기 게이트</b> (G-W13-1 ~ G-W13-6).
/// 사장님 전결 「전결. 이 두개까지만 해.」 · 결재 거리 <b>결-E′</b>.
///
/// <para>🔴 <b>내가 사장님께 올린 말은 틀렸다.</b> 「마이그 파일 자체를 도는 게이트가 0건입니다」라고
/// 적어 올렸는데, 실측하니 <b>6건 있다</b> — <see cref="Db3960BackofficeNoOpGateTests"/> ⓖ1~ⓖ6
/// (2026-10-06 작8 갈래 라)이 실물 <see cref="MigrationRunner"/> 를 레포 SQL 원문으로 태운다.
/// 정정은 [1-V] P-12 에 적었다.</para>
///
/// <para><b>참인 공백은 이 한 줄이다</b>:
/// <b>「그 차수가 새로 더한 마이그를, 신규 설치본(출하 DDL) 위에서 실물 적용기로 실제로
/// 돌려 보는 게이트」가 0건이다.</b> 근거:
/// <list type="bullet">
///   <item>ⓖ1 은 시드 <b>두 행</b>(DB-39·DB-60)만 지운다 ⇒ <c>DB-144</c>·<c>145</c>·<c>146</c> 은
///     <b>한 번도 안 돈다</b>. 그 「체인 완주」는 <b>건너뛴 사슬</b>의 완주다.</item>
///   <item>ⓖ2 는 「<b>아무것도 안 돈다</b>」가 통과 조건이다(신규설치 회귀).</item>
///   <item><see cref="MigrationLocationGuardTests"/> 4건은 <b>전부 글자 검사</b> — SQL 을 한 줄도
///     돌리지 않는다. 「멱등」조차 <c>ADD COLUMN</c> 줄에 <c>IF NOT EXISTS</c> 글자가 있나로 센다.</item>
/// </list>
/// ⇒ 🔴 <b>작3 의 <c>DB-146</c> 은 아무도 돌려 보지 않았다.</b> 시드 줄 누락은 잡혔지만
/// (내가 겪었다), SQL 자체가 도는지는 안 쟀다.</para>
///
/// <para>🔴 <b>더 큰 공백</b>: <see cref="MigrationLocationGuardTests"/> 는 <c>schema_migrations</c>
/// <b>시드 줄</b>만 대조한다. 어떤 차수가 <b>마이그에만 칸을 더하고 출하 DDL 에는 안 더하면</b> —
/// 시드 줄은 있으니 통과하고 <b>신규 고객에게 그 칸이 없다</b>(#36 위반). <b>그 모양이 이미
/// 일어나 있다</b>: <c>DB-89</c> 의 메인PC 가드 트리거 2개가 출하 DDL 에 <b>0건</b>인데 시드는
/// 「적용됨」이다([1-V] P-11 · 결재 거리 <b>결-F</b>). G-W13-2 가 이 자리를 문다.</para>
///
/// <para>🔴 <b>과녁으로 삼지 않는 것</b> — 「전건 재적용 완주」. [1-V] P-7 실측: 출하본 위에서
/// 110건을 다시 돌리면 <b>10건이 깨진다</b>(mysql CLI 기준). 마이그 폴더는 <b>누적 역사</b>이고
/// 출하 DDL 은 <b>현재 모습</b>이다 — 역사를 재생해도 현재가 안 나온다. 그래서 #36 이 출하 DDL 을
/// 단일 진실원이라 적었다. 그 숫자를 초록으로 만들려면 옛 마이그 10건을 고쳐야 하는데 범위 밖이고
/// 위험하다(#1·#43 · 결재 거리 <b>결-G</b>). ⇒ 대신 G-W13-5 가 <b>그 수가 조용히 늘지 못하게</b> 묶는다.</para>
///
/// <para>로컬 실행 = <c>HITPAN_W13_ERP_DB=test_w13_mig HITPAN_REQUIRE_DB=true</c>.
/// 🔴 선언 없는 건너뛰기는 실패다(<see cref="DbGateEnvironment.SkipOrFailStrict"/>).
/// 🔴 <b>로컬 초록은 증거가 아니다</b> — 증거는 CI <c>DB 게이트</c> 잡의 초록이다.</para>
/// </summary>
[Collection(MigrationRunnerSqlDirCollection.Name)]
public sealed class ErpMigrationApplyGateTests : IDisposable
{
    private readonly MigrationGateDb _db = new("HITPAN_W13_ERP_DB", "w13mig");

    // ══════════════════════════════════════════════════════════════
    // G-W13-1 — 🔴 **새 마이그가 실제로 돈다**
    //
    //   이 묶음의 본체다. 출하 DDL 로 신규설치본을 세우고, **번호가 가장 큰 마이그 한 건**의
    //   시드만 지운 뒤 실물 러너를 태운다 ⇒ 「그 차수가 더한 SQL 이 진짜 도나」를 잰다.
    //
    //   🔴 **파일 이름을 적지 않는다.** 번호 최대를 스스로 찾는다 — 이름을 적으면 다음 차수가
    //      이 게이트를 고쳐야 하고, 안 고치면 **조용히 옛 것만 재면서 초록**이 된다.
    //      (작3 이 겪은 모양: 게이트가 늘 같은 자리만 보며 새 자리를 안 봤다.)
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W13-1 🔴 가장 새 마이그가 신규설치본 위에서 실물 적용기로 실제로 돈다 (결-E′ 본체)")]
    public async Task G_W13_1_새_마이그가_실제로_돈다()
    {
        const string gate = "G-W13-1 새 마이그가 돈다";
        if (!await _db.ReadyAsync(gate)) return;

        var newest = MigrationGateDb.NewestMigrationId();

        await using var db = await _db.OpenAsync();

        // 신규설치본에서 **그 한 건의 시드만** 지운다 — 판정 변수를 하나로 묶는다.
        var deleted = await db.ExecuteAsync(
            "DELETE FROM schema_migrations WHERE migration_id = @Id;", new { Id = newest });
        Assert.True(deleted == 1,
            $"[{gate}] 출하 DDL 시드에 `{newest}` 가 없다(지운 행 {deleted}건) — "
          + "MigrationLocationGuardTests 가 먼저 빨강이어야 한다. 시드부터 고쳐라.");

        MigrationGateDb.MaterializeRepoSql();
        try
        {
            var r = await _db.RunRealRunnerAsync();

            Assert.True(r.Success,
                $"[{gate}] 🔴 가장 새 마이그 `{newest}` 가 신규설치본 위에서 **죽었다**.\n"
              + $"  실패 마이그: {r.FailedMigrationId}\n  사유: {r.FailureMessage}\n"
              + "  이것은 그 차수의 게시가 고객 PC 에서 **마이그 단계에서 멈춘다**는 뜻이다 — "
              + "워치독이 업데이트를 강제 중단한다(1.2.63 전례).");

            Assert.Equal(new[] { newest }, r.AppliedMigrationIds.ToArray());

            var ok = await db.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM schema_migrations WHERE migration_id = @Id AND success = 1;",
                new { Id = newest });
            Assert.True(ok == 1,
                $"[{gate}] `{newest}` 가 success=1 로 앉지 않았다 — 다음 업데이트가 같은 자리를 또 돈다.");
        }
        finally
        {
            MigrationGateDb.CleanRunnerSqlDir();
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-W13-2 — 🔴 **두 진실원이 어긋나지 않는다** (#36)
    //
    //   MigrationLocationGuardTests 는 **시드 줄**만 센다. 마이그에만 칸을 더하고 출하 DDL 에는
    //   안 더해도 **통과한다** ⇒ 신규 고객에게 그 칸이 없다. 여기서는 **모양**을 직접 비교한다:
    //   출하 DDL 직후 모양 == 새 마이그를 돌린 뒤 모양. 어긋나면 어느 칸·어느 인덱스인지 말한다.
    //
    //   🔴 `DB-89` 트리거 2개가 이 모양으로 **이미** 어긋나 있다([1-V] P-11 · 결-F). 그건 이
    //      게이트의 과녁(= 가장 새 마이그) 밖이라 여기서는 빨강이 되지 않는다 — **범위를 넓히는 것은
    //      결재 거리다.** 적어 두는 이유: 다음 사람이 「이 게이트가 전부 본다」고 읽지 않게.
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W13-2 🔴 새 마이그를 돌린 뒤 모양이 출하 DDL 과 같다 — 두 진실원이 어긋나지 않는다 (#36)")]
    public async Task G_W13_2_마이그_뒤_모양이_출하DDL과_같다()
    {
        const string gate = "G-W13-2 두 진실원 정합";
        if (!await _db.ReadyAsync(gate)) return;

        var newest = MigrationGateDb.NewestMigrationId();

        await using var db = await _db.OpenAsync();

        // ① 신규설치본 그대로의 모양 — 이것이 #36 의 단일 진실원이 말하는 모습이다.
        var before = await MigrationGateDb.ShapeAsync(db);

        await db.ExecuteAsync("DELETE FROM schema_migrations WHERE migration_id = @Id;", new { Id = newest });

        MigrationGateDb.MaterializeRepoSql();
        try
        {
            var r = await _db.RunRealRunnerAsync();
            Assert.True(r.Success, $"[{gate}] 러너가 실패했다(G-W13-1 을 먼저 보라): {r.FailureMessage}");

            // ② 마이그를 돌린 뒤의 모양
            var after = await MigrationGateDb.ShapeAsync(db);

            var onlyInMigration = after.Except(before, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var onlyInShipping = before.Except(after, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

            Assert.True(onlyInMigration.Count == 0,
                $"[{gate}] 🔴 `{newest}` 마이그가 **출하 DDL 에 없는 것**을 만들었다 — "
              + "신규 설치 고객에게는 그것이 **없다**(#36 위반).\n  마이그만 만든 것:\n    "
              + string.Join("\n    ", onlyInMigration)
              + "\n  🔴 `installer/hitpan_db_clean.sql` 에 같은 것을 더해라. "
              + "시드 줄만 더해도 MigrationLocationGuardTests 는 통과한다 — 그래서 이 게이트가 있다.");

            Assert.True(onlyInShipping.Count == 0,
                $"[{gate}] 출하 DDL 에만 있고 마이그 뒤에는 사라진 것이 있다(마이그가 뭔가를 내렸나):\n    "
              + string.Join("\n    ", onlyInShipping));
        }
        finally
        {
            MigrationGateDb.CleanRunnerSqlDir();
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-W13-3 — 🔴 **실패하면 멈추고 뒤가 안 돈다**
    //
    //   10/11 백오피스 사고의 모양을 ERP 판에서 잰다: 11번이 1064 로 죽어 **그 뒤 전부 공백**이
    //   됐다. ERP 적용기도 같은 거동이다(`MigrationRunner` — catch → success=0 기록 → 즉시 return).
    //   그 거동은 **설계 의도**이고(#15 로그+중단), 의도대로 도는지 아무도 안 쟀다.
    //
    //   🔴 깨진 통제 마이그는 **러너 폴더에만** 만든다(레포 0건) — 레포에 남으면 다른 게이트가
    //      전부 빨강이 된다. `finally` 에서 폴더째 지운다.
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W13-3 🔴 마이그가 깨지면 그 자리에서 멈추고 **뒤 마이그가 안 돈다** (10/11 백오피스 사고 모양)")]
    public async Task G_W13_3_깨지면_멈추고_뒤가_안돈다()
    {
        const string gate = "G-W13-3 실패 시 중단";
        if (!await _db.ReadyAsync(gate)) return;

        var newest = MigrationGateDb.NewestMigrationId();

        await using var db = await _db.OpenAsync();

        // 뒤에 **돌 것이 남아 있어야** 「뒤가 안 돈다」를 잴 수 있다 ⇒ 가장 새 것의 시드를 지운다.
        await db.ExecuteAsync("DELETE FROM schema_migrations WHERE migration_id = @Id;", new { Id = newest });

        MigrationGateDb.MaterializeRepoSql();
        try
        {
            // 가장 새 것보다 **앞 번호**에 깨진 통제 마이그를 끼운다.
            var brokenId = MigrationGateDb.PlantBrokenControlMigrationBefore(newest);

            var r = await _db.RunRealRunnerAsync();

            Assert.False(r.Success,
                $"[{gate}] 🔴 깨진 마이그를 끼웠는데 러너가 **성공이라 보고했다**. "
              + "실패를 삼키면 고객 DB 가 조용히 낡는다(#15).");

            Assert.Equal(brokenId, r.FailedMigrationId);

            var bad = await db.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM schema_migrations WHERE migration_id = @Id AND success = 0;",
                new { Id = brokenId });
            Assert.True(bad == 1,
                $"[{gate}] 실패가 `schema_migrations` 에 success=0 으로 안 남았다 — 다음 사람이 어디서 멈췄는지 모른다.");

            // 🔴 본체: **뒤 번호가 안 돌았다.**
            var after = await db.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM schema_migrations WHERE migration_id = @Id;", new { Id = newest });
            Assert.True(after == 0,
                $"[{gate}] 🔴 깨진 마이그 뒤의 `{newest}` 가 돌았다 — 적용기가 실패를 넘기고 전진했다. "
              + "그러면 깨진 자리가 **영영 공백으로 남은 채** 뒤가 반영돼 스키마가 반쪽이 된다.");

            Assert.DoesNotContain(newest, r.AppliedMigrationIds);
        }
        finally
        {
            MigrationGateDb.CleanRunnerSqlDir();
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-W13-4 — 🔴 **멱등** — 같은 마이그를 두 번 돌려도 모양이 안 변한다
    //
    //   업데이트는 재시도된다(워치독). 두 번째가 깨지면 그 고객은 그 자리에 갇힌다.
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W13-4 🔴 새 마이그를 두 번 돌려도 둘째는 적용 0건 · 모양 그대로 (업데이트 재시도 경로)")]
    public async Task G_W13_4_두번_돌려도_같다()
    {
        const string gate = "G-W13-4 멱등";
        if (!await _db.ReadyAsync(gate)) return;

        var newest = MigrationGateDb.NewestMigrationId();

        await using var db = await _db.OpenAsync();
        await db.ExecuteAsync("DELETE FROM schema_migrations WHERE migration_id = @Id;", new { Id = newest });

        MigrationGateDb.MaterializeRepoSql();
        try
        {
            var first = await _db.RunRealRunnerAsync();
            Assert.True(first.Success, $"[{gate}] 1회차가 실패했다: {first.FailureMessage}");
            var shapeAfterFirst = await MigrationGateDb.ShapeAsync(db);

            // 2회차 — 시드가 앉았으니 러너는 건너뛴다(멱등 skip).
            var second = await _db.RunRealRunnerAsync();
            Assert.True(second.Success, $"[{gate}] 2회차가 실패했다: {second.FailureMessage}");
            Assert.Empty(second.AppliedMigrationIds);

            var shapeAfterSecond = await MigrationGateDb.ShapeAsync(db);
            Assert.Equal(shapeAfterFirst.OrderBy(x => x, StringComparer.Ordinal),
                         shapeAfterSecond.OrderBy(x => x, StringComparer.Ordinal));

            // 🔴 시드를 또 지우고 돌리면 **파일이 다시 실제로 돈다** — 그때도 안 깨져야 멱등이다.
            //   (위 2회차는 skip 이라 SQL 을 안 돈다. 그것만 재고 「멱등」이라 적으면 거짓이다.)
            await db.ExecuteAsync("DELETE FROM schema_migrations WHERE migration_id = @Id;", new { Id = newest });
            var third = await _db.RunRealRunnerAsync();
            Assert.True(third.Success,
                $"[{gate}] 🔴 같은 마이그를 **실제로 두 번 돌리면** 깨진다: {third.FailureMessage}\n"
              + "  `IF NOT EXISTS` 가 빠졌다 — 업데이트 재시도가 그 고객을 이 자리에 갇히게 한다.");
            Assert.Equal(new[] { newest }, third.AppliedMigrationIds.ToArray());

            var shapeAfterThird = await MigrationGateDb.ShapeAsync(db);
            Assert.Equal(shapeAfterFirst.OrderBy(x => x, StringComparer.Ordinal),
                         shapeAfterThird.OrderBy(x => x, StringComparer.Ordinal));
        }
        finally
        {
            MigrationGateDb.CleanRunnerSqlDir();
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-W13-5 — 🔴 **재적용에서 깨지는 마이그 수가 조용히 늘지 않는다**
    //
    //   [1-V] P-7: 출하본 위에서 110건을 다시 돌리면 깨지는 것이 있다. 그걸 **고치는 것**은
    //   이 차수 범위가 아니다(결-G). 여기서 하는 일은 **수를 묶어 두는 것**이다 —
    //   새 차수가 멱등 아닌 마이그를 더하면 **그 이름이 찍히며 빨강**이 된다.
    //
    //   🔴 [1-V] P-8·P-9 봉합 2건:
    //     ① **한 건씩 독립으로** 잰다. CLI 로 쭉 돌렸을 때는 `DB-02` 가 중간에 멈춰
    //        뒤 문장이 안 돌았고, `DB-38` 의 「표 없음」이 그 **그림자**였다. 여기서는 매 파일마다
    //        시드를 그 한 건만 비워 **다른 파일의 영향 0** 으로 잰다.
    //     ② **실물 러너**로 잰다. CLI 와 MySqlConnector 는 여러 문장을 쪼개는 규칙이 달라
    //        `DB-89`(트리거)의 1064 는 CLI 쪽 함정일 수 있었다. 이 게이트의 숫자가 **정본**이다.
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>지금 재적용에서 깨지는 마이그</b> — 실물 러너 기준 측정값(2026-10-14 작9).
    /// <para>고치라는 목록이 <b>아니다</b>. 옛 마이그 수정은 #1·#43 이고 결재 거리 <b>결-G</b> 다.
    /// 시드가 막아 주는 동안은 고객에게 닿지 않는다. 이 목록이 하는 일은 <b>늘지 못하게</b> 하는 것이다.</para>
    /// <para>🔴 <b>새 마이그는 이 목록에 들어올 수 없다.</b> 들어오면 그 차수가 멱등 아닌 SQL 을
    /// 더한 것이고, 업데이트 재시도 경로에서 고객을 가둔다.</para>
    /// </summary>
    private static readonly string[] KnownNotReapplyable = MigrationGateDb.KnownNotReapplyable;

    [Fact(DisplayName = "G-W13-5 🔴 재적용에서 깨지는 마이그가 **늘지 않는다** — 새 마이그는 한 건도 못 들어온다")]
    public async Task G_W13_5_재적용_깨짐이_늘지_않는다()
    {
        const string gate = "G-W13-5 재적용 깨짐 고정";
        if (!await _db.ReadyAsync(gate)) return;

        await using var db = await _db.OpenAsync();

        MigrationGateDb.MaterializeRepoSql();
        try
        {
            var broken = new List<(string Id, string Why)>();
            var all = MigrationGateDb.AllMigrationIdsOnDisk();
            var measured = 0;   // 🔴 **몇 바퀴 돌았나** — 아래에서 단언한다([3-V] P-1)

            // 🔴 먼저 **전건을 적용됨으로** 앉힌다. 출하 DDL 시드에 빠진 파일이 하나라도 있으면
            //    첫 바퀴에서 그 파일까지 함께 돌아 「한 건씩 독립으로」가 깨지고(러너는 첫 실패에서
            //    멈춘다) 뒤의 측정이 전부 그 그림자를 먹는다. 시드 누락 자체는
            //    MigrationLocationGuardTests 의 과녁이다 — 여기서는 **측정을 독립으로 묶는 것**만 한다.
            foreach (var id in all)
            {
                await db.ExecuteAsync(@"
                    INSERT INTO schema_migrations (migration_id, app_version, success, applied_at)
                    VALUES (@Id, 'w13-gate-baseline', 1, CURRENT_TIMESTAMP(3))
                    ON DUPLICATE KEY UPDATE success = 1;", new { Id = id });
            }

            foreach (var id in all)
            {
                // 🔴 **두 번** 돌린다. 한 번만 돌리면 새 마이그의 멱등을 못 잰다 —
                //    새 칸은 출하본에 아직 없으니 **1회차는 성공**한다. 음성 대조군 NC-9 가 이 구멍을
                //    드러냈다: `ADD COLUMN`(IF NOT EXISTS 없음) 파일을 새로 더했는데 이 게이트가
                //    **초록이었다**. 멱등이 깨지는 자리는 **2회차**다. 옛 마이그는 출하본이 이미 그
                //    모양이라 1회차에서 드러난다 ⇒ 두 바퀴를 보면 옛 것·새 것을 **같은 잣대**로 잰다.
                string? why = null;
                for (var pass = 1; pass <= 2 && why is null; pass++)
                {
                    // 그 한 건만 미적용으로 만든다 ⇒ 러너는 그 한 건만 돈다(다른 파일의 그림자 0).
                    await db.ExecuteAsync(
                        "DELETE FROM schema_migrations WHERE migration_id = @Id;", new { Id = id });

                    var r = await _db.RunRealRunnerAsync();
                    if (!r.Success) why = $"{pass}회차 — {FirstLine(r.FailureMessage)}";
                }
                if (why is not null) broken.Add((id, why));
                measured++;

                // 다음 한 건을 깨끗한 자리에서 재려고 시드를 되돌린다(실패면 success=0 이 남는다).
                await db.ExecuteAsync(@"
                    INSERT INTO schema_migrations (migration_id, app_version, success, applied_at)
                    VALUES (@Id, 'w13-gate', 1, CURRENT_TIMESTAMP(3))
                    ON DUPLICATE KEY UPDATE success = 1;", new { Id = id });
            }

            // 🔴 **아무것도 안 재고 초록이 되는 길을 막는다** ([3-V] P-1).
            //   루프가 0바퀴여도 newlyBroken·nowFixed 가 둘 다 비면 초록이 될 수 있었다.
            //   (지금은 nowFixed 축이 **우연히** 막아 준다 — 목록이 비는 날 그 보증도 사라진다.)
            //   ⇒ 「몇 건을 실제로 돌려 봤나」를 직접 단언한다.
            Assert.Equal(all.Count, measured);
            Assert.True(measured >= 100,
                $"[{gate}] 🔴 {measured}건만 재고 끝났다 — 마이그가 110건 규모인데 너무 적다. "
              + "러너 폴더나 레포 폴더가 비었나(그 자리에서 조용한 초록이 난다).");

            var brokenIds = broken.Select(b => b.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var newlyBroken = brokenIds.Except(KnownNotReapplyable, StringComparer.OrdinalIgnoreCase).ToList();
            var nowFixed = KnownNotReapplyable.Except(brokenIds, StringComparer.OrdinalIgnoreCase).ToList();

            Assert.True(newlyBroken.Count == 0,
                $"[{gate}] 🔴 **재적용에서 깨지는 마이그가 늘었다** — 업데이트 재시도가 고객을 그 자리에 가둔다:\n  "
              + string.Join("\n  ", broken.Where(b => newlyBroken.Contains(b.Id, StringComparer.OrdinalIgnoreCase))
                                          .Select(b => $"{b.Id} — {b.Why}"))
              + "\n  🔴 `ADD COLUMN IF NOT EXISTS` · `ADD KEY IF NOT EXISTS` · `CREATE TABLE IF NOT EXISTS` 로 "
              + "다시 돌 수 있게 고쳐라. **이 목록에 이름을 더해 넘기지 마라** — 옛 것 10건은 "
              + "결재 거리(결-G)로 올려 둔 것이고, 새 차수의 면제가 아니다.");

            Assert.True(nowFixed.Count == 0,
                $"[{gate}] 목록에 적힌 마이그가 이제 **다시 돌아간다**: {string.Join(", ", nowFixed)}\n"
              + "  고친 것이면 좋은 일이다 — `MigrationGateDb.KnownNotReapplyable` 에서 그 이름을 빼라. "
              + "숫자가 어느 쪽으로든 조용히 움직이지 않게 양쪽을 다 문다.");
        }
        finally
        {
            MigrationGateDb.CleanRunnerSqlDir();
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-W13-6 — 🔴 **조용한 초록 씨앗 봉합 증명**
    //
    //   [1-V] P-15: 러너 폴더(`AppContext.BaseDirectory/Migrations/SQL`)를 쓰는 시험 클래스가
    //   둘이 됐다. `Db3960` 은 그 폴더를 **지우고 다시 만들고**, 끝에 **또 지운다**. xUnit 은
    //   클래스를 **병렬로** 돌린다 ⇒ 내 게이트가 도는 중에 그쪽이 지우면 러너가
    //   「적용 대상 0건」으로 **Success=true** 가 된다 — **아무것도 안 재고 초록.**
    //   그 경고는 `Db3960` 파일 머리에 이미 적혀 있었다(「지금은 이 클래스뿐」).
    //
    //   🔴 봉합 = 두 클래스를 같은 `[Collection]`(DisableParallelization) 에 넣는다.
    //   🔴 이 게이트는 **소스 글자가 아니라 실물 속성**을 읽는다 — xUnit 이 보는 것과 같은 것을 본다.
    // ══════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-W13-6 🔴 러너 폴더를 쓰는 두 시험 클래스가 같은 Collection 에 있다 — 서로 폴더를 지워 0건 no-op 초록이 되는 길을 닫는다")]
    public void G_W13_6_러너폴더_클래스들이_직렬화된다()
    {
        var users = new[] { typeof(ErpMigrationApplyGateTests), typeof(Db3960BackofficeNoOpGateTests) };

        var missing = new List<string>();
        var wrong = new List<string>();

        foreach (var t in users)
        {
            // 🔴 `CollectionAttribute` 는 xunit 2.9.3 에서 이름을 속성으로 내주지 않는다 ⇒
            //    생성자 인수를 **메타데이터에서** 읽는다. xunit 이 읽는 것과 같은 자리다.
            var data = t.GetCustomAttributesData()
                .FirstOrDefault(a => a.AttributeType == typeof(CollectionAttribute));
            if (data is null || data.ConstructorArguments.Count == 0) { missing.Add(t.Name); continue; }

            var name = data.ConstructorArguments[0].Value as string;
            if (!string.Equals(name, MigrationRunnerSqlDirCollection.Name, StringComparison.Ordinal))
                wrong.Add($"{t.Name} → \"{name}\"");
        }

        Assert.True(missing.Count == 0,
            "🔴 러너 폴더를 쓰는데 [Collection] 이 없는 시험 클래스: " + string.Join(", ", missing)
          + $"\n  `[Collection({nameof(MigrationRunnerSqlDirCollection)}.Name)]` 을 붙여라. "
          + "안 붙이면 xUnit 이 병렬로 돌려 서로의 Migrations/SQL 폴더를 지우고, "
          + "러너가 **적용 대상 0건으로 Success=true** 가 된다 — 아무것도 안 재고 초록이다.");

        Assert.True(wrong.Count == 0,
            "🔴 다른 Collection 에 들어간 클래스가 있다(직렬화가 깨진다): " + string.Join(", ", wrong));

        // 🔴 컬렉션이 **실제로 병렬을 끈다**는 것까지 본다 — 이름만 같고 꺼져 있으면 봉합이 아니다.
        var def = typeof(MigrationRunnerSqlDirCollection)
            .GetCustomAttributes(typeof(CollectionDefinitionAttribute), inherit: false)
            .Cast<CollectionDefinitionAttribute>().FirstOrDefault();
        Assert.NotNull(def);
        Assert.True(def!.DisableParallelization,
            "🔴 Collection 정의에 DisableParallelization 가 꺼져 있다 — 이름만 같고 병렬은 그대로다.");
    }

    private static string FirstLine(string? s) =>
        string.IsNullOrWhiteSpace(s) ? "(사유 없음)" : s.Split('\n')[0].Trim();

    public void Dispose() => _db.Dispose();
}

/// <summary>
/// 🔴 러너 폴더(<c>AppContext.BaseDirectory/Migrations/SQL</c>)를 <b>지우고 다시 만드는</b>
/// 시험 클래스들을 직렬화한다 — 20261014작9 [1-V] P-15.
/// <para>이 폴더는 프로세스 하나에 하나뿐이다. 두 클래스가 병렬로 돌면 한쪽이 다른 쪽의 폴더를
/// 지우고, 그러면 <see cref="MigrationRunner"/> 가 「적용 대상 0건」으로 <b>Success=true</b> 를
/// 돌려준다 — <b>아무것도 안 재고 초록</b>이다. G-W13-6 이 이 봉합을 지킨다.</para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MigrationRunnerSqlDirCollection
{
    public const string Name = "MigrationRunnerSqlDir";
}

/// <summary>
/// G-W13 묶음 전용 DB 장치 — 출하 DDL 로 신규설치본을 세우고 <b>실물</b>
/// <see cref="MigrationRunner"/> 를 태운다.
/// <para>🔴 <c>ErpCsGateDb</c> 를 안 쓰는 이유: 그쪽은 CS 컨트롤러 판정 캐시를 비우고 CS 전용 씨앗을
/// 심는다. 이 묶음이 재는 것은 <b>적용기</b>라서 연결 성질이 다르다 — 러너는
/// <c>AllowUserVariables=true</c> 로 열어야 한다(<c>MigrationDbConnectionFactory</c> 와 같은 성질).</para>
/// </summary>
internal sealed class MigrationGateDb : IDisposable
{
    /// <summary>
    /// 🔴 재적용에서 깨지는 옛 마이그 — <b>실물 러너 기준 측정값</b>(2026-10-14 작9 G-W13-5 로 실측).
    ///
    /// <para>🔴 <b>고치라는 목록이 아니다.</b> 옛 마이그 수정은 #1·#43 이고 결재 거리 <b>결-G</b> 로
    /// 올렸다. 시드가 막아 주는 동안은 고객에게 닿지 않는다. 이 목록이 하는 일은 <b>늘지 못하게</b>
    /// 하는 것이다 — 그리고 <b>새 마이그는 한 건도 못 들어온다</b>.</para>
    ///
    /// <para>🔴 [1-V] 가 mysql CLI 로 센 수는 <b>10</b> 이었다. 그건 <b>다른 파서</b>의 숫자이고,
    /// 게다가 앞 파일이 중간에 멈춘 <b>그림자</b>를 포함했다([1-V] P-8·P-9). <b>이 배열이 정본</b>이다 —
    /// 실물 러너로, 한 건씩 독립으로 쟀다.</para>
    /// </summary>
    /// <remarks>
    /// 🔴 <b>CLI 로 센 수(10)와 실물 러너로 센 수(9)가 다르고, 사유도 다르다.</b> 2026-10-14 실측:
    /// <list type="bullet">
    ///   <item><c>DB-89</c>(트리거) — CLI 는 1064 로 죽었지만 <b>실물 러너는 통과한다</b> ⇒
    ///     [1-V] P-9 의 의심이 맞았다. CLI 쪽 함정이었다.</item>
    ///   <item><c>DB-16</c> — CLI 는 1054(<c>nts_status</c> 없음)로 죽었는데, 실물 러너는
    ///     🔴 <b>「DELIMITER 는 MySqlConnector 와 쓸 수 없다」로 거부</b>한다. 즉 스키마 상태와
    ///     무관하게 <b>제품 적용기로는 이 파일이 영영 안 돈다</b>(레포에서 <c>DELIMITER</c> 를 쓰는
    ///     유일한 마이그다). 사람이 CLI 로 손수 돌린 것이 #39 의 뿌리다.</item>
    ///   <item><c>DB-38</c> — 한 건씩 독립으로 재니 <b>혼자서도 깨진다</b>(표
    ///     <c>reseller_commission_policies</c> 가 출하본에 없다) ⇒ [1-V] P-8 의 「앞 파일의 그림자」
    ///     가설은 <b>틀렸다</b>. 그 표는 대리점 수수료 표이고 #18·#22 로 ERP 출하본에서 내리는 쪽이
    ///     옳다 — 즉 <b>옛 마이그가 지금은 없는 것을 만지는</b> 정상적 어긋남일 수 있다.</item>
    /// </list>
    /// </remarks>
    public static readonly string[] KnownNotReapplyable =
    [
        "DB-02",   // 1060 — tenants.platform_id 중복(IF NOT EXISTS 없음)
        "DB-09",   // 1136 — INSERT … SELECT * 칸 수 불일치(그 뒤 tenants 칸이 늘었다)
        "DB-16",   // 🔴 MySqlConnector 가 DELIMITER 를 거부 — 제품 적용기로는 영영 안 돈다
        "DB-20",   // 1060 — sales_deliveries.tax_invoice_id 중복
        "DB-27",   // 1060 — tenants.ai_mode 중복
        "DB-34",   // 1060 — sales_orders.is_auto 중복
        // 🔴 DB-35 는 **1회차가 성공하고 2회차에서** 깨진다(1061 uk_journal_line_dedup).
        //   한 번만 돌려 보던 첫 판은 이걸 못 봤다 — 음성 대조군 NC-9 가 구멍을 드러내 두 바퀴로 고쳤다.
        //   🔴 그리고 1회차가 성공한다는 것은 **그 UNIQUE 키가 출하본에 없다**는 뜻이다(실측:
        //      `uk_journal_line_dedup` 는 레포 전체에서 DB-35 파일에만 있고 출하 DDL 에 0건)
        //      ⇒ 신규 설치 고객의 journal_lines 에는 **분개 중복 방지 키가 없다**(결-F 두 번째 사례).
        "DB-35",   // 1061(2회차) — 키 uk_journal_line_dedup · 🔴 출하본에 그 키가 0건
        "DB-36",   // 1060 — purchase_orders.is_auto 중복
        "DB-37",   // 1061 — 키 uq_stock_ledger_source 중복
        "DB-38",   // 1146 — 표 reseller_commission_policies 가 출하본에 없다(#18·#22 로 내린 듯 · 미확정)
    ];

    private readonly string _dbName;
    private bool _created;

    public MigrationGateDb(string envKey, string role)
    {
        var fallback = $"hitpan_gate_{role}_{Guid.NewGuid():N}"[..40];
        var given = Environment.GetEnvironmentVariable(envKey);
        if (string.IsNullOrWhiteSpace(given)) { _dbName = fallback; return; }

        // 🔴 이 장치는 DB 를 DROP 한다 — 이름에 test·gate 가 없으면 지정을 거부한다.
        var ok = given.Contains("test", StringComparison.OrdinalIgnoreCase)
              || given.Contains("gate", StringComparison.OrdinalIgnoreCase);
        if (!ok)
        {
            Console.Error.WriteLine(
                $"[G-W13] {envKey}='{given}' 는 거부했다 — 이 게이트는 DB 를 DROP 한다. "
              + "이름에 test 나 gate 가 있어야 한다. 무작위 이름으로 돈다.");
            _dbName = fallback;
            return;
        }
        _dbName = given.Trim();
    }

    public async Task<bool> ReadyAsync(string gate)
    {
        try
        {
            await using var admin = new MySqlConnection(ServerConnString());
            await admin.OpenAsync();
            await admin.ExecuteAsync($"DROP DATABASE IF EXISTS `{_dbName}`; "
                + $"CREATE DATABASE `{_dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
            _created = true;
        }
        catch (MySqlException ex)
        {
            Console.Error.WriteLine($"[{gate}] DB 준비 실패({ex.Number}): {ex.Message}");   // #15
            return !DbGateEnvironment.SkipOrFailStrict(gate);
        }

        // 🔴 파일 존재 검사는 로컬에서만 — CI 는 PATH 상의 명령 이름(`mysql`)을 준다(10/11 사고).
        if (!DbGateEnvironment.IsCi && !File.Exists(MysqlExe()))
        {
            Console.Error.WriteLine($"[{gate}] mysql 클라이언트 없음: {MysqlExe()}");
            return !DbGateEnvironment.SkipOrFailStrict(gate);
        }

        ImportShippingDdl();
        return true;
    }

    public async Task<MySqlConnection> OpenAsync()
    {
        var c = new MySqlConnection(DbConnString());
        await c.OpenAsync();
        return c;
    }

    /// <summary>🔴 실물 적용기를 태운다 — 판정용 복제 SQL 0건.</summary>
    public async Task<MigrationRunResult> RunRealRunnerAsync()
    {
        var runner = new MigrationRunner(new TestConnFactory(DbConnString()), NullLogger<MigrationRunner>.Instance);
        return await runner.ApplyPendingAsync("w13-gate");
    }

    // ══════════════════════════════════════════════════════════════
    // 모양 — 표·칸·인덱스. #36 이 말하는 「같은 판인가」를 이것으로 센다.
    // ══════════════════════════════════════════════════════════════

    /// <summary>표·칸(형·널 허용)·인덱스를 한 줄씩 떠서 집합으로 만든다.</summary>
    public static async Task<HashSet<string>> ShapeAsync(MySqlConnection db)
    {
        var cols = await db.QueryAsync<string>(@"
            SELECT CONCAT('칸 ', TABLE_NAME, '.', COLUMN_NAME, ' : ', COLUMN_TYPE, ' : ', IS_NULLABLE)
              FROM INFORMATION_SCHEMA.COLUMNS
             WHERE TABLE_SCHEMA = DATABASE()");

        var idx = await db.QueryAsync<string>(@"
            SELECT DISTINCT CONCAT('인덱스 ', TABLE_NAME, '.', INDEX_NAME)
              FROM INFORMATION_SCHEMA.STATISTICS
             WHERE TABLE_SCHEMA = DATABASE()");

        var tables = await db.QueryAsync<string>(@"
            SELECT CONCAT('표 ', TABLE_NAME)
              FROM INFORMATION_SCHEMA.TABLES
             WHERE TABLE_SCHEMA = DATABASE()");

        // 🔴 `schema_migrations` 는 과녁 밖이다 — 이 게이트가 그 표의 **행을 직접 지우며** 재므로
        //    자기 손길을 모양 차이로 되읽으면 거짓 빨강이 된다(칸 정의는 안 건드린다).
        var shape = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in cols.Concat(idx).Concat(tables))
        {
            if (line.Contains("schema_migrations", StringComparison.Ordinal)) continue;
            shape.Add(line);
        }
        return shape;
    }

    // ══════════════════════════════════════════════════════════════
    // 러너 폴더 — Db3960 과 같은 방식(선례를 베낀다 · 같은 Collection 으로 직렬화했다)
    // ══════════════════════════════════════════════════════════════

    private static readonly Regex MigrationFilePattern = new(@"^DB-(?<num>\d+)(?<suffix>[a-zA-Z]*)_", RegexOptions.Compiled);

    public static string RepoSqlDir() =>
        Path.Combine(ErpCsGateDb.RepoRoot(), "src", "HitPan.API", "Migrations", "SQL");

    public static string RunnerSqlDir() => Path.Combine(AppContext.BaseDirectory, "Migrations", "SQL");

    /// <summary>레포 SQL 원문 전부를 러너 후보 1 자리에 복사한다(현 worktree 내용 그대로).</summary>
    public static void MaterializeRepoSql()
    {
        var dst = RunnerSqlDir();
        if (Directory.Exists(dst)) Directory.Delete(dst, recursive: true);
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(RepoSqlDir(), "DB-*.sql"))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
    }

    public static void CleanRunnerSqlDir()
    {
        var dst = RunnerSqlDir();
        if (Directory.Exists(dst)) Directory.Delete(dst, recursive: true);
    }

    /// <summary>러너와 **같은 규칙**으로 정규화한 전체 마이그 식별자(번호순).</summary>
    public static IReadOnlyList<string> AllMigrationIdsOnDisk() =>
        Directory.GetFiles(RepoSqlDir(), "DB-*.sql")
            .Select(p => (Name: Path.GetFileName(p)!, Match: MigrationFilePattern.Match(Path.GetFileName(p)!)))
            .Where(x => x.Match.Success)
            .OrderBy(x => int.Parse(x.Match.Groups["num"].Value))
            .ThenBy(x => x.Match.Groups["suffix"].Value, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"DB-{x.Match.Groups["num"].Value.PadLeft(2, '0')}{x.Match.Groups["suffix"].Value}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// 🔴 <b>번호가 가장 큰 마이그</b>의 식별자 — 이름을 게이트에 적지 않기 위한 자리.
    /// <para>적으면 다음 차수가 이 게이트를 고쳐야 하고, 안 고치면 <b>조용히 옛 것만 재며 초록</b>이 된다.</para>
    /// </summary>
    public static string NewestMigrationId()
    {
        var all = AllMigrationIdsOnDisk();
        Assert.True(all.Count > 0, "마이그 파일이 0건이다 — 폴더 자리가 바뀌었나");
        return all[^1];
    }

    /// <summary>
    /// 깨진 통제 마이그를 <paramref name="beforeId"/> <b>앞 번호</b>로 러너 폴더에만 심는다.
    /// <para>🔴 레포에는 만들지 않는다 — 남으면 다른 게이트가 전부 빨강이 된다.</para>
    /// </summary>
    public static string PlantBrokenControlMigrationBefore(string beforeId)
    {
        var m = MigrationFilePattern.Match(beforeId + "_");
        Assert.True(m.Success, $"식별자 모양을 못 읽었다: {beforeId}");
        var beforeNum = int.Parse(m.Groups["num"].Value);
        Assert.True(beforeNum > 1, $"앞 번호를 만들 수 없다: {beforeId}");

        // 🔴 **한 번호 앞**에 심는다 — 러너는 번호 오름차순이고, 같은 번호끼리는 접미사 오름차순에
        //    **접미사 없는 쪽이 먼저**다. 그래서 같은 번호에 접미사를 붙이면 과녁보다 **뒤**에 서서
        //    「뒤가 안 돈다」를 못 잰다(이 게이트의 첫 판이 그렇게 틀렸고, 실측이 잡았다).
        var num = (beforeNum - 1).ToString();
        var fileName = $"DB-{num}zzgate_broken_control.sql";
        var path = Path.Combine(RunnerSqlDir(), fileName);

        File.WriteAllText(path,
            "-- 🔴 20261014작9 G-W13-3 통제 마이그 — 일부러 깨진 SQL 이다.\n"
          + "-- 러너 폴더에만 산다(레포 0건). 게이트가 finally 에서 폴더째 지운다.\n"
          + "ALTER TABLE this_table_does_not_exist_w13_gate ADD COLUMN zz_broken INT NULL;\n");

        return $"DB-{num.PadLeft(2, '0')}zzgate";
    }

    // ══════════════════════════════════════════════════════════════
    // 출하 DDL 적재 — ErpCsGateDb 와 같은 방식
    // ══════════════════════════════════════════════════════════════

    private void ImportShippingDdl()
    {
        var ddl = Path.Combine(ErpCsGateDb.RepoRoot(), "installer", "hitpan_db_clean.sql");
        Assert.True(File.Exists(ddl), $"출하 DDL 이 없다: {ddl}");

        var psi = new System.Diagnostics.ProcessStartInfo(MysqlExe())
        {
            RedirectStandardInput = true, RedirectStandardError = true, UseShellExecute = false,
            // 🔴 이걸 안 적으면 stdin 이 콘솔 인코딩(이 PC 는 CP949)으로 나가 한글이 깨지고 1067 로 죽는다.
            StandardInputEncoding = new System.Text.UTF8Encoding(false),
        };
        psi.ArgumentList.Add($"--host={Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost"}");
        psi.ArgumentList.Add($"--port={Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306"}");
        psi.ArgumentList.Add($"-u{Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root"}");
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS");
        if (!string.IsNullOrEmpty(pass)) psi.ArgumentList.Add($"-p{pass}");
        psi.ArgumentList.Add("--default-character-set=utf8mb4");
        psi.ArgumentList.Add(_dbName);

        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.StandardInput.Write(File.ReadAllText(ddl));
        proc.StandardInput.Close();
        var err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        Assert.True(proc.ExitCode == 0, $"출하 DDL import 실패 — 신규 설치가 같은 자리에서 죽는다:\n{err}");
    }

    private static string ServerConnString()
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root";
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
        return $"Server={host};Port={port};User={user};Password={pass};"
             + "DefaultCommandTimeout=180;GuidFormat=None;AllowUserVariables=true;";
    }

    /// <summary>🔴 러너 실물과 **같은 연결 성질** — <c>AllowUserVariables=true</c>(MigrationDbConnectionFactory).</summary>
    private string DbConnString() => ServerConnString() + $"Database={_dbName};";

    private static string MysqlExe() =>
        Environment.GetEnvironmentVariable("HITPAN_MYSQL") ?? @"C:\Program Files\MariaDB 11.4\bin\mysql.exe";

    public void Dispose()
    {
        if (!_created) return;
        try
        {
            using var admin = new MySqlConnection(ServerConnString());
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{_dbName}`;");
        }
        catch (MySqlException ex)
        {
            Console.Error.WriteLine($"[G-W13] 시험 DB 정리 실패: {ex.Message}");   // #15
        }
    }

    /// <summary>러너 실물에 꽂는 시험용 연결 공장 — 격리 DB 로만 연다.</summary>
    private sealed class TestConnFactory : IMigrationDbConnectionFactory
    {
        private readonly string _cs;
        public TestConnFactory(string cs) => _cs = cs;
        public async Task<System.Data.Common.DbConnection> CreateOpenAsync(CancellationToken ct = default)
        {
            var c = new MySqlConnection(_cs);
            await c.OpenAsync(ct);
            return c;
        }
    }
}
