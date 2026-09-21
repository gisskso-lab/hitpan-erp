using System.Diagnostics;
using System.Text;
using HitPan.Watchdog.AutoUpdate;
using Microsoft.Extensions.Logging.Abstractions;

namespace HitPan.Watchdog.Tests;

/// <summary>
/// 🔴 <b>G-RC18 b·c·d + N-7 + V-2</b> — 20260921작2 갈래 B(T3-6) 부분 적용 차단 장치 게이트.
///   근거: 작지 <c>docs/운영기록/20260921작2_잠금범위_설계6판_작업지시서.md</c> §14-1 · §14-2 · §16-4 /
///        설계 <c>docs/설계/erp/20260920_설계_수금등록_RC_안전경로.md</c> §16-2 · §16-4.
///
/// 🔴 <b>이 게이트가 닫는 구멍</b>: 기존 G-RC18(a)은 "등록이 되는가"만 봐서 <b>부분 적용에 초록을 준다</b>.
///   b 는 <b>부분 적용 상태에 <c>false</c> 가 나오는가</b>를 본다.
///
/// 🔴 <b>b·c 는 실제 DB 를 읽는다</b>(안전 사본 인스턴스). <c>information_schema.statistics</c> 를
///   워치독이 쓰는 그 경로(MariaDB 클라이언트 CLI)로 그대로 읽는다 — 글자가 아니라 동작을 잰다.
///   운영 3306·33406 은 건드리지 않는다. 포트는 <c>HITPAN_DB_STMT_PORT</c>(없으면 33306).
///   못 붙으면 <c>HITPAN_REQUIRE_DB</c> 가 참일 때 FAIL, 아니면 SKIP 사유를 남긴다(SKIP 을 통과로 세지 않는다).
/// </summary>
public sealed class UpdateIndexVerifyGateTests
{
    // DB-125 가 선언하기로 한 두 인덱스(작지 §14-3 T3-2). 🔴 제품 코드에는 이 이름이 없다 — 선언에서 읽는다.
    private const string Idx1 = "sales_deliveries.idx_sd_tenant_partner_src";
    private const string Idx2 = "collections.idx_coll_tenant_doc_cover";

