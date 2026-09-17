using Dapper;
using HitPan.Application.DTOs.Approval;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Moq;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 격리 DB 하나(<c>hitpan_r1_*</c>)에 출하 DDL 을 한 번 적재한다. 사례마다 테넌트를 새로 만들어 서로 안 섞인다(모든 식이 tenant 범위).
/// </summary>
public sealed class LegacyBalanceMatchDbFixture : IDisposable
{
    public string DbName { get; } = "hitpan_r1_g8_" + Guid.NewGuid().ToString("N")[..8];
    public bool Available { get; }
    private readonly bool _created;

    public LegacyBalanceMatchDbFixture()
    {
        if (!ServerAvailable()) return;

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
            StandardInputEncoding = new System.Text.UTF8Encoding(false)
        };
        psi.ArgumentList.Add($"--host={Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost"}");
        psi.ArgumentList.Add($"--port={Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306"}");
        psi.ArgumentList.Add($"-u{Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root"}");
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS");
        if (!string.IsNullOrEmpty(pass)) psi.ArgumentList.Add($"-p{pass}");
        psi.ArgumentList.Add("--default-character-set=utf8mb4");
        psi.ArgumentList.Add(DbName);

        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.StandardInput.Write(File.ReadAllText(Path.Combine(RepoRoot(), "installer", "hitpan_db_clean.sql")));
        proc.StandardInput.Close();
        var err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0) throw new InvalidOperationException($"출하 DDL import 실패:\n{err}");
        Available = true;
    }

    public string DbConnString() => ServerConnString().Replace("User=", $"Database={DbName};User=");

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

    private static string ServerConnString()
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "root";
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
        return $"Server={host};Port={port};User={user};Password={pass};DefaultCommandTimeout=90;GuidFormat=None;AllowUserVariables=true;";
    }

    private static string MysqlExe() =>
        Environment.GetEnvironmentVariable("HITPAN_MYSQL") ?? @"C:\Program Files\MariaDB 11.4\bin\mysql.exe";

    private static bool ServerAvailable()
    {
        if (DbGateEnvironment.IsCi) return true;
        if (!File.Exists(MysqlExe())) return false;
        try
        {
            using var c = new MySqlConnection(ServerConnString());
            c.Open();
            return true;
        }
        catch (MySqlException ex)
        {
            Console.Error.WriteLine($"[G8] MariaDB 연결 실패: {ex.Message}");
            return false;
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
            Console.Error.WriteLine($"[G8] 임시 DB 삭제 실패 {DbName}: {ex.Message}");
        }
    }
}

/// <summary>
/// 🔴 <b>G8 LegacyBalanceMatchGate</b> — 20260915작1 개정 3판 갈래 R1 (설계 §20·§21·§22·§25 · 작지 §15-3).
/// </summary>
/// <remarks>
/// 실제 <see cref="CollectionService"/>·<see cref="LegacyBalanceMatching"/> 를 격리 DB 에서 부른다. 운영 무접촉(#39).
/// 고정 자료: 거래처 PA 이월 미수 +100,000 · PB 이월 미지급 −80,000 · 기준일 2026-02-28.
/// 사례 a~k = 설계 §25 G8 · l~p = P2·P3·P4·S2·E 호환 보강. CI 는 <c>HITPAN_REQUIRE_DB</c> 로 SKIP=FAIL.
/// </remarks>
[Collection("DeviceAndKeyGate")]
public sealed class LegacyBalanceMatchGateTests : IClassFixture<LegacyBalanceMatchDbFixture>
{
    private readonly LegacyBalanceMatchDbFixture _fx;
    // 거래처 id 는 사례(인스턴스)마다 새로 — partners PK 가 partner_id 하나라 테넌트 간 공유 불가.
    private readonly string PA = Guid.NewGuid().ToString();
    private readonly string PB = Guid.NewGuid().ToString();
    private readonly string OtherPartner = Guid.NewGuid().ToString();
    private static readonly DateTime BaseDate = new(2026, 2, 28);
    private static readonly DateTime After = new(2026, 3, 10);

    public LegacyBalanceMatchGateTests(LegacyBalanceMatchDbFixture fx) => _fx = fx;

    // ── 준비 ──
    private bool Skip(string name)
    {
        if (_fx.Available) return false;
        return DbGateEnvironment.SkipOrFail("G8 LegacyBalanceMatchGate " + name);
    }

    private async Task<(MySqlConnection Db, string Tenant, ICollectionService Svc)> NewTenantAsync(bool withLegacy = true)
    {
        var db = new MySqlConnection(_fx.DbConnString());
        await db.OpenAsync();
        var t = Guid.NewGuid().ToString();
        await db.ExecuteAsync("""
            INSERT INTO accounts (account_code, tenant_id, account_name, account_type, sort_order) VALUES
              ('10100', @T, '현금', 'asset', 1), ('10300', @T, '보통예금', 'asset', 2),
              ('10800', @T, '외상매출금', 'asset', 3), ('23200', @T, '외상매입금', 'liability', 4)
            """, new { T = t });
        var now = DateTime.Now;
        foreach (var (id, code) in new[] { (PA, "A"), (PB, "B") })
        {
            await db.ExecuteAsync("""
                INSERT INTO partners (partner_id, tenant_id, partner_code, partner_name, partner_type, is_active, is_deleted, created_at, updated_at)
                VALUES (@Id, @T, @Code, CONCAT('G8거래처', @Code), 'both', 1, 0, @Now, @Now)
                """, new { Id = id, T = t, Code = code, Now = now });
        }
        if (withLegacy)
        {
            await InsertLegacyAsync(db, t, PA, 100_000m);
            await InsertLegacyAsync(db, t, PB, -80_000m);
        }
        return (db, t, new CollectionService(db, Mock.Of<IAuditService>()));
    }

    private static Task InsertLegacyAsync(MySqlConnection db, string t, string partner, decimal amount)
        => db.ExecuteAsync("""
            INSERT INTO partner_legacy_balances (balance_id, tenant_id, partner_id, base_date, balance_amount, source_type, source_id)
            VALUES (UUID(), @T, @P, @D, @A, 'migration', CONCAT('mig-legacybal-', @P))
            """, new { T = t, P = partner, D = BaseDate, A = amount });

    private static async Task<LegacyBalanceMatching.Remaining?> RemainingAsync(MySqlConnection db, string t, string partner, bool receivable)
        => (await LegacyBalanceMatching.ListAsync(db, null, t, receivable, CancellationToken.None)).SingleOrDefault(r => r.PartnerId == partner);

    private static CreateCollectionRequest LegacyCollection(string partner, decimal amount, DateTime? date = null, string? refId = null)
        => new() { PartnerId = partner, CollectionDate = date ?? After, Amount = amount, CollectionMethod = "cash", RefDocType = LegacyBalanceMatching.RefType, RefDocId = refId ?? partner };

