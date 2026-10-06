using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261006 작8 갈래 라 — DB-39·DB-60 「본사 전용 — ERP no-op」 봉합 게이트 ⓖ1~ⓖ6.
///
/// [사고] ERP 마이그 러너는 Migrations/SQL 폴더 <b>전부</b>를 번호순으로 돌린다(본사/ERP 구분 0 —
///   <c>MigrationRunner.cs:64-74</c>). 시드 두 행(DB-39·DB-60)이 없는 <b>옛 고객 DB</b> 에서는
///   DB-39 원본이 고객 DB 안에 본사 표 5개 + tenants FK + 본사 관리자 계정을 만들고(#18·#22 위반 모양),
///   DB-60 이 errno 150 으로 실패 → 러너 return → <b>DB-70~137(봉합 전부)이 영영 미적용</b>.
///   기동은 계속돼 고객은 아무 증상도 못 본다 — 스키마만 조용히 낡는다.
///   근거: docs/검증/선행/20261006_선행검증서_DB39_60_옛DB생성_대조실측.md §3.
///
/// [봉합] 두 파일 본문을 「본사 전용 — ERP no-op」 주석 + <c>SELECT 1;</c> 으로 교체(파일·이름·번호 보존).
///   결재: 작업지시서 §7-1(1안 ⓑ) · §7-6 CTO 조건부 승인(1회성 예외) · §7-7 사장님 전결(10/6).
///
/// [게이트]
///   ⓖ1 옛 DB(시드 두 행 없음) + 러너 → DB-39·60 no-op 통과 + 체인 완주 + 본사 표 0.
///   ⓖ2 신규설치(시드 있음) 회귀 — 변화 0.
///   ⓖ3 출하 DDL smoke 불변(시드 두 행 + 본사 표 0).
///   ⓖ4 본사 관리자 계정(admin@hitpan.kr) 행 0.
///   ⓖ5 손상상태 재개 — DB-39 success=1 + 잔존물 + DB-60 success=0 → 러너가 DB-60 재시도·완주·잔존과 충돌 0.
///   ⓖ6 예외 전제 고정(음성대조군) — success=1 파일은 본문이 바뀌어도 skip. 훗날 러너에 checksum
///       비교가 생기면 이 게이트가 FAIL 로 알린다(그때 no-op 교체 예외가 깨진다).
///
/// 🔴 실물 러너(<c>MigrationRunner</c>)를 실제 레포 SQL 파일로 돌린다 — 판정 SQL 복사본 없음.
///    러너는 <c>AppContext.BaseDirectory/Migrations/SQL</c> 을 후보 1 로 읽으므로(고정 경로),
///    각 시험이 레포 원문(또는 ⓖ6 의 통제 파일)을 그 자리에 복사하고 <b>finally 에서 지운다</b>.
///    이 폴더를 쓰는 다른 시험 클래스가 생기면 병렬 충돌 주의(지금은 이 클래스뿐).
///
/// 🔴 로컬(DB 없음)은 [SKIP] — <b>아무것도 검사하지 않는다.</b> CI <c>db-gate</c> 잡이 유일한 계측 경로다
///    (<c>HITPAN_REQUIRE_DB</c> 로 SKIP=FAIL). 개발 PC 의 hitpan 계정은 CREATE DATABASE 거부.
///    봉합 전 FAIL·봉합 후 PASS 실측(시험 DB hitpan_e2e · 2026-10-06):
///    docs/개발/erp/20261006작8_갈래라_DB3960_개발명세서.md §3.
/// </summary>
public sealed class Db3960BackofficeNoOpGateTests : IDisposable
{
    private readonly string _dbName = "hitpan_db3960_" + Guid.NewGuid().ToString("N")[..8];
    private bool _created;