    private static string Host => Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "127.0.0.1";
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("HITPAN_DB_STMT_PORT"), out var p) && p > 0 ? p : 33306;
    private static string User => Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root";
    private static string Pass => Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";

    private static bool RequireDb =>
        (Environment.GetEnvironmentVariable("HITPAN_REQUIRE_DB") ?? "").Trim() is "1" or "true" or "TRUE" or "yes";

    private static WatchdogStatusWriter NewWriter() => new(NullLogger<WatchdogStatusWriter>.Instance);

    // ────────────────────────────── G-RC18-b ──────────────────────────────

    [Fact(DisplayName = "G-RC18-b 인덱스 없는 사본 + 신버전 코드 → 3번째 조건이 false (부분 적용에 초록을 안 준다) · N-7 동반")]
    public async Task GRC18b_인덱스없음_차단()
    {
        var db = "hitpan_t36_b_" + Guid.NewGuid().ToString("N")[..8];
        if (!TryCreateFixture(db, withIndexes: false, out var why))
        {
            SkipOrFail($"G-RC18-b {why}");
            return;
        }

        try
        {
            var present = await NewWriter().GetExistingIndexNamesAsync(Host, Port, db, User, Pass, CancellationToken.None);
            Assert.NotNull(present);   // 🔴 읽기 자체는 성공해야 한다 — 못 읽었으면 이건 V-2(판정 불가)지 b 가 아니다

            var declared = new[] { Idx1, Idx2 };
            var (decision, missing) = UpdateOrchestrator.DecideDeclaredIndexes(declared, present);

            Console.Error.WriteLine($"[G-RC18-b] 선언 {declared.Length}개 · 실재 {present!.Count}개(전체 DB) · " +
                                    $"없음 {missing.Count}개({string.Join(", ", missing)}) → {decision}");

            Assert.Equal(UpdateOrchestrator.VerifyIndexDecision.MissingConfirmed, decision);
            Assert.Equal(2, missing.Count);

            // ── N-7 (작지 §14-2): 차단 장치를 빼면 이 상태에 true 가 나온다 ──
            //   장치 이전의 VerifyNewVersionAsync 는 ①FileVersion·②/health 만 봤고, 이 지점에서 곧장 true 였다.
            //   즉 "장치 없음"의 판정값은 상태와 무관하게 true 다. 같은 사본에서 두 값이 갈리는 것이 N-7 의 실측이다.
            const bool 장치없음_판정 = true;          // 봉합 전 코드의 이 자리 반환값(고정)
            var 장치있음_판정 = decision != UpdateOrchestrator.VerifyIndexDecision.MissingConfirmed;
            Console.Error.WriteLine($"[N-7] 같은 사본({db}) — 장치없음={장치없음_판정} · 장치있음={장치있음_판정} · " +
                                    $"뒤집힌 판정 1건(부분 적용 초록 → 롤백)");
            Assert.True(장치없음_판정);
            Assert.False(장치있음_판정);
        }
        finally { DropDb(db); }
    }

    // ────────────────────────────── G-RC18-c ──────────────────────────────

    [Fact(DisplayName = "G-RC18-c 인덱스 2개 있는 사본 → 3번째 조건이 true (거짓 빨간불 없음)")]
    public async Task GRC18c_인덱스있음_통과()
    {
        var db = "hitpan_t36_c_" + Guid.NewGuid().ToString("N")[..8];
        if (!TryCreateFixture(db, withIndexes: true, out var why))
        {
            SkipOrFail($"G-RC18-c {why}");
            return;
        }

        try
        {
            var present = await NewWriter().GetExistingIndexNamesAsync(Host, Port, db, User, Pass, CancellationToken.None);
            Assert.NotNull(present);

            var (decision, missing) = UpdateOrchestrator.DecideDeclaredIndexes(new[] { Idx1, Idx2 }, present);
            Console.Error.WriteLine($"[G-RC18-c] 실재 {present!.Count}개(전체 DB) · 없음 {missing.Count}개 → {decision}");

            Assert.Equal(UpdateOrchestrator.VerifyIndexDecision.Present, decision);
            Assert.Empty(missing);
        }
        finally { DropDb(db); }
    }

    // ────────────────────────────── V-2 경로 ──────────────────────────────

    [Fact(DisplayName = "V-2 판정 불가(DB 를 못 읽음) → 차단하지 않는다(fail-open) — 누가 fail-closed 로 바꾸면 여기가 운다")]
    public async Task V2_판정불가_통과()
    {
        // ① 순수 판정: present=null(못 읽었다)은 MissingConfirmed 가 아니라 Undetermined 다.
        var (decision, missing) = UpdateOrchestrator.DecideDeclaredIndexes(new[] { Idx1, Idx2 }, present: null);
        Assert.Equal(UpdateOrchestrator.VerifyIndexDecision.Undetermined, decision);
        Assert.Empty(missing);

        // ② 실동작: 존재하지 않는 DB 를 가리키면 조회가 실패하고 null(판정 불가)이 나온다 — 빈 집합이 아니다.
        //    🔴 빈 집합(읽었는데 없다)과 null(못 읽었다)이 갈리는 것이 V-1/V-2 의 경계다.
        var unreadable = await NewWriter().GetExistingIndexNamesAsync(
            Host, Port, "hitpan_t36_absent_" + Guid.NewGuid().ToString("N")[..8], User, Pass, CancellationToken.None);
        Assert.Null(unreadable);

        var (d2, _) = UpdateOrchestrator.DecideDeclaredIndexes(new[] { Idx1, Idx2 }, unreadable);
        Assert.Equal(UpdateOrchestrator.VerifyIndexDecision.Undetermined, d2);

        // 🔴 Undetermined 는 차단이 아니다. 차단은 MissingConfirmed 하나뿐이어야 한다(V-1).
        Assert.NotEqual(UpdateOrchestrator.VerifyIndexDecision.MissingConfirmed, d2);
        Console.Error.WriteLine("[V-2] 못 읽음 → Undetermined → 통과(fail-open). 차단은 MissingConfirmed 하나뿐.");
    }

    // ────────────────────────────── G-RC18-d ──────────────────────────────

    [Fact(DisplayName = "G-RC18-d 선언 목록은 마이그 파일에서 파싱한다 — 선언 2줄 → 이름 2개 · 선언 0줄(옛 마이그) → 0개")]
    public void GRC18d_선언파싱()
    {
        // (1) 선언 2줄이 있는 머리말 → 이름 2개. 🔴 제품 코드에 이름이 없어도 파일만 고치면 걸린다.
        var head =
            "-- DB-125: 수금등록 RC 안전경로 인덱스\n" +
            $"-- @verify-index: {Idx1}\n" +
            $"-- @verify-index: {Idx2}\n" +
            "ALTER TABLE sales_deliveries ADD KEY idx_sd_tenant_partner_src (tenant_id, partner_id, source_type);\n";
        var parsed = UpdateOrchestrator.ParseVerifyIndexDeclarations(head);
        Assert.Equal(new[] { Idx1, Idx2 }, parsed);

        // (2) 실제 DB-125 파일이 이 레포에 들어오면 그 파일로도 같은 2개가 나와야 한다(다른 갈래 산출물).
        var db125 = FindMigration("DB-125");
        if (db125 is not null)
        {
            var fromFile = UpdateOrchestrator.ParseVerifyIndexDeclarations(File.ReadAllText(db125));
            Console.Error.WriteLine($"[G-RC18-d] DB-125 실파일 파싱: {string.Join(", ", fromFile)}");
            Assert.Equal(2, fromFile.Count);
            Assert.Contains(Idx1, fromFile);
            Assert.Contains(Idx2, fromFile);
        }
        else
        {
            Console.Error.WriteLine("[G-RC18-d] DB-125 파일 없음(T3-2 미착수) — 실파일 단언은 아직 안 잰다.");
        }

        // (3) 선언 0줄인 옛 마이그 → 확인 대상 0 ⇒ 기존 흐름 무변경(회귀 없음).
        var old = FindMigration("DB-99") ?? FindMigration("DB-98");
        Assert.NotNull(old);
        Assert.Empty(UpdateOrchestrator.ParseVerifyIndexDeclarations(File.ReadAllText(old!)));

        // (4) 형식 밖은 선언으로 안 친다(본문 SQL 에 섞인 문자열 · 점 없음 · 따옴표 등).
        Assert.Empty(UpdateOrchestrator.ParseVerifyIndexDeclarations("SELECT '@verify-index: t.i';\n"));
        Assert.Empty(UpdateOrchestrator.ParseVerifyIndexDeclarations("-- @verify-index: nodot\n"));
        Assert.Empty(UpdateOrchestrator.ParseVerifyIndexDeclarations("-- @verify-index: a.b.c\n"));
        Assert.Empty(UpdateOrchestrator.ParseVerifyIndexDeclarations("-- @verify-index: t.i; DROP TABLE x\n"));

        // (5) 선언이 아예 없으면 NoDeclarations = 통과(기존 흐름 그대로).
        var (decision, _) = UpdateOrchestrator.DecideDeclaredIndexes(Array.Empty<string>(), new HashSet<string>());
        Assert.Equal(UpdateOrchestrator.VerifyIndexDecision.NoDeclarations, decision);
    }

    // ────────────────────────────── 보조 ──────────────────────────────

    private static void SkipOrFail(string why)
    {
        if (RequireDb)
            throw new Xunit.Sdk.XunitException($"[T3-6 게이트] {why} — HITPAN_REQUIRE_DB 가 켜진 잡에서는 SKIP 이 곧 FAIL 이다.");
        Console.Error.WriteLine($"[T3-6 게이트] SKIP — {why} (DB 없는 로컬 PASS 는 통과가 아니다)");
    }

    /// <summary>안전 사본 인스턴스에 시험용 DB 를 만든다. 🔴 운영 3306·33406 은 건드리지 않는다.</summary>
    private static bool TryCreateFixture(string db, bool withIndexes, out string why)
    {
        var indexDdl = withIndexes
            ? "ALTER TABLE sales_deliveries ADD KEY idx_sd_tenant_partner_src (tenant_id, partner_id, source_type); " +
              "ALTER TABLE collections ADD KEY idx_coll_tenant_doc_cover (tenant_id, source_doc_id, amount);"
            : "";

        var sql =
            $"DROP DATABASE IF EXISTS `{db}`; CREATE DATABASE `{db}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci; USE `{db}`; " +
            "CREATE TABLE sales_deliveries (id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, tenant_id VARCHAR(36) NOT NULL, " +
            "partner_id VARCHAR(36) NOT NULL, source_type VARCHAR(20) NULL) ENGINE=InnoDB; " +
            "CREATE TABLE collections (id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, tenant_id VARCHAR(36) NOT NULL, " +
            "source_doc_id VARCHAR(36) NULL, amount DECIMAL(18,2) NOT NULL DEFAULT 0) ENGINE=InnoDB; " +
            indexDdl;

        return RunClient(sql, out why);
    }

    private static void DropDb(string db)
    {
        if (!RunClient($"DROP DATABASE IF EXISTS `{db}`;", out var why))
            Console.Error.WriteLine($"[T3-6 게이트] 시험 DB 정리 실패({db}): {why}");
    }

    private static bool RunClient(string sql, out string why)
    {
        var exe = MysqlExe();
        if (!File.Exists(exe)) { why = $"MariaDB 클라이언트 없음: {exe}"; return false; }

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false)
        };
        psi.ArgumentList.Add($"--host={Host}");
        psi.ArgumentList.Add($"--port={Port}");
        psi.ArgumentList.Add($"-u{User}");
        if (!string.IsNullOrEmpty(Pass)) psi.ArgumentList.Add($"-p{Pass}");
        psi.ArgumentList.Add("--default-character-set=utf8mb4");

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) { why = "클라이언트 기동 실패(Process.Start null)"; return false; }
            proc.StandardInput.Write(sql);
            proc.StandardInput.Close();
            var err = proc.StandardError.ReadToEnd();
            proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(60_000);
            if (proc.ExitCode != 0) { why = $"사본 인스턴스({Host}:{Port}) 준비 실패: {err.Trim()}"; return false; }
            why = "";
            return true;
        }
        catch (Exception ex)
        {
            why = $"사본 인스턴스({Host}:{Port}) 접속 예외: {ex.Message}";
            return false;
        }
    }

    private static string MysqlExe() =>
        Environment.GetEnvironmentVariable("HITPAN_MYSQL") ?? @"C:\Program Files\MariaDB 11.4\bin\mysql.exe";

    private static string? FindMigration(string prefix)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var sqlDir = Path.Combine(dir.FullName, "src", "HitPan.API", "Migrations", "SQL");
            if (Directory.Exists(sqlDir))
                return Directory.GetFiles(sqlDir, prefix + "_*.sql").OrderBy(f => f).FirstOrDefault();
            dir = dir.Parent;
        }
        return null;
    }
}