    private static CreatePaymentRequest LegacyPayment(string partner, decimal amount, DateTime? date = null)
        => new() { PartnerId = partner, PaymentDate = date ?? After, Amount = amount, PaymentMethod = "bank_transfer", PaymentType = LegacyBalanceMatching.RefType, RefOrderId = partner };

    private static Task<string> SnapshotAsync(MySqlConnection db, string t)
        => db.ExecuteScalarAsync<string>("""
            SELECT CONCAT_WS('|',
              (SELECT COUNT(*) FROM collections WHERE tenant_id = @T),
              (SELECT COUNT(*) FROM payments WHERE tenant_id = @T),
              (SELECT COUNT(*) FROM journal_entries WHERE tenant_id = @T),
              (SELECT COUNT(*) FROM journal_lines WHERE tenant_id = @T),
              (SELECT COALESCE(SUM(total_receipt), 0) FROM partner_balance WHERE tenant_id = @T),
              (SELECT COALESCE(SUM(total_payment), 0) FROM partner_balance WHERE tenant_id = @T))
            """, new { T = t })!;

    private static async Task<InvalidOperationException> RejectedUnchangedAsync(MySqlConnection db, string t, Func<Task> act)
    {
        var before = await SnapshotAsync(db, t);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal(before, await SnapshotAsync(db, t));
        return ex;
    }

