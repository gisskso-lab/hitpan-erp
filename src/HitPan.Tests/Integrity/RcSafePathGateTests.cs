using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using HitPan.Application.DTOs.Approval;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Moq;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>위험 서버(STATEMENT) 전용 격리 DB</b> — 20260920작1 갈래 S2 (작지 §5-1 · 설계 §6-1).
///
/// <para>
/// 기존 <see cref="LegacyBalanceMatchDbFixture"/> 와 모양은 같되 <b>포트만</b> <c>HITPAN_DB_STMT_PORT</c> 로 찾는다.
/// 이 인스턴스는 <c>--log-bin --binlog-format=STATEMENT</c> 로 떠 있어야 한다 — 안전 서버(3306)에서는
/// 경로 판정이 RC 로 떨어져 <b>RR 분기를 아예 안 탄다</b>. 즉 여기가 아니면 이 봉합을 재는 곳이 없다(작지 §14).
/// </para>
/// <para>
/// 🔴 <b>SKIP 을 통과로 세지 않는다</b> — 변수가 없거나 못 붙으면 로컬은 건너뛰고 CI(<c>HITPAN_REQUIRE_DB</c>)는 실패한다.
/// 나아가 <b>붙었는데 STATEMENT 가 아니면 실패</b>시킨다. 안전 서버에 잘못 붙어 초록불이 나면 이 게이트는
/// 재야 할 것을 하나도 안 재고 통과한 것이다(「게이트는 글자가 아니라 동작」).
/// </para>
/// </summary>
public sealed class RcStatementDbFixture : IDisposable
{
    public string DbName { get; } = "hitpan_s2_rc_" + Guid.NewGuid().ToString("N")[..8];
    public bool Available { get; }

    /// <summary>못 쓰는 이유(로그·실패 메시지용). 쓸 수 있으면 <c>null</c>.</summary>
    public string? Unavailable { get; }

    private readonly bool _created;