    // ══════════════════════════════════════════════════════════════
    // 준비물 — 선례(MainPcRestartMarkGateTests)와 같은 방식
    // ══════════════════════════════════════════════════════════════

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — 시험이 출하 DDL·마이그를 읽을 수 없다.");
    }

    private static string RepoSqlDir() =>
        Path.Combine(RepoRoot(), "src", "HitPan.API", "Migrations", "SQL");

    private static string ServerConnString()
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root";
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
        return $"Server={host};Port={port};User={user};Password={pass};"
             + "DefaultCommandTimeout=120;GuidFormat=None;AllowUserVariables=true;";
    }

    /// <summary>🔴 러너 실물과 같은 연결 성질 — <c>AllowUserVariables=true</c>(<c>MigrationDbConnectionFactory</c>).</summary>
    private string DbConnString() =>
        ServerConnString().Replace("User=", $"Database={_dbName};User=");

    private static string MysqlExe() =>
        Environment.GetEnvironmentVariable("HITPAN_MYSQL")
        ?? @"C:\Program Files\MariaDB 11.4\bin\mysql.exe";

    private static bool ServerAvailable()
    {
        if (DbGateEnvironment.IsCi) return true;   // CI 는 DB 필수 — 못 붙으면 아래에서 실패로 드러난다
        if (!File.Exists(MysqlExe())) return false;
        try
        {
            using var c = new MySqlConnection(ServerConnString());
            c.Open();
            return true;
        }
        catch (MySqlException)
        {
            return false;
        }
    }

    /// <summary>🔴 신규 설치 그대로 — 빈 DB 에 출하 DDL 한 방(헌법 #36). import 실패 자체가 ⓖ3 FAIL 이다.</summary>
    private void SetUpFreshInstall()
    {
        var ddlPath = Path.Combine(RepoRoot(), "installer", "hitpan_db_clean.sql");
        Assert.True(File.Exists(ddlPath), $"출하 DDL 이 없다: {ddlPath}");

        using (var admin = new MySqlConnection(ServerConnString()))
        {
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{_dbName}`; "
                        + $"CREATE DATABASE `{_dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
        }
        _created = true;

        var psi = new System.Diagnostics.ProcessStartInfo(MysqlExe())
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add($"--host={Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost"}");
        psi.ArgumentList.Add($"--port={Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306"}");
        psi.ArgumentList.Add($"-u{Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root"}");
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS");
        if (!string.IsNullOrEmpty(pass)) psi.ArgumentList.Add($"-p{pass}");
        psi.ArgumentList.Add("--default-character-set=utf8mb4");
        psi.ArgumentList.Add(_dbName);

        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.StandardInput.Write(File.ReadAllText(ddlPath));
        proc.StandardInput.Close();
        var err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        Assert.True(proc.ExitCode == 0,
            $"ⓖ3 출하 DDL import 가 실패했다 — 신규 설치가 같은 자리에서 죽는다:\n{err}");
    }

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
            // #15 — 뒷정리 실패는 삼키지 않고 흔적을 남긴다(시험 판정에는 영향 없음).
            Console.Error.WriteLine($"[Db3960Gate] 시험 DB 뒷정리 실패({_dbName}): {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════════════════════
    // 러너 실물 태우기 — 고정 경로(baseDir/Migrations/SQL)에 원문을 복사한다
    // ══════════════════════════════════════════════════════════════

    private static string RunnerSqlDir() =>
        Path.Combine(AppContext.BaseDirectory, "Migrations", "SQL");

    /// <summary>레포 SQL 원문 전부를 러너 후보 1 자리에 복사한다(매번 새로 — 현 worktree 내용 그대로).</summary>
    private static void MaterializeRepoSql()
    {
        var dst = RunnerSqlDir();
        if (Directory.Exists(dst)) Directory.Delete(dst, recursive: true);
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(RepoSqlDir(), "DB-*.sql"))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
    }

    private static void CleanRunnerSqlDir()
    {
        var dst = RunnerSqlDir();
        if (Directory.Exists(dst)) Directory.Delete(dst, recursive: true);
    }

    private async Task<MigrationRunResult> RunRealRunnerAsync()
    {
        var runner = new MigrationRunner(
            new TestConnFactory(DbConnString()),
            NullLogger<MigrationRunner>.Instance);
        return await runner.ApplyPendingAsync("db3960-gate");
    }

    /// <summary>레포 SQL 폴더의 전체 마이그 식별자(러너와 같은 규칙으로 정규화 — DB-02·DB-08b 모양).</summary>
    private static IReadOnlyList<string> AllMigrationIdsOnDisk()
    {
        var pattern = new Regex(@"^DB-(?<num>\d+)(?<suffix>[a-zA-Z]*)_", RegexOptions.Compiled);
        return Directory.GetFiles(RepoSqlDir(), "DB-*.sql")
            .Select(Path.GetFileName)
            .Select(n => pattern.Match(n!))
            .Where(m => m.Success)
            .Select(m => $"DB-{m.Groups["num"].Value.PadLeft(2, '0')}{m.Groups["suffix"].Value}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private const string BackofficeTablesIn =
        "('platform_admins','resellers','reseller_accounts','reseller_commissions','commission_settlements')";

    private async Task<(int BackofficeTables, int FkTenantsReseller, int Success0Rows)> ReadSealStateAsync(MySqlConnection db)
    {
        var bo = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name IN " + BackofficeTablesIn + ";");
        var fk = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM information_schema.table_constraints WHERE table_schema=DATABASE() AND constraint_name='fk_tenants_reseller';");
        var fail = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM schema_migrations WHERE success=0;");
        return (bo, fk, fail);
    }

    // ══════════════════════════════════════════════════════════════
    // ⓖ1 — 옛 DB(시드 두 행 없음)에서 체인이 끝까지 이어진다 + 본사 표 0
    // ══════════════════════════════════════════════════════════════
    [Fact]
    public async Task G1_OldDb_RunnerCompletesChain_WithoutBackofficeTables()
    {
        if (!ServerAvailable()) { if (DbGateEnvironment.SkipOrFail("ⓖ1 Db3960 옛 DB 체인 재개")) return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        // 옛 고객 DB 재현 — 시드 두 행만 삭제(선행검증 §3-0 의 통제 재현 — 판정 변수는 이 두 행의 부재 하나).
        var deleted = await db.ExecuteAsync(
            "DELETE FROM schema_migrations WHERE migration_id IN ('DB-39','DB-60');");
        Assert.Equal(2, deleted);

        MaterializeRepoSql();
        try
        {
            var r = await RunRealRunnerAsync();

            // 🔴 봉합 전엔 여기서 죽는다 — DB-39 가 본사 표를 만들고 DB-60 이 errno 150 으로 실패(실측 §3-2).
            Assert.True(r.Success,
                $"ⓖ1 FAIL — 옛 DB 에서 러너가 중단됐다. Failed={r.FailedMigrationId} Msg={r.FailureMessage}\n"
              + "  이 고객은 다음 업데이트마다 같은 자리에서 멈추고, DB-70~137(봉합 전부)이 영영 미적용이다.");

            // no-op 두 건이 「새로 적용」으로 이어져야 체인이 되살아난 것이다.
            Assert.Contains("DB-39", r.AppliedMigrationIds);
            Assert.Contains("DB-60", r.AppliedMigrationIds);

            // 체인 완주 — 폴더의 전체 식별자가 전부 success=1 (success=0 잔류 0).
            var successIds = (await db.QueryAsync<string>(
                "SELECT migration_id FROM schema_migrations WHERE success=1;"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = AllMigrationIdsOnDisk().Where(id => !successIds.Contains(id)).ToList();
            Assert.True(missing.Count == 0,
                $"ⓖ1 FAIL — 체인이 완주하지 못했다. success=1 아닌 마이그: {string.Join(",", missing)}");

            var st = await ReadSealStateAsync(db);
            Assert.True(st.Success0Rows == 0, $"ⓖ1 FAIL — success=0 행이 남았다({st.Success0Rows}건).");
            Assert.True(st.BackofficeTables == 0,
                $"ⓖ1 FAIL — 본사 표가 고객 DB 에 생겼다({st.BackofficeTables}개). #18·#22 위반 모양.");
            Assert.True(st.FkTenantsReseller == 0,
                "ⓖ1 FAIL — 출하 DDL 이 보안 격벽으로 제거한 fk_tenants_reseller 가 되살아났다.");
        }
        finally
        {
            CleanRunnerSqlDir();
        }
    }

    // ══════════════════════════════════════════════════════════════
    // ⓖ2 — 신규설치(시드 있음) 회귀: 변화 0
    // ══════════════════════════════════════════════════════════════
    [Fact]
    public async Task G2_FreshInstall_RunnerIsNoChange()
    {
        if (!ServerAvailable()) { if (DbGateEnvironment.SkipOrFail("ⓖ2 Db3960 신규설치 회귀")) return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var tablesBefore = (await db.QueryAsync<string>(
            "SELECT table_name FROM information_schema.tables WHERE table_schema=DATABASE() ORDER BY table_name;")).ToList();
        var rowsBefore = (await db.QueryAsync<(string Id, string App, bool Ok)>(
            "SELECT migration_id, app_version, success FROM schema_migrations ORDER BY migration_id;")).ToList();

        MaterializeRepoSql();
        try
        {
            var r = await RunRealRunnerAsync();

            Assert.True(r.Success, $"ⓖ2 FAIL — 신규설치 직후 러너가 실패했다: {r.FailedMigrationId} {r.FailureMessage}");
            Assert.True(r.AppliedMigrationIds.Count == 0,
                $"ⓖ2 FAIL — 신규설치(전부 시드됨)에서 러너가 무언가를 새로 적용했다: {string.Join(",", r.AppliedMigrationIds)}\n"
              + "  출하 DDL 시드와 폴더가 어긋났다(#36) — 변화 0 이어야 한다.");

            var tablesAfter = (await db.QueryAsync<string>(
                "SELECT table_name FROM information_schema.tables WHERE table_schema=DATABASE() ORDER BY table_name;")).ToList();
            Assert.Equal(tablesBefore, tablesAfter);

            var rowsAfter = (await db.QueryAsync<(string Id, string App, bool Ok)>(
                "SELECT migration_id, app_version, success FROM schema_migrations ORDER BY migration_id;")).ToList();
            Assert.Equal(rowsBefore, rowsAfter);   // app_version 까지 그대로 = 두 행이 재실행되지 않았다

            var st = await ReadSealStateAsync(db);
            Assert.True(st.BackofficeTables == 0 && st.FkTenantsReseller == 0,
                $"ⓖ2 FAIL — 신규설치에 본사 흔적(표 {st.BackofficeTables} · FK {st.FkTenantsReseller}).");
        }
        finally
        {
            CleanRunnerSqlDir();
        }
    }

    // ══════════════════════════════════════════════════════════════
    // ⓖ3 — 출하 DDL smoke 불변: import 성공 + 시드 두 행 + 본사 표 0
    // ══════════════════════════════════════════════════════════════
    [Fact]
    public async Task G3_CleanDdl_Smoke_SeedRowsPresent_NoBackoffice()
    {
        if (!ServerAvailable()) { if (DbGateEnvironment.SkipOrFail("ⓖ3 Db3960 출하 DDL smoke")) return; }
        SetUpFreshInstall();   // import 실패 = 여기서 FAIL

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        var seed = (await db.QueryAsync<(string Id, string App, bool Ok)>(
            "SELECT migration_id, app_version, success FROM schema_migrations "
          + "WHERE migration_id IN ('DB-39','DB-60') ORDER BY migration_id;")).ToList();
        Assert.True(seed.Count == 2 && seed.All(s => s.Ok && s.App == "clean-ddl"),
            "ⓖ3 FAIL — 출하 DDL 의 DB-39/DB-60 시드 두 행이 없거나 모양이 다르다(#36 단일 진실원).\n"
          + $"  실측: {string.Join(" · ", seed.Select(s => $"{s.Id}/{s.App}/{s.Ok}"))}");

        var tableCount = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE();");
        Assert.True(tableCount >= 100,
            $"ⓖ3 FAIL — 출하 DDL 표 개수가 비정상({tableCount}) — DDL 이 중간에 끊겼다.");

        var st = await ReadSealStateAsync(db);
        Assert.True(st.BackofficeTables == 0 && st.FkTenantsReseller == 0,
            $"ⓖ3 FAIL — 출하 DDL 이 본사 흔적을 만든다(표 {st.BackofficeTables} · FK {st.FkTenantsReseller}).");
    }

    // ══════════════════════════════════════════════════════════════
    // ⓖ4 — 본사 관리자 계정(admin@hitpan.kr) 행 0
    // ══════════════════════════════════════════════════════════════
    [Fact]
    public async Task G4_OldDb_NoFixedPasswordAdminAccount()
    {
        if (!ServerAvailable()) { if (DbGateEnvironment.SkipOrFail("ⓖ4 Db3960 고정 비번 계정 0")) return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();
        await db.ExecuteAsync("DELETE FROM schema_migrations WHERE migration_id IN ('DB-39','DB-60');");

        MaterializeRepoSql();
        try
        {
            var r = await RunRealRunnerAsync();
            Assert.True(r.Success, $"ⓖ4 전제 붕괴 — 러너가 중단됐다: {r.FailedMigrationId} {r.FailureMessage}");

            // 표 자체가 없어야 한다 — 표가 없으면 행도 0 이다. 표가 있으면(봉합 되돌림) 행까지 센다.
            var adminTable = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='platform_admins';");
            if (adminTable > 0)
            {
                var rows = await db.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM platform_admins WHERE email='admin@hitpan.kr';");
                Assert.True(rows == 0,
                    "ⓖ4 FAIL — 고객 DB 에 본사 관리자 계정(admin@hitpan.kr · 고정 비밀번호)이 심어졌다.");
                Assert.True(adminTable == 0, "ⓖ4 FAIL — platform_admins 표가 고객 DB 에 생겼다.");
            }

            // ERP 사용자 표에도 그 계정이 없어야 한다(대조 폭 넓히기 — 값싼 한 줄).
            var erpRows = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM users WHERE email='admin@hitpan.kr';");
            Assert.True(erpRows == 0, "ⓖ4 FAIL — users 에 admin@hitpan.kr 행이 있다.");
        }
        finally
        {
            CleanRunnerSqlDir();
        }
    }

    // ══════════════════════════════════════════════════════════════
    // ⓖ5 — 손상상태 재개: DB-39 success=1 + 잔존물 + DB-60 success=0 → 재시도·완주·충돌 0
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 손상상태 픽스처 — 이미 사고가 난 고객 DB 모양(원본 DB-39 가 남긴 잔존물)을 재현한다.
    /// 🔴 이것은 「판정 SQL 복사본」이 아니라 <b>피검 상태를 만드는 재료</b>다 — 원본 DB-39 는
    ///    no-op 교체로 제품에서 사라졌으므로, 사고 당시 본문(git 이력 · 선행검증 §2)을 요약 재현한다.
    ///    판정하는 쪽은 실물 러너 + information_schema 다.
    /// </summary>
    private const string DamagedStateFixtureSql = """
        CREATE TABLE IF NOT EXISTS platform_admins (
            admin_id      CHAR(36)     NOT NULL,
            email         VARCHAR(100) NOT NULL,
            password_hash VARCHAR(256) NOT NULL,
            admin_name    VARCHAR(50)  NOT NULL,
            role          ENUM('super_admin','billing_admin','cs_admin','readonly') NOT NULL,
            is_active     TINYINT(1)   NOT NULL DEFAULT 1,
            CONSTRAINT pk_platform_admins PRIMARY KEY (admin_id),
            CONSTRAINT uk_platform_admins_email UNIQUE (email)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        CREATE TABLE IF NOT EXISTS resellers (
            reseller_id   CHAR(36)     NOT NULL,
            reseller_code VARCHAR(20)  NOT NULL,
            reseller_name VARCHAR(100) NOT NULL,
            CONSTRAINT pk_resellers PRIMARY KEY (reseller_id),
            CONSTRAINT uk_resellers_code UNIQUE (reseller_code)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        CREATE TABLE IF NOT EXISTS reseller_accounts (
            account_id  CHAR(36) NOT NULL,
            reseller_id CHAR(36) NOT NULL,
            CONSTRAINT pk_reseller_accounts PRIMARY KEY (account_id),
            CONSTRAINT fk_reseller_accounts_reseller
                FOREIGN KEY (reseller_id) REFERENCES resellers (reseller_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        CREATE TABLE IF NOT EXISTS reseller_commissions (
            commission_id CHAR(36)     NOT NULL,
            reseller_id   CHAR(36)     NOT NULL,
            rate          DECIMAL(5,2) NOT NULL,
            CONSTRAINT pk_reseller_commissions PRIMARY KEY (commission_id),
            CONSTRAINT fk_reseller_commissions_reseller
                FOREIGN KEY (reseller_id) REFERENCES resellers (reseller_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        CREATE TABLE IF NOT EXISTS commission_settlements (
            settlement_id CHAR(36) NOT NULL,
            reseller_id   CHAR(36) NOT NULL,
            approved_by   CHAR(36) NULL,
            CONSTRAINT pk_commission_settlements PRIMARY KEY (settlement_id),
            CONSTRAINT fk_commission_settlements_reseller
                FOREIGN KEY (reseller_id) REFERENCES resellers (reseller_id),
            CONSTRAINT fk_commission_settlements_admin
                FOREIGN KEY (approved_by) REFERENCES platform_admins (admin_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        ALTER TABLE tenants
            ADD CONSTRAINT fk_tenants_reseller
                FOREIGN KEY (reseller_id) REFERENCES resellers (reseller_id)
                ON UPDATE CASCADE ON DELETE SET NULL;

        INSERT IGNORE INTO platform_admins (admin_id, email, password_hash, admin_name, role, is_active)
        VALUES ('00000000-0000-0000-0000-000000000001', 'admin@hitpan.kr', 'damaged-state-fixture-hash',
                '히트판 관리자', 'super_admin', 1);
        """;

    [Fact]
    public async Task G5_DamagedState_RunnerRetriesDb60_AndCompletes_WithResidueIntact()
    {
        if (!ServerAvailable()) { if (DbGateEnvironment.SkipOrFail("ⓖ5 Db3960 손상상태 재개")) return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        // 손상상태 — 원본 DB-39 는 success=1 로 끝났고 잔존물이 남았으며, DB-60 은 success=0.
        await db.ExecuteAsync("DELETE FROM schema_migrations WHERE migration_id IN ('DB-39','DB-60');");
        await db.ExecuteAsync(new CommandDefinition(DamagedStateFixtureSql, commandTimeout: 120));
        await db.ExecuteAsync(
            "INSERT INTO schema_migrations (migration_id, app_version, success) "
          + "VALUES ('DB-39','damaged-fixture',1),('DB-60','damaged-fixture',0);");

        MaterializeRepoSql();
        try
        {
            var r = await RunRealRunnerAsync();

            Assert.True(r.Success,
                $"ⓖ5 FAIL — 손상상태에서 러너가 재개하지 못했다: {r.FailedMigrationId} {r.FailureMessage}\n"
              + "  이미 사고가 난 고객(DB-39 적용 + DB-60 실패)은 영영 못 빠져나온다.");

            // 전제 검증 — 러너는 success=0 행을 재실행한다(skip 근거는 success=1 뿐 — MigrationRunner.cs:91-94).
            Assert.Contains("DB-60", r.AppliedMigrationIds);
            // success=1 인 DB-39 는 재실행하지 않는다.
            Assert.DoesNotContain("DB-39", r.AppliedMigrationIds);

            var st = await ReadSealStateAsync(db);
            Assert.True(st.Success0Rows == 0, $"ⓖ5 FAIL — 재개 후에도 success=0 행이 남았다({st.Success0Rows}건).");

            // 잔존물은 그대로여야 한다 — 뒷정리는 범위 밖(§7-2 · #37 계열 사장님 결재 사안). 충돌 0 의 증거.
            Assert.True(st.BackofficeTables == 5,
                $"ⓖ5 FAIL — 잔존 표가 {st.BackofficeTables}개다(5개여야 한다). 이 갈래는 뒷정리를 하지 않는다.");
            Assert.True(st.FkTenantsReseller == 1, "ⓖ5 FAIL — 잔존 FK 가 사라졌다. 이 갈래는 뒷정리를 하지 않는다.");
            var adminRows = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM platform_admins WHERE email='admin@hitpan.kr';");
            Assert.True(adminRows == 1, "ⓖ5 FAIL — 잔존 계정 행이 사라졌다. 이 갈래는 뒷정리를 하지 않는다.");
        }
        finally
        {
            CleanRunnerSqlDir();
        }
    }

    // ══════════════════════════════════════════════════════════════
    // ⓖ6 — 예외 전제 고정(음성대조군): success=1 파일은 본문이 바뀌어도 skip
    // ══════════════════════════════════════════════════════════════
    [Fact]
    public async Task G6_NegativeControl_Success1FileWithChangedBody_IsSkipped()
    {
        if (!ServerAvailable()) { if (DbGateEnvironment.SkipOrFail("ⓖ6 Db3960 checksum 미비교 전제")) return; }
        SetUpFreshInstall();

        await using var db = new MySqlConnection(DbConnString());
        await db.OpenAsync();

        // 통제 폴더 — 진짜 레포 파일 대신 탐침 2개만 러너 후보 1 자리에 둔다.
        //   DB-998: 이력 없음        → 실행돼야 한다(양성 대조 — 러너가 실제로 돌았다는 증거).
        //   DB-999: success=1 이력   → 본문(checksum)이 기록과 달라도 skip 돼야 한다(no-op 교체 예외의 전제).
        var dst = RunnerSqlDir();
        if (Directory.Exists(dst)) Directory.Delete(dst, recursive: true);
        Directory.CreateDirectory(dst);
        File.WriteAllText(Path.Combine(dst, "DB-998_gate_probe_run.sql"),
            "CREATE TABLE IF NOT EXISTS g6_probe_ran (id INT PRIMARY KEY) ENGINE=InnoDB;");
        File.WriteAllText(Path.Combine(dst, "DB-999_gate_probe_skip.sql"),
            "CREATE TABLE IF NOT EXISTS g6_probe_must_not_exist (id INT PRIMARY KEY) ENGINE=InnoDB;");

        // success=1 이력 — checksum 은 일부러 「다른 본문」의 기록값 모양(실제 파일과 불일치).
        await db.ExecuteAsync(
            "INSERT INTO schema_migrations (migration_id, app_version, checksum, success) "
          + "VALUES ('DB-999','g6-previous-body', REPEAT('0',64), 1);");

        try
        {
            var r = await RunRealRunnerAsync();

            Assert.True(r.Success, $"ⓖ6 전제 붕괴 — 탐침 러너 실행이 실패했다: {r.FailedMigrationId} {r.FailureMessage}");

            // 양성 대조 — 러너는 실제로 돌았다.
            Assert.Contains("DB-998", r.AppliedMigrationIds);
            var ran = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='g6_probe_ran';");
            Assert.True(ran == 1, "ⓖ6 양성 대조 실패 — 이력 없는 탐침이 실행되지 않았다. 이 게이트는 아무것도 재지 못했다.");

            // 🔴 본판 — success=1 이면 본문이 바뀌어도 skip. 이것이 깨지는 날 = 러너에 내용 비교가 생긴 날.
            //    그러면 no-op 교체(1회성 예외 · §7-6)가 깨진다 — 이 FAIL 이 그 알림이다.
            Assert.DoesNotContain("DB-999", r.AppliedMigrationIds);
            var mustNot = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='g6_probe_must_not_exist';");
            Assert.True(mustNot == 0,
                "ⓖ6 FAIL — success=1 인 파일이 본문이 바뀌었다고 재실행됐다.\n"
              + "  러너에 checksum 비교가 생겼다 — DB-39·DB-60 no-op 교체(작8 §7-6 1회성 예외)의 전제가 깨졌다.\n"
              + "  no-op 교체 전략 전체를 재결재하라(이미 배포된 고객 DB 에서 두 마이그가 재실행된다).");
        }
        finally
        {
            CleanRunnerSqlDir();
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
