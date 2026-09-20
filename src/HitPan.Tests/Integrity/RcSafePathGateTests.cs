using System.Security.Cryptography;
using System.Text;
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

        var ops = new (string Name, string Sql)[]
        {
            ("B1 collections UPDATE", $"UPDATE collections SET memo='rc12' WHERE tenant_id=@T AND partner_id='{noiseUpd}'"),
            ("B2 collections INSERT", $"INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, ref_doc_type, ref_doc_id, is_active) VALUES (UUID(), @T, '{noiseIns}', '2026-03-11', 1000, 'legacy_balance', '{noiseIns}', 1)"),
            ("B3 payments UPDATE", $"UPDATE payments SET memo='rc12' WHERE tenant_id=@T AND partner_id='{noiseUpd}'"),
            ("B4 payments INSERT", $"INSERT INTO payments (payment_id, tenant_id, partner_id, payment_type, amount, payment_date, ref_order_id, is_active) VALUES (UUID(), @T, '{noiseIns}', 'legacy_balance', 1000, '2026-03-11', '{noiseIns}', 1)"),
            ("B5 purchase_returns UPDATE", $"UPDATE purchase_returns SET memo='rc12' WHERE tenant_id=@T AND partner_id='{noiseUpd}'"),
            ("B6 purchase_returns INSERT", $"INSERT INTO purchase_returns (return_id, tenant_id, receipt_id, return_no, partner_id, return_date, status, total_amount, vat_amount, is_deleted) VALUES (UUID(), @T, 'np-{t[..8]}-9', CONCAT('RT-', SUBSTRING(UUID(),1,8)), '{noiseIns}', '2026-03-11', 'draft', 1000, 0, 0)"),
        };

        var blocked = new List<string>();
        foreach (var (name, sql) in ops)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await b.ExecuteAsync(sql, new { T = t });
                Console.Error.WriteLine($"[G-RC12] {name} 🟢 {sw.ElapsedMilliseconds}ms");
            }
            catch (MySqlException ex)
            {
                Console.Error.WriteLine($"[G-RC12] {name} 🔴 {sw.ElapsedMilliseconds}ms errno={ex.Number} {ex.Message}");
                blocked.Add($"{name} (errno={ex.Number})");
            }
        }
        await atx.RollbackAsync();

        Assert.True(blocked.Count == 0,
            "🔴 RR 매칭 잠금이 **무관 거래처**의 등록까지 막았다(병렬이슈53 재발) — " + string.Join(" · ", blocked) + "\n"
          + "  DB-124 잠금범위 인덱스가 빠졌거나 옵티마이저가 안 고른 것이다. 다른 거래처가 못 쓰는 ERP 는 흐름이 끊긴 것이다(#20).");
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
    /// 🔴 <b>테넌트를 넘는 인덱스</b> — 이걸 고르면 잠금·스캔 범위가 <b>남의 회사 행</b>까지 간다(PM 판정 ① · 작지 §18).
    /// <para>
    /// ⚠️ 여기 없는 것도 정직하게 적는다 — <c>fk_pr_partner</c>(<c>purchase_receipts(partner_id)</c>)·
    /// <c>idx_return</c>(<c>purchase_return_items(return_id)</c>)은 접두가 <c>tenant_id</c> 가 아니지만 <b>막지 않는다</b>:
    /// 둘 다 값이 전역 유일한 id 라 그 범위 안에 남의 회사 행이 들어올 수 없고, S3 0단계 실측에서 옵티마이저가 고른 계획이다.
    /// 「접두가 tenant_id 여야 한다」로 막으면 제품을 고쳐야 하는데, 그건 이 갈래의 범위가 아니다(#33).
    /// </para>
    /// </summary>
    private static readonly string[] CrossTenantIndexes = { "idx_pay_partner", "idx_pay_date", "idx_pay_type", "idx_partner" };

    /// <summary>문장별 기대 인덱스 — S3 0단계 실측(명세서 §2-2)과 같은 계획이어야 한다.</summary>
    private static readonly (string Name, bool Receivable, bool DocParam, string Sql, string RequiredKey)[] LockedStatements =
    {
        ("L1 ReceivableMatchedLegacyLockedSql", true,  false, LegacyBalanceMatching.ReceivableMatchedLegacyLockedSql, "idx_coll_tenant_doc"),
        ("L2 ReceivableMatchedDocLockedSql",    true,  false, LegacyBalanceMatching.ReceivableMatchedDocLockedSql,    "idx_coll_tenant_doc"),
        ("L3 PayableMatchedLegacyLockedSql",    false, false, LegacyBalanceMatching.PayableMatchedLegacyLockedSql,    "idx_pay_tenant_type_ref"),
        ("L4 PayableMatchedDocLockedSql",       false, false, LegacyBalanceMatching.PayableMatchedDocLockedSql,       "idx_pay_tenant_type_ref"),
        ("L5 PayableMatchedReturnLockedSql",    false, false, LegacyBalanceMatching.PayableMatchedReturnLockedSql,    "idx_rt_tenant_receipt"),
    };

    /// <summary>훑는 행 상한 — 「그 거래처/그 전표 몫」. 무관 행을 아무리 심어도 이 안이어야 한다.</summary>
    private const long RowsCap = 8;

    [Fact(DisplayName = "G-RC13 EXPLAIN — 제품 문장 8개가 DB-124 인덱스를 타고(테넌트 넘는 인덱스 금지) · 무관 행 10배(30→300)에도 훑는 행 불변")]
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
        var before = await ExplainAllAsync(db, t);
        AssertPlans(before, "무관 행 30건");

        // 🔴 대조군 — 무관 행을 10배로. `rows` 는 추정치라 「상한 이하」 하나만으로는 흔들린다(설계 §12-6).
        await SeedNoiseRowsAsync(db, t, 31, 300);
        await SeedNoiseRowsAsync(db, other, 31, 300);
        var after = await ExplainAllAsync(db, t);
        AssertPlans(after, "무관 행 300건");

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
        foreach (var tbl in new[] { "collections", "payments", "purchase_returns", "purchase_return_items", "sales_deliveries", "purchase_receipts" })
            await db.ExecuteAsync($"ANALYZE TABLE {tbl}");   // 추정치는 통계에 달려 있다

        var result = new Dictionary<string, List<ExplainRow>>(StringComparer.Ordinal);
        foreach (var (name, receivable, _, sql, _) in LockedStatements)
            result[name] = (await db.QueryAsync<ExplainRow>("EXPLAIN " + sql,
                new { TenantId = t, PartnerId = receivable ? PA : PB })).AsList();

        result["D1-a DeliveryUsedSql"] = (await db.QueryAsync<ExplainRow>("EXPLAIN " + CollectionService.DeliveryUsedSql(RR),
            new { TenantId = t, RefId = DocD })).AsList();
        result["D1-b ReceiptPaidSql"] = (await db.QueryAsync<ExplainRow>("EXPLAIN " + CollectionService.ReceiptPaidSql(RR),
            new { TenantId = t, RefId = DocR })).AsList();
        result["D1-c ReceiptReturnedSql"] = (await db.QueryAsync<ExplainRow>("EXPLAIN " + CollectionService.ReceiptReturnedSql(RR),
            new { TenantId = t, RefId = DocR })).AsList();
        return result;
    }

    private static void AssertPlans(Dictionary<string, List<ExplainRow>> plans, string when)
    {
        var required = LockedStatements.ToDictionary(s => s.Name, s => s.RequiredKey, StringComparer.Ordinal);
        required["D1-a DeliveryUsedSql"] = "idx_coll_tenant_doc";
        required["D1-b ReceiptPaidSql"] = "idx_pay_tenant_type_ref";
        required["D1-c ReceiptReturnedSql"] = "idx_rt_tenant_receipt";

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
                Assert.True(Array.IndexOf(CrossTenantIndexes, r.Key) < 0,
                    $"🔴 {name} · 표 {r.Table}({when}): **테넌트를 넘는 인덱스**({r.Key})를 골랐다.\n"
                  + "  범위 안에 남의 회사 행이 들어온다 — 잠금 범위이자 테넌트 격리 문제다(작지 §18 PM 판정 ①).");
                Assert.True(r.Rows is not null && r.Rows <= RowsCap,
                    $"🔴 {name} · 표 {r.Table}({when}): 훑는 행 {r.Rows} 가 상한 {RowsCap} 을 넘었다 — 「그 전표 몫」이 아니다.");
            }
            Assert.True(rows.Any(r => string.Equals(r.Key, required[name], StringComparison.Ordinal)),
                $"🔴 {name}({when}): DB-124 인덱스 {required[name]} 를 아무 표도 안 탔다 — 계획: "
              + string.Join(" · ", rows.Select(r => $"{r.Table}:{r.Key ?? "NULL"}")) + "\n"
              + "  옵티마이저가 인덱스를 안 고르면 봉합은 「넣었다」로 끝난 것이다(작지 §17 C-3 — 멈추고 PM 보고).");
        }
    }

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
}
