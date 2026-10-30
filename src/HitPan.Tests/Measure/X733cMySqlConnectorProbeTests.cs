using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Dapper;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using HitPan.Backoffice.API.Services;
using HitPan.Tests.Integrity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Measure;

/// <summary>
/// 🔴 X733c 계측 시험 — MySqlConnector 2.5.0 ↔ 2.6.2 × LF ↔ CRLF 격리 CI 실행 대조.
/// 근거: <c>docs/운영기록/20261030작_MySqlConnector실행대조_작업지시서.md</c> §3-4 · §3-5 · §3-7 · §5 · §PM.
///
/// <para>🔴 <b>계측 가지 <c>measure/20261030-x733c-mysqlconnector</c> 전용 · 머지 금지.</b>
/// 이 파일은 제품 동작을 지키는 게이트가 아니다. 한 칸(판 × 줄끝)의 원결과를 JSON 으로 남기는 장치다.
/// 같다/다르다/무효 판정은 <c>tests/measure/x733c/compare_cells.py</c> 가 한다. 이 시험은 해석하지 않는다.</para>
///
/// <para>🔴 <b>실행 거부</b>: GitHub Actions 칸 환경 변수(<c>GITHUB_ACTIONS</c>·<c>CI</c>·<c>HITPAN_REQUIRE_DB</c>·
/// <c>HITPAN_X733C_*</c>)가 하나라도 없으면 DB 에 붙기 <b>전에</b> 실패한다(SKIP 아님). 이 장치는
/// <c>SET SESSION sql_log_off</c>·<c>TRUNCATE mysql.general_log</c>·<c>DROP DATABASE</c> 를 하므로
/// 개발 PC(3306 = 실물 DB)에서 돌면 안 된다.</para>
///
/// <para>같은 실행기: ERP 는 실물 <see cref="MigrationRunner"/> 를 G-W13-5
/// (<c>ErpMigrationApplyGateTests.cs</c> :305~404)와 같은 시드·한 건씩·두 바퀴로 돌린다. 이 시험이 루프를
/// 다시 적었으므로 「같은 실행기」의 닻은 같은 칸에서 먼저 도는 <b>G-W13-5 무수정 실행</b>이다 —
/// 이 시험의 깨짐 집합이 명단(<see cref="MigrationGateDb.KnownNotReapplyable"/>)과 다르면 「무효 — 원인 미확정」으로 실패한다
/// (PM 조건 ④). 연결 공장·로거만 이 시험 것이다(오류 번호·예외 형을 받으려고 · 연결 성질은 같다).</para>
/// </summary>
[Collection(MigrationRunnerSqlDirCollection.Name)]
public sealed class X733cMySqlConnectorProbeTests
{
    private const string ErpDbName = "gate_x733c";
    private const string BoDbName = "gate_x733c_bo";
    private const string ProbeMigrationFile = "DB-9999zzx733c_probe.sql";
    private const string ProbeMigrationId = "DB-9999zzx733c";