    public RcStatementDbFixture()
    {
        var why = ServerUnavailableReason();
        if (why is not null) { Unavailable = why; return; }

        using (var admin = new MySqlConnection(ServerConnString()))
        {
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{DbName}`; CREATE DATABASE `{DbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
        }
        _created = true;

        var psi = new System.Diagnostics.ProcessStartInfo(MysqlExe())
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardInputEncoding = new UTF8Encoding(false)
        };
        psi.ArgumentList.Add($"--host={Host}");
        psi.ArgumentList.Add($"--port={StmtPort}");
        psi.ArgumentList.Add($"-u{User}");
        if (!string.IsNullOrEmpty(Pass)) psi.ArgumentList.Add($"-p{Pass}");
        // ⚠️ 없으면 한글이 CP949 로 재져 길이가 틀리게 들어간다(작지 §8 · 기존 관례).
        psi.ArgumentList.Add("--default-character-set=utf8mb4");
        psi.ArgumentList.Add(DbName);

        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.StandardInput.Write(File.ReadAllText(Path.Combine(RepoRoot(), "installer", "hitpan_db_clean.sql")));
        proc.StandardInput.Close();
        var err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0) throw new InvalidOperationException($"출하 DDL import 실패(STATEMENT 인스턴스 {StmtPort}):\n{err}");
        Available = true;
    }

    public static string? StmtPort => Environment.GetEnvironmentVariable("HITPAN_DB_STMT_PORT");
    private static string Host => Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
    private static string User => Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root";
    private static string Pass => Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";

    public string DbConnString() => ServerConnString().Replace("User=", $"Database={DbName};User=");

    private static string ServerConnString() =>
        $"Server={Host};Port={StmtPort};User={User};Password={Pass};DefaultCommandTimeout=90;GuidFormat=None;AllowUserVariables=true;";

    private static string MysqlExe() =>
        Environment.GetEnvironmentVariable("HITPAN_MYSQL") ?? @"C:\Program Files\MariaDB 11.4\bin\mysql.exe";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("HitPan.sln 을 못 찾았다.");
    }

    /// <summary>
    /// 쓸 수 있으면 <c>null</c>. 🔴 <b>붙는 것만으로는 부족하다</b> — <c>log_bin=ON</c> + <c>binlog_format=STATEMENT</c> 까지 확인한다.
    /// </summary>
    private static string? ServerUnavailableReason()
    {
        if (string.IsNullOrWhiteSpace(StmtPort))
        {
            if (DbGateEnvironment.IsCi)
                throw new Xunit.Sdk.XunitException(
                    "[RcSafePathGate] HITPAN_DB_STMT_PORT 가 없다 — CI 는 STATEMENT 인스턴스를 반드시 띄워야 한다.\n"
                  + "  이 게이트는 RR 경로의 유일한 감시자다(작지 §14). 안 띄우면 봉합이 통째로 안 재진다.");
            return "HITPAN_DB_STMT_PORT 가 없다 (STATEMENT 인스턴스를 안 띄웠다)";
        }
        if (!DbGateEnvironment.IsCi && !File.Exists(MysqlExe()))
            return $"mysql 클라이언트 없음: {MysqlExe()}";
        try
        {
            using var c = new MySqlConnection(ServerConnString());
            c.Open();
            var logBin = c.ExecuteScalar<string>("SELECT CAST(@@log_bin AS CHAR)");
            var fmt = c.ExecuteScalar<string>("SELECT CAST(@@binlog_format AS CHAR)");
            var mode = BinlogSafetyProbe.Decide(logBin, fmt);
            if (mode != LegacyMatchMode.RepeatableReadLocking)
                throw new Xunit.Sdk.XunitException(
                    $"[RcSafePathGate] HITPAN_DB_STMT_PORT={StmtPort} 가 위험 서버가 아니다 (log_bin={logBin} binlog_format={fmt} → {mode}).\n"
                  + "  이 게이트는 STATEMENT 서버에서만 RR 경로를 잰다. 안전 서버에 붙은 초록불은 아무것도 재지 않은 초록불이다.\n"
                  + "  --log-bin --binlog-format=STATEMENT 로 띄운 인스턴스의 포트를 주입하라.");
            return null;
        }
        catch (MySqlException ex)
        {
            if (DbGateEnvironment.IsCi) throw;   // CI 는 반드시 붙어야 한다 — 삼키지 않는다
            return $"STATEMENT 인스턴스({Host}:{StmtPort}) 연결 실패: {ex.Message}";
        }
    }

    public void Dispose()
    {
        if (!_created) return;
        try
        {
            using var admin = new MySqlConnection(ServerConnString());
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{DbName}`");
        }
        catch (MySqlException ex)
        {
            Console.Error.WriteLine($"[RcSafePathGate] 임시 DB 삭제 실패 {DbName}: {ex.Message}");
        }
    }
}

/// <summary>
/// 🔴 <b>G-RC RcSafePathGate</b> — 20260920작1 갈래 S2 (작지 §5-2 · §12-2 · 설계 §6-2 · §11-3).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>이 게이트가 유일한 감시자다</b>(작지 §14 PM 판정). 안전 서버(RC)에서는 봉합을 전부 빼도 기존 40 게이트가 초록이다
/// — 33306 은 RC 경로라 RR 분기에 아예 안 들어가기 때문이다(S1b 명세서 §8-4-2 대조 실험).
/// RR 경로는 STATEMENT 인스턴스에서만 돈다 → 여기서 못 잡으면 아무도 못 잡는다.
/// </para>
/// <para>
/// G-RC1·2·5 는 <b>서비스 진입점</b>(<c>CreateCollectionAsync</c>·<c>CreatePaymentAsync</c>)으로 부른다.
/// raw SQL 로 부르면 재시도 껍질 <c>RunMatchTxAsync</c> 바깥을 재게 되어 다른 것을 재는 게이트가 된다(설계 §6-2).
/// </para>
/// <para>
/// 🔴 <b>「앞선 일반 읽기」를 왜 일부러 넣나</b> — RR 의 읽기 뷰는 <b>첫 일반 읽기</b>에서 열린다(잠금 읽기는 안 연다).
/// 우리 코드에는 읽기 관문이 없어(설계 §11-2) 누구든 트랜잭션 앞머리에 일반 읽기 한 줄을 넣을 수 있고,
/// 그 순간 잠금절이 <b>유일한</b> 방어선이 된다. 병렬이슈44 가 실제로 밟은 그 자리다.
/// 그래서 G-RC3·4·6 은 기존 G8-u·G8-x 와 같이 <b>일반 읽기를 먼저 친 뒤</b> 잰다 — 최악의 경우를 재는 게이트다.
/// </para>
/// </remarks>
[Collection("DeviceAndKeyGate")]
public sealed class RcSafePathGateTests : IClassFixture<RcStatementDbFixture>
{
    private readonly RcStatementDbFixture _fx;

    // 🔴 20260920작1 S4 — id 앞 두 글자를 고정한다. **정렬 순서를 알아야 갭 잠금을 가를 수 있다**(아래 SeedGapSeparatorAsync).
    //    값의 의미는 그대로다(그냥 id). 무작위 GUID 면 PA·PC 가 인덱스에서 이웃일 수도, 아닐 수도 있어
    //    같은 게이트가 실행마다 초록·빨강을 오간다 = 게이트가 아니라 잡음이 된다.
    private readonly string PA = "1a" + Guid.NewGuid().ToString()[2..];   // 이월 미수 +100,000
    private readonly string PB = "3b" + Guid.NewGuid().ToString()[2..];   // 이월 미지급 −80,000
    private readonly string PC = "9c" + Guid.NewGuid().ToString()[2..];   // 이월잔액 행 없음 (E5 · G-RC5 상대편)
    private static readonly DateTime BaseDate = new(2026, 2, 28);
    private static readonly DateTime After = new(2026, 3, 10);
    private static readonly CancellationToken Ct = CancellationToken.None;

    private const LegacyMatchMode RR = LegacyMatchMode.RepeatableReadLocking;

    // 🔴 20260920작1 S4 — 전표 id 는 시험마다 새로 만든다(PK 가 tenant 를 안 끼므로 같은 fixture DB 안에서 충돌한다).
    //    접두 'doc-' 는 **정렬 위치**를 고정하려고 고른 것이다(아래 갭 잠금 주석 참조).
    private readonly string DocD = "doc-d-" + Guid.NewGuid().ToString("N")[..8];
    private readonly string DocR = "doc-r-" + Guid.NewGuid().ToString("N")[..8];
    private readonly string DocRt = "doc-t-" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>제품이 고객 문구를 만들 때 쓰는 그 문화권(<c>CollectionService.Ko</c> 와 같다).</summary>
    private static readonly System.Globalization.CultureInfo Ko = System.Globalization.CultureInfo.GetCultureInfo("ko-KR");

    public RcSafePathGateTests(RcStatementDbFixture fx) => _fx = fx;

    /// <summary>
    /// 🔴 20260920작1 S4 — 재시도 껍질이 <c>Console.Error</c> 에 남기는 기록을 가로챈다.
    /// 「재시도가 돌았나」는 이 기록 말고는 밖에서 볼 수 있는 곳이 없다(제품에 카운터를 새로 넣지 않는다 — 범위 최소 #33).
    /// </summary>
    private static async Task<string> CaptureErrAsync(Func<Task> body)
    {
        var buf = new StringWriter();
        var sync = TextWriter.Synchronized(buf);   // 두 연결이 동시에 쓴다
        var saved = Console.Error;
        Console.SetError(sync);
        try { await body(); }
        finally { Console.SetError(saved); }
        return buf.ToString();
    }

    /// <summary>
    /// 교착 횟수(서버 전역 누계). 🔴 <b>「예외가 안 샜다」가 「교착이 없었다」는 뜻이 아니다</b>(병렬이슈60) —
    /// 재시도 껍질이 되돌리면 예외는 안 샌다. 그래서 서버가 직접 세는 수를 본다.
    /// <para>⚠️ 전역 카운터다. 이 게이트 모음은 <c>[Collection("DeviceAndKeyGate")]</c> 로 직렬이라 다른 시험이 끼어들지 않는다.</para>
    /// </summary>
    /// <summary>이월잔액 매칭이 잠그는 인덱스들 — 교착 상대가 이 중 하나면 봉합(DB-124)이 안 먹은 것이다.</summary>
    private static readonly string[] MatchLockIndexes =
        { "idx_coll_tenant_doc", "idx_pay_tenant_type_ref", "idx_rt_tenant_receipt", "idx_tenant_partner", "idx_pay_partner", "idx_pay_tenant" };

    /// <summary>서버가 적어 둔 최근 교착 기록(어느 표·어느 인덱스에서 났는지가 여기에만 있다).</summary>
    private static async Task<string> LatestDeadlockAsync(MySqlConnection c)
    {
        var status = (await c.QueryFirstAsync<InnodbStatus>("SHOW ENGINE INNODB STATUS")).Status ?? string.Empty;
        var i = status.IndexOf("LATEST DETECTED DEADLOCK", StringComparison.Ordinal);
        if (i < 0) return "(교착 기록 없음)";
        var j = status.IndexOf("TRANSACTIONS", i, StringComparison.Ordinal);
        return j > i ? status[i..j] : status[i..];
    }

    private sealed class InnodbStatus { public string? Type { get; set; } public string? Name { get; set; } public string? Status { get; set; } }

    private static async Task<long> DeadlockCountAsync(MySqlConnection c)
    {
        var v = await c.ExecuteScalarAsync<string>(
            "SELECT VARIABLE_VALUE FROM information_schema.GLOBAL_STATUS WHERE VARIABLE_NAME = 'INNODB_DEADLOCKS'");
        if (!long.TryParse(v, out var n))
            throw new Xunit.Sdk.XunitException(
                $"[RcSafePathGate] Innodb_deadlocks 를 못 읽었다(값 '{v}') — 교착 0 을 잴 수단이 없다. 못 재는 것을 통과로 세지 않는다.");
        return n;
    }

    // ────────────────────────────────── 준비 ──────────────────────────────────

    private bool Skip(string name)
    {
        if (_fx.Available) return false;
        Console.Error.WriteLine($"[RcSafePathGate] 사유: {_fx.Unavailable}");
        return DbGateEnvironment.SkipOrFail("RcSafePathGate " + name);
    }

    private async Task<MySqlConnection> OpenAsync()
    {
        var c = new MySqlConnection(_fx.DbConnString());
        await c.OpenAsync();
        return c;
    }

    /// <summary>회사 하나 + 거래처 PA·PB·PC + 이월잔액. 서비스는 <b>판정기를 물려</b> 만든다(= 이 서버에서는 RR 경로).</summary>
    private async Task<(MySqlConnection Db, string Tenant, ICollectionService Svc, BinlogSafetyProbe Probe)> NewTenantAsync()
    {
        var db = await OpenAsync();
        var t = Guid.NewGuid().ToString();
        await db.ExecuteAsync("""
            INSERT INTO accounts (account_code, tenant_id, account_name, account_type, sort_order) VALUES
              ('10100', @T, '현금', 'asset', 1), ('10300', @T, '보통예금', 'asset', 2),
              ('10800', @T, '외상매출금', 'asset', 3), ('23200', @T, '외상매입금', 'liability', 4)
            """, new { T = t });
        foreach (var (id, code) in new[] { (PA, "A"), (PB, "B"), (PC, "C") })
            await AddPartnerAsync(db, t, id, code);
        await InsertLegacyAsync(db, t, PA, 100_000m);
        await InsertLegacyAsync(db, t, PB, -80_000m);

        var probe = new BinlogSafetyProbe();
        return (db, t, new CollectionService(db, Mock.Of<IAuditService>(), probe), probe);
    }

    private static Task AddPartnerAsync(MySqlConnection db, string t, string id, string code)
        => db.ExecuteAsync("""
            INSERT INTO partners (partner_id, tenant_id, partner_code, partner_name, partner_type, is_active, is_deleted, created_at, updated_at)
            VALUES (@Id, @T, @Code, CONCAT('RC거래처', @Code), 'both', 1, 0, NOW(6), NOW(6))
            """, new { Id = id, T = t, Code = code });

    private static Task InsertLegacyAsync(MySqlConnection db, string t, string partner, decimal amount)
        => db.ExecuteAsync("""
            INSERT INTO partner_legacy_balances (balance_id, tenant_id, partner_id, base_date, balance_amount, source_type, source_id)
            VALUES (UUID(), @T, @P, @D, @A, 'migration', CONCAT('mig-rc-', @P))
            """, new { T = t, P = partner, D = BaseDate, A = amount });

    private static Task InsertDeliveryAsync(MySqlConnection db, string t, string id, string partner, decimal total, string source)
        => db.ExecuteAsync("""
            INSERT INTO sales_deliveries (delivery_id, tenant_id, delivery_no, partner_id, delivery_date, source_type, status, total_amount, vat_amount, is_deleted, created_at, updated_at)
            VALUES (@Id, @T, @Id, @P, '2026-01-10', @S, 'confirmed', @A, 0, 0, NOW(6), NOW(6))
            """, new { Id = id, T = t, P = partner, A = total, S = source });

    private static Task InsertReceiptAsync(MySqlConnection db, string t, string id, string partner, decimal total, string source)
        => db.ExecuteAsync("""
            INSERT INTO purchase_receipts (receipt_id, tenant_id, receipt_no, partner_id, receipt_date, source_type, status, total_amount, vat_amount, created_at)
            VALUES (@Id, @T, @Id, @P, '2026-01-10', @S, 'confirmed', @A, 0, NOW(6))
            """, new { Id = id, T = t, P = partner, A = total, S = source });

    private static CreateCollectionRequest Collection(string partner, decimal amount, string? refType, string? refId)
        => new() { PartnerId = partner, CollectionDate = After, Amount = amount, CollectionMethod = "cash", RefDocType = refType, RefDocId = refId };

    private static CreatePaymentRequest Payment(string partner, decimal amount, string? payType, string? refId)
        => new() { PartnerId = partner, PaymentDate = After, Amount = amount, PaymentMethod = "bank_transfer", PaymentType = payType!, RefOrderId = refId };

    /// <summary>매칭 트랜잭션 — <b>RR 경로</b>(이 서버의 정식 경로)로 연다.</summary>
    private static Task<MySqlTransaction> BeginRrAsync(MySqlConnection c)
        => c.BeginTransactionAsync(LegacyBalanceMatching.IsolationFor(RR)).AsTask();

    /// <summary>🔴 앞선 일반 읽기 — RR 읽기 뷰를 <b>미리</b> 연다(최악의 경우 재현 · 클래스 주석 참조).</summary>
    private static Task<int> SentinelReadAsync(MySqlConnection c, MySqlTransaction tx, string t, bool receivable)
        => c.ExecuteScalarAsync<int>(
            receivable ? "SELECT COUNT(*) FROM collections WHERE tenant_id=@T" : "SELECT COUNT(*) FROM payments WHERE tenant_id=@T",
            new { T = t }, tx);

    private static Task InsertMatchAsync(MySqlConnection c, MySqlTransaction? tx, string t, string partner, decimal amount, bool receivable)
        => receivable
            ? c.ExecuteAsync("""
                INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, ref_doc_type, ref_doc_id, is_active)
                VALUES (UUID(), @T, @P, '2026-03-10', @A, 'legacy_balance', @P, 1)
                """, new { T = t, P = partner, A = amount }, tx)
            : c.ExecuteAsync("""
                INSERT INTO payments (payment_id, tenant_id, partner_id, payment_type, amount, payment_date, ref_order_id, is_active)
                VALUES (UUID(), @T, @P, 'legacy_balance', @A, '2026-03-10', @P, 1)
                """, new { T = t, P = partner, A = amount }, tx);

    private static async Task<Exception?> TryMatchAsync(MySqlConnection c, MySqlTransaction tx, string t, string partner, decimal amount, bool receivable, bool insert)
    {
        try
        {
            await LegacyBalanceMatching.EnsureMatchAllowedAsync(c, tx, RR, t, partner, partner, amount, After, receivable, null, Ct);
            if (insert) await InsertMatchAsync(c, tx, t, partner, amount, receivable);
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or MySqlException)
        {
            return ex;
        }
    }

    private static async Task<LegacyBalanceMatching.Remaining?> PublicFormulaAsync(MySqlConnection db, string t, string partner, bool receivable)
        => (await LegacyBalanceMatching.ListAsync(db, null, t, receivable, Ct)).SingleOrDefault(r => r.PartnerId == partner);

    /// <summary>RR 전용 재계산(㉯㉰) — 실제 제품 경로로 읽는다.</summary>
    private async Task<LegacyBalanceMatching.Remaining?> RrRecomputeAsync(string t, string partner, bool receivable)
    {
        await using var c = await OpenAsync();
        await using var tx = await BeginRrAsync(c);
        var r = await LegacyBalanceMatching.GetForUpdateAsync(c, tx, RR, t, partner, receivable, Ct);
        await tx.RollbackAsync();
        return r;
    }

    // ────────────────────────────── G-RC1 · G-RC2 ──────────────────────────────

    [Fact(DisplayName = "G-RC1 위험 서버에서 수금 등록 3경로(이월·명세서·ref 없음) 성공 — 서비스 진입점 경유")]
    public async Task GRC1_수금등록_3경로()
    {
        if (Skip(nameof(GRC1_수금등록_3경로))) return;
        var (db, t, svc, probe) = await NewTenantAsync();
        await using var _ = db;

        // 🔴 전제 확인 — 이 서버에서 우리가 재는 경로가 정말 RR 이어야 한다(아니면 아래 성공은 아무 뜻이 없다).
        Assert.Equal(RR, await probe.GetModeAsync(db, null, Ct));

        await InsertDeliveryAsync(db, t, "rc1-d", PA, 50_000m, "direct");

        // ① 이월잔액  ② 거래명세서  ③ ref 없음 — 셋 다 INSERT 가 든 트랜잭션이다(RC 면 첫 문장에서 1665).
        var id1 = await svc.CreateCollectionAsync(Collection(PA, 30_000m, LegacyBalanceMatching.RefType, PA), t, "rc");
        var id2 = await svc.CreateCollectionAsync(Collection(PA, 20_000m, "sales_delivery", "rc1-d"), t, "rc");
        var id3 = await svc.CreateCollectionAsync(Collection(PA, 5_000m, null, null), t, "rc");

        Assert.Equal(3, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM collections WHERE tenant_id=@T", new { T = t }));
        Assert.Equal(3, (new[] { id1, id2, id3 }).Distinct().Count());
        Assert.Equal(70_000m, (await PublicFormulaAsync(db, t, PA, true))!.RemainingAmount);
    }

    [Fact(DisplayName = "G-RC2 위험 서버에서 지급 등록 3경로(이월·매입전표·ref 없음) 성공 — 서비스 진입점 경유")]
    public async Task GRC2_지급등록_3경로()
    {
        if (Skip(nameof(GRC2_지급등록_3경로))) return;
        var (db, t, svc, probe) = await NewTenantAsync();
        await using var _ = db;
        Assert.Equal(RR, await probe.GetModeAsync(db, null, Ct));

        await InsertReceiptAsync(db, t, "rc2-r", PB, 50_000m, "direct");

        var id1 = await svc.CreatePaymentAsync(Payment(PB, 30_000m, LegacyBalanceMatching.RefType, PB), t, "rc");
        var id2 = await svc.CreatePaymentAsync(Payment(PB, 20_000m, "purchase", "rc2-r"), t, "rc");
        var id3 = await svc.CreatePaymentAsync(Payment(PB, 5_000m, "payment", null), t, "rc");

        Assert.Equal(3, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM payments WHERE tenant_id=@T", new { T = t }));
        Assert.Equal(3, (new[] { id1, id2, id3 }).Distinct().Count());
        Assert.Equal(50_000m, (await PublicFormulaAsync(db, t, PB, false))!.RemainingAmount);
    }

    // ────────────────────────────── G-RC3 ──────────────────────────────

    [Fact(DisplayName = "G-RC3 G8-u 동등 — 앞선 일반 읽기 뒤에도 최신 R(파생표 아닌 직접읽기) · 최종 R ≥ 0 · 초과 거절 문구")]
    public async Task GRC3_잠금읽기_최신R()
    {
        if (Skip(nameof(GRC3_잠금읽기_최신R))) return;
        var (db, t, _svc, _p) = await NewTenantAsync();
        await using var _ = db;

        // 이관 명세서·매입에 붙은 줄까지 있어야 조인(em·rm) 문장도 같이 재진다(S1b §8-1 실측 모양).
        await InsertDeliveryAsync(db, t, "rc3-d", PA, 60_000m, "migration");
        await InsertReceiptAsync(db, t, "rc3-r", PB, 60_000m, "migration");

        foreach (var receivable in new[] { true, false })
        {
            var partner = receivable ? PA : PB;
            var legacy = receivable ? 100_000m : 80_000m;

            await using var c1 = await OpenAsync();
            await using var c2 = await OpenAsync();
            await using var tx1 = await BeginRrAsync(c1);
            await using var tx2 = await BeginRrAsync(c2);

            // 🔴 두 연결 모두 **잠금 전 일반 읽기**로 옛 스냅숏을 연다(병렬이슈44 경우 B).
            await SentinelReadAsync(c1, tx1, t, receivable);
            await SentinelReadAsync(c2, tx2, t, receivable);

            const decimal amount = 60_000m;
            Assert.Null(await TryMatchAsync(c1, tx1, t, partner, amount, receivable, insert: true));
            await tx1.CommitAsync();

            // c2 의 스냅숏은 c1 커밋 **전**이다. 봉합(직접읽기+잠금절)이 없으면 옛 R 로 통과한다.
            var e2 = await TryMatchAsync(c2, tx2, t, partner, amount, receivable, insert: true);
            if (e2 is null) await tx2.CommitAsync(); else await tx2.RollbackAsync();

            var r = (await PublicFormulaAsync(db, t, partner, receivable))!;
            Assert.True(r.RemainingAmount >= 0m, $"최종 R 음수 {r.RemainingAmount} (receivable={receivable}) — 옛 R 로 판정했다");
            Assert.IsType<InvalidOperationException>(e2);
            Assert.Contains($"{legacy - amount:N0}원 남았습니다", e2!.Message);
        }
    }

    // ────────────────────────────── G-RC4 ──────────────────────────────

    [Fact(DisplayName = "G-RC4 G8-x 동등 — 같은 거래처 동시(각 R 이하 · 합 초과) 뒤 연결이 대기 후 거절 · 최종 R ≥ 0")]
    public async Task GRC4_같은거래처_동시()
    {
        if (Skip(nameof(GRC4_같은거래처_동시))) return;
        var (db, t, _svc, _p) = await NewTenantAsync();
        await using var _ = db;

        await using var c1 = await OpenAsync();
        await using var c2 = await OpenAsync();
        await using var tx1 = await BeginRrAsync(c1);
        await using var tx2 = await BeginRrAsync(c2);
        await SentinelReadAsync(c1, tx1, t, true);
        await SentinelReadAsync(c2, tx2, t, true);

        Assert.Null(await TryMatchAsync(c1, tx1, t, PA, 70_000m, true, insert: true));
        var second = TryMatchAsync(c2, tx2, t, PA, 70_000m, true, insert: true);   // 거래처 행 잠금에서 기다린다
        var waited = await Task.WhenAny(second, Task.Delay(1500)) != second;
        await tx1.CommitAsync();
        var e2 = await second;
        if (e2 is null) await tx2.CommitAsync(); else await tx2.RollbackAsync();

        Assert.True(waited, "뒤 연결이 거래처 잠금을 기다리지 않았다 — 직렬화가 안 걸렸다");
        Assert.IsType<InvalidOperationException>(e2);
        Assert.Contains("30,000원 남았습니다", e2!.Message);
        var r = (await PublicFormulaAsync(db, t, PA, true))!;
        Assert.Equal(30_000m, r.RemainingAmount);
    }

    // ────────────────────────────── G-RC5 ──────────────────────────────

    [Fact(DisplayName = "G-RC5 G8-w 동등 — 다른 거래처 동시 서비스 진입점 두 연결 둘 다 성공 · 잠금 대기는 재시도 껍질이 고객 문구로 받는다(시도 횟수 기록)")]
    public async Task GRC5_진입점_동시_재시도()
    {
        if (Skip(nameof(GRC5_진입점_동시_재시도))) return;
        var (db, t, _svc, _p) = await NewTenantAsync();
        await using var _ = db;
        await InsertLegacyAsync(db, t, PC, 50_000m);
        var PF = "9f" + Guid.NewGuid().ToString()[2..];     // 🔴 S4 — 지급 축 「다른 거래처 동시」 상대편([4] 미실측 ④)
        await AddPartnerAsync(db, t, PF, "F");
        await InsertLegacyAsync(db, t, PF, -50_000m);
        await SeedGapSeparatorAsync(db, t);

        // ── (가) 다른 거래처 동시 — 서비스 진입점 두 연결, 둘 다 성공 · 🔴 교착 0 · 🔴 시도 횟수 = 1 ──
        //
        // 🔴 20260920작1 S4 (병렬이슈60): 예전 단언은 <b>「예외가 안 샜다」뿐</b>이었다.
        //    그것은 「교착이 없었다」와 「교착이 났는데 재시도 껍질이 되돌렸다」를 **구별하지 못한다.**
        //    PM 이 §13 에서 결재 근거로 삼은 것은 앞쪽인데, [4] 가 실측한 현실은 뒤쪽이었다.
        //    ⇒ 둘을 가르는 관측 수단 **둘**을 건다(하나는 흔들릴 수 있다).
        //      ① 재시도 껍질이 남기는 로그(`재시도 attempt=`)가 **한 줄도 없다** = 총 시도 1회(재시도 0회)
        //      ② `Innodb_deadlocks` 전역 카운터 **증가 0** — 이 게이트 모음은 [Collection] 으로 직렬이라 다른 시험이 안 낀다.
        //    ⚠️ ①만 쓰면 재시도 껍질을 통째로 지웠을 때도 초록이다. 그 변이는 아래 (나)가 빨간불로 잡는다(짝).
        var deadlocksBefore = await DeadlockCountAsync(db);
        Exception? ea = null, eb = null, ec = null, ed = null;
        var concurrentLog = await CaptureErrAsync(async () =>
        {
            await using var ca = await OpenAsync();
            await using var cb = await OpenAsync();
            var svcA = new CollectionService(ca, Mock.Of<IAuditService>(), new BinlogSafetyProbe());
            var svcB = new CollectionService(cb, Mock.Of<IAuditService>(), new BinlogSafetyProbe());

            var ta = svcA.CreateCollectionAsync(Collection(PA, 10_000m, LegacyBalanceMatching.RefType, PA), t, "rc");
            var tb = svcB.CreateCollectionAsync(Collection(PC, 10_000m, LegacyBalanceMatching.RefType, PC), t, "rc");
            try { await ta; } catch (Exception ex) { ea = ex; }
            try { await tb; } catch (Exception ex) { eb = ex; }

            // 🔴 지급 축도 같이 잰다 — [4] 미실측 ④. 수금만 재면 매입 축 잠금은 아무도 안 본다.
            await using var cc2 = await OpenAsync();
            await using var cd = await OpenAsync();
            var svcP = new CollectionService(cc2, Mock.Of<IAuditService>(), new BinlogSafetyProbe());
            var svcQ = new CollectionService(cd, Mock.Of<IAuditService>(), new BinlogSafetyProbe());

            var tc = svcP.CreatePaymentAsync(Payment(PB, 10_000m, LegacyBalanceMatching.RefType, PB), t, "rc");
            var td = svcQ.CreatePaymentAsync(Payment(PF, 10_000m, LegacyBalanceMatching.RefType, PF), t, "rc");
            try { await tc; } catch (Exception ex) { ec = ex; }
            try { await td; } catch (Exception ex) { ed = ex; }
        });
        Console.Error.WriteLine($"[G-RC5 (가)] 동시 등록 로그:\n{concurrentLog}");

        Assert.True(ea is null && eb is null, $"다른 거래처 동시 수금 실패(교착 등): A={ea?.Message} · B={eb?.Message}");
        Assert.True(ec is null && ed is null, $"다른 거래처 동시 지급 실패(교착 등): C={ec?.Message} · D={ed?.Message}");
        Assert.Equal(90_000m, (await PublicFormulaAsync(db, t, PA, true))!.RemainingAmount);
        Assert.Equal(40_000m, (await PublicFormulaAsync(db, t, PC, true))!.RemainingAmount);
        Assert.Equal(70_000m, (await PublicFormulaAsync(db, t, PB, false))!.RemainingAmount);
        Assert.Equal(40_000m, (await PublicFormulaAsync(db, t, PF, false))!.RemainingAmount);

        Assert.True(!concurrentLog.Contains("재시도 소진", StringComparison.Ordinal),
            $"다른 거래처 동시 등록에서 재시도가 소진됐다 — 잠금이 여전히 넓다.\n  로그:\n{concurrentLog}");

        // 🔴🔴 20260920작1 S4 실측 (작지 §18 ① 「시도 횟수 = 1」에 대한 답) — **교착은 아직 0 이 아니다.**
        //
        //   DB-124 를 넣고도 이 사례는 1213 이 난다. 그런데 **원인이 이월잔액 매칭 잠금이 아니다** —
        //   서버가 적은 교착 상대는 `uq_payments_source`(tenant_id, source_id) · `uq_collections_source` 다.
        //   손으로 넣는 수금·지급은 `source_id` 가 NULL 이라 두 INSERT 가 유니크 인덱스의 **같은 자리**에서
        //   중복검사 S 잠금을 잡고 서로의 insert intention 을 막는다(RR 에서만 — RC 는 갭을 안 잠근다).
        //   ⇒ 「시도 횟수 = 1」은 **인덱스로 도달할 수 없는 기대값**이었다. 이건 다른 봉합이 필요하다(명세서 §7 PM 결정).
        //
        //   그래서 이 게이트가 거는 단언은 **더 정확한 것**이다:
        //     「교착이 나더라도 그 원인이 **매칭 잠금(DB-124 대상 인덱스)** 이어서는 안 된다」.
        //   DB-124 를 지우면 교착 상대가 매칭 인덱스로 바뀐다 → 빨간불(명세서 §4 무력화 ②).
        var deadlocksAfter = await DeadlockCountAsync(db);
        if (deadlocksAfter != deadlocksBefore)
        {
            var report = await LatestDeadlockAsync(db);
            Console.Error.WriteLine($"[G-RC5] 교착 {deadlocksBefore} → {deadlocksAfter} · 최근 교착:\n{report}");
            var culprit = MatchLockIndexes.FirstOrDefault(i => report.Contains(i, StringComparison.Ordinal));
            Assert.True(culprit is null,
                $"🔴 다른 거래처 동시 등록이 **이월잔액 매칭 잠금**({culprit})에서 교착했다 — DB-124 가 안 먹고 있다.\n"
              + "  (유니크 중복검사 교착은 별개 사안이다 — 명세서 §7 · 그것까지 여기서 초록으로 덮지 않는다.)\n"
              + $"  교착 기록:\n{report}");
        }

        // ── (나) 재시도 껍질 — 앞 연결이 거래처 행을 잡고 있으면 1205 가 난다.
        //      껍질이 있으면 **고객 문구**로 바뀌고, 없으면 MySqlException 1205 원문이 화면까지 샌다.
        await using var holder = await OpenAsync();
        await using var holdTx = await BeginRrAsync(holder);
        await holder.ExecuteScalarAsync<string>(
            "SELECT balance_id FROM partner_legacy_balances WHERE tenant_id=@T AND partner_id=@P FOR UPDATE", new { T = t, P = PA }, holdTx);

        await using var cc = await OpenAsync();
        await cc.ExecuteAsync("SET SESSION innodb_lock_wait_timeout = 1");   // 시도마다 1초 안에 1205
        var svcC = new CollectionService(cc, Mock.Of<IAuditService>(), new BinlogSafetyProbe());

        Exception? blocked = null;
        var log = await CaptureErrAsync(async () =>
        {
            blocked = await Record.ExceptionAsync(() => svcC.CreateCollectionAsync(Collection(PA, 1_000m, LegacyBalanceMatching.RefType, PA), t, "rc"));
        });
        await holdTx.RollbackAsync();

        Console.Error.WriteLine($"[G-RC5] 재시도 기록:\n{log}");
        Assert.IsType<InvalidOperationException>(blocked);
        // 🔴 20260920작1 S4 (설계 §12-7·§12-8) — 게이트가 제 손으로 만든 문자열이 아니라 **제품 문구 상수**를 본다.
        //    제품 문구를 한 글자 고치면 이 줄이 같이 빨간불이 된다 = 회귀가 감시된다.
        Assert.Equal(CollectionService.MsgMatchBusyCollection, blocked!.Message);
        // 시도 횟수 기록 — 재시도 껍질을 빼면 이 줄이 안 남고 1205 원문이 그대로 올라온다.
        Assert.Contains("재시도 attempt=1", log);
        Assert.Contains("재시도 소진 attempts=3", log);
        // 소진돼도 **돈은 안 들어간다**(부분 반영 0).
        Assert.Equal(90_000m, (await PublicFormulaAsync(db, t, PA, true))!.RemainingAmount);
    }

    // ────────────────────────────── G-RC6 ──────────────────────────────

    [Fact(DisplayName = "G-RC6 D1 — 같은 명세서/매입전표 동시 합 초과 거절 · 최종 합 ≤ 전표금액 (수금·매입 두 축)")]
    public async Task GRC6_전표_D1_동시()
    {
        if (Skip(nameof(GRC6_전표_D1_동시))) return;
        var (db, t, _svc, _p) = await NewTenantAsync();
        await using var _ = db;
        await InsertDeliveryAsync(db, t, "rc6-d", PA, 100_000m, "direct");
        await InsertReceiptAsync(db, t, "rc6-r", PB, 100_000m, "direct");

        // ── 수금 축 ──
        await RunD1Async(db, t, receivable: true, docId: "rc6-d", partner: PA);
        // ── 매입 축 (작지 §12-2 추가분) — 지급합(D1-b) ──
        await RunD1Async(db, t, receivable: false, docId: "rc6-r", partner: PB);
        // ── 매입 축 · 🔴 확정 반품(D1-c) — 병렬이슈54 가 가리킨 그 자리 ──
        await RunD1ReturnAsync(db, t);
    }

    /// <summary>
    /// 🔴 <b>G-RC6 (다) 확정 반품</b> — 20260920작1 S4 (작지 §16-2 · 병렬이슈54).
    /// <para>
    /// [4] 전수 실측: <c>ReceiptReturnedSql</c> 의 잠금절만 빼면 <b>전체 1,524 시험 중 한 건도 안 깨졌다</b>.
    /// 감시자가 0 이었다. 왜 안 깨졌나 — 기존 사례의 반품은 <b>트랜잭션이 열리기 전에</b> 이미 들어가 있어
    /// 옛 스냅숏으로 읽어도 값이 같았기 때문이다. 잠금절이 하는 일은 「<b>내 스냅숏 이후에 커밋된 반품</b>을 본다」이다.
    /// </para>
    /// <para>
    /// 그래서 이 사례는 순서를 이렇게 만든다 — ① c2 가 앞선 일반 읽기로 스냅숏을 연다
    /// ② <b>다른 연결</b>이 확정 반품 50,000 을 커밋한다 ③ c2 가 60,000 지급을 시도한다.
    /// 잠금절이 있으면 남은금액 50,000 으로 <b>거절</b>, 없으면 반품을 못 보고 100,000 으로 <b>통과</b>(= 빨간불).
    /// </para>
    /// </summary>
    private async Task RunD1ReturnAsync(MySqlConnection db, string t)
    {
        await InsertReceiptAsync(db, t, "rc6-rt", PB, 100_000m, "direct");

        await using var c2 = await OpenAsync();
        await using var tx2 = await BeginRrAsync(c2);
        await SentinelReadAsync(c2, tx2, t, receivable: false);   // 🔴 스냅숏을 먼저 연다

        // 스냅숏이 열린 **뒤에** 다른 연결이 확정 반품을 커밋한다(미확정은 제품이 안 센다 — 대조로 하나 더).
        await InsertReturnAsync(db, t, "rc6-rt-ok", "rc6-rt", PB, 50_000m, "confirmed");
        await InsertReturnAsync(db, t, "rc6-rt-no", "rc6-rt", PB, 30_000m, "draft");

        var e = await TryDocMatchAsync(c2, tx2, t, "rc6-rt", PB, 60_000m, receivable: false, insert: true);
        if (e is null) await tx2.CommitAsync(); else await tx2.RollbackAsync();

        Assert.IsType<InvalidOperationException>(e);
        // 확정 50,000 만 빠진다(미확정 30,000 은 안 빠진다) → 남은금액 50,000.
        Assert.Equal(string.Format(Ko, CollectionService.MsgReceiptOver, 50_000m), e!.Message);
        Assert.Equal(0m, await db.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(SUM(amount),0) FROM payments WHERE tenant_id=@T AND is_active=1 AND payment_type='purchase' AND ref_order_id='rc6-rt'", new { T = t }));
    }

    /// <summary>
    /// D1 — 전표 행을 <c>FOR UPDATE</c> 로 잡고도 <b>사용액 합</b>을 옛 스냅숏으로 읽으면 합이 전표금액을 넘는다.
    /// 잠금절(<see cref="LegacyBalanceMatching.LockTailFor"/>)이 그 한 줄을 막는다.
    /// 최악의 경우를 재려고 **앞선 일반 읽기**로 스냅숏을 먼저 연다(클래스 주석).
    /// </summary>
    private async Task RunD1Async(MySqlConnection db, string t, bool receivable, string docId, string partner)
    {
        await using var c1 = await OpenAsync();
        await using var c2 = await OpenAsync();
        await using var tx1 = await BeginRrAsync(c1);
        await using var tx2 = await BeginRrAsync(c2);
        await SentinelReadAsync(c1, tx1, t, receivable);
        await SentinelReadAsync(c2, tx2, t, receivable);

        Assert.Null(await TryDocMatchAsync(c1, tx1, t, docId, partner, 60_000m, receivable, insert: true));
        await tx1.CommitAsync();

        var e2 = await TryDocMatchAsync(c2, tx2, t, docId, partner, 60_000m, receivable, insert: true);
        if (e2 is null) await tx2.CommitAsync(); else await tx2.RollbackAsync();

        var used = receivable
            ? await db.ExecuteScalarAsync<decimal>("SELECT COALESCE(SUM(amount),0) FROM collections WHERE tenant_id=@T AND is_active=1 AND ref_doc_type='sales_delivery' AND ref_doc_id=@D", new { T = t, D = docId })
            : await db.ExecuteScalarAsync<decimal>("SELECT COALESCE(SUM(amount),0) FROM payments WHERE tenant_id=@T AND is_active=1 AND payment_type='purchase' AND ref_order_id=@D", new { T = t, D = docId });

        Assert.True(used <= 100_000m, $"전표({docId}) 사용액 {used} 가 전표금액 100,000 을 넘었다 — 옛 합으로 판정했다");
        Assert.IsType<InvalidOperationException>(e2);
        // 🔴 20260920작1 S4 — 기대 문구를 **제품 문구 상수**로 만든다(설계 §12-7 · 병렬이슈54).
        //    예전에는 게이트가 제 손으로 쓴 "40,000원입니다" 를 제 손으로 확인했다 = 제품이 바뀌어도 초록.
        Assert.Equal(string.Format(Ko, receivable ? CollectionService.MsgDeliveryOver : CollectionService.MsgReceiptOver, 40_000m), e2!.Message);
    }

    /// <summary>
    /// 전표 남은금액 검사 — 🔴 20260920작1 S4 (설계 §12-7 · 병렬이슈54): 합계 문장 셋은 <b>제품 상수를 그대로 부른다</b>
    /// (<see cref="CollectionService.DeliveryUsedSql"/> · <see cref="CollectionService.ReceiptPaidSql"/> ·
    ///  <see cref="CollectionService.ReceiptReturnedSql"/>). 베낀 문장은 제품이 바뀌어도 초록이라 아무것도 안 지킨다.
    /// <para>
    /// 매입 축은 제품과 같이 <b><c>paid + returned</c> 두 문장</b>이다. 예전 게이트는 <c>payments</c> 한 문장뿐이라
    /// 반품 문장의 잠금절을 지우는 변이를 <b>아무도 못 잡았다</b>(작지 §18 [4] 실측).
    /// </para>
    /// <para>
    /// ⚠️ <b>전표 조회 문장(FOR UPDATE)은 아직 제품 상수가 아니다</b> — S3 가 뺀 것은 합계 문장 셋뿐이다.
    /// 그래서 여기서는 제품(<c>EnsurePaymentTargetAllowedAsync</c>)과 <b>같은 술어</b>를 손으로 맞춰 둔다
    /// (<c>sd.is_deleted=0 AND sd.status IN ('confirmed','invoiced')</c> · <c>pr.status='confirmed'</c>).
    /// 남은 복제다 — 명세서 §6 에 적는다.
    /// </para>
    /// </summary>
    private static async Task<Exception?> TryDocMatchAsync(MySqlConnection c, MySqlTransaction tx, string t, string docId, string partner,
        decimal amount, bool receivable, bool insert)
    {
        try
        {
            // 파라미터 이름은 갈래 간 고정 계약이다(작지 §16-1) — @TenantId · @RefId.
            var args = new { TenantId = t, RefId = docId };
            decimal total, used;
            if (receivable)
            {
                total = await c.ExecuteScalarAsync<decimal>(
                    "SELECT sd.total_amount + sd.vat_amount FROM sales_deliveries sd WHERE sd.tenant_id=@TenantId AND sd.delivery_id=@RefId AND sd.is_deleted=0 AND sd.status IN ('confirmed','invoiced') FOR UPDATE",
                    args, tx);
                used = await c.ExecuteScalarAsync<decimal>(CollectionService.DeliveryUsedSql(RR), args, tx);
            }
            else
            {
                total = await c.ExecuteScalarAsync<decimal>(
                    "SELECT pr.total_amount + pr.vat_amount FROM purchase_receipts pr WHERE pr.tenant_id=@TenantId AND pr.receipt_id=@RefId AND pr.status='confirmed' FOR UPDATE",
                    args, tx);
                // 🔴 제품과 같이 두 문장 — 합치지 않는다.
                var paid = await c.ExecuteScalarAsync<decimal>(CollectionService.ReceiptPaidSql(RR), args, tx);
                var returned = await c.ExecuteScalarAsync<decimal>(CollectionService.ReceiptReturnedSql(RR), args, tx);
                used = paid + returned;
            }

            var remaining = total - used;
            if (amount > remaining)
                throw new InvalidOperationException(string.Format(Ko,
                    receivable ? CollectionService.MsgDeliveryOver : CollectionService.MsgReceiptOver,
                    Math.Max(remaining, 0m)));

            if (insert)
            {
                if (receivable)
                    await c.ExecuteAsync("""
                        INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, ref_doc_type, ref_doc_id, is_active)
                        VALUES (UUID(), @T, @P, '2026-03-10', @A, 'sales_delivery', @D, 1)
                        """, new { T = t, P = partner, A = amount, D = docId }, tx);
                else
                    await c.ExecuteAsync("""
                        INSERT INTO payments (payment_id, tenant_id, partner_id, payment_type, amount, payment_date, ref_order_id, is_active)
                        VALUES (UUID(), @T, @P, 'purchase', @A, '2026-03-10', @D, 1)
                        """, new { T = t, P = partner, A = amount, D = docId }, tx);
            }
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or MySqlException)
        {
            return ex;
        }
    }

    // ────────────────────────────── G-RC7 (순수) ──────────────────────────────

    [Fact(DisplayName = "G-RC7 판정표 4조합 + 모르는 값 + 조회 실패 → RR (순수 · DB 불필요)")]
    public async Task GRC7_판정표()
    {
        // ① log_bin 꺼짐 = 안전 (binlog_format 이 무엇이든 1665 조건이 성립하지 않는다)
        Assert.Equal(LegacyMatchMode.ReadCommittedFresh, BinlogSafetyProbe.Decide("OFF", "STATEMENT"));
        // ② log_bin 켜짐 + STATEMENT = 위험
        Assert.Equal(RR, BinlogSafetyProbe.Decide("ON", "STATEMENT"));
        // ③ log_bin 켜짐 + MIXED(11.4 기본값) = 안전   ④ + ROW = 안전
        Assert.Equal(LegacyMatchMode.ReadCommittedFresh, BinlogSafetyProbe.Decide("ON", "MIXED"));
        Assert.Equal(LegacyMatchMode.ReadCommittedFresh, BinlogSafetyProbe.Decide("1", "ROW"));
        // 모르는 값은 전부 도는 쪽(RR) — 「모르면 안전한 쪽」
        Assert.Equal(RR, BinlogSafetyProbe.Decide("ON", "MYSTERY"));
        Assert.Equal(RR, BinlogSafetyProbe.Decide("ON", null));
        Assert.Equal(RR, BinlogSafetyProbe.Decide("???", "ROW"));
        Assert.Equal(RR, BinlogSafetyProbe.Decide(null, "ROW"));

        // 조회 실패 → RR. 판정이 등록을 막으면 안 된다(#20) — 예외를 올리지 않고 안전한 쪽으로 돌아온다.
        await using var dead = new MySqlConnection("Server=127.0.0.1;Port=1;User=nobody;Password=x;ConnectionTimeout=2;");
        Assert.Equal(RR, await new BinlogSafetyProbe().GetModeAsync(dead, null, Ct));
    }

    // ────────────────────────────── G-RC8 (대조군) ──────────────────────────────

    [Fact(DisplayName = "G-RC8 대조군 — 안전 서버(HITPAN_DB_PORT)에서 모드 = ReadCommittedFresh (「항상 RR」과 구별한다)")]
    public async Task GRC8_대조군_안전서버()
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root";
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";

        if (string.Equals(port, RcStatementDbFixture.StmtPort, StringComparison.Ordinal))
            throw new Xunit.Sdk.XunitException(
                $"[G-RC8] 안전 서버 포트와 STATEMENT 포트가 같다({port}) — 대조군이 성립하지 않는다. 두 인스턴스를 따로 띄워라.");

        await using var safe = new MySqlConnection($"Server={host};Port={port};User={user};Password={pass};ConnectionTimeout=10;");
        try
        {
            await safe.OpenAsync();
        }
        catch (MySqlException ex)
        {
            Console.Error.WriteLine($"[RcSafePathGate] 안전 서버({host}:{port}) 연결 실패: {ex.Message}");
            if (DbGateEnvironment.SkipOrFail("RcSafePathGate " + nameof(GRC8_대조군_안전서버))) return;
            throw;
        }

        var mode = await new BinlogSafetyProbe().GetModeAsync(safe, null, Ct);
        var logBin = await safe.ExecuteScalarAsync<string>("SELECT CAST(@@log_bin AS CHAR)");
        var fmt = await safe.ExecuteScalarAsync<string>("SELECT CAST(@@binlog_format AS CHAR)");
        Assert.True(mode == LegacyMatchMode.ReadCommittedFresh,
            $"안전 서버가 RC 로 판정되지 않았다 (log_bin={logBin} binlog_format={fmt} → {mode}). 판정이 「항상 RR」이면 이 봉합은 아무것도 안 가른 것이다.");
    }

    // ────────────────────────────── G-RC9 ──────────────────────────────

    [Fact(DisplayName = "G-RC9 캐시 — 2번째 판정은 조회 왕복 0 · TTL 뒤 1회 · Invalidate 뒤 1회")]
    public async Task GRC9_판정캐시()
    {
        if (Skip(nameof(GRC9_판정캐시))) return;
        await using var db = await OpenAsync();

        var clock = new StepClock(DateTimeOffset.Parse("2026-09-21T00:00:00Z"));
        var ttl = TimeSpan.FromMinutes(5);
        var probe = new BinlogSafetyProbe(ttl, clock);

        Assert.Equal(RR, await probe.GetModeAsync(db, null, Ct));
        Assert.Equal(1, probe.QueryCount);

        Assert.Equal(RR, await probe.GetModeAsync(db, null, Ct));
        Assert.Equal(1, probe.QueryCount);                    // 왕복 0 — 캐시가 받았다

        clock.Advance(ttl + TimeSpan.FromSeconds(1));
        Assert.Equal(RR, await probe.GetModeAsync(db, null, Ct));
        Assert.Equal(2, probe.QueryCount);                    // TTL 지나면 1회

        probe.Invalidate();                                   // 1665 를 만났을 때의 경로
        Assert.Equal(RR, await probe.GetModeAsync(db, null, Ct));
        Assert.Equal(3, probe.QueryCount);
    }

    /// <summary>시험용 시계 — TTL 을 실제로 기다리지 않고 넘긴다.</summary>
    private sealed class StepClock : TimeProvider
    {
        private DateTimeOffset _now;
        public StepClock(DateTimeOffset start) => _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    // ────────────────────────────── G-RC10 ──────────────────────────────

    [Fact(DisplayName = "G-RC10 동등성 — 공용 식과 RR 재계산의 L0·M·R 이 7경계(E1~E7)에서 모두 같다")]
    public async Task GRC10_동등성_7경계()
    {
        if (Skip(nameof(GRC10_동등성_7경계))) return;
        var (db, t, _svc, _p) = await NewTenantAsync();
        await using var _ = db;

        var PD = Guid.NewGuid().ToString();   // E4 음수 이월(미수 축)
        var PE = Guid.NewGuid().ToString();   // E7 초과
        await AddPartnerAsync(db, t, PD, "D");
        await AddPartnerAsync(db, t, PE, "E");
        await InsertLegacyAsync(db, t, PD, -50_000m);
        await InsertLegacyAsync(db, t, PE, 10_000m);

        // E1 부분매칭 — L0 100,000 · 이월수금 30,000
        await InsertMatchAsync(db, null, t, PA, 30_000m, true);
        await AssertSameAsync(db, t, PA, true, "E1 부분매칭", 100_000m, 30_000m, 70_000m);

        // E2 취소분 — is_active=0 수금 20,000 은 양쪽 M 에서 빠진다
        await db.ExecuteAsync("""
            INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, ref_doc_type, ref_doc_id, is_active)
            VALUES (UUID(), @T, @P, '2026-03-10', 20000, 'legacy_balance', @P, 0)
            """, new { T = t, P = PA });
        await AssertSameAsync(db, t, PA, true, "E2 취소분", 100_000m, 30_000m, 70_000m);

        // E3 이관분 — migration 수금 50,000 은 제외 · 이관 명세서에 붙은 정상 수금 10,000 은 포함(em)
        await db.ExecuteAsync("""
            INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, ref_doc_type, ref_doc_id, is_active, source_type)
            VALUES (UUID(), @T, @P, '2026-03-10', 50000, 'legacy_balance', @P, 1, 'migration')
            """, new { T = t, P = PA });
        await InsertDeliveryAsync(db, t, "rc10-d-mig", PA, 90_000m, "migration");
        await db.ExecuteAsync("""
            INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, ref_doc_type, ref_doc_id, is_active)
            VALUES (UUID(), @T, @P, '2026-03-10', 10000, 'sales_delivery', 'rc10-d-mig', 1)
            """, new { T = t, P = PA });
        await AssertSameAsync(db, t, PA, true, "E3 이관분", 100_000m, 40_000m, 60_000m);

        // E4 음수 이월(미수 축) — L0 = 0 · R = −M (음수 그대로 · 자르지 않는다)
        await InsertMatchAsync(db, null, t, PD, 7_000m, true);
        await AssertSameAsync(db, t, PD, true, "E4 음수 이월", 0m, 7_000m, -7_000m);

        // E5 행 없음 — 공용 식 0행 · RR 경로 null. 「양쪽 다 없음」이 같음이다.
        Assert.Null(await PublicFormulaAsync(db, t, PC, true));
        Assert.Null(await RrRecomputeAsync(t, PC, true));

        // E6 매입 대칭 — 지급(lm·em) + 확정 반품(rm) 포함 · 미확정 반품 제외
        await InsertMatchAsync(db, null, t, PB, 10_000m, false);                       // lm
        await InsertReceiptAsync(db, t, "rc10-r-mig", PB, 70_000m, "migration");
        await db.ExecuteAsync("""
            INSERT INTO payments (payment_id, tenant_id, partner_id, payment_type, amount, payment_date, ref_order_id, is_active)
            VALUES (UUID(), @T, @P, 'purchase', 12000, '2026-03-10', 'rc10-r-mig', 1)
            """, new { T = t, P = PB });                                               // em
        await InsertReturnAsync(db, t, "rc10-rt-ok", "rc10-r-mig", PB, 3_000m, "confirmed");   // rm 포함
        await InsertReturnAsync(db, t, "rc10-rt-no", "rc10-r-mig", PB, 9_000m, "draft");       // rm 제외
        await AssertSameAsync(db, t, PB, false, "E6 매입 대칭", 80_000m, 25_000m, 55_000m);

        // E7 초과 — M > L0 → 양쪽 R 이 같은 음수(자르지 않는다)
        await InsertMatchAsync(db, null, t, PE, 25_000m, true);
        await AssertSameAsync(db, t, PE, true, "E7 초과", 10_000m, 25_000m, -15_000m);
    }

    /// <remarks>#13 — 출하 DDL DESCRIBE 선행. <c>partner_id</c>·<c>item_id</c>·<c>qty</c>·<c>unit_price</c> 는 NOT NULL 기본값 없음이라 채운다.</remarks>
    private static Task InsertReturnAsync(MySqlConnection db, string t, string returnId, string receiptId, string partner, decimal amount, string status)
        => db.ExecuteAsync("""
            INSERT INTO purchase_returns (return_id, tenant_id, receipt_id, return_no, partner_id, return_date, status, total_amount, vat_amount, is_deleted, created_at, updated_at)
            VALUES (@R, @T, @Rc, @R, @P, '2026-03-05', @S, @A, 0, 0, NOW(6), NOW(6));
            INSERT INTO purchase_return_items (return_item_id, tenant_id, return_id, item_id, qty, unit_price, supply_amount, vat_amount)
            VALUES (UUID(), @T, @R, CONCAT('item-', @R), 1, @A, @A, 0);
            """, new { R = returnId, T = t, Rc = receiptId, P = partner, S = status, A = amount });

    /// <summary>
    /// 🔴 동등성 단언 — <b>공용 식</b>과 <b>RR 재계산</b>이 L0·M·R 셋 다 같아야 한다.
    /// 기대값도 같이 못 박는다(대조군) — 두 식이 **같이 틀리면** 「같다」만으로는 못 잡는다.
    /// </summary>
    private async Task AssertSameAsync(MySqlConnection db, string t, string partner, bool receivable, string caseName,
        decimal expectL0, decimal expectM, decimal expectR)
    {
        var pub = await PublicFormulaAsync(db, t, partner, receivable);
        var rr = await RrRecomputeAsync(t, partner, receivable);
        Assert.True(pub is not null, $"{caseName}: 공용 식이 행을 안 돌려줬다");
        Assert.True(rr is not null, $"{caseName}: RR 재계산이 null 이다");

        Assert.True(pub!.LegacyAmount == rr!.LegacyAmount && pub.MatchedAmount == rr.MatchedAmount && pub.RemainingAmount == rr.RemainingAmount,
            $"{caseName}: 두 식이 갈라졌다 — 공용(L0 {pub.LegacyAmount} · M {pub.MatchedAmount} · R {pub.RemainingAmount}) "
          + $"vs RR(L0 {rr.LegacyAmount} · M {rr.MatchedAmount} · R {rr.RemainingAmount})");

        Assert.True(pub.LegacyAmount == expectL0 && pub.MatchedAmount == expectM && pub.RemainingAmount == expectR,
            $"{caseName}: 기대값과 다르다 — 기대(L0 {expectL0} · M {expectM} · R {expectR}) vs 실제(L0 {pub.LegacyAmount} · M {pub.MatchedAmount} · R {pub.RemainingAmount})");
    }

    // ────────────────────────────── G-RC12 (대조군 · 🆕 S4) ──────────────────────────────

    /// <summary>🔴 이 갈래의 시드 규칙 — 인덱스 정렬 순서를 <b>일부러</b> 고정한다.</summary>
    /// <remarks>
    /// <para>
    /// 잠금 범위를 좁혀도 <b>갭 잠금</b>은 남는다(설계 §12-6). RR 의 next-key 잠금은 스캔한 마지막 레코드 <b>다음 간극</b>까지
    /// 잠그므로, 인덱스 순서상 <b>바로 옆</b>에 끼어드는 INSERT 는 거래처가 달라도 막힌다. 그건 인덱스로 못 지운다.
    /// </para>
    /// <para>
    /// 그래서 이 게이트는 「무관 거래처면 <b>무조건</b> 안 막힌다」를 재지 않는다 — 그건 사실이 아니다.
    /// 재는 것은 「<b>인접하지 않은</b> 무관 거래처가 안 막힌다」이고, 그것이 병렬이슈53 이 실제로 신고한 사고다
    /// (그때는 <b>회사 전체</b>가 막혔다). 그래서 전표 id 는 <c>doc-</c>(정렬 앞) · 무관 거래처는 <c>noise-</c>(정렬 뒤)로 두고,
    /// <c>noise-01</c> 을 <b>완충</b>으로 깔아 둔 뒤 <c>noise-03</c>·<c>noise-09</c> 로 잰다.
    /// 완충 없이 재면 갭 잠금 때문에 흔들리는 게이트가 된다(초록·빨강이 GUID 운에 달린다).
    /// </para>
    /// </remarks>
    private static string NoiseId(string tenant, int seq) => $"noise-{tenant[..8]}-{seq:D4}";

    /// <summary>
    /// 🔴 <b>갭 잠금 완충</b> — 설계 §12-6 이 「게이트로 못 지운다」고 적어 둔 그 자리를 **다루는** 방법.
    /// <para>
    /// 좁힌 뒤에도 RR 의 next-key 잠금은 스캔 위치에 <b>인접한 간극</b>을 잠근다. 거래처가 둘뿐인 시험용 표에서는
    /// 두 거래처가 인덱스에서 <b>바로 이웃</b>이라, 서로 상대의 간극에 INSERT 하게 되어 <b>거래처가 달라도</b> 교착한다
    /// (실측: 이 완충을 빼면 1213 · 명세서 §4). 실데이터에는 그 사이에 다른 거래처 행이 있다.
    /// </para>
    /// <para>
    /// 🔴 <b>그래서 이 게이트가 재는 것은</b> 「무관 거래처면 <b>무조건</b> 안 막힌다」가 <b>아니다</b> — 그건 사실이 아니고,
    /// 설계도 그렇게 약속하지 않았다. 재는 것은 「<b>회사 전체가 통째로 막히지는 않는다</b>」이고, 그게 병렬이슈53 의 사고다.
    /// 인접 간극에 남는 차단·교착은 <b>재시도 껍질이 마지막 방어선</b>이다(설계 §12-8).
    /// </para>
    /// <para>id 를 <c>5s-</c> 로 시작하게 두는 이유: <c>1a…</c>(PA)·<c>3b…</c>(PB) 와 <c>9c…</c>(PC)·<c>9f…</c>(PF) <b>사이</b>에 놓이게 하려고.</para>
    /// </summary>
    private static async Task SeedGapSeparatorAsync(MySqlConnection db, string t)
    {
        var sep = "5s-" + Guid.NewGuid().ToString("N")[..8];
        await AddPartnerAsync(db, t, sep, sep);
        await db.ExecuteAsync("""
            INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, ref_doc_type, ref_doc_id, is_active)
            VALUES (UUID(), @T, @P, '2026-03-09', 1000, 'legacy_balance', @P, 1);
            INSERT INTO payments (payment_id, tenant_id, partner_id, payment_type, amount, payment_date, ref_order_id, is_active)
            VALUES (UUID(), @T, @P, 'legacy_balance', 1000, '2026-03-09', @P, 1);
            """, new { T = t, P = sep });
    }

    [Fact(DisplayName = "G-RC12 대조군 — RR 매칭 잠금 중에도 무관 거래처의 수금·지급·매입반품 UPDATE/INSERT 6건이 1205 없이 성공")]
    public async Task GRC12_무관거래처_비차단()
    {
        if (Skip(nameof(GRC12_무관거래처_비차단))) return;
        var (db, t, _svc, _p) = await NewTenantAsync();
        await using var _ = db;

        await InsertDeliveryAsync(db, t, DocD, PA, 100_000m, "migration");
        await InsertReceiptAsync(db, t, DocR, PB, 100_000m, "migration");
        await InsertReturnAsync(db, t, DocRt, DocR, PB, 10_000m, "confirmed");
        // 무관 거래처 30명 + 각자의 수금·지급·매입반품 — 잴 대상(0003·0009)의 **양옆에 남의 행이 있게** 만든다.
        await SeedNoiseRowsAsync(db, t, 1, 30);
        await SeedGapSeparatorAsync(db, t);   // PA·PB 의 스캔 위치와 무관 행 블록 사이를 가른다(갭 잠금)
        var noiseUpd = NoiseId(t, 3);   // 병렬이슈53 의 P3
        var noiseIns = NoiseId(t, 9);   // 병렬이슈53 의 P9

        // ── 세션 A — RR 매칭 잠금 8문장을 전부 쥔 채로 유지 (S3 명세서 §3-1 과 같은 모양) ──
        await using var a = await OpenAsync();
        await using var atx = await BeginRrAsync(a);
        await SentinelReadAsync(a, atx, t, true);
        await LegacyBalanceMatching.EnsureMatchAllowedAsync(a, atx, RR, t, PA, PA, 10_000m, After, receivable: true, null, Ct);    // L1·L2
        await LegacyBalanceMatching.EnsureMatchAllowedAsync(a, atx, RR, t, PB, PB, 10_000m, After, receivable: false, null, Ct);  // L3·L4·L5
        var docArgs = new { TenantId = t, RefId = DocD };
        await a.ExecuteScalarAsync<decimal>(CollectionService.DeliveryUsedSql(RR), docArgs, atx);                                  // D1-a
        var recArgs = new { TenantId = t, RefId = DocR };
        await a.ExecuteScalarAsync<decimal>(CollectionService.ReceiptPaidSql(RR), recArgs, atx);                                   // D1-b
        await a.ExecuteScalarAsync<decimal>(CollectionService.ReceiptReturnedSql(RR), recArgs, atx);                               // D1-c

        // ── 세션 B — 무관 거래처 6건. 막히면 3초 안에 1205 로 떨어진다(영원히 안 기다린다) ──
        await using var b = await OpenAsync();
        await b.ExecuteAsync("SET SESSION innodb_lock_wait_timeout = 3");

        var ops = new (string Name, string Sql, string Table)[]
        {
            ("B1 collections UPDATE", $"UPDATE collections SET memo='rc12' WHERE tenant_id=@T AND partner_id='{noiseUpd}'", "collections"),
            ("B2 collections INSERT", $"INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, ref_doc_type, ref_doc_id, is_active) VALUES (UUID(), @T, '{noiseIns}', '2026-03-11', 1000, 'legacy_balance', '{noiseIns}', 1)", "collections"),
            ("B3 payments UPDATE", $"UPDATE payments SET memo='rc12' WHERE tenant_id=@T AND partner_id='{noiseUpd}'", "payments"),
            ("B4 payments INSERT", $"INSERT INTO payments (payment_id, tenant_id, partner_id, payment_type, amount, payment_date, ref_order_id, is_active) VALUES (UUID(), @T, '{noiseIns}', 'legacy_balance', 1000, '2026-03-11', '{noiseIns}', 1)", "payments"),
            ("B5 purchase_returns UPDATE", $"UPDATE purchase_returns SET memo='rc12' WHERE tenant_id=@T AND partner_id='{noiseUpd}'", "purchase_returns"),
            ("B6 purchase_returns INSERT", $"INSERT INTO purchase_returns (return_id, tenant_id, receipt_id, return_no, partner_id, return_date, status, total_amount, vat_amount, is_deleted) VALUES (UUID(), @T, 'np-{t[..8]}-9', CONCAT('RT-', SUBSTRING(UUID(),1,8)), '{noiseIns}', '2026-03-11', 'draft', 1000, 0, 0)", "purchase_returns"),
        };

        // ══════════════════════════════════════════════════════════════════════════════
        // 🔴 B7·B8 — **다른 회사** 축. 작지 §20-2 (PM 두 번째 자백 · [4] F-1).
        //
        // PM 이 §19-1 에 적은 「idx_pay_partner 와 idx_pay_tenant_partner 는 잠그는 집합이 같다」는 **틀렸다.**
        // RR 의 잠금은 일치 행 + **그 옆 간극**(넥스트키)이다. idx_pay_partner 는 회사 구분 없이
        // partner_id 하나로 전역 정렬되므로 **간극의 이웃이 남의 회사 행**이다.
        // ⇒ 「안전하다」를 논증으로 두지 않고 **여기서 잰다.**
        //   · 안 막히면 → 결론(제품 무변경) 유지 · 근거는 논증이 아니라 이 실측이 된다.
        //   · 막히면   → 🔴 멈추고 설계 4판(L3 의 인덱스 축 고정 · 작지 §17 C-3).
        //
        // 📐 왜 「바로 옆」이어야 하나: 간극 잠금은 **인덱스에서 이웃한 자리**에만 걸린다.
        //   그래서 다른 회사 거래처 id 를 PA·PB 와 **앞 35글자가 같게** 만든다 — 그 둘 사이에는
        //   다른 값이 끼어들 수 없으므로 정렬상 반드시 이웃이다. 무작위 id 로는 실행마다 결과가 달라진다.
        // ══════════════════════════════════════════════════════════════════════════════
        var otherT = Guid.NewGuid().ToString();
        var xPay = PB[..^1] + (PB[^1] == 'z' ? 'y' : 'z');    // payments 축 — L3 이 PB 를 잠근다
        var xColl = PA[..^1] + (PA[^1] == 'z' ? 'y' : 'z');   // collections 축 — L1 이 PA 를 잠근다
        await AddPartnerAsync(db, otherT, xPay, "XP");
        await AddPartnerAsync(db, otherT, xColl, "XC");

        // 🔴 대조군 짝 — B9·B10 은 같은 「다른 회사」인데 거래처 id 가 PA·PB 와 **정렬상 멀다**(무작위).
        //    B7·B8 만 막히고 B9·B10 은 통과하면 원인은 **이웃 간극**(인덱스 축)이다 → 설계 사안.
        //    넷 다 막히면 원인은 이웃이 아니라 **다른 무엇**이다 — 그때 인덱스를 고치면 엉뚱한 곳을 고치는 것이다.
        var fPay = "ff" + Guid.NewGuid().ToString()[2..];
        var fColl = "fe" + Guid.NewGuid().ToString()[2..];
        await AddPartnerAsync(db, otherT, fPay, "FP");
        await AddPartnerAsync(db, otherT, fColl, "FC");

        var crossOps = new (string Name, string Sql, string Table)[]
        {
            ("B7 다른 회사 payments INSERT (이웃)", $"INSERT INTO payments (payment_id, tenant_id, partner_id, payment_type, amount, payment_date, ref_order_id, is_active) VALUES (UUID(), '{otherT}', '{xPay}', 'legacy_balance', 1000, '2026-03-11', '{xPay}', 1)", "payments"),
            ("B8 다른 회사 collections INSERT (이웃)", $"INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, ref_doc_type, ref_doc_id, is_active) VALUES (UUID(), '{otherT}', '{xColl}', '2026-03-11', 1000, 'legacy_balance', '{xColl}', 1)", "collections"),
            ("B9 다른 회사 payments INSERT (대조군·멂)", $"INSERT INTO payments (payment_id, tenant_id, partner_id, payment_type, amount, payment_date, ref_order_id, is_active) VALUES (UUID(), '{otherT}', '{fPay}', 'legacy_balance', 1000, '2026-03-11', '{fPay}', 1)", "payments"),
            ("B10 다른 회사 collections INSERT (대조군·멂)", $"INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, ref_doc_type, ref_doc_id, is_active) VALUES (UUID(), '{otherT}', '{fColl}', '2026-03-11', 1000, 'legacy_balance', '{fColl}', 1)", "collections"),
        };

        // 🔴 PM 판정 S4-2(작지 §19) — 막히면 **무엇에** 막혔는지 찍는다. 1205 는 교착이 아니라
        //    `INNODB STATUS` 에 사후 기록이 안 남는다 → **대기하는 동안** 다른 연결로 들여다봐야 한다.
        await using var probe = await OpenAsync();
        var blocked = new List<string>();
        var crossBlocked = new List<string>();
        foreach (var (name, sql, table) in ops.Concat(crossOps))
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var run = b.ExecuteAsync(sql, new { T = t });
            var waited = await Task.WhenAny(run, Task.Delay(700)) != run;
            var waitLock = waited ? await WaitingLockAsync(probe, table) : null;
            try
            {
                await run;
                Console.Error.WriteLine($"[G-RC12] {name} 🟢 {sw.ElapsedMilliseconds}ms");
            }
            catch (MySqlException ex)
            {
                Console.Error.WriteLine($"[G-RC12] {name} 🔴 {sw.ElapsedMilliseconds}ms errno={ex.Number} · 대기 상대:\n{waitLock ?? "(못 찍었다)"}");
                var entry = $"{name} (errno={ex.Number} · 대기 상대 {Summarize(waitLock)})";
                if (Array.Exists(crossOps, o => o.Name == name)) crossBlocked.Add(entry); else blocked.Add(entry);
                var culprit = MatchLockIndexes.FirstOrDefault(i => (waitLock ?? string.Empty).Contains(i, StringComparison.Ordinal));
                Assert.True(culprit is null,
                    $"🔴 {name} 이 **이월잔액 매칭 잠금**({culprit})에 막혔다 — 병렬이슈53 재발이다.\n"
                  + "  DB-124 잠금범위 인덱스가 빠졌거나 옵티마이저가 안 고른 것이다. 다른 거래처가 못 쓰는 ERP 는 흐름이 끊긴 것이다(#20).\n"
                  + $"  대기 상대:\n{waitLock}");
            }
        }
        await atx.RollbackAsync();

        // 🔴 PM 판정 S4-2 — 매칭 잠금이 아닌 이유로 막히는 것이 **현재 동작**이다(수기 등록의 유니크 간극 · 명세서 §3-1).
        //    그것까지 초록으로 덮지 않고, **지금 몇 건이 그러한지**를 고정한다. 늘어나면 빨간불이다.
        Assert.True(blocked.Count <= MaxNonMatchBlocked,
            $"무관 거래처 차단이 {blocked.Count}건으로 늘었다(고정값 {MaxNonMatchBlocked}) — " + string.Join(" · ", blocked) + "\n"
          + "  매칭 잠금이 원인은 아니지만(위 단언이 통과했다), 막히는 자리가 늘어난 것 자체가 회귀다.");
        Console.Error.WriteLine($"[G-RC12] 매칭 잠금 외 사유로 막힌 건수 = {blocked.Count} (고정값 {MaxNonMatchBlocked})");

        // ══════════════════════════════════════════════════════════════════════════════
        // 🔴 다른 회사 축 — 실측 결과(작지 §21). **0 이 아니다. 지금은 4 다.**
        //
        // 대조군이 갈랐다: 이웃(B7·B8)만이 아니라 **멀리 떨어진 B9·B10 도 똑같이 막혔다.**
        // ⇒ 원인은 「L3 이 회사 접두 없는 인덱스를 탄다」가 **아니다.** 그 가설은 실측으로 기각됐다.
        //   잡힌 상대는 `uq_payments_source` 의 간극 — 수기 등록의 `source_id` NULL 자리,
        //   즉 **이미 별건으로 뺀 교착 원인 ②**(작지 §19-4)다. 회사 접두가 있는데도 이웃이라 막힌다.
        //
        // 그래서 여기서 인덱스를 고치면 **엉뚱한 곳을 고치는 것**이다. 대신 —
        //   · 상대가 **매칭 잠금이면** 위 `culprit` 단언이 먼저 빨간불을 낸다(그건 이 갈래의 책임이다).
        //   · 매칭 잠금이 아닌 차단은 **지금 몇 건인지 못 박는다.** 늘면 빨간불이다.
        // 🔴 목표값은 여전히 **0** 이다. 0 으로 내리는 일은 별건 설계(교착 원인 ②)가 한다 — 그때 이 값을 내린다.
        // ══════════════════════════════════════════════════════════════════════════════
        Assert.True(crossBlocked.Count <= MaxCrossTenantBlocked,
            $"🔴 **다른 회사**의 등록이 막힌 건수가 {crossBlocked.Count}건으로 늘었다(고정값 {MaxCrossTenantBlocked}) — "
          + string.Join(" · ", crossBlocked) + "\n"
          + "  상대가 매칭 잠금이 아닌 것은 위 단언이 이미 확인했다. 그래도 **막히는 자리가 늘어난 것 자체가 회귀다.**\n"
          + "  원인이 매칭 잠금으로 바뀌었다면 게이트를 고치지 말고 **멈추고 설계로 올려라**(작지 §21 · §17 C-3).");

        var unnamed = crossBlocked.Count(x => x.Contains("(못 찍었다)", StringComparison.Ordinal));
        Console.Error.WriteLine($"[G-RC12] 다른 회사 축 4건 → 막힘 {crossBlocked.Count}건(고정값 {MaxCrossTenantBlocked}) · 상대를 못 찍은 건 {unnamed}건");
        // 🔴 못 찍은 건수도 고정한다 — 「모르는 채로 통과」가 늘어나는 것을 막는다([4] F-8 과 같은 이유).
        Assert.True(unnamed <= MaxCrossTenantUnnamed,
            $"다른 회사 차단 중 **상대를 못 찍은 건**이 {unnamed}건으로 늘었다(고정값 {MaxCrossTenantUnnamed}).\n"
          + "  상대를 모르면 「매칭 잠금이 아니다」도 증명된 것이 아니다. 관측 장치(WaitingLockAsync)부터 고쳐라.");
    }

    /// <summary>
    /// 🔴 20260921 실측 고정값 — <b>다른 회사</b>의 등록 4건 중 막히는 건수. 현재 <b>4</b>(전부).
    /// <para>상대는 매칭 잠금이 아니라 <c>uq_*_source</c> 의 NULL 간극(교착 원인 ② · 작지 §19-4 별건).
    /// ⬜ 그 별건이 봉합되면 이 값을 <b>0</b> 으로 내린다 — 목표값은 0 이다.</para>
    /// </summary>
    private const int MaxCrossTenantBlocked = 4;

    /// <summary>🔴 그중 <b>상대를 못 찍은</b> 건수 — 현재 2(<c>collections</c> 축). 관측의 한계이지 안전의 증거가 아니다.</summary>
    private const int MaxCrossTenantUnnamed = 2;

    /// <summary>
    /// 🔴 20260920작1 S4 실측 고정값 — 무관 거래처 6건 중 <b>매칭 잠금이 아닌</b> 사유로 막히는 건수.
    /// 현재 1건(B4 `payments` INSERT · 수기 등록의 <c>source_id</c> NULL 유니크 간극 · 명세서 §3-1·§3-2).
    /// ⬜ 별건 설계로 없애면 이 값을 0 으로 내린다(PM 판정 S4-5).
    /// </summary>
    private const int MaxNonMatchBlocked = 1;

    /// <summary>
    /// 지금 이 서버에서 <b>대기 중</b>인 잠금 — 어느 표·어느 인덱스인지가 여기에만 보인다.
    /// <para>
    /// 🔴 20260921 봉합: 예전 판은 <c>INNODB STATUS</c> 에서 <b>첫 번째</b> 「WAITING FOR THIS LOCK」 만 잘라 왔다.
    /// 그래서 <c>collections</c> 에 넣다 막힌 건인데 <c>payments</c> 의 옛 기록이 찍혔다(실측).
    /// <b>엉뚱한 상대를 근거로 원인을 정하면 그 위에 쌓는 것이 전부 틀린다</b>(인계서 §5 「재현했다고 원인이 아니다」).
    /// ⇒ 트랜잭션 블록으로 자른 뒤 <b>지금 기다리는(LOCK WAIT) · 그 표를 건드리는</b> 블록만 고른다.
    /// </para>
    /// </summary>
    private static async Task<string?> WaitingLockAsync(MySqlConnection c, string tableHint)
    {
        var status = (await c.QueryFirstAsync<InnodbStatus>("SHOW ENGINE INNODB STATUS")).Status ?? string.Empty;
        var blocks = status.Split("---TRANSACTION", StringSplitOptions.None);
        foreach (var block in blocks)
        {
            if (!block.Contains("WAITING FOR THIS LOCK TO BE GRANTED", StringComparison.Ordinal)) continue;
            if (!block.Contains(tableHint, StringComparison.Ordinal)) continue;   // 그 표를 건드리는 블록만
            var i = block.IndexOf("WAITING FOR THIS LOCK TO BE GRANTED", StringComparison.Ordinal);
            return block[i..Math.Min(i + 1500, block.Length)];
        }
        return null;   // 못 찍었으면 못 찍었다고 한다 — 아무거나 주워오지 않는다
    }

    private static string Summarize(string? waitLock)
    {
        if (waitLock is null) return "(못 찍었다)";
        var i = waitLock.IndexOf("index ", StringComparison.Ordinal);
        if (i < 0) return "(인덱스 줄 없음)";
        var j = waitLock.IndexOf('\n', i);
        return j > i ? waitLock[i..j].Trim() : waitLock[i..].Trim();
    }

    // ────────────────────────────── G-RC14 (유니크 축 · 🆕 S4) ──────────────────────────────

    /// <summary>
    /// 🔴 <b>G-RC14 — 유니크 축</b>. 20260920작1 S4 (PM 판정 S4-5 · 작지 §19).
    /// <para>
    /// 매칭 잠금과 <b>다른 뿌리</b>다. 같은 이관 키(<c>source_id</c>)로 두 연결이 동시에 수금을 넣으면
    /// <c>uq_collections_source</c>(<c>tenant_id</c>,<c>source_id</c>)가 <b>한쪽만</b> 통과시킨다 — 돈이 두 번 들어가지 않는다.
    /// </para>
    /// <para>
    /// ⚠️ 이 게이트는 <b>고치는 게이트가 아니라 고정하는 게이트</b>다. 수기 등록(<c>source_id</c> NULL)이
    /// 이 유니크 인덱스의 같은 자리에서 RR 간극 잠금으로 교착하는 것(명세서 §3-1)은 <b>별건 설계</b>로 넘겼다(S4-5).
    /// 여기서는 「<b>멱등은 지켜진다</b> · 교착이 나면 재시도 껍질이 삼킨다」는 <b>현재 사실</b>만 못 박는다.
    /// 나중에 그 별건이 손대면 이 게이트가 먼저 말을 한다.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "G-RC14 유니크 축 — 같은 이관 키(source_id)로 동시 수금 INSERT 는 한쪽만 성공(멱등) · 교착은 재시도가 삼킨다")]
    public async Task GRC14_유니크축_동시()
    {
        if (Skip(nameof(GRC14_유니크축_동시))) return;
        var (db, t, _svc, _p) = await NewTenantAsync();
        await using var _ = db;

        const string sourceId = "mig-rc14-same-key";
        var sql = """
            INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, ref_doc_type, ref_doc_id, is_active, source_type, source_id)
            VALUES (UUID(), @T, @P, '2026-03-10', 10000, 'legacy_balance', @P, 1, 'migration', @S)
            """;

        await using var c1 = await OpenAsync();
        await using var c2 = await OpenAsync();
        await using var tx1 = await BeginRrAsync(c1);
        await using var tx2 = await BeginRrAsync(c2);

        var e1 = await Record.ExceptionAsync(() => c1.ExecuteAsync(sql, new { T = t, P = PA, S = sourceId }, tx1));
        // 뒤 연결은 같은 유니크 자리에서 기다린다 — 앞이 커밋하면 1062, 롤백하면 통과.
        var second = c2.ExecuteAsync(sql, new { T = t, P = PC, S = sourceId }, tx2);
        var waited = await Task.WhenAny(second, Task.Delay(1000)) != second;
        await tx1.CommitAsync();
        var e2 = await Record.ExceptionAsync(() => second);
        if (e2 is null) await tx2.CommitAsync(); else await tx2.RollbackAsync();

        Assert.Null(e1);
        Assert.True(waited, "뒤 연결이 유니크 자리에서 기다리지 않았다 — 같은 이관 키가 직렬화되지 않는다(멱등이 깨진다).");
        Assert.True(e2 is MySqlException my && my.Number == 1062,
            $"같은 이관 키 두 번째 INSERT 가 막히지 않았다 — 이관 수금이 두 줄이 된다(멱등 깨짐). 실제: {e2?.GetType().Name} {e2?.Message}");
        Assert.Equal(1, await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM collections WHERE tenant_id=@T AND source_id=@S", new { T = t, S = sourceId }));
    }

    // ────────────────────────────── G-RC13 (EXPLAIN · 🆕 S4) ──────────────────────────────

    /// <summary>EXPLAIN 한 줄. Dapper 가 대소문자 무시로 <c>key</c>→<c>Key</c>, <c>rows</c>→<c>Rows</c> 를 맞춘다.</summary>
    private sealed class ExplainRow
    {
        public string? Table { get; set; }
        public string? Type { get; set; }
        public string? PossibleKeys { get; set; }
        public string? Key { get; set; }
        public string? KeyLen { get; set; }
        public long? Rows { get; set; }
        public string? Extra { get; set; }
    }

    /// <summary>
    /// RR 전용 잠금 읽기 문장 5개 — <c>EXPLAIN</c> 으로 잠금 범위를 재는 대상.
    /// <para>
    /// 🔴 [4] F-6 — 예전에는 여기에 「기대 인덱스 이름」 칸이 있었다. <b>지웠다.</b> 이름을 고정하면
    /// 시드 분포에 따라 초록·빨강을 오가고(명세서 §3-3), 고정해야 할 것은 이름이 아니라
    /// <b>회사 경계 안에서만 훑고 잠그는가</b>이기 때문이다(작지 §19-1 · <see cref="IndexSafety"/>).
    /// 쓰지 않는 칸을 남겨 두면 다음 사람이 그걸 아직 재는 줄 안다.
    /// </para>
    /// </summary>
    private static readonly (string Name, bool Receivable, bool DocParam, string Sql)[] LockedStatements =
    {
        ("L1 ReceivableMatchedLegacyLockedSql", true,  false, LegacyBalanceMatching.ReceivableMatchedLegacyLockedSql),
        ("L2 ReceivableMatchedDocLockedSql",    true,  false, LegacyBalanceMatching.ReceivableMatchedDocLockedSql),
        ("L3 PayableMatchedLegacyLockedSql",    false, false, LegacyBalanceMatching.PayableMatchedLegacyLockedSql),
        ("L4 PayableMatchedDocLockedSql",       false, false, LegacyBalanceMatching.PayableMatchedDocLockedSql),
        ("L5 PayableMatchedReturnLockedSql",    false, false, LegacyBalanceMatching.PayableMatchedReturnLockedSql),
    };

    /// <summary>훑는 행 상한 — 「그 거래처/그 전표 몫」. 무관 행을 아무리 심어도 이 안이어야 한다.</summary>
    private const long RowsCap = 8;

    [Fact(DisplayName = "G-RC13 EXPLAIN — 제품 문장 8개가 회사 경계 안에서만 훑는다(스키마로 판정 · 음성 대조군 포함) · 무관 행 10배(30→300)에도 훑는 행 불변")]
    public async Task GRC13_EXPLAIN_잠금범위()
    {
        if (Skip(nameof(GRC13_EXPLAIN_잠금범위))) return;
        var (db, t, _svc, _p) = await NewTenantAsync();
        await using var _ = db;

        // 이관 전표에 붙은 줄까지 있어야 조인 문장(L2·L4·L5)이 실제 모양으로 재진다.
        await InsertDeliveryAsync(db, t, DocD, PA, 100_000m, "migration");
        await InsertReceiptAsync(db, t, DocR, PB, 100_000m, "migration");
        await InsertReturnAsync(db, t, DocRt, DocR, PB, 10_000m, "confirmed");

        // 무관 행 30건(같은 회사) + 다른 회사 30건 — 테넌트를 넘는 인덱스가 매력적으로 보이게 만드는 대조 시드.
        var other = Guid.NewGuid().ToString();
        await SeedNoiseRowsAsync(db, t, 1, 30);
        await SeedNoiseRowsAsync(db, other, 1, 30);
        var safety = await IndexSafety.MeasureAsync(db, ExplainTables);
        var before = await ExplainAllAsync(db, t);
        AssertPlans(before, "무관 행 30건", safety);

        // 🔴 대조군 — 무관 행을 10배로. `rows` 는 추정치라 「상한 이하」 하나만으로는 흔들린다(설계 §12-6).
        await SeedNoiseRowsAsync(db, t, 31, 300);
        await SeedNoiseRowsAsync(db, other, 31, 300);
        // 🔴 10배로 늘린 뒤 **다시 잰다** — 「두 회사에 걸친 값 0건」은 지금 데이터에 대한 사실이지 영구 사실이 아니다.
        var safetyAfter = await IndexSafety.MeasureAsync(db, ExplainTables);
        var after = await ExplainAllAsync(db, t);
        AssertPlans(after, "무관 행 300건", safetyAfter);

        foreach (var (name, rows) in after)
        {
            var was = before[name];
            foreach (var row in rows)
            {
                var prev = was.FirstOrDefault(x => string.Equals(x.Table, row.Table, StringComparison.Ordinal));
                Assert.True(prev is not null, $"{name}: 무관 행을 늘렸더니 계획에 없던 표({row.Table})가 생겼다");
                Assert.True(prev!.Rows == row.Rows,
                    $"🔴 {name} · 표 {row.Table}: 무관 행을 30 → 300 으로 늘렸더니 훑는 행이 {prev.Rows} → {row.Rows} 로 늘었다.\n"
                  + "  = 잠금·스캔 범위가 「그 전표 몫」이 아니라 **표 전체를 따라 커진다**. DB-124 가 안 먹고 있다(병렬이슈53).");
            }
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // 🔴 음성 대조군 — [4] F-2. 조건 ③(「두 회사에 걸친 값 0건」)이 **장식이 아님**을 증명한다.
        //
        // 시험 시드는 거래처 id 를 `noise-{회사8자리}-NNNN` 로 만든다 → 두 회사에 걸친 값이 **구조상 생길 수 없다.**
        // 그러면 ③ 은 언제나 0 을 돌려주고, 아무것도 안 재는 단언이 된다(감시자 0).
        // ⇒ 여기서 **일부러 걸치게 만들고**, 판정이 실제로 빨간불로 바뀌는지 본다. 안 바뀌면 그 조건은 지워야 한다.
        // ══════════════════════════════════════════════════════════════════════════════
        var spanT = Guid.NewGuid().ToString();
        await db.ExecuteAsync("""
            INSERT INTO payments (payment_id, tenant_id, partner_id, payment_type, amount, payment_date, ref_order_id, is_active)
            VALUES (UUID(), @ST, @P, 'legacy_balance', 1000, '2026-03-12', @P, 1)
            """, new { ST = spanT, P = PB });   // PB 는 회사 t 의 거래처다 → 이제 payments 에서 두 회사에 걸친다
        try
        {
            var polluted = await IndexSafety.MeasureAsync(db, ExplainTables);
            var stillSafe = polluted.IsSafe("payments", "idx_pay_partner", out var whyPolluted);
            Console.Error.WriteLine($"[G-RC13 음성대조군] idx_pay_partner 판정: {(stillSafe ? "안전" : "위험")} — {whyPolluted}");
            Assert.False(stillSafe,
                "🔴 거래처 하나를 **두 회사에 걸치게** 만들었는데도 안전판정이 그대로 통과했다.\n"
              + "  = 조건 ③(두 회사에 걸친 값 0건)이 아무것도 안 재고 있다는 뜻이다. 그 조건은 장식이다 — 지우거나 고쳐라.\n"
              + $"  판정 사유: {whyPolluted}");
            Assert.Contains("두 회사에 걸쳐", whyPolluted, StringComparison.Ordinal);
        }
        finally
        {
            // 🔴 반드시 치운다 — 남기면 **다음 실행의 ③ 이 계속 빨간불**이 되어 게이트가 잡음이 된다.
            await db.ExecuteAsync("DELETE FROM payments WHERE tenant_id = @ST", new { ST = spanT });
        }
    }

    /// <summary>
    /// 무관 행 — 거래처 <c>noise-NNNN</c> 과 그 거래처의 수금·지급·매입·명세서·반품 각 1행.
    /// <para>MariaDB 의 <c>seq_x_to_y</c>(SEQUENCE 엔진)로 표당 한 문장에 심는다 — 왕복 수천 번을 안 한다.</para>
    /// <para>⚠️ <c>sales_deliveries</c>·<c>purchase_receipts</c> 는 <c>partners</c> 로 FK 가 걸려 있어 거래처를 먼저 심는다(#13 DESCRIBE 선행).</para>
    /// </summary>
    private static async Task SeedNoiseRowsAsync(MySqlConnection db, string t, int from, int to)
    {
        var seq = $"seq_{from}_to_{to}";
        await db.ExecuteAsync($"""
            INSERT INTO partners (partner_id, tenant_id, partner_code, partner_name, partner_type, is_active, is_deleted, created_at, updated_at)
            SELECT CONCAT('noise-', @T8, '-', LPAD(seq,4,'0')), @T, CONCAT('NC', LPAD(seq,4,'0')), CONCAT('무관거래처', seq), 'both', 1, 0, NOW(6), NOW(6) FROM {seq};
            INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, ref_doc_type, ref_doc_id, is_active)
            SELECT UUID(), @T, CONCAT('noise-', @T8, '-', LPAD(seq,4,'0')), '2026-03-09', 1000, 'legacy_balance', CONCAT('noise-', @T8, '-', LPAD(seq,4,'0')), 1 FROM {seq};
            INSERT INTO payments (payment_id, tenant_id, partner_id, payment_type, amount, payment_date, ref_order_id, is_active)
            SELECT UUID(), @T, CONCAT('noise-', @T8, '-', LPAD(seq,4,'0')), 'legacy_balance', 1000, '2026-03-09', CONCAT('noise-', @T8, '-', LPAD(seq,4,'0')), 1 FROM {seq};
            INSERT INTO sales_deliveries (delivery_id, tenant_id, delivery_no, partner_id, delivery_date, source_type, status, total_amount, vat_amount, is_deleted, created_at, updated_at)
            SELECT CONCAT('nd-', @T8, '-', seq), @T, CONCAT('ND', LPAD(seq,4,'0')), CONCAT('noise-', @T8, '-', LPAD(seq,4,'0')), '2026-01-10', 'migration', 'confirmed', 1000, 0, 0, NOW(6), NOW(6) FROM {seq};
            INSERT INTO purchase_receipts (receipt_id, tenant_id, receipt_no, partner_id, receipt_date, source_type, status, total_amount, vat_amount, created_at)
            SELECT CONCAT('np-', @T8, '-', seq), @T, CONCAT('NP', LPAD(seq,4,'0')), CONCAT('noise-', @T8, '-', LPAD(seq,4,'0')), '2026-01-10', 'migration', 'confirmed', 1000, 0, NOW(6) FROM {seq};
            INSERT INTO purchase_returns (return_id, tenant_id, receipt_id, return_no, partner_id, return_date, status, total_amount, vat_amount, is_deleted, created_at, updated_at)
            SELECT CONCAT('nr-', @T8, '-', seq), @T, CONCAT('np-', @T8, '-', seq), CONCAT('NR', LPAD(seq,4,'0')), CONCAT('noise-', @T8, '-', LPAD(seq,4,'0')), '2026-03-05', 'confirmed', 1000, 0, 0, NOW(6), NOW(6) FROM {seq};
            INSERT INTO purchase_return_items (return_item_id, tenant_id, return_id, item_id, qty, unit_price, supply_amount, vat_amount)
            SELECT UUID(), @T, CONCAT('nr-', @T8, '-', seq), CONCAT('ni-', seq), 1, 1000, 1000, 0 FROM {seq};
            """, new { T = t, T8 = t[..8] });
    }

    /// <summary>제품 문장 8개(L1~L5 + D1-a/b/c)를 <c>EXPLAIN</c> 한다 — <b>문장은 제품 상수를 그대로 이어 붙인다</b>.</summary>
    private async Task<Dictionary<string, List<ExplainRow>>> ExplainAllAsync(MySqlConnection db, string t)
    {
        foreach (var tbl in ExplainTables)
            await db.ExecuteAsync($"ANALYZE TABLE {tbl}");   // 추정치는 통계에 달려 있다

        var result = new Dictionary<string, List<ExplainRow>>(StringComparer.Ordinal);
        foreach (var (name, receivable, _, sql) in LockedStatements)
        {
            _aliasOf[name] = AliasesOf(sql);
            result[name] = (await db.QueryAsync<ExplainRow>("EXPLAIN " + sql,
                new { TenantId = t, PartnerId = receivable ? PA : PB })).AsList();
        }

        foreach (var (name, sql, refId) in new[]
                 {
                     ("D1-a DeliveryUsedSql",   CollectionService.DeliveryUsedSql(RR),   DocD),
                     ("D1-b ReceiptPaidSql",    CollectionService.ReceiptPaidSql(RR),    DocR),
                     ("D1-c ReceiptReturnedSql", CollectionService.ReceiptReturnedSql(RR), DocR),
                 })
        {
            _aliasOf[name] = AliasesOf(sql);
            result[name] = (await db.QueryAsync<ExplainRow>("EXPLAIN " + sql, new { TenantId = t, RefId = refId })).AsList();
        }
        return result;
    }

    /// <summary>안전판정을 재는 표 — <c>EXPLAIN</c> 이 닿는 표 전부.</summary>
    private static readonly string[] ExplainTables =
        { "collections", "payments", "purchase_returns", "purchase_return_items", "sales_deliveries", "purchase_receipts", "partners" };

    private void AssertPlans(Dictionary<string, List<ExplainRow>> plans, string when, IndexSafety safety)
    {
        foreach (var (name, rows) in plans)
        {
            foreach (var r in rows)
                Console.Error.WriteLine($"[G-RC13 {when}] {name} · {r.Table} · type={r.Type} · key={r.Key} · key_len={r.KeyLen} · rows={r.Rows}");

            Assert.True(rows.Count > 0, $"{name}: EXPLAIN 이 한 줄도 안 나왔다({when})");
            foreach (var r in rows)
            {
                Assert.True(!string.Equals(r.Type, "ALL", StringComparison.OrdinalIgnoreCase) && r.Key is not null,
                    $"🔴 {name} · 표 {r.Table}({when}): 인덱스를 안 탄다(type={r.Type} key={r.Key ?? "NULL"}).\n"
                  + "  = 그 회사 표를 통째로 훑으면서 잠근다. 상관없는 거래처의 수금·지급이 막힌다(병렬이슈53).");
                // 🔴 PM 판정 S4-3(작지 §19-1) — 「어느 인덱스 이름인지」는 시드 분포에 따라 갈린다(명세서 §3-3).
                //    고정할 것은 이름이 아니라 **성질**이다: 그 인덱스로 훑고 잠그는 범위에 **남의 회사 행이 들어올 수 있는가**.
                //    손으로 적은 예외 목록은 두지 않는다 — 이름만 더하면 조용히 초록이 되는 길이다(감시자 0 · 인계서 §5-2).
                //    ⚠️ 행 수 상한보다 **먼저** 잰다 — 범위가 남의 회사까지 가는 것이 더 근본적인 사고이고,
                //       행 수로 먼저 걸리면 「왜 빨간불인지」가 잘못 읽힌다.
                var table = RealTableOf(name, r.Table);
                Assert.True(safety.IsSafe(table, r.Key!, out var why),
                    $"🔴 {name} · 표 {r.Table}(={table} · {when}): 고른 인덱스 {r.Key} 의 범위에 **남의 회사 행**이 들어올 수 있다 — {why}.\n"
                  + "  잠금 범위이자 테넌트 격리 문제다(작지 §19-1 PM 판정).");
                Console.Error.WriteLine($"[G-RC13 {when}] {name} · {table} · {r.Key} 안전판정: {why}");

                Assert.True(r.Rows is not null && r.Rows <= RowsCap,
                    $"🔴 {name} · 표 {r.Table}({when}): 훑는 행 {r.Rows} 가 상한 {RowsCap} 을 넘었다 — 「그 전표 몫」이 아니다.");
            }
        }
    }

    private sealed class StatRow
    {
        public string TableName { get; set; } = string.Empty;
        public string IndexName { get; set; } = string.Empty;
        public string ColumnName { get; set; } = string.Empty;
    }

    /// <summary>
    /// 🔴 <b>잠금 범위 안전 판정</b> — 작지 §19-1 (PM 판정 S4-3).
    /// <para>
    /// 지켜야 하는 것은 「인덱스 첫 칸이 <c>tenant_id</c> 다」가 <b>아니라</b>
    /// 「그 인덱스로 훑고 잠그는 범위에 <b>남의 회사 행이 들어올 수 없다</b>」이다. 대리물이 아니라 목적을 잰다.
    /// </para>
    /// <para>셋 다 <b>잰다</b> — 손으로 적은 예외 목록은 두지 않는다:
    /// ① 첫 칸이 <c>tenant_id</c> 인가(<c>information_schema.STATISTICS</c>)
    /// ② 아니라면 그 칸이 <b>어느 표의 단일칸 PRIMARY KEY</b> 인가(= 값 하나가 표 전체에 한 행 → 회사도 하나)
    /// ③ 그리고 <b>실제로</b> 그 칸이 두 회사에 걸친 값이 없는가(회사 2개가 심긴 시험 DB 에서 COUNT).
    /// </para>
    /// <para>
    /// ⚠️ ②만으로 통과시키지 않는 이유: <c>payments.partner_id</c> 에는 FK 가 없어서 스키마가 「한 회사」를 강제하지 못한다.
    /// 그래서 ③ 으로 <b>센다.</b> 데이터가 규칙을 깨면 여기서 먼저 빨간불이 난다.
    /// </para>
    /// </summary>
    private sealed class IndexSafety
    {
        private readonly Dictionary<(string Table, string Index), string> _firstCol = new();
        private readonly HashSet<string> _globallyUniqueId = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Table, string Column), long> _spanning = new();

        public static async Task<IndexSafety> MeasureAsync(MySqlConnection db, string[] tables)
        {
            var s = new IndexSafety();

            foreach (var r in await db.QueryAsync<StatRow>("""
                SELECT TABLE_NAME AS TableName, INDEX_NAME AS IndexName, COLUMN_NAME AS ColumnName
                  FROM information_schema.STATISTICS
                 WHERE TABLE_SCHEMA = DATABASE() AND SEQ_IN_INDEX = 1 AND TABLE_NAME IN @T
                """, new { T = tables }))
                s._firstCol[(r.TableName, r.IndexName)] = r.ColumnName;

            // ② 단일칸 PK = 그 값 하나가 표 전체에 한 행뿐이다 → 그 행의 회사도 하나다.
            //    🔴 [4] F-3 — **반드시 `tables` 로 좁힌다.** DB 전체로 열면 단일칸 PK 이름이 ~110개(`id` 포함)나 되어
            //       「손으로 적은 예외 3개」를 「자동 생성 예외 110개」로 바꾸는 꼴이 된다. 그건 더 나쁘다.
            foreach (var c in await db.QueryAsync<string>("""
                SELECT k.COLUMN_NAME
                  FROM information_schema.KEY_COLUMN_USAGE k
                  JOIN (SELECT TABLE_NAME FROM information_schema.KEY_COLUMN_USAGE
                         WHERE TABLE_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'PRIMARY' AND TABLE_NAME IN @T
                         GROUP BY TABLE_NAME HAVING COUNT(*) = 1) one ON one.TABLE_NAME = k.TABLE_NAME
                 WHERE k.TABLE_SCHEMA = DATABASE() AND k.CONSTRAINT_NAME = 'PRIMARY' AND k.TABLE_NAME IN @T
                """, new { T = tables }))
                s._globallyUniqueId.Add(c);

            var hasTenant = new HashSet<string>(await db.QueryAsync<string>("""
                SELECT TABLE_NAME FROM information_schema.COLUMNS
                 WHERE TABLE_SCHEMA = DATABASE() AND COLUMN_NAME = 'tenant_id' AND TABLE_NAME IN @T
                """, new { T = tables }), StringComparer.Ordinal);

            // ③ 센다 — 시험 DB 에는 회사가 2개 심겨 있다(무관 행 시드).
            foreach (var kv in s._firstCol)
            {
                var (table, col) = (kv.Key.Table, kv.Value);
                if (string.Equals(col, "tenant_id", StringComparison.Ordinal)) continue;
                if (!hasTenant.Contains(table) || s._spanning.ContainsKey((table, col))) continue;
                s._spanning[(table, col)] = await db.ExecuteScalarAsync<long>(
                    $"SELECT COUNT(*) FROM (SELECT `{col}` FROM `{table}` GROUP BY `{col}` HAVING COUNT(DISTINCT tenant_id) > 1) x");
            }
            return s;
        }

        public bool IsSafe(string table, string index, out string why)
        {
            if (!_firstCol.TryGetValue((table, index), out var col))
            {
                why = $"인덱스 {index} 를 표 {table} 의 스키마에서 못 찾았다 — 판정 불가라 통과시키지 않는다";
                return false;
            }
            if (string.Equals(col, "tenant_id", StringComparison.Ordinal))
            {
                why = "첫 칸이 tenant_id";
                return true;
            }
            if (!_globallyUniqueId.Contains(col))
            {
                why = $"첫 칸이 {col} 다 — tenant_id 도 아니고 어느 표의 단일칸 PK 도 아니다(한 값이 여러 회사에 걸칠 수 있다)";
                return false;
            }
            // 🔴 [4] F-8 — **모르면 막는다**(fail-closed). 측정값이 없다는 것은 「안전하다」가 아니라
            //    「안 쟀다」이고, 안 잰 것을 통과시키면 그게 감시자 0 이다.
            if (!_spanning.TryGetValue((table, col), out var n))
            {
                why = $"첫 칸 {col} 이 두 회사에 걸치는지 **안 쟀다**({table} 에 tenant_id 가 없거나 측정에서 빠졌다) — 모르면 통과시키지 않는다";
                return false;
            }
            if (n > 0)
            {
                why = $"첫 칸 {col} 이 단일칸 PK 인데도 {table} 에서 {n}개 값이 두 회사에 걸쳐 있다 — 데이터가 규칙을 깼다";
                return false;
            }
            why = $"첫 칸 {col} 은 단일칸 PK(한 값 = 한 회사) · {table} 에서 두 회사에 걸친 값 0건";
            return true;
        }
    }

    /// <summary>
    /// <c>EXPLAIN</c> 의 <c>table</c> 칸은 <b>별칭</b>(<c>lp</c>·<c>ec</c>…)이라 스키마에 물어볼 수가 없다.
    /// → 제품 문장에서 <c>FROM/JOIN 표 별칭</c> 을 읽어 <b>진짜 표 이름</b>으로 되돌린다.
    /// </summary>
    private static readonly Regex FromJoinAlias = new(
        @"\b(?:FROM|JOIN)\s+`?([A-Za-z_][A-Za-z0-9_]*)`?\s+(?:AS\s+)?`?([A-Za-z_][A-Za-z0-9_]*)`?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] SqlKeywords = { "ON", "WHERE", "JOIN", "LOCK", "GROUP", "ORDER", "LEFT", "INNER", "AND", "FOR", "UNION" };

    private static Dictionary<string, string> AliasesOf(string sql)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Text.RegularExpressions.Match m in FromJoinAlias.Matches(sql))
        {
            var alias = m.Groups[2].Value;
            if (Array.Exists(SqlKeywords, k => string.Equals(k, alias, StringComparison.OrdinalIgnoreCase))) continue;
            map[alias] = m.Groups[1].Value;
        }
        return map;
    }

    /// <summary>문장 이름 + EXPLAIN 별칭 → 진짜 표 이름. 못 되돌리면 <b>별칭 그대로</b> 돌려준다(그러면 안전판정이 빨간불이 된다).</summary>
    private string RealTableOf(string statement, string? alias)
        => alias is not null && _aliasOf.TryGetValue(statement, out var m) && m.TryGetValue(alias, out var real) ? real : alias ?? "(없다)";

    private readonly Dictionary<string, Dictionary<string, string>> _aliasOf = new(StringComparer.Ordinal);

    // ────────────────────────────── G-RC11 (순수) ──────────────────────────────

    /// <summary>
    /// 🔴 공용 식 2개의 SHA-256 앵커 (설계 §11-3 장치 ②).
    /// <para>
    /// RR 재계산 상수는 공용 식의 술어를 <b>손으로 옮긴 사본</b>이다. 공용 식만 고치면 둘이 조용히 갈라진다.
    /// 이 앵커가 <b>먼저</b> 빨간불이 되어 「RR 식도 같이 고치고 앵커를 갱신하라」고 막아선다.
    /// </para>
    /// <para>
    /// ⚠️ <b>줄바꿈을 LF 로 맞춰서</b> 잰다 — C# raw string 은 소스 파일의 줄바꿈을 그대로 담는다.
    ///   Windows 작업트리(CRLF)와 리눅스 CI(LF)가 다른 해시를 내면 앵커가 아니라 잡음이 된다.
    /// </para>
    /// </summary>
    private const string ReceivableSqlSha256 = "4c5d415a3777656a2b9191ac5773e0772514fc7460b6a4987084027500b7d775";
    private const string PayableSqlSha256 = "9aeee4762b7190685f7c2eb6d616d26809cf8bd35ebef671480c7d058fdad837";

    [Fact(DisplayName = "G-RC11 앵커 — 공용 식 2개의 SHA-256 이 기록값과 같다 (공용 식을 고치면 RR 식도 같이 고치게 만든다)")]
    public void GRC11_공용식_앵커()
    {
        var recv = Sha256Lf(LegacyBalanceMatching.ReceivableRemainingSql);
        var pay = Sha256Lf(LegacyBalanceMatching.PayableRemainingSql);

        Assert.True(recv == ReceivableSqlSha256,
            $"ReceivableRemainingSql 이 바뀌었다.\n  기록 {ReceivableSqlSha256}\n  현재 {recv}\n"
          + "  🔴 RR 전용 재계산 상수(ReceivableMatchedLegacyLockedSql·ReceivableMatchedDocLockedSql)가 같은 술어를 쓰는지 확인하고,\n"
          + "     G-RC10 을 돌려 두 식의 L0·M·R 이 여전히 같은지 잰 뒤 이 앵커를 갱신하라.");
        Assert.True(pay == PayableSqlSha256,
            $"PayableRemainingSql 이 바뀌었다.\n  기록 {PayableSqlSha256}\n  현재 {pay}\n"
          + "  🔴 RR 전용 재계산 상수(PayableMatchedLegacyLockedSql·PayableMatchedDocLockedSql·PayableMatchedReturnLockedSql)를 같이 확인하라.");
    }

    private static string Sha256Lf(string s)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s.Replace("\r\n", "\n")))).ToLowerInvariant();

    /// <summary>
    /// 🔴 <b>고객 노출 문구 앵커</b> — 작지 §19-6-1 (PM 판정 · 무력화 ② 축 변경).
    /// <para>
    /// 다른 사례들은 기대 문구를 <b>제품 상수에서 만든다</b>(복제 금지 · §18 요구 ②). 그래서 상수를 고치면
    /// 양쪽이 같이 바뀌어 <b>아무도 못 잡는다</b> — 무력화 ②가 초록으로 남은 이유다(실측).
    /// </para>
    /// <para>
    /// 여기서는 문구 <b>본문</b>을 기록값으로 박아 둔다. 고치면 이 사례가 먼저 빨간불이 나고,
    /// 앵커를 같이 고쳐야 통과한다 = <b>고객이 보는 말을 바꾸는 일이 의도적인 행위가 된다</b>(#23 · #25).
    /// 문구를 정말 바꿔야 할 때는 기록값을 고치면 된다 — 막는 게이트가 아니라 <b>알리는 게이트</b>다.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "G-RC15 문구 앵커 — 고객이 보는 문구 4개가 기록값과 한 글자도 다르지 않다 (조용한 변경을 막는다)")]
    public void GRC15_고객문구_앵커()
    {
        var anchors = new (string Name, string Actual, string Recorded)[]
        {
            ("MsgDeliveryOver", CollectionService.MsgDeliveryOver,
                "이 거래명세서에 남은 받을 돈은 {0:N0}원입니다. 그보다 큰 금액은 맞출 수 없습니다."),
            ("MsgReceiptOver", CollectionService.MsgReceiptOver,
                "이 매입전표에 남은 줄 돈은 {0:N0}원입니다. 그보다 큰 금액은 맞출 수 없습니다."),
            // 🔴 S3 ㉶ (PM 결재 C-2): 「같은 거래처의」를 뺐다 — 사실이 아니었다. 이 앵커가 그 결정을 지킨다.
            ("MsgMatchBusyCollection", CollectionService.MsgMatchBusyCollection,
                "지금 다른 사용자가 수금을 저장하고 있습니다. 잠시 후 다시 저장해 주세요."),
            ("MsgMatchBusyPayment", CollectionService.MsgMatchBusyPayment,
                "지금 다른 사용자가 지급을 저장하고 있습니다. 잠시 후 다시 저장해 주세요."),
        };

        foreach (var (name, actual, recorded) in anchors)
            Assert.True(string.Equals(actual, recorded, StringComparison.Ordinal),
                $"🔴 고객 노출 문구 {name} 이 바뀌었다.\n  기록: {recorded}\n  현재: {actual}\n"
              + "  고객이 보는 말이다. 바꾸는 것이 맞다면 이 기록값도 같이 고쳐라 — 그러면 통과한다(작지 §19-6-1).\n"
              + "  ⚠️ 개발용어(AI·Claude·에러코드·인덱스명)가 섞이지 않았는지도 같이 보라(#23).");
    }
}

// ══════════════════════════════════════════════════════════════════════════════
//  G-RC17 · G-RC18 — 20260921작2 갈래 A (T3-1) · 작지 §14-1 · 설계 §16-1
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// 🔴 <b>쌍 사본 고정물</b> — G-RC17 이 재는 「봉합 전 ↔ 봉합 후」 두 DB 를 만든다.
///
/// <para>
/// <b>쌍의 정의</b>(설계 §16-1(2)) — <b>같은 회차 · 같은 시드 스크립트 · 같은 채움 수</b>에서 두 사본을 번갈아 잰다.
/// <list type="bullet">
///   <item><b>기준선 사본</b> — DB-125 <b>미적용</b> + ㉪ <b>없는</b> SQL</item>
///   <item><b>봉합 사본</b> — DB-125 <b>적용</b> + ㉪ <b>있는</b> SQL</item>
/// </list>
/// </para>
/// <para>
/// 🔴 <b>토대는 출하 DDL 이다</b>(작지 §3 T3-0) — 오염된 기존 DB 를 쓰지 않는다.
/// 🔴 출하 DDL 에 DB-125 KEY 가 편입된 뒤에도(T3-4) 쌍이 성립하도록, 기준선 사본은 두 인덱스를
/// <b>명시적으로 DROP</b> 하고 봉합 사본은 <b>명시적으로 ADD</b> 한다. 「출하 DDL 이 어느 쪽이냐」에 안 기댄다.
/// </para>
/// <para>
/// 🔴 <b>SKIP 을 통과로 세지 않는다</b> — 변수가 없으면 로컬은 건너뛰고 CI 는 실패한다.
/// </para>
/// </summary>
/// <summary>
/// 🔴 이웃 프로브 한 건. <paramref name="Judged"/> 가 <c>false</c> 면 <b>기록만</b> 한다(CTO C-C3 · G-RC16 ④).
/// </summary>
public sealed record NeighborProbe(string Name, string Sql, bool Judged);

/// <summary>이웃 프로브 결과. <paramref name="Error"/> 는 <b>막힘이 아닌</b> 오류만 담는다.</summary>
public sealed record NeighborProbeResult(string Name, bool Blocked, string? Error, long ElapsedMs);

public sealed class RcRatioPairDbFixture : IDisposable
{
    /// <summary>기준선 사본 — DB-125 인덱스 <b>없음</b>.</summary>
    public string BaseDb { get; } = "hitpan_a_base_" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>봉합 사본 — DB-125 인덱스 <b>있음</b>.</summary>
    public string SealDb { get; } = "hitpan_a_seal_" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>시드 규모 — 실측(작2 §12-3)이 나온 규모와 같은 자릿수로 맞춘다.</summary>
    public const int DeliveryRows = 115_150;

    /// <inheritdoc cref="DeliveryRows"/>
    public const int CollectionRows = 109_822;

    /// <summary>편중 칸 — 이 거래처 하나가 배송 <see cref="HotPartnerDeliveries"/> 건을 가진다.</summary>
    public const string HotPartner = "p-hot";

    /// <inheritdoc cref="HotPartner"/>
    public const int HotPartnerDeliveries = 1_114;

    /// <summary>테넌트는 하나다. 🔴 <c>tenants</c> 를 조인하지 않는다(DB-111/112 전례).</summary>
    public const string TenantId = "a-gate-tenant";

    /// <summary>🔴 비영 진실집합 — <c>seq % 7 == 0</c> 인 수금만 <c>manual</c>. 나머지는 <c>migration</c>.</summary>
    public const int NonMigrationEvery = 7;

    public bool Available { get; }

    /// <summary>못 쓰는 이유. 쓸 수 있으면 <c>null</c>.</summary>
    public string? Unavailable { get; }

    private readonly bool _created;

    public RcRatioPairDbFixture()
    {
        var why = UnavailableReason();
        if (why is not null) { Unavailable = why; return; }

        using (var admin = new MySqlConnection(ServerConnString()))
        {
            admin.Open();
            foreach (var db in new[] { BaseDb, SealDb })
                admin.Execute($"DROP DATABASE IF EXISTS `{db}`; CREATE DATABASE `{db}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
        }
        _created = true;

        foreach (var db in new[] { BaseDb, SealDb })
        {
            ImportShippingDdl(db);
            Seed(db);
        }

        // 🔴 쌍을 확정한다 — 출하 DDL 이 어느 쪽이든 결과가 같게.
        using (var b = new MySqlConnection(DbConnString(BaseDb)))
        {
            b.Open();
            DropIfExists(b, BaseDb, "sales_deliveries", "idx_sd_tenant_partner_src");
            DropIfExists(b, BaseDb, "collections", "idx_coll_tenant_doc_cover");
        }
        using (var s = new MySqlConnection(DbConnString(SealDb)))
        {
            s.Open();
            AddIfMissing(s, SealDb, "sales_deliveries", "idx_sd_tenant_partner_src",
                "(`tenant_id`,`partner_id`,`source_type`)");
            AddIfMissing(s, SealDb, "collections", "idx_coll_tenant_doc_cover",
                "(`tenant_id`,`ref_doc_type`,`ref_doc_id`,`is_active`,`source_type`,`amount`)");
        }

        Available = true;
    }

    private static bool IndexExists(MySqlConnection c, string db, string table, string index) =>
        c.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM information_schema.statistics "
          + "WHERE table_schema=@db AND table_name=@t AND index_name=@i",
            new { db, t = table, i = index }) > 0;

    private static void DropIfExists(MySqlConnection c, string db, string table, string index)
    {
        if (IndexExists(c, db, table, index))
            c.Execute($"ALTER TABLE `{table}` DROP INDEX `{index}`");
    }

    private static void AddIfMissing(MySqlConnection c, string db, string table, string index, string cols)
    {
        if (!IndexExists(c, db, table, index))
            c.Execute($"ALTER TABLE `{table}` ADD KEY `{index}` {cols}");
    }

    /// <summary>
    /// 🔴 <b>채움 비율</b>을 쌍의 양쪽에 <b>같은 규칙 · 같은 수</b>로 건다.
    /// 반환값 = 실제로 채워진 행 수(A-2 가 이 값을 기대값과 대조한다).
    /// </summary>
    public long SetFill(string db, int percent)
    {
        var k = (long)Math.Floor(CollectionRows * (double)percent / 100.0);
        using var c = new MySqlConnection(DbConnString(db));
        c.Open();
        c.Execute("UPDATE collections SET ref_doc_id = NULL", commandTimeout: 300);
        if (k > 0)
        {
            c.Execute(
                "UPDATE collections SET ref_doc_id = CONCAT('d', LPAD(seq_no, 9, '0')) WHERE seq_no <= @k",
                new { k }, commandTimeout: 300);
        }
        return c.ExecuteScalar<long>("SELECT COUNT(*) FROM collections WHERE ref_doc_id IS NOT NULL");
    }

    /// <summary>🔴 판정 원칙 5 — 측정 직전 <c>ANALYZE</c>. <b>쌍의 양쪽 모두.</b> 한쪽만 하면 거짓 초록(K-4).</summary>
    public void Analyze(string db)
    {
        using var c = new MySqlConnection(DbConnString(db));
        c.Open();
        c.Execute("ANALYZE TABLE collections", commandTimeout: 300);
        c.Execute("ANALYZE TABLE sales_deliveries", commandTimeout: 300);
    }

    /// <summary>판정 원칙 6 — <c>History list length</c>. 0 이 아니면 퍼지 잔재가 남아 있다.</summary>
    public long HistoryListLength()
    {
        using var c = new MySqlConnection(ServerConnString());
        c.Open();
        var status = c.QuerySingle<dynamic>("SHOW ENGINE INNODB STATUS").Status as string ?? "";
        var m = Regex.Match(status, @"History list length\s+(\d+)");
        return m.Success ? long.Parse(m.Groups[1].Value) : -1;
    }

    /// <summary>
    /// 🔴 <b>잠금 행 수 측정</b> — 한 연결에 한 문장, <c>ROLLBACK</c> 으로 닫는다.
    /// 읽기 프로브가 아니다: <c>LOCK IN SHARE MODE</c> 가 실제로 잠근 행 수를 <c>innodb_trx</c> 에서 읽는다.
    ///
    /// <para>
    /// 🔴 <b><c>innodb_trx</c> 는 즉시 보이지 않는다</b>(A-1 봉합 · 작2 §17-3 정정).
    /// InnoDB 는 <c>information_schema.innodb_trx</c> 를 <b>내부 캐시(<c>trx_i_s_cache</c>)</b>로 돌려주고
    /// 그 캐시는 <b>최소 유휴 시간</b> 동안 갱신되지 않는다. 그래서 <b>쌍의 두 번째 측정</b>처럼
    /// 직전 조회와 간격이 짧으면 <b>자기 트랜잭션이 아직 없던 스냅샷</b>이 돌아와 행이 0건이 된다.
    /// 🔴 <b>연결 문제가 아니다</b> — 같은 연결·같은 <c>CONNECTION_ID()</c> 인데도 안 잡힌다
    /// (작2 §17-3 의 「다른 연결에서 읽는다」 진단을 이 주석이 정정한다. 코드는 처음부터 같은 연결이었다).
    /// </para>
    /// <para>
    /// ⇒ <b>캐시가 갱신될 때까지 다시 읽는다.</b> 🔴 끝내 못 잡으면 <c>-1</c> 을 그대로 돌려준다 —
    /// <b>0 으로 갈음해 통과시키지 않는다</b>(A-1′ 가 그 거짓 초록을 잡는 자리다).
    /// <paramref name="Attempts"/> 는 <b>실험이 실제로 잡혔다는 증거</b>로 보고표에 같이 싣는다.
    /// </para>
    /// </summary>
    public (decimal Sum, long Locked, int Attempts) MeasureLock(string db, string sql, string partnerId)
    {
        using var c = new MySqlConnection(DbConnString(db));
        c.Open();
        using var tx = c.BeginTransaction();
        var sum = c.ExecuteScalar<decimal>(sql, new { TenantId, PartnerId = partnerId }, tx, commandTimeout: 300);

        var (locked, attempts) = ReadTrxRowsLocked(c, tx);
        tx.Rollback();
        return (sum, locked, attempts);
    }

    /// <summary>
    /// 🔴 「행이 없다」와 「0 이다」를 구별해 <c>trx_rows_locked</c> 를 읽는다.
    /// 캐시가 갱신될 때까지 다시 읽고, <b>끝내 못 잡으면 <c>-1</c></b> 을 돌려준다(0 으로 갈음 금지).
    /// </summary>
    private static (long Locked, int Attempts) ReadTrxRowsLocked(MySqlConnection c, MySqlTransaction tx)
    {
        long? locked = null;
        var attempts = 0;
        while (attempts < TrxLookupMaxAttempts)
        {
            attempts++;
            locked = c.QuerySingleOrDefault<long?>(
                "SELECT trx_rows_locked FROM information_schema.innodb_trx WHERE trx_mysql_thread_id = CONNECTION_ID()",
                transaction: tx);
            if (locked.HasValue) break;
            Thread.Sleep(TrxLookupRetryDelayMs);
        }
        return (locked ?? -1, attempts);
    }

    /// <summary>🔴 <c>trx_i_s_cache</c> 갱신을 기다리는 최대 횟수. 못 잡으면 <c>-1</c> 로 남긴다(거짓 초록 금지).</summary>
    private const int TrxLookupMaxAttempts = 12;

    /// <summary>재조회 간격. InnoDB 캐시 최소 유휴 시간(0.1초)보다 넉넉히 잡는다.</summary>
    private const int TrxLookupRetryDelayMs = 150;

    /// <summary>프로브가 막힘을 판정하는 대기 시간(초). 짧게 잡아 게이트가 오래 안 걸리게 한다.</summary>
    public const int ProbeLockWaitSeconds = 3;

    /// <summary>
    /// 🔴 <b>이웃 프로브 실행</b> — W-3(등록 경로 침해 없음)을 재는 유일한 수단(작2 §16-3).
    ///
    /// <para>
    /// 🔴 <b>읽기 프로브가 아니다</b>. 보유자가 <c>LOCK IN SHARE MODE</c> 로 쥐고 있는 동안
    /// <b>다른 연결에서 경로 끝 <c>INSERT</c></b> 를 시도한다. 읽기끼리는 서로 안 막으므로
    /// 프로브를 읽기로 만들면 <b>영원히 초록</b>이다(작2 §5 N-6).
    /// </para>
    /// <para>
    /// <paramref name="holderSql"/> 가 <c>null</c> 이면 <b>보유자 없는 대조군</b>이다 — 전부 통과해야 한다.
    /// 통과하지 않으면 프로브 자체가 <b>다른 이유로</b> 막히는 것이고, 그때 잠금을 논하면 엉뚱한 곳을 본다.
    /// </para>
    /// <para>
    /// 🔴 <b>막힘(1205·1213)과 그 밖의 오류를 구별한다.</b> 스키마 오류를 「막혔다」로 세면
    /// 게이트가 아무것도 안 재고 빨간불을 낸다. 그 밖의 오류는 <c>Error</c> 에 담아 <b>그대로 드러낸다</b>.
    /// </para>
    /// </summary>
    public (long HolderLocked, int HolderAttempts, List<NeighborProbeResult> Results) RunNeighborProbes(
        string db, string? holderSql, string partnerId, IReadOnlyList<NeighborProbe> probes)
    {
        using var holder = new MySqlConnection(DbConnString(db));
        holder.Open();

        MySqlTransaction? htx = null;
        long holderLocked = 0;
        var holderAttempts = 0;

        if (holderSql is not null)
        {
            htx = holder.BeginTransaction();
            holder.ExecuteScalar<decimal>(holderSql, new { TenantId, PartnerId = partnerId }, htx, commandTimeout: 300);
            // 🔴 보유자가 실제로 쥐었다는 증거를 남긴다 — 안 쥔 채 「통과」를 세면 거짓 초록이다.
            (holderLocked, holderAttempts) = ReadTrxRowsLocked(holder, htx);
        }

        var results = new List<NeighborProbeResult>();
        try
        {
            using var probe = new MySqlConnection(DbConnString(db));
            probe.Open();
            probe.Execute($"SET SESSION innodb_lock_wait_timeout = {ProbeLockWaitSeconds}");

            foreach (var p in probes)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using var ptx = probe.BeginTransaction();
                try
                {
                    probe.Execute(p.Sql, transaction: ptx, commandTimeout: 60);
                    ptx.Rollback();                       // 🔴 프로브는 자취를 안 남긴다
                    results.Add(new NeighborProbeResult(p.Name, false, null, sw.ElapsedMilliseconds));
                }
                catch (MySqlException ex)
                {
                    try { ptx.Rollback(); }
                    catch (MySqlException rex)
                    {
                        // #15 — 빈 catch 금지. 롤백 실패는 판정을 뒤집지 않는다.
                        Console.Error.WriteLine($"[G-RC16] 프로브 롤백 실패(무해): {rex.Message}");
                    }

                    var blocked = ex.Number is 1205 or 1213;   // 1205 잠금 대기 초과 · 1213 교착
                    results.Add(new NeighborProbeResult(
                        p.Name, blocked, blocked ? null : $"{ex.Number} {ex.Message}", sw.ElapsedMilliseconds));
                }
            }
        }
        finally
        {
            if (htx is not null) { htx.Rollback(); htx.Dispose(); }
        }

        return (holderLocked, holderAttempts, results);
    }

    private void ImportShippingDdl(string db)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(MysqlExe())
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardInputEncoding = new UTF8Encoding(false)
        };
        psi.ArgumentList.Add($"--host={Host}");
        psi.ArgumentList.Add($"--port={Port}");
        psi.ArgumentList.Add($"-u{User}");
        if (!string.IsNullOrEmpty(Pass)) psi.ArgumentList.Add($"-p{Pass}");
        psi.ArgumentList.Add("--default-character-set=utf8mb4");
        psi.ArgumentList.Add(db);

        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.StandardInput.Write(File.ReadAllText(Path.Combine(RepoRoot(), "installer", "hitpan_db_clean.sql")));
        proc.StandardInput.Close();
        var err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"출하 DDL import 실패({db}):\n{err}");
    }

    /// <summary>
    /// 🔴 <b>시드</b> — 실측이 나온 규모·모양을 그대로 만든다.
    /// <list type="bullet">
    ///   <item>거래처 편중: <see cref="HotPartner"/> 하나가 배송 <see cref="HotPartnerDeliveries"/> 건</item>
    ///   <item>타입 칸 선택도 0: <c>sales_deliveries.source_type</c> 전부 <c>migration</c></item>
    ///   <item>🔴 비영 진실집합: 수금 <see cref="NonMigrationEvery"/> 개마다 하나가 <c>manual</c></item>
    /// </list>
    /// <c>seq_no</c> 는 채움 비율을 결정적으로 걸기 위한 보조 컬럼이다(추가만 · 기존 컬럼 무접촉).
    /// </summary>
    private void Seed(string db)
    {
        using var c = new MySqlConnection(DbConnString(db));
        c.Open();

        // 🔴 부모 먼저 — sales_deliveries.partner_id 에 FK(fk_sd_partner → partners)가 걸려 있다.
        //    partners 자신은 FK 가 없으므로 tenants 는 만들지 않는다(마이그가 tenants 를 안 보는 것과 같은 결).
        c.Execute($@"
            INSERT INTO partners
                (partner_id, tenant_id, partner_code, partner_name, partner_type,
                 is_active, created_at, updated_at)
            SELECT CONCAT('p', LPAD(seq, 8, '0')),
                   '{TenantId}',
                   CONCAT('PC', LPAD(seq, 8, '0')),
                   CONCAT('거래처', seq),
                   '[]', 1, NOW(6), NOW(6)
              FROM seq_1_to_{DeliveryRows}", commandTimeout: 600);

        c.Execute($@"
            INSERT INTO partners
                (partner_id, tenant_id, partner_code, partner_name, partner_type,
                 is_active, created_at, updated_at)
            VALUES ('{HotPartner}', '{TenantId}', 'PC-HOT', '편중 거래처', '[]', 1, NOW(6), NOW(6))",
            commandTimeout: 300);

        c.Execute($@"
            INSERT INTO sales_deliveries
                (delivery_id, tenant_id, delivery_no, partner_id, delivery_date,
                 source_type, status, total_amount, vat_amount, created_at, updated_at)
            SELECT CONCAT('d', LPAD(seq, 9, '0')),
                   '{TenantId}',
                   CONCAT('DN', seq),
                   IF(seq <= {HotPartnerDeliveries}, '{HotPartner}', CONCAT('p', LPAD(seq, 8, '0'))),
                   '2026-01-01',
                   'migration', 'confirmed', 1000.00, 100.00, NOW(6), NOW(6)
              FROM seq_1_to_{DeliveryRows}", commandTimeout: 600);

        c.Execute("ALTER TABLE collections ADD COLUMN seq_no INT NULL", commandTimeout: 300);

        c.Execute($@"
            INSERT INTO collections
                (collection_id, tenant_id, partner_id, collection_date, amount,
                 collection_method, ref_doc_type, ref_doc_id, is_active, source_type, seq_no)
            SELECT CONCAT('c', LPAD(seq, 9, '0')),
                   '{TenantId}',
                   CONCAT('p', LPAD(seq, 8, '0')),
                   '2026-01-01',
                   100.00,
                   'cash',
                   'sales_delivery',
                   NULL,
                   1,
                   IF(seq % {NonMigrationEvery} = 0, 'manual', 'migration'),
                   seq
              FROM seq_1_to_{CollectionRows}", commandTimeout: 600);

        c.Execute("ALTER TABLE collections ADD KEY idx_a_gate_seq (seq_no)", commandTimeout: 300);
    }

    /// <summary>🔴 이 시드에서 L2 의 <b>기대 합계</b> — 0 이 아니어야 한다(A-4).</summary>
    public decimal ExpectedL2Sum(int fillPercent)
    {
        var k = (long)Math.Floor(CollectionRows * (double)fillPercent / 100.0);
        var overlap = Math.Min(k, HotPartnerDeliveries);
        var hits = overlap / NonMigrationEvery;
        return hits * 100.00m;
    }

    private static string? Port => Environment.GetEnvironmentVariable("HITPAN_DB_LOCK_PORT")
                                ?? Environment.GetEnvironmentVariable("HITPAN_DB_STMT_PORT");
    private static string Host => Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
    private static string User => Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root";
    private static string Pass => Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";

    public string DbConnString(string db) => ServerConnString().Replace("User=", $"Database={db};User=");

    private static string ServerConnString() =>
        $"Server={Host};Port={Port};User={User};Password={Pass};DefaultCommandTimeout=600;GuidFormat=None;AllowUserVariables=true;";

    private static string MysqlExe() =>
        Environment.GetEnvironmentVariable("HITPAN_MYSQL") ?? @"C:\Program Files\MariaDB 11.4\bin\mysql.exe";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("HitPan.sln 을 못 찾았다.");
    }

    /// <summary>
    /// 🔴 <b>SEQUENCE 엔진</b>이 있어야 시드가 한 문장으로 선다. 없으면 <b>건너뛰지 말고 못 쓴다고 말한다</b>.
    /// </summary>
    private static string? UnavailableReason()
    {
        if (string.IsNullOrWhiteSpace(Port))
        {
            if (DbGateEnvironment.IsCi)
                throw new Xunit.Sdk.XunitException(
                    "[G-RC17] HITPAN_DB_LOCK_PORT(또는 HITPAN_DB_STMT_PORT)가 없다 — CI 는 반드시 띄워야 한다.\n"
                  + "  이 게이트가 잠금 범위 봉합의 유일한 감시자다(작2 §14-1).");
            return "HITPAN_DB_LOCK_PORT / HITPAN_DB_STMT_PORT 가 없다";
        }
        if (!DbGateEnvironment.IsCi && !File.Exists(MysqlExe()))
            return $"mysql 클라이언트 없음: {MysqlExe()}";
        try
        {
            using var c = new MySqlConnection(ServerConnString());
            c.Open();
            var seq = c.ExecuteScalar<long>(
                "SELECT COUNT(*) FROM information_schema.engines WHERE engine='SEQUENCE' AND support IN ('YES','DEFAULT')");
            if (seq == 0) return "SEQUENCE 엔진이 없다 — 이 시드는 seq_1_to_N 을 쓴다";
            return null;
        }
        catch (MySqlException ex)
        {
            return $"측정 인스턴스에 못 붙는다: {ex.Message}";
        }
    }

    public void Dispose()
    {
        if (!_created) return;
        try
        {
            using var admin = new MySqlConnection(ServerConnString());
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{BaseDb}`; DROP DATABASE IF EXISTS `{SealDb}`;");
        }
        catch (MySqlException ex)
        {
            // #15 — 빈 catch 금지. 정리 실패는 게이트 판정을 뒤집지 않는다.
            Console.Error.WriteLine($"[G-RC17] 사본 정리 실패(무해): {ex.Message}");
        }
    }
}

/// <summary>
/// 🔴 <b>G-RC17 · G-RC18</b> — 20260921작2 갈래 A · T3-1 (작지 §14-1 · 설계 §16-1).
///
/// <para>
/// <b>G-RC17 판정식</b>: <c>봉합후_trx_rows_locked ≤ 봉합전_trx_rows_locked × 0.80</c><br/>
/// <b>판정 구간</b> = <c>0% · 2% · 4%</c> 세 칸 <b>전부</b>. 5% 이상은 <b>기록만</b>(K-6 · 비의 수치 미실측).
/// </para>
///
/// <para>
/// 🔴 <b><c>0.80</c> 은 「고정 상수 상한」이 아니다.</b> 잠금 행 수에 거는 절대값이 아니라
/// <b>같은 회차 쌍의 비</b>다. 그래서 작지 §0 의 「고정 상수 상한 금지」·N-5 와 충돌하지 않는다.
/// 시드가 0건이면 <b>분모가 0 이라 A-1 이 먼저 운다</b>. 재는 사례 수와 같은 상한도 아니다
/// (<c>MaxCrossTenantBlocked=4</c> 사고와 다른 모양이다).
/// </para>
///
/// <para><b>🔴 계수 0.80 이 깨지는 조건 — 설계 §16-1(4) K-1~K-6 을 그대로 옮긴다</b>
/// <list type="bullet">
///   <item><b>K-1</b> 🔴 <b>2% 근방 칸 — 완충이 <c>0.059</c> 뿐이다</b>(관측비 0.741).
///         봉합후가 <b>8% 나빠지거나</b> 기준선이 <b>8% 좋아지면</b> 거짓 빨간불이 난다.
///         <b>이 칸이 가장 얇다.</b></item>
///   <item><b>K-2</b> 🔴 <b>몰림 분포</b>(채움이 한 거래처·한 기간에 쏠림) — C2 의 <c>partner_id</c> 선택도가
///         죽어 비가 1 로 올라 빨간불. ⚠️ <b>미실측</b>(CTO C-D3).</item>
///   <item><b>K-3</b> 🔴 <b>옵티마이저(MariaDB) 버전 변경</b> — ㉪ 가 무시되면 0% 칸 비가 1 로 간다(CTO C-B2).</item>
///   <item><b>K-4</b> 🔴 <b>쌍의 한쪽만 <c>ANALYZE</c> 하면 거짓 초록</b> — 기준선 쪽을 빠뜨리면
///         비가 <c>0.02</c> 까지 떨어진다(실측 2% 칸 37배 플립). <b>가장 위험하다.</b>
///         그래서 이 게이트는 <b>양쪽 모두</b> ANALYZE 한다.</item>
///   <item><b>K-5</b> 🔴 <b>퍼지 잔재(<c>HLL &gt; 0</c>)</b> — 봉합후 칸을 <b>260배</b>까지 때린다(43→11,159).
///         0% 칸은 완충이 커서 견디나 <b>2% 칸은 못 견딘다</b> ⇒ 거짓 빨간불.</item>
///   <item><b>K-6</b> ⚠️ <b>채움 5% 이상 구간</b> — 9구간 전부 기준선 이하였으나 <b>비의 수치는 기록이 없다</b>
///         ⇒ <b>판정은 0·2·4% 세 칸만</b>, 5·10·50·100% 는 기록만.</item>
/// </list>
/// </para>
/// </summary>
public sealed class RcLockRatioGateTests : IClassFixture<RcRatioPairDbFixture>
{
    private readonly RcRatioPairDbFixture _fx;

    public RcLockRatioGateTests(RcRatioPairDbFixture fx) => _fx = fx;

    /// <summary>🔴 계수. 설계 §16-1(3) · PM 결재 P-20.</summary>
    private const double RatioCeiling = 0.80;

    /// <summary>A-3 — 봉합 전(= 쌍이 같은 상태)이면 비가 이 값 이상이라 빨간불이어야 한다.</summary>
    private const double AliveRatioFloor = 0.95;

    /// <summary>㉪ — L2 에 들어가는 술어 한 줄.</summary>
    private const string KappaPredicate = "ec.ref_doc_id IS NOT NULL";

    /// <summary>🔴 판정 구간. 5% 이상은 기록만 한다(K-6).</summary>
    private static readonly int[] JudgedFills = { 0, 2, 4 };

    /// <summary>🔴 기록만 하는 구간.</summary>
    private static readonly int[] RecordedFills = { 5, 10 };

    /// <summary>
    /// 🔴 <b>봉합 전 SQL</b> — 제품 문장에서 ㉪ 한 줄만 뺀다.
    /// <para>
    /// 🔴 이렇게 만드는 이유: <b>A-3(살아있음)이 저절로 걸리게 하려고</b>다.
    /// 누가 제품에서 ㉪ 를 되돌리면 두 문장이 <b>같아지고</b> 비가 1.0 이 되어 게이트가 운다.
    /// 즉 이 게이트는 「인덱스가 있나」가 아니라 <b>「봉합이 아직 거기 있나」</b>를 잰다.
    /// </para>
    /// </summary>
    private static string BaselineSql(string productSql)
    {
        var kept = productSql
            .Split('\n')
            .Where(l => !l.Contains(KappaPredicate, StringComparison.Ordinal));
        return string.Join("\n", kept);
    }

    [Fact(DisplayName = "G-RC17 쌍 부등식 — L2 잠금이 0·2·4% 세 칸 전부에서 봉합 전의 0.80 배 이하 (양쪽 ANALYZE · 살아있음 단언 동반)")]
    public void G_RC17_PairInequality()
    {
        if (!_fx.Available) { Assert.True(DbGateEnvironment.SkipOrFail(_fx.Unavailable!)); return; }

        var productSql = LegacyBalanceMatching.ReceivableMatchedDocLockedSql;
        var baselineSql = BaselineSql(productSql);

        // ── A-3 살아있음 ① — 제품에 ㉪ 가 실제로 있나 ──────────────────────────
        // 없으면 두 문장이 같다 = 쌍이 같은 상태 = 비가 1.0 = 빨간불. 그게 「봉합 전」이다.
        Assert.True(
            !string.Equals(productSql, baselineSql, StringComparison.Ordinal),
            "🔴 G-RC17 이 잴 것이 없다 — 제품 L2(ReceivableMatchedDocLockedSql)에 ㉪ 가 없다.\n"
          + $"  찾은 술어: `{KappaPredicate}`\n"
          + "  봉합 전이라면 이 빨간불이 정상이다(작2 §3 T3-1 → T3-3 순서).\n"
          + "  봉합 뒤인데 이게 뜨면 ㉪ 가 되돌려진 것이다 — 그대로 두지 마라.");

        var report = new StringBuilder();
        var failures = new List<string>();

        foreach (var fill in JudgedFills.Concat(RecordedFills))
        {
            var judged = JudgedFills.Contains(fill);

            // ── A-2 — 쌍의 양쪽에 같은 규칙 · 같은 수로 채운다 ──────────────
            var expected = (long)Math.Floor(RcRatioPairDbFixture.CollectionRows * (double)fill / 100.0);
            var baseFilled = _fx.SetFill(_fx.BaseDb, fill);
            var sealFilled = _fx.SetFill(_fx.SealDb, fill);

            Assert.True(baseFilled == expected && sealFilled == expected,
                $"🔴 A-2 — 채움 수가 기대와 다르다 @ {fill}%.\n"
              + $"  기대 {expected} · 기준선 {baseFilled} · 봉합 {sealFilled}\n"
              + "  쌍이 「같은 채움 수」가 아니면 비는 아무것도 뜻하지 않는다.");

            // ── 🔴 판정 원칙 5 — 양쪽 모두 ANALYZE (K-4) ────────────────────
            _fx.Analyze(_fx.BaseDb);
            _fx.Analyze(_fx.SealDb);

            // ── 판정 원칙 6 — HLL 확인 (K-5) ────────────────────────────────
            var hll = _fx.HistoryListLength();

            var b = _fx.MeasureLock(_fx.BaseDb, baselineSql, RcRatioPairDbFixture.HotPartner);
            var s = _fx.MeasureLock(_fx.SealDb, productSql, RcRatioPairDbFixture.HotPartner);

            var ratio = b.Locked > 0 ? (double)s.Locked / b.Locked : -1;

            // 🔴 조회 횟수를 같이 싣는다 — 「실험이 실제로 잡혔다」는 증거다(작2 §17-3 A-1 봉합).
            //    2 이상이면 innodb_trx 캐시가 갱신되기를 기다린 것이다. 1 이면 첫 조회에 잡혔다.
            report.AppendLine(
                $"[G-RC17] 채움 {fill,3}% · 채운 행 {baseFilled,7} · HLL {hll,4} · "
              + $"봉합전 {b.Locked,8}(조회 {b.Attempts}) · 봉합후 {s.Locked,8}(조회 {s.Attempts}) · 비 {ratio:F4} · "
              + (judged ? "판정" : "기록만"));

            if (!judged) continue;

            // ── A-1 — 분모가 0 이면 시드가 안 돈 것이다 ─────────────────────
            if (b.Locked <= 0)
            {
                failures.Add($"A-1 위반 @ {fill}% — 봉합 전 잠금이 {b.Locked} 다. 시드가 안 돌았다(분모 0).");
                continue;
            }

            // ── 🔴 A-1′ — 봉합 쪽 측정이 「등록조차 안 됐다」를 0 으로 착각하지 않는다 ──
            //    합계가 0 이 아니라면 그 행들을 실제로 잠갔다는 뜻이다. 그런데 잠금 행 수가 0 이면
            //    innodb_trx 에서 이 트랜잭션을 못 찾은 것이다 = 측정 실패지 「완벽한 봉합」이 아니다.
            //    이걸 통과로 세면 게이트가 영원히 초록이 된다(작2 §0 「읽기 프로브 금지」와 같은 사고).
            if (s.Sum != 0 && s.Locked <= 0)
            {
                failures.Add(
                    $"A-1′ 위반 @ {fill}% — 봉합 쪽 합계가 {s.Sum} 인데 잠금 행 수가 {s.Locked} 다"
                  + $"(innodb_trx 조회 {s.Attempts}회).\n"
                  + "    합계가 0 이 아니면 그 행들을 잠갔어야 한다 ⇒ 측정이 안 잡힌 것이다(거짓 초록).\n"
                  + "    🔴 조회 횟수가 상한까지 갔다면 캐시 지연이 아니라 다른 원인이다 — 새로 규명하라.");
                continue;
            }

            // ── A-4 — 값 동등의 기대값이 0 이 아니어야 한다 ─────────────────
            //    🔴 `0.00 = 0.00` 은 증명이 아니다(작2 §16-3 W-1).
            //    단 0% 칸은 「맞은 전표가 하나도 없다」가 정의라서 구조적으로 0 이다 — 거기선 면제한다.
            var expectedSum = _fx.ExpectedL2Sum(fill);
            if (fill > 0 && expectedSum <= 0)
                failures.Add($"A-4 위반 @ {fill}% — 기대 합계가 0 이다. 비영 진실집합이 아니다.");

            // ── W-1 — 금액 불변. 봉합 전후 값이 같아야 한다 ─────────────────
            if (b.Sum != s.Sum)
                failures.Add($"🔴 W-1 위반 @ {fill}% — 금액이 바뀌었다. 봉합전 {b.Sum} · 봉합후 {s.Sum}");
            if (fill > 0 && s.Sum != expectedSum)
                failures.Add($"🔴 W-1 위반 @ {fill}% — 봉합후 합계 {s.Sum} 가 기대값 {expectedSum} 과 다르다.");

            // ── 판정식 ──────────────────────────────────────────────────────
            if (ratio > RatioCeiling)
                failures.Add(
                    $"🔴 G-RC17 @ {fill}% — 비 {ratio:F4} 가 상한 {RatioCeiling:F2} 를 넘었다 "
                  + $"(봉합전 {b.Locked} · 봉합후 {s.Locked}).");
        }

        Console.Error.Write(report.ToString());

        Assert.True(failures.Count == 0,
            "🔴 G-RC17 실패:\n  " + string.Join("\n  ", failures) + "\n\n" + report
          + "\n  ⚠️ 비가 1.0 근처라면 봉합(C2·C4·㉪) 중 하나가 빠진 것이다 — 셋은 한 묶음이다(작2 §0).\n"
          + "  ⚠️ 비가 0.02 처럼 비현실적으로 좋다면 쌍의 한쪽만 ANALYZE 된 것을 의심하라(K-4).");
    }

    [Fact(DisplayName = "G-RC17 살아있음(A-3) — 쌍을 같은 상태로 두면 비가 0.95 이상이라 반드시 빨간불이다 (게이트가 영원히 초록이 아님을 증명)")]
    public void G_RC17_Alive_A3()
    {
        if (!_fx.Available) { Assert.True(DbGateEnvironment.SkipOrFail(_fx.Unavailable!)); return; }

        // 🔴 「봉합 전」을 흉내낸다 — 두 사본 모두 기준선 SQL 로 잰다(= ㉪ 없는 상태).
        //    인덱스는 봉합 사본에 그대로 둔 채다. 그래도 ㉪ 가 없으면 0% 칸에서 비가 1 로 간다(N-2).
        var baselineSql = BaselineSql(LegacyBalanceMatching.ReceivableMatchedDocLockedSql);

        var expected = 0L;
        var baseFilled = _fx.SetFill(_fx.BaseDb, 0);
        var sealFilled = _fx.SetFill(_fx.SealDb, 0);
        Assert.True(baseFilled == expected && sealFilled == expected, "A-2 — 0% 채움이 안 걸렸다.");

        _fx.Analyze(_fx.BaseDb);
        _fx.Analyze(_fx.SealDb);

        var b = _fx.MeasureLock(_fx.BaseDb, baselineSql, RcRatioPairDbFixture.HotPartner);
        var s = _fx.MeasureLock(_fx.SealDb, baselineSql, RcRatioPairDbFixture.HotPartner);

        Assert.True(b.Locked > 0,
            $"A-1 — 봉합 전 잠금이 {b.Locked} 다(innodb_trx 조회 {b.Attempts}회). 시드가 안 돌았다.");

        // 🔴 측정 실패(-1)를 「좋은 비」로 읽지 않는다.
        //    여기서 s.Locked 가 -1 이면 비가 음수가 되어 「게이트가 안 살아있다」는 엉뚱한 판정이 난다.
        //    A-1′ 와 같은 자리다 — 「행이 없다」는 「0 이다」가 아니다.
        Assert.True(s.Locked > 0,
            $"A-1′ — 봉합 쪽 잠금이 {s.Locked} 다(innodb_trx 조회 {s.Attempts}회).\n"
          + "  같은 기준선 SQL 을 쟀으니 잠갔어야 한다 ⇒ 측정이 안 잡힌 것이다.\n"
          + "  🔴 이걸 비에 넣으면 음수가 되어 A-3 이 「게이트가 죽었다」로 잘못 운다.");

        var ratio = (double)s.Locked / b.Locked;
        Console.Error.WriteLine(
            $"[G-RC17/A-3] ㉪ 없이 0% — 봉합전 {b.Locked}(조회 {b.Attempts}) · "
          + $"봉합후 {s.Locked}(조회 {s.Attempts}) · 비 {ratio:F4} (기대: {AliveRatioFloor} 이상)");

        Assert.True(ratio >= AliveRatioFloor,
            $"🔴 A-3 — ㉪ 를 뺐는데도 비가 {ratio:F4} 로 좋다(기대 {AliveRatioFloor} 이상).\n"
          + "  그러면 G-RC17 은 ㉪ 가 되돌려져도 초록일 수 있다 = 살아있는 게이트가 아니다.\n"
          + "  인덱스만으로 0% 구간이 고쳐졌다는 뜻이므로, 봉합의 근거(작2 §0 「술어를 못 빼는 이유」)를 다시 재라.");
    }

    // ────────────────────────────── G-RC16 (이웃 프로브 · W-3) ──────────────────────────────

    /// <summary>🔴 편중 거래처와 <b>무관한</b> 거래처. 시드가 만든 <c>p00000001</c> 이다(<see cref="RcRatioPairDbFixture.HotPartner"/> 가 아니다).</summary>
    private const string ColdPartner = "p00000001";

    /// <summary>
    /// 🔴 <b>경로 끝 INSERT 프로브 5종</b>(작2 §4 G-RC16 ①~⑤).
    /// <list type="bullet">
    ///   <item>①② <b>무관 거래처</b> 수금·지급 등록</item>
    ///   <item>③ <b>같은 거래처</b> 매입 등록 — 구동표를 안 옮긴다는 확인</item>
    ///   <item>④ <b>같은 거래처</b> 매입반품 — 🔴 <b>판정에서 뺀다</b>(L5 미해결 · CTO C-C3). <b>상태만 기록</b></item>
    ///   <item>⑤ 다른 거래처 <b>지급 등록 경로</b> — L4 를 읽고 이어서 등록한다</item>
    /// </list>
    /// <para>
    /// ⚠️ <b>⑤ 는 근사다</b> — 서비스 계층(<c>PaymentService</c>)을 부르는 게 아니라
    /// 제품 문장 <c>PayableMatchedDocLockedSql</c> 을 같은 트랜잭션에서 읽고 <c>payments</c> 에 넣는다.
    /// <b>잠금 경로는 같지만 경로 「전체」는 아니다.</b> 이 한계를 개발명세서에 적는다.
    /// </para>
    /// </summary>
    private static IReadOnlyList<NeighborProbe> BuildProbes()
    {
        const string t = RcRatioPairDbFixture.TenantId;
        const string hot = RcRatioPairDbFixture.HotPartner;

        var payableRead = LegacyBalanceMatching.PayableMatchedDocLockedSql
            .Replace("@TenantId", $"'{t}'", StringComparison.Ordinal)
            .Replace("@PartnerId", $"'{ColdPartner}'", StringComparison.Ordinal);

        return new[]
        {
            new NeighborProbe("① 무관 거래처 collections INSERT",
                $"INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, "
              + $"ref_doc_type, ref_doc_id, is_active, source_type) "
              + $"VALUES (UUID(), '{t}', '{ColdPartner}', '2026-09-21', 1000.00, 'sales_delivery', NULL, 1, 'manual')",
                true),

            new NeighborProbe("② 무관 거래처 payments INSERT",
                $"INSERT INTO payments (payment_id, tenant_id, partner_id, payment_type, amount, payment_date, "
              + $"ref_order_id, is_active, source_type) "
              + $"VALUES (UUID(), '{t}', '{ColdPartner}', 'purchase', 1000.00, '2026-09-21', NULL, 1, 'manual')",
                true),

            new NeighborProbe("③ 같은 거래처 purchase_receipts INSERT",
                $"INSERT INTO purchase_receipts (receipt_id, tenant_id, receipt_no, partner_id, receipt_date, "
              + $"source_type, status, total_amount, vat_amount, created_at) "
              + $"VALUES (UUID(), '{t}', CONCAT('PR-', SUBSTRING(UUID(), 1, 8)), '{hot}', '2026-09-21', "
              + $"'manual', 'draft', 1000.00, 100.00, NOW(6))",
                true),

            // 🔴 ④ 는 판정하지 않는다 — L5 가 처방 없이 닫혔으므로 막힘도 통과도 초록이다(CTO C-C3).
            new NeighborProbe("④ 같은 거래처 purchase_returns INSERT (기록만 · L5 미해결)",
                $"INSERT INTO purchase_returns (return_id, tenant_id, return_no, receipt_id, partner_id, return_date, "
              + $"return_type, status, total_amount, vat_amount) "
              + $"VALUES (UUID(), '{t}', CONCAT('RT-', SUBSTRING(UUID(), 1, 8)), NULL, '{hot}', '2026-09-21', "
              + $"'purchase_return', 'draft', 1000.00, 100.00)",
                false),

            new NeighborProbe("⑤ 다른 거래처 지급 등록 경로(L4 읽기 + 등록)",
                payableRead + ";\n"
              + $"INSERT INTO payments (payment_id, tenant_id, partner_id, payment_type, amount, payment_date, "
              + $"ref_order_id, is_active, source_type) "
              + $"VALUES (UUID(), '{t}', '{ColdPartner}', 'purchase', 2000.00, '2026-09-21', NULL, 1, 'manual')",
                true),
        };
    }

    [Fact(DisplayName = "G-RC16 이웃 프로브 — 봉합 전에 통과하던 등록이 봉합 후에 막히지 않는다 (W-3 · 경로 끝 INSERT · 보유자 없는 대조군 동반)")]
    public void G_RC16_NeighborProbes_W3()
    {
        if (!_fx.Available) { Assert.True(DbGateEnvironment.SkipOrFail(_fx.Unavailable!)); return; }

        var productSql = LegacyBalanceMatching.ReceivableMatchedDocLockedSql;
        var baselineSql = BaselineSql(productSql);
        var probes = BuildProbes();

        var report = new StringBuilder();
        var failures = new List<string>();
        var baselineBlockedTotal = 0;

        // ── 대조군 먼저 — 보유자 없이 5/5 통과해야 한다 ─────────────────────────
        //    🔴 여기서 막히면 프로브가 「잠금 말고 다른 이유」로 막히는 것이다.
        //       그 상태에서 잠금을 논하면 엉뚱한 곳을 본다(9/21 「대조군이 원인을 갈랐다」).
        foreach (var db in new[] { _fx.BaseDb, _fx.SealDb })
        {
            _fx.SetFill(db, 0);
            var (_, _, ctrl) = _fx.RunNeighborProbes(db, holderSql: null, RcRatioPairDbFixture.HotPartner, probes);
            var bad = ctrl.Where(r => r.Blocked || r.Error is not null).ToList();
            var tag = db == _fx.BaseDb ? "기준선" : "봉합";

            report.AppendLine($"[G-RC16/대조군·{tag}] 보유자 없음 · 통과 {ctrl.Count - bad.Count}/{ctrl.Count}");
            foreach (var r in bad)
                failures.Add($"🔴 대조군({tag}) — {r.Name} 이 보유자도 없는데 "
                           + (r.Blocked ? "막혔다" : $"오류가 났다: {r.Error}"));
        }

        // ── 본 판정 — 쌍의 양쪽을 같은 채움에서 잰다 ────────────────────────────
        foreach (var fill in new[] { 0, 2 })
        {
            _fx.SetFill(_fx.BaseDb, fill);
            _fx.SetFill(_fx.SealDb, fill);
            _fx.Analyze(_fx.BaseDb);
            _fx.Analyze(_fx.SealDb);

            var (bLocked, bAttempts, bRes) =
                _fx.RunNeighborProbes(_fx.BaseDb, baselineSql, RcRatioPairDbFixture.HotPartner, probes);
            var (sLocked, sAttempts, sRes) =
                _fx.RunNeighborProbes(_fx.SealDb, productSql, RcRatioPairDbFixture.HotPartner, probes);

            report.AppendLine(
                $"[G-RC16] 채움 {fill,3}% · 보유자 잠금 기준선 {bLocked}(조회 {bAttempts}) · 봉합 {sLocked}(조회 {sAttempts})");

            // 🔴 보유자가 실제로 쥐었는지부터 — 안 쥐었으면 「통과」가 아무 뜻이 없다.
            if (bLocked <= 0)
                failures.Add($"🔴 보유자 미성립 @ {fill}% — 기준선 보유자 잠금이 {bLocked} 다. 프로브가 아무것도 안 쟀다.");
            if (sLocked <= 0)
                failures.Add($"🔴 보유자 미성립 @ {fill}% — 봉합 보유자 잠금이 {sLocked} 다. 프로브가 아무것도 안 쟀다.");

            for (var i = 0; i < probes.Count; i++)
            {
                var p = probes[i];
                var b = bRes[i];
                var s = sRes[i];

                report.AppendLine(
                    $"    {p.Name,-46} 기준선 {(b.Blocked ? "막힘" : "통과")}({b.ElapsedMs}ms) → "
                  + $"봉합 {(s.Blocked ? "막힘" : "통과")}({s.ElapsedMs}ms)"
                  + (p.Judged ? "" : "  ⟵ 기록만"));

                // 막힘이 아닌 오류는 숨기지 않는다 — 판정 대상이든 아니든.
                if (b.Error is not null) failures.Add($"🔴 기준선 프로브 오류 @ {fill}% — {p.Name}: {b.Error}");
                if (s.Error is not null) failures.Add($"🔴 봉합 프로브 오류 @ {fill}% — {p.Name}: {s.Error}");

                if (!p.Judged) continue;
                if (b.Blocked) baselineBlockedTotal++;

                // ── 🔴 W-3 — 오더의 핵심. 봉합 전에 통과하던 것이 봉합 후에 막히면 그 자리에서 중단이다.
                if (!b.Blocked && s.Blocked)
                    failures.Add(
                        $"🔴 W-3 위반 @ {fill}% — {p.Name} 이 봉합 전에는 통과했는데 봉합 후에 막혔다.\n"
                      + "    사장님 오더(작2 §16-3 W-3)의 중단 조건이다 — 그 자리에서 멈춘다.");
            }
        }

        // ── 🔴 살아있음 — 기준선에서 하나도 안 막혔다면 이 프로브는 아무것도 안 재고 있다 ──
        if (failures.Count == 0 && baselineBlockedTotal == 0)
            failures.Add(
                "🔴 살아있음 위반 — 기준선(봉합 전)에서 막힌 판정 사례가 0건이다.\n"
              + "    봉합 전에 아무도 안 막히면 이 게이트는 무엇이 좋아졌는지도, 나빠졌는지도 못 잰다.\n"
              + "    프로브가 보유자와 같은 자리를 짚고 있는지 다시 보라(대조군이 통과했으므로 프로브 자체는 돈다).");

        Console.Error.Write(report.ToString());

        Assert.True(failures.Count == 0, "🔴 G-RC16 실패:\n  " + string.Join("\n  ", failures) + "\n\n" + report);
    }

    [Fact(DisplayName = "G-RC18-a 힌트 0개 감시자 — DB-125 미적용 사본에서도 L2·L4 가 오류 없이 돈다 (인덱스 없는 구버전 DB 에서 등록이 죽지 않는다)")]
    public void G_RC18a_NoHintSurvivesWithoutIndexes()
    {
        if (!_fx.Available) { Assert.True(DbGateEnvironment.SkipOrFail(_fx.Unavailable!)); return; }

        // 🔴 이 게이트의 뜻을 한정한다(작2 §14-1 G-RC18-a):
        //    「오류 0」은 **힌트가 안 들어왔다**는 뜻이지, **부분 적용이 안전하다**는 뜻이 아니다.
        //    부분 적용을 막는 것은 T3-6 차단 장치(갈래 B)이고 G-RC18-b·c·d 가 거기 붙는다.
        var statements = new (string Name, string Sql)[]
        {
            ("L2 ReceivableMatchedDocLockedSql", LegacyBalanceMatching.ReceivableMatchedDocLockedSql),
            ("L4 PayableMatchedDocLockedSql", LegacyBalanceMatching.PayableMatchedDocLockedSql),
        };

        // ① 힌트가 0개인가 — 인덱스 없는 DB 에서 1176 으로 등록을 죽이는 것이 힌트다(B-3 · N-4).
        foreach (var (name, sql) in statements)
        {
            foreach (var hint in new[] { "FORCE INDEX", "USE INDEX", "IGNORE INDEX", "STRAIGHT_JOIN" })
                Assert.True(
                    sql.IndexOf(hint, StringComparison.OrdinalIgnoreCase) < 0,
                    $"🔴 N-4 — {name} 에 힌트 `{hint}` 가 들어왔다.\n"
                  + "  인덱스가 없는 구버전 DB 에서 **1176 으로 등록이 죽는다**. 힌트는 0개다(작2 §0 · B-3).");
        }

        // ② DB-125 인덱스가 하나도 없는 사본에서 실제로 돈다 — 느린 것은 무방, 오류가 0 이어야 한다.
        foreach (var (name, sql) in statements)
        {
            var ex = Record.Exception(() => _fx.MeasureLock(_fx.BaseDb, sql, RcRatioPairDbFixture.HotPartner));
            Assert.True(ex is null,
                $"🔴 G-RC18-a — DB-125 미적용 사본에서 {name} 이 실패했다: {ex?.Message}\n"
              + "  구버전 DB 는 **느릴 뿐 등록은 살아야 한다**(작2 §0).");
        }

        Console.Error.WriteLine("[G-RC18-a] 힌트 0개 · DB-125 미적용 사본에서 L2·L4 오류 0 🟢");
    }
}
