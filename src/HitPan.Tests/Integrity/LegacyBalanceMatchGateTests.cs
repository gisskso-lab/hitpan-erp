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