    [Fact(DisplayName = "X733c 계측 — 한 칸(판 × 줄끝)의 원결과를 JSON 으로 남긴다 (계측 가지 전용 · 판정 안 함)")]
    public async Task X733c_한칸_원결과_기록()
    {
        var cell = CellEnv.ReadOrRefuse();

        // [4] 권고 2 — DB 에 쓰기 **전에** 서버 지문을 본다(읽기 SELECT 하나 · 쓰기 0).
        //   칸 컨테이너는 --general-log=1 --log-output=TABLE 로 뜬다. 아니면 칸 컨테이너가 아니다 → 거부.
        await RefuseUnlessMeasureServerAsync(cell);

        var report = new Dictionary<string, object?>
        {
            ["schema"] = "x733c-probe/1",
            ["cell"] = new { id = cell.Id, kind = cell.Kind, driver = cell.Driver, eol = cell.Eol },
            ["runtime"] = RuntimeAssembly(),
        };
        var deviceErrors = new List<string>();
        report["device_errors"] = deviceErrors;

        IReadOnlyList<string> erpBrokenIds = Array.Empty<string>();
        var erpMeasured = -1;
        var erpAll = -1;

        try
        {
            if (cell.Kind == "mariadb")
            {
                var erp = await MeasureErpAsync(cell, report, deviceErrors);
                erpBrokenIds = erp.Broken;
                erpMeasured = erp.Measured;
                erpAll = erp.All;
            }

            await MeasureBackofficeAndControl2Async(cell, report, deviceErrors);
        }
        catch (Exception ex)
        {
            // #15 — 삼키지 않는다. 기록하고 그대로 던진다(칸 JSON 에 「중단」이 남는다).
            report["aborted"] = Describe(ex);
            Console.Error.WriteLine($"[X733c] 측정 중단: {ex.GetType().FullName}: {FirstLine(ex.Message)}");
            throw;
        }
        finally
        {
            MigrationGateDb.CleanRunnerSqlDir();
            WriteJson(Path.Combine(cell.OutDir, "probe.json"), report);
        }

        // ── 장치 단언(판정 아님) — JSON 을 남긴 **뒤에** 실패시킨다 ──
        Assert.True(deviceErrors.Count == 0,
            "[X733c] 장치 전제가 서지 않았다:\n  " + string.Join("\n  ", deviceErrors));

        if (cell.Kind == "mariadb")
        {
            Assert.Equal(erpAll, erpMeasured);
            Assert.True(erpMeasured >= 100, $"[X733c] {erpMeasured}건만 쟀다 — 레포 SQL 폴더가 비었나");

            var known = MigrationGateDb.KnownNotReapplyable;
            var extra = erpBrokenIds.Except(known, StringComparer.OrdinalIgnoreCase).ToList();
            var missing = known.Except(erpBrokenIds, StringComparer.OrdinalIgnoreCase).ToList();
            Assert.True(extra.Count == 0 && missing.Count == 0,
                "[X733c] 🔴 측정 시험의 깨짐 집합이 G-W13-5 명단과 다르다 — 무효 — 원인 미확정(PM 조건 ④).\n"
              + $"  명단에 없는데 깨짐: {string.Join(", ", extra)}\n"
              + $"  명단에 있는데 안 깨짐: {string.Join(", ", missing)}");
        }
    }

    // ══════════════════════════════════════════════════════════════
    // ERP — 실물 MigrationRunner · G-W13-5 와 같은 시드·한 건씩·두 바퀴
    // ══════════════════════════════════════════════════════════════