    private static Task<int> EntryCountAsync(MySqlConnection db, string t, string sourceType, string sourceId)
        => db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM journal_entries WHERE tenant_id=@T AND source_type=@S AND source_id=@Id",
            new { T = t, S = sourceType, Id = sourceId });

    // ── a ──
    [Fact(DisplayName = "G8-a 이월 수금 → R −금액 · total_receipt +금액 · 분개 1")]
    public async Task G8a_이월수금()
    {
        if (Skip(nameof(G8a_이월수금))) return;
        var (db, t, svc) = await NewTenantAsync();
        await using var _ = db;

        var id = await svc.CreateCollectionAsync(LegacyCollection(PA, 30_000m), t, "g8");

        var r = await RemainingAsync(db, t, PA, receivable: true);
        Assert.NotNull(r);
        Assert.Equal(100_000m, r!.LegacyAmount);
        Assert.Equal(30_000m, r.MatchedAmount);
        Assert.Equal(70_000m, r.RemainingAmount);
        Assert.Equal(30_000m, await db.ExecuteScalarAsync<decimal>("SELECT total_receipt FROM partner_balance WHERE tenant_id=@T AND partner_id=@P", new { T = t, P = PA }));
        Assert.Equal(1, await EntryCountAsync(db, t, "collection", id));
    }

    // ── b ──
    [Fact(DisplayName = "G8-b R 초과 거절 + DB 무변화 (검사 빼면 FAIL)")]
    public async Task G8b_초과거절()
    {
        if (Skip(nameof(G8b_초과거절))) return;
        var (db, t, svc) = await NewTenantAsync();
        await using var _ = db;
        await svc.CreateCollectionAsync(LegacyCollection(PA, 30_000m), t, "g8");

        var ex = await RejectedUnchangedAsync(db, t, () => svc.CreateCollectionAsync(LegacyCollection(PA, 70_001m), t, "g8"));
        Assert.Contains("이전 프로그램 이월잔액이 70,000원 남았습니다", ex.Message);

        // 경계: 딱 R 은 된다
        await svc.CreateCollectionAsync(LegacyCollection(PA, 70_000m), t, "g8");
        Assert.Equal(0m, (await RemainingAsync(db, t, PA, true))!.RemainingAmount);
    }

    // ── c ──
    [Fact(DisplayName = "G8-c 미지급 거래처에 수금 매칭 · 미수 거래처에 지급 매칭 거절")]
    public async Task G8c_방향거절()
    {
        if (Skip(nameof(G8c_방향거절))) return;
        var (db, t, svc) = await NewTenantAsync();
        await using var _ = db;

        var ex1 = await RejectedUnchangedAsync(db, t, () => svc.CreateCollectionAsync(LegacyCollection(PB, 1_000m), t, "g8"));
        Assert.Contains("이월 미수금)이 없습니다", ex1.Message);
        var ex2 = await RejectedUnchangedAsync(db, t, () => svc.CreatePaymentAsync(LegacyPayment(PA, 1_000m), t, "g8"));
        Assert.Contains("이월 미지급금)이 없습니다", ex2.Message);
    }

    // ── d ──
    [Fact(DisplayName = "G8-d ref ≠ partner 거절")]
    public async Task G8d_ref불일치거절()
    {
        if (Skip(nameof(G8d_ref불일치거절))) return;
        var (db, t, svc) = await NewTenantAsync();
        await using var _ = db;
        await InsertLegacyAsync(db, t, OtherPartner, 50_000m);

        var ex = await RejectedUnchangedAsync(db, t, () => svc.CreateCollectionAsync(
            LegacyCollection(PA, 1_000m, refId: OtherPartner), t, "g8"));
        Assert.Contains("같은 거래처에만", ex.Message);
    }

    // ── e · f ──
    [Fact(DisplayName = "G8-e migration 행 M 제외 · 목록에는 남음")]
    public async Task G8e_이관행_M제외()
    {
        if (Skip(nameof(G8e_이관행_M제외))) return;
        var (db, t, svc) = await NewTenantAsync();
        await using var _ = db;
        await db.ExecuteAsync("""
            INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, ref_doc_type, ref_doc_id, is_active, source_type, source_id)
            VALUES (UUID(), @T, @P, '2026-01-20', 40000, 'legacy_balance', @P, 1, 'migration', 'mig-g8-e')
            """, new { T = t, P = PA });
        await db.ExecuteAsync("""
            INSERT INTO payments (payment_id, tenant_id, partner_id, payment_type, amount, payment_date, ref_order_id, is_active, source_type, source_id)
            VALUES (UUID(), @T, @P, 'legacy_balance', 20000, '2026-01-20', @P, 1, 'migration', 'mig-g8-e')
            """, new { T = t, P = PB });

        Assert.Equal(100_000m, (await RemainingAsync(db, t, PA, true))!.RemainingAmount);
        Assert.Equal(80_000m, (await RemainingAsync(db, t, PB, false))!.RemainingAmount);
        // 🔴 사장님 원칙 — 이관 줄은 목록에서 빠지지 않는다
        var cols = await svc.GetCollectionsAsync(t);
        Assert.Single(cols, c => c.SourceType == "migration" && c.Amount == 40_000m);
        var pays = await svc.GetPaymentsAsync(t);
        Assert.Single(pays, p => p.SourceType == "migration" && p.Amount == 20_000m);
    }

    [Fact(DisplayName = "G8-f is_active=0 M 제외")]
    public async Task G8f_비활성_M제외()
    {
        if (Skip(nameof(G8f_비활성_M제외))) return;
        var (db, t, _svc) = await NewTenantAsync();
        await using var _ = db;
        await db.ExecuteAsync("""
            INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, ref_doc_type, ref_doc_id, is_active)
            VALUES (UUID(), @T, @P, '2026-03-05', 25000, 'legacy_balance', @P, 0)
            """, new { T = t, P = PA });
        await db.ExecuteAsync("""
            INSERT INTO payments (payment_id, tenant_id, partner_id, payment_type, amount, payment_date, ref_order_id, is_active)
            VALUES (UUID(), @T, @P, 'legacy_balance', 15000, '2026-03-05', @P, 0)
            """, new { T = t, P = PB });

        Assert.Equal(100_000m, (await RemainingAsync(db, t, PA, true))!.RemainingAmount);
        Assert.Equal(80_000m, (await RemainingAsync(db, t, PB, false))!.RemainingAmount);
    }

    // ── g ──
    [Fact(DisplayName = "G8-g 이월잔액 행 삭제·재삽입(새 balance_id) 뒤 M 유지")]
    public async Task G8g_재삽입_M유지()
    {
        if (Skip(nameof(G8g_재삽입_M유지))) return;
        var (db, t, svc) = await NewTenantAsync();
        await using var _ = db;
        await svc.CreateCollectionAsync(LegacyCollection(PA, 30_000m), t, "g8");
        var oldId = await db.ExecuteScalarAsync<string>("SELECT balance_id FROM partner_legacy_balances WHERE tenant_id=@T AND partner_id=@P", new { T = t, P = PA });

        await db.ExecuteAsync("DELETE FROM partner_legacy_balances WHERE tenant_id=@T AND partner_id=@P", new { T = t, P = PA });
        await InsertLegacyAsync(db, t, PA, 100_000m);
        var newId = await db.ExecuteScalarAsync<string>("SELECT balance_id FROM partner_legacy_balances WHERE tenant_id=@T AND partner_id=@P", new { T = t, P = PA });

        Assert.NotEqual(oldId, newId);
        var r = await RemainingAsync(db, t, PA, true);
        Assert.Equal(30_000m, r!.MatchedAmount);
        Assert.Equal(70_000m, r.RemainingAmount);
    }

    // ── h ──
    [Fact(DisplayName = "G8-h 이월 지급 R 감소 · 초과 거절 · total_payment · 분개")]
    public async Task G8h_이월지급()
    {
        if (Skip(nameof(G8h_이월지급))) return;
        var (db, t, svc) = await NewTenantAsync();
        await using var _ = db;

        var id = await svc.CreatePaymentAsync(LegacyPayment(PB, 50_000m), t, "g8");
        var r = await RemainingAsync(db, t, PB, false);
        Assert.Equal(30_000m, r!.RemainingAmount);
        Assert.Equal(1, await EntryCountAsync(db, t, "payment", id));
        Assert.Equal(50_000m, await db.ExecuteScalarAsync<decimal>("SELECT total_payment FROM partner_balance WHERE tenant_id=@T AND partner_id=@P", new { T = t, P = PB }));

        var ex = await RejectedUnchangedAsync(db, t, () => svc.CreatePaymentAsync(LegacyPayment(PB, 30_001m), t, "g8"));
        Assert.Contains("30,000원 남았습니다", ex.Message);
    }

    // ── i ──
    [Fact(DisplayName = "G8-i 수금·지급 삭제 → 역분개 1 · 계정 합 0 · R 복원 (역분개 빼면 FAIL)")]
    public async Task G8i_삭제역분개()
    {
        if (Skip(nameof(G8i_삭제역분개))) return;
        var (db, t, svc) = await NewTenantAsync();
        await using var _ = db;
        var cid = await svc.CreateCollectionAsync(LegacyCollection(PA, 30_000m), t, "g8");
        var pid = await svc.CreatePaymentAsync(LegacyPayment(PB, 50_000m), t, "g8");

        await svc.DeleteCollectionAsync(cid, t);
        await svc.DeletePaymentAsync(pid, t);
        await svc.DeleteCollectionAsync(cid, t);   // 두 번째 삭제는 아무 일도 안 한다

        Assert.Equal(1, await EntryCountAsync(db, t, "collection_cancel", cid));
        Assert.Equal(1, await EntryCountAsync(db, t, "payment_cancel", pid));
        Assert.Equal(new DateTime(2026, 3, 10), await db.ExecuteScalarAsync<DateTime>(
            "SELECT entry_date FROM journal_entries WHERE tenant_id=@T AND source_type='collection_cancel' AND source_id=@Id", new { T = t, Id = cid }));

        var nonZero = await db.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM (
              SELECT jl.account_code, SUM(jl.debit_amount - jl.credit_amount) AS net
                FROM journal_lines jl JOIN journal_entries je ON je.entry_id = jl.entry_id
               WHERE je.tenant_id = @T AND je.source_id IN (@C, @P)
               GROUP BY jl.account_code) x
             WHERE x.net <> 0
            """, new { T = t, C = cid, P = pid });
        Assert.Equal(0, nonZero);
        Assert.Equal(100_000m, (await RemainingAsync(db, t, PA, true))!.RemainingAmount);
        Assert.Equal(80_000m, (await RemainingAsync(db, t, PB, false))!.RemainingAmount);
        Assert.Equal(0m, await db.ExecuteScalarAsync<decimal>("SELECT total_receipt FROM partner_balance WHERE tenant_id=@T AND partner_id=@P", new { T = t, P = PA }));
    }

    // ── j ──
    [Fact(DisplayName = "G8-j 요약 #3·#4 = R · LegacyBalances 한 줄")]
    public async Task G8j_요약()
    {
        if (Skip(nameof(G8j_요약))) return;
        var (db, t, svc) = await NewTenantAsync();
        await using var _ = db;
        await svc.CreateCollectionAsync(LegacyCollection(PA, 30_000m), t, "g8");
        await svc.CreatePaymentAsync(LegacyPayment(PB, 50_000m), t, "g8");

        var recv = await svc.GetReceivablesAsync(t);
        Assert.Equal(70_000m, recv.Summary.Single(s => s.PartnerId == PA).Outstanding);
        var lr = Assert.Single(recv.LegacyBalances);
        Assert.Equal((PA, 100_000m, 30_000m, 70_000m, BaseDate), (lr.PartnerId, lr.LegacyAmount, lr.MatchedAmount, lr.RemainingAmount, lr.BaseDate));
        Assert.Equal("G8거래처A", lr.PartnerName);

        var pay = await svc.GetPayablesAsync(t);
        Assert.Equal(30_000m, pay.Summary.Single(s => s.PartnerId == PB).Outstanding);
        var lp = Assert.Single(pay.LegacyBalances);
        Assert.Equal((PB, 80_000m, 50_000m, 30_000m), (lp.PartnerId, lp.LegacyAmount, lp.MatchedAmount, lp.RemainingAmount));

        // R = 0 이 되면 한 줄이 사라진다
        await svc.CreateCollectionAsync(LegacyCollection(PA, 70_000m), t, "g8");
        Assert.Empty((await svc.GetReceivablesAsync(t)).LegacyBalances);
    }

    // ── k ──
    [Fact(DisplayName = "G8-k 대조군 — 이월잔액 없는 회사: 종전 값 · 명세서 쪽 조건 = 종전 필터")]
    public async Task G8k_대조군()
    {
        if (Skip(nameof(G8k_대조군))) return;
        var (db, t, svc) = await NewTenantAsync(withLegacy: false);
        await using var _ = db;
        await InsertDeliveryAsync(db, t, "g8k-d-hum", PA, 110_000m, "direct");
        await InsertDeliveryAsync(db, t, "g8k-d-mig", PA, 50_000m, "migration");
        await db.ExecuteAsync("""
            INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, ref_doc_type, ref_doc_id, is_active, source_type, source_id)
            VALUES (UUID(), @T, @P, '2026-01-20', 20000, 'sales_delivery', 'g8k-d-mig', 1, 'migration', 'mig-g8k')
            """, new { T = t, P = PA });
        await svc.CreateCollectionAsync(new CreateCollectionRequest { PartnerId = PA, CollectionDate = After, Amount = 10_000m, RefDocType = "sales_delivery", RefDocId = "g8k-d-hum" }, t, "g8");
        // 이월잔액 없는 회사는 이관 명세서에도 새 수금이 된다(P3 는 이월잔액 회사만)
        await svc.CreateCollectionAsync(new CreateCollectionRequest { PartnerId = PA, CollectionDate = After, Amount = 5_000m, RefDocType = "sales_delivery", RefDocId = "g8k-d-mig" }, t, "g8");

        Assert.Empty(await LegacyBalanceMatching.ListAsync(db, null, t, true, CancellationToken.None));
        var recv = await svc.GetReceivablesAsync(t);
        Assert.Empty(recv.LegacyBalances);
        Assert.Equal(100_000m + 25_000m, recv.Summary.Single(s => s.PartnerId == PA).Outstanding);

        var old = await db.ExecuteScalarAsync<decimal>("SELECT COALESCE(SUM(amount),0) FROM collections c WHERE c.tenant_id=@T AND c.is_active=1 AND c.ref_doc_type='sales_delivery'", new { T = t });
        var docSide = await db.ExecuteScalarAsync<decimal>($"SELECT COALESCE(SUM(amount),0) FROM collections c WHERE c.tenant_id=@TenantId AND {LegacyBalanceMatching.CollectionDocSideWhere("c")}", new { TenantId = t });
        Assert.Equal(35_000m, old);
        Assert.Equal(old, docSide);
    }

    // ── l~p 보강 ──
    [Fact(DisplayName = "G8-l S2 기준일 당일·이전 날짜 이월 매칭 거절 · 다음 날 허용")]
    public async Task G8l_기준일()
    {
        if (Skip(nameof(G8l_기준일))) return;
        var (db, t, svc) = await NewTenantAsync();
        await using var _ = db;

        Assert.True(LegacyBalanceMatching.RejectBeforeBaseDate);
        var ex = await RejectedUnchangedAsync(db, t, () => svc.CreateCollectionAsync(LegacyCollection(PA, 1_000m, BaseDate), t, "g8"));
        Assert.Contains("기준일(2026-02-28)까지의 수금은 이미 이월잔액에 들어 있어", ex.Message);
        var exP = await RejectedUnchangedAsync(db, t, () => svc.CreatePaymentAsync(LegacyPayment(PB, 1_000m, new DateTime(2026, 2, 20)), t, "g8"));
        Assert.Contains("까지의 지급은", exP.Message);
        await svc.CreateCollectionAsync(LegacyCollection(PA, 1_000m, BaseDate.AddDays(1)), t, "g8");
    }

    [Fact(DisplayName = "G8-m P2 명세서·매입 남은 금액 초과 거절 · 없는 명세서 거절 · 경계 허용")]
    public async Task G8m_명세서초과()
    {
        if (Skip(nameof(G8m_명세서초과))) return;
        var (db, t, svc) = await NewTenantAsync(withLegacy: false);
        await using var _ = db;
        await InsertDeliveryAsync(db, t, "g8m-d", PA, 110_000m, "direct");
        await InsertReceiptAsync(db, t, "g8m-r", PB, 55_000m, "direct");

        await svc.CreateCollectionAsync(new CreateCollectionRequest { PartnerId = PA, CollectionDate = After, Amount = 10_000m, RefDocType = "sales_delivery", RefDocId = "g8m-d" }, t, "g8");
        var ex = await RejectedUnchangedAsync(db, t, () => svc.CreateCollectionAsync(new CreateCollectionRequest { PartnerId = PA, CollectionDate = After, Amount = 100_001m, RefDocType = "sales_delivery", RefDocId = "g8m-d" }, t, "g8"));
        Assert.Contains("100,000원", ex.Message);
        var ex2 = await RejectedUnchangedAsync(db, t, () => svc.CreateCollectionAsync(new CreateCollectionRequest { PartnerId = PA, CollectionDate = After, Amount = 1m, RefDocType = "sales_delivery", RefDocId = "nope" }, t, "g8"));
        Assert.Contains("맞출 거래명세서가 없습니다", ex2.Message);
        await svc.CreateCollectionAsync(new CreateCollectionRequest { PartnerId = PA, CollectionDate = After, Amount = 100_000m, RefDocType = "sales_delivery", RefDocId = "g8m-d" }, t, "g8");

        var ex3 = await RejectedUnchangedAsync(db, t, () => svc.CreatePaymentAsync(new CreatePaymentRequest { PartnerId = PB, PaymentDate = After, Amount = 55_001m, PaymentType = "purchase", RefOrderId = "g8m-r" }, t, "g8"));
        Assert.Contains("55,000원", ex3.Message);
        await svc.CreatePaymentAsync(new CreatePaymentRequest { PartnerId = PB, PaymentDate = After, Amount = 55_000m, PaymentType = "purchase", RefOrderId = "g8m-r" }, t, "g8");
    }

    [Fact(DisplayName = "G8-n P3 이월잔액 회사의 이관 명세서·매입에 새 수금·지급 거절")]
    public async Task G8n_이관명세서거절()
    {
        if (Skip(nameof(G8n_이관명세서거절))) return;
        var (db, t, svc) = await NewTenantAsync();
        await using var _ = db;
        await InsertDeliveryAsync(db, t, "g8n-d", PA, 110_000m, "migration");
        await InsertReceiptAsync(db, t, "g8n-r", PB, 55_000m, "migration");

        var ex = await RejectedUnchangedAsync(db, t, () => svc.CreateCollectionAsync(new CreateCollectionRequest { PartnerId = PA, CollectionDate = After, Amount = 1_000m, RefDocType = "sales_delivery", RefDocId = "g8n-d" }, t, "g8"));
        Assert.Contains("이전 프로그램에서 옮겨온 거래명세서", ex.Message);
        var ex2 = await RejectedUnchangedAsync(db, t, () => svc.CreatePaymentAsync(new CreatePaymentRequest { PartnerId = PB, PaymentDate = After, Amount = 1_000m, PaymentType = "purchase", RefOrderId = "g8n-r" }, t, "g8"));
        Assert.Contains("이전 프로그램에서 옮겨온 매입전표", ex2.Message);
    }

    [Fact(DisplayName = "G8-o P4 마감된 달의 수금·지급 삭제 거절")]
    public async Task G8o_마감삭제거절()
    {
        if (Skip(nameof(G8o_마감삭제거절))) return;
        var (db, t, svc) = await NewTenantAsync();
        await using var _ = db;
        var cid = await svc.CreateCollectionAsync(LegacyCollection(PA, 30_000m), t, "g8");
        var pid = await svc.CreatePaymentAsync(LegacyPayment(PB, 30_000m), t, "g8");
        await db.ExecuteAsync("""
            INSERT INTO monthly_closing (closing_id, tenant_id, `year_month`, status, sales_amount, purchase_amount, receipt_amount, payment_amount, created_at, updated_at)
            VALUES (UUID(), @T, '202603', 'closed', 0, 0, 0, 0, NOW(6), NOW(6))
            """, new { T = t });

        await RejectedUnchangedAsync(db, t, () => svc.DeleteCollectionAsync(cid, t));
        await RejectedUnchangedAsync(db, t, () => svc.DeletePaymentAsync(pid, t));
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT is_active FROM collections WHERE collection_id=@Id", new { Id = cid }));
    }

    [Fact(DisplayName = "G8-p E 호환 — 이관 명세서·매입에 붙은 사람 수금·지급·반품은 M · 명세서 쪽에서 빠짐(합계 보존)")]
    public async Task G8p_E호환()
    {
        if (Skip(nameof(G8p_E호환))) return;
        var (db, t, _svc) = await NewTenantAsync();
        await using var _ = db;
        await InsertDeliveryAsync(db, t, "g8p-d-mig", PA, 110_000m, "migration");
        await InsertDeliveryAsync(db, t, "g8p-d-hum", PA, 110_000m, "direct");
        await InsertReceiptAsync(db, t, "g8p-r-mig", PB, 55_000m, "migration");
        await db.ExecuteAsync("""
            INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, ref_doc_type, ref_doc_id, is_active) VALUES
              (UUID(), @T, @PA, '2026-03-02', 5000, 'sales_delivery', 'g8p-d-mig', 1),
              (UUID(), @T, @PA, '2026-03-02', 7000, 'sales_delivery', 'g8p-d-hum', 1)
            """, new { T = t, PA });
        await db.ExecuteAsync("""
            INSERT INTO payments (payment_id, tenant_id, partner_id, payment_type, amount, payment_date, ref_order_id, is_active)
            VALUES (UUID(), @T, @PB, 'purchase', 3000, '2026-03-02', 'g8p-r-mig', 1)
            """, new { T = t, PB });

        Assert.Equal(95_000m, (await RemainingAsync(db, t, PA, true))!.RemainingAmount);
        Assert.Equal(77_000m, (await RemainingAsync(db, t, PB, false))!.RemainingAmount);

        var docSide = await db.ExecuteScalarAsync<decimal>($"SELECT COALESCE(SUM(amount),0) FROM collections c WHERE c.tenant_id=@TenantId AND {LegacyBalanceMatching.CollectionDocSideWhere("c")}", new { TenantId = t });
        Assert.Equal(7_000m, docSide);
        var paySide = await db.ExecuteScalarAsync<decimal>($"SELECT COALESCE(SUM(amount),0) FROM payments p WHERE p.tenant_id=@TenantId AND {LegacyBalanceMatching.PaymentDocSideWhere("p")}", new { TenantId = t });
        Assert.Equal(0m, paySide);
    }

    [Fact(DisplayName = "G8-q 이관 수금·지급 삭제 거절 — 잔액·is_active·분개 무변화 (PM 후속 1)")]
    public async Task G8q_이관삭제거절()
    {
        if (Skip(nameof(G8q_이관삭제거절))) return;
        var (db, t, svc) = await NewTenantAsync();
        await using var _ = db;
        var cid = Guid.NewGuid().ToString();
        var pid = Guid.NewGuid().ToString();
        await db.ExecuteAsync("""
            INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, ref_doc_type, ref_doc_id, is_active, source_type, source_id)
            VALUES (@Id, @T, @P, '2026-01-20', 40000, 'sales_delivery', 'mig-q', 1, 'migration', 'mig-g8-q')
            """, new { Id = cid, T = t, P = PA });
        await db.ExecuteAsync("""
            INSERT INTO payments (payment_id, tenant_id, partner_id, payment_type, amount, payment_date, ref_order_id, is_active, source_type, source_id)
            VALUES (@Id, @T, @P, 'purchase', 20000, '2026-01-20', 'mig-q', 1, 'migration', 'mig-g8-q')
            """, new { Id = pid, T = t, P = PB });
        // 사람 입력으로 잔액 칸이 있는 상태에서 — 이관 삭제가 이 값을 깎으면 FAIL
        await svc.CreateCollectionAsync(LegacyCollection(PA, 30_000m), t, "g8");
        await svc.CreatePaymentAsync(LegacyPayment(PB, 30_000m), t, "g8");

        var ex1 = await RejectedUnchangedAsync(db, t, () => svc.DeleteCollectionAsync(cid, t));
        Assert.Equal("이전 프로그램에서 옮겨 온 수금은 삭제할 수 없습니다.", ex1.Message);
        var ex2 = await RejectedUnchangedAsync(db, t, () => svc.DeletePaymentAsync(pid, t));
        Assert.Equal("이전 프로그램에서 옮겨 온 지급은 삭제할 수 없습니다.", ex2.Message);

        Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT is_active FROM collections WHERE collection_id=@Id", new { Id = cid }));
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT is_active FROM payments WHERE payment_id=@Id", new { Id = pid }));
        Assert.Equal(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM journal_entries WHERE tenant_id=@T AND source_id IN (@C, @P)", new { T = t, C = cid, P = pid }));
        Assert.Single(await svc.GetCollectionsAsync(t), c => c.CollectionId == cid);
    }

    // ═══ R1b — [3-V] 병렬이슈 42·43·44·46 (작지 §15-12) ═══

    /// <summary>저장된 글자 그대로(콜레이션 무시) — BINARY 로 읽어 UTF-8 로 되돌린다.</summary>
    private static async Task<string> StoredBinaryAsync(MySqlConnection db, string sql, string id)
        => System.Text.Encoding.UTF8.GetString((await db.ExecuteScalarAsync<byte[]>(sql, new { Id = id }))!);

    [Fact(DisplayName = "G8-r 병렬이슈42 수금 유형 표기 변형(대소문자·끝 공백·전각) — 음수·기준일 이전·초과·명세서 초과 전부 거절 · 정상은 정규 값으로 저장")]
    public async Task G8r_수금유형변형()
    {
        if (Skip(nameof(G8r_수금유형변형))) return;
        var (db, t, svc) = await NewTenantAsync();
        await using var _ = db;
        await InsertDeliveryAsync(db, t, "g8r-d", PA, 100_000m, "direct");

        CreateCollectionRequest Req(string type, decimal amount, DateTime? date = null, string? refId = null)
            => new() { PartnerId = PA, CollectionDate = date ?? After, Amount = amount, CollectionMethod = "cash", RefDocType = type, RefDocId = refId ?? PA };

        await RejectedUnchangedAsync(db, t, () => svc.CreateCollectionAsync(Req("Legacy_Balance", -500_000m, new DateTime(2025, 1, 1)), t, "g8"));
        var s2 = await RejectedUnchangedAsync(db, t, () => svc.CreateCollectionAsync(Req("legacy_balance ", 70_000m, new DateTime(2025, 1, 1)), t, "g8"));
        Assert.Contains("기준일(2026-02-28)까지", s2.Message);
        var over = await RejectedUnchangedAsync(db, t, () => svc.CreateCollectionAsync(Req("LEGACY_BALANCE", 100_001m), t, "g8"));
        Assert.Contains("100,000원 남았습니다", over.Message);
        await RejectedUnchangedAsync(db, t, () => svc.CreateCollectionAsync(Req("LEGACY_BALANCE", 1m, refId: PA.ToUpperInvariant()), t, "g8"));
        var p2 = await RejectedUnchangedAsync(db, t, () => svc.CreateCollectionAsync(Req("Sales_Delivery", 100_001m, refId: "g8r-d"), t, "g8"));
        Assert.Contains("100,000원입니다", p2.Message);
        // 전각 ｌ — unicode_ci 는 'legacy_balance' 와 같게 본다 → 목록 밖으로 거절
        var unk = await RejectedUnchangedAsync(db, t, () => svc.CreateCollectionAsync(Req("ｌegacy_balance", 1_000m), t, "g8"));
        Assert.Equal(CollectionService.MsgUnknownCollectionType, unk.Message);
        await RejectedUnchangedAsync(db, t, () => svc.CreateCollectionAsync(Req("delivery", 1_000m, refId: "g8r-d"), t, "g8"));

        var ok = await svc.CreateCollectionAsync(Req(" Legacy_Balance ", 30_000m), t, "g8");
        const string colSql = "SELECT CAST(ref_doc_type AS BINARY) FROM collections WHERE collection_id=@Id";
        Assert.Equal("legacy_balance", await StoredBinaryAsync(db, colSql, ok));
        var okDoc = await svc.CreateCollectionAsync(Req("SALES_DELIVERY", 10_000m, refId: "g8r-d"), t, "g8");
        Assert.Equal("sales_delivery", await StoredBinaryAsync(db, colSql, okDoc));
        var noRef = await svc.CreateCollectionAsync(Req("   ", 1_000m, refId: null), t, "g8");
        Assert.Null(await db.ExecuteScalarAsync<string?>("SELECT ref_doc_type FROM collections WHERE collection_id=@Id", new { Id = noRef }));

        var r = (await RemainingAsync(db, t, PA, true))!;
        Assert.Equal(30_000m, r.MatchedAmount);
        Assert.Equal(70_000m, r.RemainingAmount);
    }

    [Fact(DisplayName = "G8-s 병렬이슈42 지급 유형 표기 변형 — 음수·초과·매입 초과·모르는 값 거절 · 정상은 정규 값으로 저장")]
    public async Task G8s_지급유형변형()
    {
        if (Skip(nameof(G8s_지급유형변형))) return;
        var (db, t, svc) = await NewTenantAsync();
        await using var _ = db;
        await InsertReceiptAsync(db, t, "g8s-r", PB, 55_000m, "direct");

        CreatePaymentRequest Req(string? type, decimal amount, DateTime? date = null, string? refId = null)
            => new() { PartnerId = PB, PaymentDate = date ?? After, Amount = amount, PaymentMethod = "bank_transfer", PaymentType = type!, RefOrderId = refId ?? PB };

        await RejectedUnchangedAsync(db, t, () => svc.CreatePaymentAsync(Req("Legacy_Balance", -500_000m, new DateTime(2025, 1, 1)), t, "g8"));
        await RejectedUnchangedAsync(db, t, () => svc.CreatePaymentAsync(Req("legacy_balance ", 70_000m, new DateTime(2025, 1, 1)), t, "g8"));
        var over = await RejectedUnchangedAsync(db, t, () => svc.CreatePaymentAsync(Req("LEGACY_BALANCE", 80_001m), t, "g8"));
        Assert.Contains("80,000원 남았습니다", over.Message);
        var p2 = await RejectedUnchangedAsync(db, t, () => svc.CreatePaymentAsync(Req("PURCHASE ", 55_001m, refId: "g8s-r"), t, "g8"));
        Assert.Contains("55,000원입니다", p2.Message);
        var unk = await RejectedUnchangedAsync(db, t, () => svc.CreatePaymentAsync(Req("ｌegacy_balance", 1_000m), t, "g8"));
        Assert.Equal(CollectionService.MsgUnknownPaymentType, unk.Message);
        await RejectedUnchangedAsync(db, t, () => svc.CreatePaymentAsync(Req("", 1_000m), t, "g8"));
        await RejectedUnchangedAsync(db, t, () => svc.CreatePaymentAsync(Req(null, 1_000m), t, "g8"));

        var ok = await svc.CreatePaymentAsync(Req("Legacy_Balance", 30_000m), t, "g8");
        const string paySql = "SELECT CAST(payment_type AS BINARY) FROM payments WHERE payment_id=@Id";
        Assert.Equal("legacy_balance", await StoredBinaryAsync(db, paySql, ok));
        var okDoc = await svc.CreatePaymentAsync(Req(" Purchase", 5_000m, refId: "g8s-r"), t, "g8");
        Assert.Equal("purchase", await StoredBinaryAsync(db, paySql, okDoc));

        var r = (await RemainingAsync(db, t, PB, false))!;
        Assert.Equal(30_000m, r.MatchedAmount);
        Assert.Equal(50_000m, r.RemainingAmount);
    }

    [Fact(DisplayName = "G8-t 병렬이슈43 수금·지급 금액 0 이하·소수 셋째 자리 거절(ref 없음·명세서·이월 모두) · 둘째 자리는 허용")]
    public async Task G8t_금액검사()
    {
        if (Skip(nameof(G8t_금액검사))) return;
        var (db, t, svc) = await NewTenantAsync();
        await using var _ = db;
        await InsertDeliveryAsync(db, t, "g8t-d", PA, 110_000m, "direct");
        await InsertReceiptAsync(db, t, "g8t-r", PB, 55_000m, "direct");

        foreach (var amount in new[] { -50_000m, 0m, 0.004m, 1.005m })
        {
            var e1 = await RejectedUnchangedAsync(db, t, () => svc.CreateCollectionAsync(new CreateCollectionRequest { PartnerId = PA, CollectionDate = After, Amount = amount, CollectionMethod = "cash" }, t, "g8"));
            Assert.Equal(CollectionService.MsgAmountInvalid, e1.Message);
            await RejectedUnchangedAsync(db, t, () => svc.CreateCollectionAsync(new CreateCollectionRequest { PartnerId = PA, CollectionDate = After, Amount = amount, CollectionMethod = "cash", RefDocType = "sales_delivery", RefDocId = "g8t-d" }, t, "g8"));
            await RejectedUnchangedAsync(db, t, () => svc.CreateCollectionAsync(LegacyCollection(PA, amount), t, "g8"));
            var e2 = await RejectedUnchangedAsync(db, t, () => svc.CreatePaymentAsync(new CreatePaymentRequest { PartnerId = PB, PaymentDate = After, Amount = amount, PaymentMethod = "cash", PaymentType = "payment" }, t, "g8"));
            Assert.Equal(CollectionService.MsgAmountInvalid, e2.Message);
            await RejectedUnchangedAsync(db, t, () => svc.CreatePaymentAsync(new CreatePaymentRequest { PartnerId = PB, PaymentDate = After, Amount = amount, PaymentMethod = "cash", PaymentType = "purchase", RefOrderId = "g8t-r" }, t, "g8"));
            await RejectedUnchangedAsync(db, t, () => svc.CreatePaymentAsync(LegacyPayment(PB, amount), t, "g8"));
        }

        // 이슈43 재현 경로: 음수로 명세서 남은 금액을 늘린 뒤 초과 수금 — 음수가 막히니 110,000 까지만 된다
        await RejectedUnchangedAsync(db, t, () => svc.CreateCollectionAsync(new CreateCollectionRequest { PartnerId = PA, CollectionDate = After, Amount = 160_000m, CollectionMethod = "cash", RefDocType = "sales_delivery", RefDocId = "g8t-d" }, t, "g8"));

        var id = await svc.CreateCollectionAsync(new CreateCollectionRequest { PartnerId = PA, CollectionDate = After, Amount = 1.25m, CollectionMethod = "cash" }, t, "g8");
        Assert.Equal(1.25m, await db.ExecuteScalarAsync<decimal>("SELECT amount FROM collections WHERE collection_id=@Id", new { Id = id }));
    }

    [Fact(DisplayName = "G8-u 병렬이슈44 두 연결 — 잠금 전 일반 읽기가 있어도 나중 연결은 최신 R 로 판정 · 최종 R 음수 안 됨(미수·미지급)")]
    public async Task G8u_잠금읽기_최신R()
    {
        if (Skip(nameof(G8u_잠금읽기_최신R))) return;
        var (db, t, _svc) = await NewTenantAsync();
        await using var _ = db;

        foreach (var receivable in new[] { true, false })
        {
            var partner = receivable ? PA : PB;
            var legacy = receivable ? 100_000m : 80_000m;
            await using var c1 = new MySqlConnection(_fx.DbConnString());
            await using var c2 = new MySqlConnection(_fx.DbConnString());
            await c1.OpenAsync();
            await c2.OpenAsync();
            // PM 후속 2 — 서비스와 같은 격리 수준(RC). 다른 격리 수준은 계약 가드가 막는다(아래 끝).
            using var tx1 = await BeginMatchTxAsync(c1);
            using var tx2 = await BeginMatchTxAsync(c2);

            // 이슈44 경우 B — 잠금 전 일반 SELECT 가 스냅숏을 연다(두 연결 모두 옛 R 을 본다)
            var sentinel = receivable ? "SELECT COUNT(*) FROM collections WHERE tenant_id=@T" : "SELECT COUNT(*) FROM payments WHERE tenant_id=@T";
            await c1.ExecuteScalarAsync<int>(sentinel, new { T = t }, tx1);
            await c2.ExecuteScalarAsync<int>(sentinel, new { T = t }, tx2);

            var amount = 60_000m;
            await LegacyBalanceMatching.EnsureMatchAllowedAsync(c1, tx1, t, partner, partner, amount, After, receivable, null, CancellationToken.None);
            await InsertMatchAsync(c1, tx1, t, partner, amount, receivable);
            await tx1.CommitAsync();

            InvalidOperationException? rejected = null;
            try
            {
                await LegacyBalanceMatching.EnsureMatchAllowedAsync(c2, tx2, t, partner, partner, amount, After, receivable, null, CancellationToken.None);
                await InsertMatchAsync(c2, tx2, t, partner, amount, receivable);
                await tx2.CommitAsync();
            }
            catch (InvalidOperationException ex)
            {
                rejected = ex;
                await tx2.RollbackAsync();
            }

            var r = (await RemainingAsync(db, t, partner, receivable))!;
            Assert.True(r.RemainingAmount >= 0m, $"최종 R 음수 {r.RemainingAmount} (receivable={receivable})");
            Assert.NotNull(rejected);
            Assert.Contains($"{legacy - amount:N0}원 남았습니다", rejected!.Message);
        }

        // 계약 가드 — REPEATABLE READ 트랜잭션으로 부르면 옛 R 로 판정하지 않고 바로 막힌다
        await using var rr = new MySqlConnection(_fx.DbConnString());
        await rr.OpenAsync();
        await using var rrTx = await rr.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        await Assert.ThrowsAsync<NotSupportedException>(() => LegacyBalanceMatching.GetForUpdateAsync(rr, rrTx, t, PA, true, CancellationToken.None));
    }

    private const System.Data.IsolationLevel MatchIsolation = LegacyBalanceMatching.MatchIsolation;

    /// <summary>매칭 트랜잭션 — 서비스와 같은 격리 수준으로 연다.</summary>
    private static async Task<MySqlTransaction> BeginMatchTxAsync(MySqlConnection c)
        => await c.BeginTransactionAsync(MatchIsolation);

    /// <summary>한 연결의 매칭(잠금·검사·INSERT)을 커밋 없이 돌린다. 거절이면 예외를 돌려준다.</summary>
    private static async Task<Exception?> TryMatchAsync(MySqlConnection c, MySqlTransaction tx, string t, string partner, decimal amount, bool receivable, bool insert)
    {
        try
        {
            await LegacyBalanceMatching.EnsureMatchAllowedAsync(c, tx, t, partner, partner, amount, After, receivable, null, CancellationToken.None);
            if (insert) await InsertMatchAsync(c, tx, t, partner, amount, receivable);
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or MySqlException)
        {
            return ex;
        }
    }

    [Fact(DisplayName = "G8-w 두 연결이 서로 다른 거래처에 동시 이월 매칭 — 둘 다 성공 · 교착 0")]
    public async Task G8w_다른거래처_동시()
    {
        if (Skip(nameof(G8w_다른거래처_동시))) return;
        var (db, t, _svc) = await NewTenantAsync();
        await using var _ = db;
        await db.ExecuteAsync("""
            INSERT INTO partners (partner_id, tenant_id, partner_code, partner_name, partner_type, is_active, is_deleted, created_at, updated_at)
            VALUES (@Id, @T, 'C', 'G8거래처C', 'both', 1, 0, NOW(), NOW())
            """, new { Id = OtherPartner, T = t });
        await InsertLegacyAsync(db, t, OtherPartner, 50_000m);
        // 두 거래처 모두 이관 명세서·사람 수금이 있어야 em 파생표가 훑을 줄이 생긴다
        await InsertDeliveryAsync(db, t, "g8w-d1", PA, 10_000m, "migration");
        await InsertDeliveryAsync(db, t, "g8w-d2", OtherPartner, 10_000m, "migration");

        await using var c1 = new MySqlConnection(_fx.DbConnString());
        await using var c2 = new MySqlConnection(_fx.DbConnString());
        await c1.OpenAsync();
        await c2.OpenAsync();
        await using var tx1 = await BeginMatchTxAsync(c1);
        await using var tx2 = await BeginMatchTxAsync(c2);

        // 둘 다 잠금·검사까지 → 그다음 둘 다 INSERT (서로의 잠금이 겹치면 여기서 교착)
        Assert.Null(await TryMatchAsync(c1, tx1, t, PA, 10_000m, true, insert: false));
        Assert.Null(await TryMatchAsync(c2, tx2, t, OtherPartner, 10_000m, true, insert: false));
        var ins1 = InsertMatchAsync(c1, tx1, t, PA, 10_000m, true);
        var ins2 = InsertMatchAsync(c2, tx2, t, OtherPartner, 10_000m, true);
        Exception? e1 = null, e2 = null;
        try { await ins1; } catch (MySqlException ex) { e1 = ex; }
        try { await ins2; } catch (MySqlException ex) { e2 = ex; }
        Assert.True(e1 is null && e2 is null, $"동시 이월 매칭 실패(교착 등): c1={e1?.Message} · c2={e2?.Message}");
        await tx1.CommitAsync();
        await tx2.CommitAsync();

        Assert.Equal(90_000m, (await RemainingAsync(db, t, PA, true))!.RemainingAmount);
        Assert.Equal(40_000m, (await RemainingAsync(db, t, OtherPartner, true))!.RemainingAmount);
    }

    [Fact(DisplayName = "G8-x 같은 거래처 동시(각 R 이하 · 합 초과) — 뒤 연결은 앞 커밋을 기다렸다 거절 · 최종 R ≥ 0")]
    public async Task G8x_같은거래처_동시()
    {
        if (Skip(nameof(G8x_같은거래처_동시))) return;
        var (db, t, _svc) = await NewTenantAsync();
        await using var _ = db;

        await using var c1 = new MySqlConnection(_fx.DbConnString());
        await using var c2 = new MySqlConnection(_fx.DbConnString());
        await c1.OpenAsync();
        await c2.OpenAsync();
        await using var tx1 = await BeginMatchTxAsync(c1);
        await using var tx2 = await BeginMatchTxAsync(c2);
        // 둘 다 잠금 전 일반 읽기(옛 스냅숏을 여는 호출 순서)
        await c1.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM collections WHERE tenant_id=@T", new { T = t }, tx1);
        await c2.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM collections WHERE tenant_id=@T", new { T = t }, tx2);

        Assert.Null(await TryMatchAsync(c1, tx1, t, PA, 70_000m, true, insert: true));
        var second = TryMatchAsync(c2, tx2, t, PA, 70_000m, true, insert: true);   // 거래처 잠금에서 기다린다
        var waited = await Task.WhenAny(second, Task.Delay(1500)) != second;
        await tx1.CommitAsync();
        var e2 = await second;
        if (e2 is null) await tx2.CommitAsync(); else await tx2.RollbackAsync();

        Assert.True(waited, "뒤 연결이 거래처 잠금을 기다리지 않았다");
        Assert.IsType<InvalidOperationException>(e2);
        Assert.Contains("30,000원 남았습니다", e2!.Message);
        var r = (await RemainingAsync(db, t, PA, true))!;
        Assert.True(r.RemainingAmount >= 0m, $"최종 R 음수 {r.RemainingAmount}");
        Assert.Equal(30_000m, r.RemainingAmount);
    }

    private static Task InsertMatchAsync(MySqlConnection c, MySqlTransaction tx, string t, string partner, decimal amount, bool receivable)
        => receivable
            ? c.ExecuteAsync("""
                INSERT INTO collections (collection_id, tenant_id, partner_id, collection_date, amount, ref_doc_type, ref_doc_id, is_active)
                VALUES (UUID(), @T, @P, '2026-03-10', @A, 'legacy_balance', @P, 1)
                """, new { T = t, P = partner, A = amount }, tx)
            : c.ExecuteAsync("""
                INSERT INTO payments (payment_id, tenant_id, partner_id, payment_type, amount, payment_date, ref_order_id, is_active)
                VALUES (UUID(), @T, @P, 'legacy_balance', @A, '2026-03-10', @P, 1)
                """, new { T = t, P = partner, A = amount }, tx);

    [Fact(DisplayName = "G8-v 병렬이슈46 마감 처리가 커밋 전일 때 삭제 — 트랜잭션 안 검사가 기다렸다가 거절 · is_active·역분개 무변화")]
    public async Task G8v_삭제월마감_트랜잭션안()
    {
        if (Skip(nameof(G8v_삭제월마감_트랜잭션안))) return;
        var (db, t, svc) = await NewTenantAsync();
        await using var _ = db;
        var cid = await svc.CreateCollectionAsync(LegacyCollection(PA, 30_000m), t, "g8");
        var pid = await svc.CreatePaymentAsync(LegacyPayment(PB, 30_000m, new DateTime(2026, 4, 10)), t, "g8");

        foreach (var (ym, act, table, idCol, id, cancelType) in new[]
                 {
                     ("202603", (Func<Task>)(() => svc.DeleteCollectionAsync(cid, t)), "collections", "collection_id", cid, "collection_cancel"),
                     ("202604", (Func<Task>)(() => svc.DeletePaymentAsync(pid, t)), "payments", "payment_id", pid, "payment_cancel"),
                 })
        {
            await using var closer = new MySqlConnection(_fx.DbConnString());
            await closer.OpenAsync();
            using var ctx = await closer.BeginTransactionAsync();
            await closer.ExecuteAsync("""
                INSERT INTO monthly_closing (closing_id, tenant_id, `year_month`, status, sales_amount, purchase_amount, receipt_amount, payment_amount, created_at, updated_at)
                VALUES (UUID(), @T, @Ym, 'closed', 0, 0, 0, 0, NOW(6), NOW(6))
                """, new { T = t, Ym = ym }, ctx);

            var delete = act();
            var finishedEarly = await Task.WhenAny(delete, Task.Delay(1500)) == delete;
            await ctx.CommitAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => delete);
            Assert.Contains("마감된 기간입니다", ex.Message);
            Assert.False(finishedEarly, "삭제가 마감 커밋을 기다리지 않았다");
            Assert.Equal(1, await db.ExecuteScalarAsync<int>($"SELECT is_active FROM {table} WHERE {idCol}=@Id", new { Id = id }));
            Assert.Equal(0, await EntryCountAsync(db, t, cancelType, id));
        }
    }

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
}