    private static async Task<(IReadOnlyList<string> Broken, int Measured, int All)> MeasureErpAsync(
        CellEnv cell, Dictionary<string, object?> report, List<string> deviceErrors)
    {
        using var gateDb = new MigrationGateDb("HITPAN_X733C_DB", "x733c");
        Assert.True(await gateDb.ReadyAsync("X733c 계측"), "[X733c] 빈 DB + 출하 DDL 적재가 서지 않았다");

        await using var quiet = await QuietOpenAsync(cell, ErpDbName);
        report["server"] = await ServerFactsAsync(quiet, deviceErrors);

        MigrationGateDb.MaterializeRepoSql();
        report["dir_recount_runner"] = Recount(MigrationGateDb.RunnerSqlDir(), "DB-*.sql");

        var factory = new ProbeConnFactory(ErpConnString(cell, ErpDbName));
        var logger = new CapturingLogger();
        var runner = new MigrationRunner(factory, logger);

        var all = MigrationGateDb.AllMigrationIdsOnDisk();
        foreach (var id in all)
        {
            await quiet.ExecuteAsync(@"
                INSERT INTO schema_migrations (migration_id, app_version, success, applied_at)
                VALUES (@Id, 'w13-gate-baseline', 1, CURRENT_TIMESTAMP(3))
                ON DUPLICATE KEY UPDATE success = 1;", new { Id = id });
        }

        var files = new List<object>();
        var broken = new List<object>();
        var brokenIds = new List<string>();
        var measured = 0;
        var applied = 0;

        foreach (var id in all)
        {
            string? why = null;
            var passes = new List<object>();
            for (var pass = 1; pass <= 2 && why is null; pass++)
            {
                await quiet.ExecuteAsync("DELETE FROM schema_migrations WHERE migration_id = @Id;", new { Id = id });
                var (r, rec) = await RunOnceAsync(runner, logger, factory, quiet, id, pass);
                passes.Add(rec);
                if (!r.Success)
                {
                    why = $"{pass}회차 — {FirstLine(r.FailureMessage)}";
                    broken.Add(new { id, why, pass, detail = rec });
                    brokenIds.Add(id);
                }
                else if (r.AppliedMigrationIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                {
                    applied++;
                }
            }
            measured++;

            var checksum = await quiet.ExecuteScalarAsync<string?>(
                "SELECT checksum FROM schema_migrations WHERE migration_id = @Id;", new { Id = id });
            files.Add(new { id, checksum, passes });

            await quiet.ExecuteAsync(@"
                INSERT INTO schema_migrations (migration_id, app_version, success, applied_at)
                VALUES (@Id, 'w13-gate', 1, CURRENT_TIMESTAMP(3))
                ON DUPLICATE KEY UPDATE success = 1;", new { Id = id });
        }

        // ── 대조군 ① DB-16 원본 — 한 건만(이력 삭제 → 러너) ──
        const string db16 = "DB-16";
        await quiet.ExecuteAsync("DELETE FROM schema_migrations WHERE migration_id = @Id;", new { Id = db16 });
        var (c1, c1rec) = await RunOnceAsync(runner, logger, factory, quiet, db16, 1);
        await quiet.ExecuteAsync(@"
            INSERT INTO schema_migrations (migration_id, app_version, success, applied_at)
            VALUES (@Id, 'w13-gate', 1, CURRENT_TIMESTAMP(3))
            ON DUPLICATE KEY UPDATE success = 1;", new { Id = db16 });
        report["control1_db16"] = new
        {
            note = "원본 거부 대조 — 같은 SHA · 무수정(줄끝 축만 바뀜) DB-16 을 G-W13-5 방식으로 한 건만 실물 러너에 태운 원결과. "
                 + "실제 설치/업데이트 경로의 기존 한계(DELIMITER 를 제품 적용기가 못 돌림)는 이 칸이 재는 것이 아니다.",
            applied_count = c1.AppliedMigrationIds.Count,
            result = c1rec,
        };

        // ── 대조군 ②′ .sql 경로 변형(기록만) — 러너 폴더에만 두고 실물 러너(매개변수 0) ──
        var c2bSrc = Path.Combine(ErpCsGateDb.RepoRoot(), "tests", "measure", "x733c", "controls", "c2b_qmark.sql");
        var c2bDst = Path.Combine(MigrationGateDb.RunnerSqlDir(), ProbeMigrationFile);
        File.Copy(c2bSrc, c2bDst, overwrite: true);
        try
        {
            var bytes = await File.ReadAllBytesAsync(c2bDst);
            var (c2b, c2brec) = await RunOnceAsync(runner, logger, factory, quiet, ProbeMigrationId, 1);
            report["control2b_qmark"] = new
            {
                note = "기록만 — 판정하지 않는다(작업지시서 §5 ②′).",
                file = FileFacts(ProbeMigrationFile, bytes),
                applied = c2b.AppliedMigrationIds.Contains(ProbeMigrationId, StringComparer.OrdinalIgnoreCase),
                result = c2brec,
            };
        }
        finally
        {
            File.Delete(c2bDst);
            await quiet.ExecuteAsync("DELETE FROM schema_migrations WHERE migration_id = @Id;", new { Id = ProbeMigrationId });
        }

        // ── 확장 모양(표·칸 형·널·DEFAULT·COMMENT·인덱스) — schema_migrations 제외 ──
        report["erp_shape"] = await ShapeAsync(quiet, ErpDbName, "schema_migrations");

        report["erp"] = new
        {
            measured,
            applied,
            all_count = all.Count,
            known_not_reapplyable = MigrationGateDb.KnownNotReapplyable,
            broken_ids = brokenIds,
            broken,
            runner_threads = factory.Threads.Count,
            log_filter_rule = "mysql.general_log 에서 command_type='Query' 이고 thread_id 가 이 시험의 러너 연결 "
                            + "ServerThread 집합에 든 행만 · 측정 연결은 sql_log_off=1 · 회차마다 TRUNCATE 뒤 실행",
            files,
        };

        return (brokenIds, measured, all.Count);
    }

    private static async Task<(MigrationRunResult Result, object Record)> RunOnceAsync(
        MigrationRunner runner, CapturingLogger logger, ProbeConnFactory factory,
        MySqlConnection quiet, string id, int pass)
    {
        await quiet.ExecuteAsync("TRUNCATE TABLE mysql.general_log;");
        logger.Reset();
        var r = await runner.ApplyPendingAsync("w13-gate");
        var rows = await ReadLogAsync(quiet);

        var queries = rows.Where(x => x.CommandType == "Query" && factory.Threads.Contains(x.ThreadId))
                          .Select(x => new { sha256 = Sha256(x.Argument), len = x.Argument.Length })
                          .ToList();
        var otherCommands = rows.Where(x => factory.Threads.Contains(x.ThreadId) && x.CommandType != "Query")
                                .GroupBy(x => x.CommandType)
                                .ToDictionary(g => g.Key, g => g.Count());

        var ex = logger.Last;
        var record = new
        {
            id,
            pass,
            ok = r.Success,
            applied = r.AppliedMigrationIds.Contains(id, StringComparer.OrdinalIgnoreCase),
            applied_count = r.AppliedMigrationIds.Count,
            failed_id = r.FailedMigrationId,
            runner_msg1 = r.Success ? null : FirstLine(r.FailureMessage),
            ex_type = ex?.GetType().FullName,
            err_no = (ex as MySqlException)?.Number,
            sqlstate = (ex as MySqlException)?.SqlState,
            ex_msg1 = ex is null ? null : FirstLine(ex.Message),
            log_queries = queries,
            log_other_commands = otherCommands,
        };
        return (r, record);
    }

    // ══════════════════════════════════════════════════════════════
    // 백오피스 SchemaMigrator(빈 DB) + 대조군 ②(합성 · 살았나)
    // ══════════════════════════════════════════════════════════════

    private static async Task MeasureBackofficeAndControl2Async(
        CellEnv cell, Dictionary<string, object?> report, List<string> deviceErrors)
    {
        await MySqlConnection.ClearAllPoolsAsync();

        await using var quiet = await QuietOpenAsync(cell, database: null);
        if (cell.Kind == "mysql8")
        {
            report["server"] = await ServerFactsAsync(quiet, deviceErrors);
        }

        await quiet.ExecuteAsync($"DROP DATABASE IF EXISTS `{BoDbName}`; "
            + $"CREATE DATABASE `{BoDbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
        try
        {
            var boDir = Path.Combine(ErpCsGateDb.RepoRoot(), "installer", "backoffice");
            report["dir_recount_backoffice"] = Recount(boDir, "*.sql");

            // ── 대조군 ② — 러너와 같은 성질(AllowUserVariables=true) 연결 · 매개변수 P=7 ──
            //    🔴 ERP DB(gate_x733c)는 MeasureErpAsync 끝에서 이미 지워졌다 ⇒ 연결 성질은 러너와 같게,
            //       붙는 DB 만 방금 만든 빈 백오피스 DB 로 둔다(문장이 표를 안 읽어 결과에 영향 0).
            report["control2_param"] = await Control2Async(quiet, ErpConnString(cell, BoDbName));

            // ── 백오피스 — 실물 SchemaMigrator.ApplyAsync · 폴더 = 기존 MySQL 8 게이트와 같은 installer/backoffice ──
            await quiet.ExecuteAsync("TRUNCATE TABLE mysql.general_log;");
            object? boError = null;
            try
            {
                await new SchemaMigrator(BoConnString(cell, BoDbName), boDir, NullLogger<SchemaMigrator>.Instance)
                    .ApplyAsync();
            }
            catch (Exception ex)
            {
                // #15 — 적용기 실패는 결과다. 기록하고 다음 항목(이력·모양)을 잰다.
                boError = Describe(ex);
                Console.Error.WriteLine($"[X733c] 백오피스 적용 실패: {ex.GetType().FullName}: {FirstLine(ex.Message)}");
            }
            var rows = await ReadLogAsync(quiet);
            var boQueries = rows.Where(x => x.CommandType == "Query")
                                .Select(x => new { sha256 = Sha256(x.Argument), len = x.Argument.Length })
                                .ToList();

            var applied = (await quiet.QueryAsync<(string FileName, string ContentHash)>(
                    $"SELECT file_name, content_hash FROM `{BoDbName}`.`_schema_migrations` ORDER BY file_name;"))
                .ToList();

            report["backoffice"] = new
            {
                applied_count = applied.Count,
                file_names = applied.Select(a => a.FileName).ToList(),
                content_hash = applied.ToDictionary(a => a.FileName, a => a.ContentHash, StringComparer.Ordinal),
                error = boError,
                log_filter_rule = "mysql.general_log 에서 command_type='Query' 전부 · 측정 연결은 sql_log_off=1 · "
                                + "적용 직전 연결 풀 비움 + TRUNCATE ⇒ 남는 것은 SchemaMigrator 연결뿐(파일별이 아니라 적용 전체의 문장 순서)",
                log_queries = boQueries,
            };
            report["backoffice_shape"] = await ShapeAsync(quiet, BoDbName, "_schema_migrations");
        }
        finally
        {
            await quiet.ExecuteAsync($"DROP DATABASE IF EXISTS `{BoDbName}`;");
        }
    }

    private static async Task<object> Control2Async(MySqlConnection quiet, string cs)
    {
        var path = Path.Combine(ErpCsGateDb.RepoRoot(), "tests", "measure", "x733c", "controls", "c2_param.sql");
        var bytes = await File.ReadAllBytesAsync(path);
        var sql = await File.ReadAllTextAsync(path, Encoding.UTF8);

        await quiet.ExecuteAsync("TRUNCATE TABLE mysql.general_log;");
        object? a = null, b = null;
        object? error = null;
        long thread;
        await using (var c = new MySqlConnection(cs))
        {
            await c.OpenAsync();
            thread = c.ServerThread;
            try
            {
                await using var cmd = new MySqlCommand(sql, c);
                cmd.Parameters.AddWithValue("@P", 7);
                await using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    for (var i = 0; i < r.FieldCount; i++)
                    {
                        var v = r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture);
                        if (r.GetName(i) == "a") a = v;
                        else if (r.GetName(i) == "b") b = v;
                    }
                }
            }
            catch (Exception ex)
            {
                // #15 — 대조군의 예외는 결과다(2.6.2 × LF 에서 다를 수 있는 자리). 기록한다.
                error = Describe(ex);
                Console.Error.WriteLine($"[X733c] 대조군 ② 예외: {ex.GetType().FullName}: {FirstLine(ex.Message)}");
            }
        }

        var received = (await ReadLogAsync(quiet))
            .Where(x => x.ThreadId == thread && x.CommandType == "Query")
            .Select(x => Encoding.UTF8.GetString(x.Argument))
            .ToList();

        var outcome = error is not null ? "error"
                    : $"a={a ?? "NULL"},b={b ?? "NULL"}";
        return new
        {
            file = FileFacts("c2_param.sql", bytes),
            parameter = "P=7",
            a,
            b,
            error,
            outcome,
            server_received = received,
        };
    }

    // ══════════════════════════════════════════════════════════════
    // 공용 — 연결 · 로그 · 모양 · 서버 전제 · 파일 수 세기
    // ══════════════════════════════════════════════════════════════

    private sealed record LogRow(long ThreadId, string CommandType, byte[] Argument);

    private static async Task<List<LogRow>> ReadLogAsync(MySqlConnection quiet)
    {
        // CSV 엔진 로그 표 — ORDER BY 없이 읽으면 쓰인 순서다.
        var rows = new List<LogRow>();
        await using var cmd = new MySqlCommand(
            "SELECT thread_id, command_type, CAST(argument AS BINARY) FROM mysql.general_log;", quiet);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            rows.Add(new LogRow(
                Convert.ToInt64(r.GetValue(0), CultureInfo.InvariantCulture),
                r.GetString(1),
                r.IsDBNull(2) ? Array.Empty<byte>() : (byte[])r.GetValue(2)));
        }
        return rows;
    }

    private static async Task<object> ShapeAsync(MySqlConnection quiet, string schema, string excludeTable)
    {
        var tables = await quiet.QueryAsync<string>(@"
            SELECT CONCAT('표 ', TABLE_NAME)
              FROM INFORMATION_SCHEMA.TABLES
             WHERE TABLE_SCHEMA = @S AND TABLE_NAME <> @X", new { S = schema, X = excludeTable });

        var cols = await quiet.QueryAsync<string>(@"
            SELECT CONCAT('칸 ', TABLE_NAME, '.', COLUMN_NAME,
                          ' : ', COLUMN_TYPE,
                          ' : ', IS_NULLABLE,
                          ' : 기본 ', IFNULL(COLUMN_DEFAULT, '<NULL>'),
                          ' : 설명 ', IFNULL(COLUMN_COMMENT, ''))
              FROM INFORMATION_SCHEMA.COLUMNS
             WHERE TABLE_SCHEMA = @S AND TABLE_NAME <> @X", new { S = schema, X = excludeTable });

        var idx = await quiet.QueryAsync<string>(@"
            SELECT CONCAT('인덱스 ', TABLE_NAME, '.', INDEX_NAME, ' : 고유아님 ', MAX(NON_UNIQUE),
                          ' : ', GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX SEPARATOR ','))
              FROM INFORMATION_SCHEMA.STATISTICS
             WHERE TABLE_SCHEMA = @S AND TABLE_NAME <> @X
             GROUP BY TABLE_NAME, INDEX_NAME", new { S = schema, X = excludeTable });

        var lines = tables.Concat(cols).Concat(idx).OrderBy(x => x, StringComparer.Ordinal).ToList();
        return new
        {
            excluded_table = excludeTable,
            count = lines.Count,
            sha256 = Sha256(Encoding.UTF8.GetBytes(string.Join("\n", lines))),
            lines,
        };
    }

    private static async Task<object> ServerFactsAsync(MySqlConnection quiet, List<string> deviceErrors)
    {
        var facts = new Dictionary<string, string?>(StringComparer.Ordinal);
        string[] names =
        [
            "VERSION()", "@@version_comment", "@@sql_mode", "@@character_set_server", "@@collation_server",
            "@@max_connections", "@@general_log", "@@log_output", "@@lower_case_table_names", "@@time_zone",
        ];
        foreach (var n in names)
        {
            var v = await quiet.ExecuteScalarAsync<object?>($"SELECT {n};");
            facts[n] = v is null or DBNull ? null : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        var logCol = (await quiet.QueryAsync<(string? DataType, string? Charset)>(@"
            SELECT DATA_TYPE, CHARACTER_SET_NAME FROM INFORMATION_SCHEMA.COLUMNS
             WHERE TABLE_SCHEMA = 'mysql' AND TABLE_NAME = 'general_log' AND COLUMN_NAME = 'argument'")).FirstOrDefault();

        if (facts["@@general_log"] is not ("1" or "ON"))
            deviceErrors.Add($"general_log 이 켜져 있지 않다({facts["@@general_log"]}) — 바이트 대조(K4)를 못 잰다");
        if (facts["@@log_output"]?.Contains("TABLE", StringComparison.OrdinalIgnoreCase) != true)
            deviceErrors.Add($"log_output 에 TABLE 이 없다({facts["@@log_output"]}) — 바이트 대조(K4)를 못 잰다");

        return new
        {
            vars = facts,
            general_log_argument = new { data_type = logCol.DataType, charset = logCol.Charset },
        };
    }

    private static List<object> Recount(string dir, string pattern)
    {
        var list = new List<object>();
        foreach (var f in Directory.GetFiles(dir, pattern).OrderBy(Path.GetFileName, StringComparer.Ordinal))
            list.Add(FileFacts(Path.GetFileName(f), File.ReadAllBytes(f)));
        return list;
    }

    private static object FileFacts(string name, byte[] data)
    {
        int lf = 0, cr = 0, crlf = 0;
        for (var i = 0; i < data.Length; i++)
        {
            if (data[i] == (byte)'\n') lf++;
            else if (data[i] == (byte)'\r')
            {
                cr++;
                if (i + 1 < data.Length && data[i + 1] == (byte)'\n') crlf++;
            }
        }
        return new { name, bytes = data.Length, lf, cr, crlf, sha256 = Sha256(data) };
    }

    private static object RuntimeAssembly()
    {
        var asm = typeof(MySqlConnection).Assembly;
        return new
        {
            informational_version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            file_version = asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version,
            assembly_version = asm.GetName().Version?.ToString(),
            file_name = Path.GetFileName(asm.Location),
        };
    }

    /// <summary>
    /// 🔴 서버 지문 — DB 에 아무것도 쓰기 전에 부른다([4] 권고 2). 읽기 SELECT 하나뿐이다
    /// (DROP·CREATE·SET·TRUNCATE 0). <c>general_log=ON</c> 이고 <c>log_output=TABLE</c> 이 아니면
    /// 칸 컨테이너가 아니므로 거부한다 — 개발 PC 3306 실물에 칸 환경 변수를 손으로 넣어도 여기서 멈춘다.
    /// </summary>
    private static async Task RefuseUnlessMeasureServerAsync(CellEnv cell)
    {
        string? generalLog;
        string? logOutput;
        await using (var c = new MySqlConnection(cell.ServerConnString("DefaultCommandTimeout=30;Pooling=false;")))
        {
            await c.OpenAsync();
            await using var cmd = new MySqlCommand("SELECT @@general_log, @@log_output;", c);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
            {
                Assert.Fail("[X733c] 실행 거부 — 서버 지문을 못 읽었다(DB 쓰기 0으로 멈췄다)");
            }
            generalLog = r.IsDBNull(0) ? null : Convert.ToString(r.GetValue(0), CultureInfo.InvariantCulture);
            logOutput = r.IsDBNull(1) ? null : Convert.ToString(r.GetValue(1), CultureInfo.InvariantCulture);
        }

        var why = new List<string>();
        if (generalLog is not ("1" or "ON")) why.Add($"@@general_log='{generalLog}' (칸 컨테이너는 ON)");
        if (!string.Equals(logOutput, "TABLE", StringComparison.OrdinalIgnoreCase))
            why.Add($"@@log_output='{logOutput}' (칸 컨테이너는 TABLE)");
        if (why.Count > 0)
        {
            Assert.Fail("[X733c] 실행 거부 — 서버 지문이 칸 컨테이너가 아니다(DB 쓰기 0으로 멈췄다):\n  "
                      + string.Join("\n  ", why));
        }
    }

    /// <summary>측정 연결 — 풀 안 씀 · sql_log_off=1 ⇒ 로그에 이 시험의 손길이 안 남는다.</summary>
    private static async Task<MySqlConnection> QuietOpenAsync(CellEnv cell, string? database)
    {
        var cs = cell.ServerConnString("DefaultCommandTimeout=180;GuidFormat=None;AllowUserVariables=true;Pooling=false;")
               + (database is null ? string.Empty : $"Database={database};");
        var c = new MySqlConnection(cs);
        await c.OpenAsync();
        await c.ExecuteAsync("SET SESSION sql_log_off = 1;");
        return c;
    }

    /// <summary>러너 연결 — <c>MigrationGateDb.DbConnString</c> 과 같은 성질(<c>AllowUserVariables=true</c>).</summary>
    private static string ErpConnString(CellEnv cell, string db) =>
        cell.ServerConnString("DefaultCommandTimeout=180;GuidFormat=None;AllowUserVariables=true;") + $"Database={db};";

    /// <summary>백오피스 연결 — 기존 MySQL 8 게이트(<c>Mysql8GateDb.ConnString</c>)와 같은 성질.</summary>
    private static string BoConnString(CellEnv cell, string db) =>
        cell.ServerConnString("DefaultCommandTimeout=90;GuidFormat=None;AllowUserVariables=true;") + $"Database={db};";

    private static void WriteJson(string path, Dictionary<string, object?> report)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(report, options), new UTF8Encoding(false));
    }

    private static object Describe(Exception ex) => new
    {
        ex_type = ex.GetType().FullName,
        err_no = (ex as MySqlException)?.Number,
        sqlstate = (ex as MySqlException)?.SqlState,
        msg1 = FirstLine(ex.Message),
    };

    private static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static string FirstLine(string? s) =>
        string.IsNullOrWhiteSpace(s) ? "(사유 없음)" : s.Split('\n')[0].Trim();

    /// <summary>러너에 꽂는 연결 공장 — 연결 성질은 G-W13 의 TestConnFactory 와 같고, ServerThread 만 적어 둔다.</summary>
    private sealed class ProbeConnFactory : IMigrationDbConnectionFactory
    {
        private readonly string _cs;
        public ProbeConnFactory(string cs) => _cs = cs;
        public HashSet<long> Threads { get; } = new();

        public async Task<DbConnection> CreateOpenAsync(CancellationToken ct = default)
        {
            var c = new MySqlConnection(_cs);
            await c.OpenAsync(ct);
            Threads.Add(c.ServerThread);
            return c;
        }
    }

    /// <summary>러너가 <c>LogError(ex, …)</c> 로 넘기는 예외를 잡아 오류 번호·예외 형을 남긴다(판정에 안 씀 · 기록만).</summary>
    private sealed class CapturingLogger : ILogger<MigrationRunner>
    {
        public Exception? Last { get; private set; }
        public void Reset() => Last = null;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (exception is not null) Last = exception;
        }
    }

    /// <summary>
    /// 칸 환경 — 하나라도 어긋나면 DB 에 붙기 전에 <b>실패</b>한다(SKIP 아님 · 실행 거부).
    /// </summary>
    private sealed record CellEnv(string Id, string Kind, string Driver, string Eol, string OutDir,
        string Host, string Port, string User, string Pass)
    {
        public string ServerConnString(string tail) =>
            $"Server={Host};Port={Port};User={User};Password={Pass};" + tail;

        /// <summary>🔴 record 기본 ToString 은 비번까지 찍는다 — 칸 이름만 내보낸다(PM 조건 ③).</summary>
        public override string ToString() => Id;

        public static CellEnv ReadOrRefuse()
        {
            static string Env(string k) => Environment.GetEnvironmentVariable(k) ?? string.Empty;

            var why = new List<string>();
            if (Env("GITHUB_ACTIONS") != "true") why.Add("GITHUB_ACTIONS ≠ true (GitHub Actions 칸이 아니다)");
            if (Env("CI") != "true") why.Add("CI ≠ true");
            if (!DbGateEnvironment.IsCi) why.Add("HITPAN_REQUIRE_DB 가 참이 아니다");
            var id = Env("HITPAN_X733C_CELL");
            if (id.Length == 0) why.Add("HITPAN_X733C_CELL 없음");
            var kind = Env("HITPAN_X733C_KIND");
            if (kind is not ("mariadb" or "mysql8")) why.Add($"HITPAN_X733C_KIND='{kind}' (mariadb|mysql8)");
            var driver = Env("HITPAN_X733C_DRIVER");
            if (driver is not ("2.5.0" or "2.6.2")) why.Add($"HITPAN_X733C_DRIVER='{driver}' (2.5.0|2.6.2)");
            var eol = Env("HITPAN_X733C_EOL");
            if (eol is not ("lf" or "crlf")) why.Add($"HITPAN_X733C_EOL='{eol}' (lf|crlf)");
            var outDir = Env("HITPAN_X733C_OUT");
            if (outDir.Length == 0 || !Directory.Exists(outDir)) why.Add("HITPAN_X733C_OUT 폴더 없음");
            // [4] 권고 2 — 산출물 자리는 러너 임시 폴더(RUNNER_TEMP) 아래만 허용.
            var runnerTemp = Env("RUNNER_TEMP");
            if (runnerTemp.Length == 0 || !Directory.Exists(runnerTemp))
            {
                why.Add("RUNNER_TEMP 없음 (GitHub Actions 러너가 아니다)");
            }
            else if (outDir.Length > 0)
            {
                var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(runnerTemp)) + Path.DirectorySeparatorChar;
                var full = Path.GetFullPath(outDir);
                if (!full.StartsWith(root, StringComparison.Ordinal))
                    why.Add("HITPAN_X733C_OUT 이 RUNNER_TEMP 아래가 아니다");
            }
            if (Env("HITPAN_X733C_DB") != ErpDbName) why.Add($"HITPAN_X733C_DB ≠ {ErpDbName}");
            var host = Env("HITPAN_DB_HOST");
            if (host != "127.0.0.1") why.Add("HITPAN_DB_HOST ≠ 127.0.0.1 (칸 안 컨테이너만 허용)");
            if (Env("HITPAN_DB_PASS").Length == 0) why.Add("HITPAN_DB_PASS 없음 (런 안 난수여야 한다)");

            if (why.Count > 0)
            {
                Assert.Fail("[X733c] 실행 거부 — 이 시험은 계측 가지의 격리 CI 칸에서만 돈다(DB 접속 0으로 멈췄다):\n  "
                          + string.Join("\n  ", why));
            }

            if (!id.StartsWith($"{kind}-{driver}-{eol}", StringComparison.Ordinal))
                Assert.Fail($"[X733c] 실행 거부 — 칸 이름 '{id}' 이 종류·판·줄끝과 안 맞는다");

            // 드라이버 판 대조는 여기서 하지 않는다(기록만) — 판정은 compare_cells.py 가 증거 4종으로 한다.
            return new CellEnv(id, kind, driver, eol, outDir, host,
                Env("HITPAN_DB_PORT") is { Length: > 0 } p ? p : "3306",
                Env("HITPAN_DB_USER") is { Length: > 0 } u ? u : "root",
                Env("HITPAN_DB_PASS"));
        }
    }
}
