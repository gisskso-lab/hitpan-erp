using System.Diagnostics;
using System.Text.Json;
using Dapper;
using HitPan.Backoffice.API.Services;
using HitPan.Backoffice.Components.Pages.Platform.Components;
using MySqlConnector;
using Xunit;
using Xunit.Abstractions;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261102작 — 고객사 화면 「ERP 설치」 칸 게이트 (설계 <c>docs/설계/백오피스/20261102_설치유무표시_설계문서.md</c> §7).
///
/// <para>재는 것은 둘이다: <b>거짓 완료</b>(시리얼만 통과 · 옛 칸 값 · 업데이트 보고)와 <b>거짓 미설치</b>(재료 있음 · 업데이트 보고만 있음 · 읽기 실패).
/// 실물 적용기 · 실물 JWT · 실물 컨트롤러로 HTTP 요청을 흘리고, 응답을 <b>화면이 읽는 그 형</b>
/// (<see cref="TenantInstallFields"/> 상속)으로 받아 <see cref="TenantInstallText"/> 를 불러 판독한다.
/// 사전으로 읽지 않는다(10/13 <c>attempt_count</c> 사고).</para>
///
/// <para>🔴 <b>CI 자리는 <c>bo-mysql8-gate</c> 잡이다.</b> <c>build</c> 잡은 <c>[SKIP]</c> 으로 조용히 초록이 된다
/// (G-7 은 DB 를 안 타므로 어디서나 돈다). 운영 백오피스는 MySQL 8 이라 CI 에서 MariaDB 에 붙으면 FAIL 한다.</para>
///
/// <para>음성 대조(설계 §7-3 · 작업리뷰서 §5)는 CI 계측 가지에서 잰다 — 개발명세서 §4.</para>
/// </summary>
[Collection(BoSignupTenantKeyGateCollection.Name)]
public sealed class BackofficeInstallStateGateTests : IDisposable
{
    private readonly BackofficeRoleGateHarness _h = new("bo_install_state_gate");
    private readonly ITestOutputHelper _out;

    // 재는 판 · G-8 수치는 개발명세서가 인용한다 ⇒ 통과한 시험도 로그에 남는 길로 찍는다(CI 단계 verbosity=detailed).
    public BackofficeInstallStateGateTests(ITestOutputHelper output) => _out = output;

    public void Dispose() => _h.Dispose();

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // UTC 00:30 — 화면에는 09:30 으로 나와야 한다(G-7 과 같은 값).
    private static readonly DateTime OwnerAt = new(2026, 11, 2, 0, 30, 0);

    // 하니스가 심는 고객사 4행 + 대조군 — 행이 불어나면 이 수가 깨진다.
    private const int HarnessTenants = 4;

    private const string ListPath = "/api/backoffice/tenants";

    private const string PaymentOrderId = "GATE-ORDER-1";

    // 5초 상한 + 취소 대기 2초(드라이버 기본값) + 여유. commandTimeout 이 빠지면 연결 기본값(하니스 90초)까지 늘어져 이 선을 넘는다.
    private static readonly TimeSpan LockedCallLimit = TimeSpan.FromSeconds(8);

    // 이보다 빨리 끝나면 잠금을 안 기다린 것이다 — 다른 이유의 실패를 시간 초과 증거로 읽으면 안 된다.
    private static readonly TimeSpan LockedCallFloor = TimeSpan.FromSeconds(4);

    private sealed class GateRow : TenantInstallFields
    {
        public string TenantId { get; set; } = "";
        public string TenantCode { get; set; } = "";
        public int IsLocked { get; set; }
        public DateTime? BootstrapAt { get; set; }
    }

    private sealed class ListBody
    {
        public List<GateRow> Items { get; set; } = new();
    }

    private sealed class PaymentRow
    {
        public string? OrderId { get; set; }
    }

    private sealed class DetailBody
    {
        public GateRow? Item { get; set; }
        public List<PaymentRow> Payments { get; set; } = new();
    }

    // 구판 화면 모델 모양 — 새 칸을 모른다.
    private sealed class OldScreenRow
    {
        public string TenantId { get; set; } = "";
        public int IsLocked { get; set; }
        public DateTime? BootstrapAt { get; set; }
    }

    private sealed class IdRow
    {
        public string TenantId { get; set; } = "";
    }

    private sealed class ExplainRow
    {
        public string? Table { get; set; }
        public string? Type { get; set; }
        public string? Key { get; set; }
    }

    // ══════════════════════════════════════════════════════════════
    // 준비 · 판독
    // ══════════════════════════════════════════════════════════════

    /// <summary>격리 DB + 판 확인. 하니스는 판을 안 묻는다 ⇒ 여기서 묻는다. false = 이 자리는 재지 않는다.</summary>
    private async Task<bool> SetUpAsync(string gate)
    {
        if (!await _h.TrySetUpAsync(gate)) return false;

        await using var db = await _h.OpenAsync();
        var version = await db.ExecuteScalarAsync<string>("SELECT VERSION()") ?? string.Empty;
        _out.WriteLine($"[{gate}] 재는 판: {version}");
        if (!version.Contains("mariadb", StringComparison.OrdinalIgnoreCase)) return true;

        if (DbGateEnvironment.IsCi)
        {
            throw new Xunit.Sdk.XunitException(
                $"[게이트 오배치] {gate} — 붙은 서버가 MariaDB({version})다. 이 게이트는 bo-mysql8-gate 잡에서만 의미가 있다.");
        }
        Console.Error.WriteLine($"[SKIP] {gate} — 붙은 서버가 MariaDB({version}). MySQL 8 판이 아니라 재지 않는다.");
        DbGateEnvironment.SkipOrFailStrict(gate);
        return false;
    }

    /// <summary>
    /// 대조군 7건 (설계 §7-1 + 지시서 §8-1). A = 대표 아이디 수집만 · B = 성공 업데이트 보고만(같은 고객 2행) + tenant_id NULL 행 ·
    /// C = 시리얼 통과 3행 · D = 재료 없음 + 옛 칸 값 · E = 재료 없음 + 자기 행에 다른 결과 글자 ·
    /// F = 실패 업데이트 보고만 · G = 시리얼 통과 + 성공 업데이트 보고(로그인 불가 설치).
    /// </summary>
    private async Task<Dictionary<string, string>> SeedControlsAsync()
    {
        var seeded = new[] { "A", "B", "C", "D", "E", "F", "G" }.ToDictionary(k => k, _ => Guid.NewGuid().ToString());
        await using var db = await _h.OpenAsync();

        foreach (var (key, id) in seeded)
        {
            await db.ExecuteAsync(@"
                INSERT INTO tenants (tenant_id, tenant_code, company_name, reseller_id, status, subscription_tier,
                                     owner_collected_at, is_locked_from_landing, bootstrap_at)
                VALUES (@id, @code, @nm, @rid, 'active', 'basic', @owner, @locked, @boot)",
                new
                {
                    id,
                    code = $"GI{key}",
                    nm = $"설치시험{key}",
                    rid = _h.ResellerA,
                    owner = key == "A" ? OwnerAt : (DateTime?)null,
                    locked = key == "D" ? 1 : 0,
                    // 옛 칸 값은 기동 시각과 달라야 한다 — 설치⑦ 이 응답 원문에서 기동 시각만 찾는다.
                    boot = key == "D" ? OwnerAt.AddDays(-1) : (DateTime?)null,
                });
        }

        await db.ExecuteAsync(@"
            INSERT INTO tenant_update_state (tenant_id_hash, tenant_id, current_version, last_result, last_reported_at)
            VALUES ('gate-b-1', @b, '1.3.60', 'success', UTC_TIMESTAMP(6)),
                   ('gate-b-2', @b, '1.3.60', 'success', UTC_TIMESTAMP(6)),
                   ('gate-f', @f, NULL, 'failed', UTC_TIMESTAMP(6)),
                   ('gate-g', @g, '1.3.60', 'success', UTC_TIMESTAMP(6)),
                   ('gate-null', NULL, '1.3.60', 'success', UTC_TIMESTAMP(6))",
            new { b = seeded["B"], f = seeded["F"], g = seeded["G"] });

        await db.ExecuteAsync(@"
            INSERT INTO serial_verify_attempts (tenant_id, submitted_hash, result)
            VALUES (@c, 'gate-c', 'installer-bootstrap'),
                   (@c, 'gate-c', 'installer-bootstrap'),
                   (@c, 'gate-c', 'installer-bootstrap'),
                   (@e, 'gate-e', 'installer-mismatch'),
                   (@g, 'gate-g', 'installer-bootstrap')",
            new { c = seeded["C"], e = seeded["E"], g = seeded["G"] });

        return seeded;
    }

    /// <summary>결제 1건 — 판정이 실패해도 상세의 결제 칸이 살아 있는지 재는 표식.</summary>
    private async Task SeedPaymentAsync(string companyName)
    {
        await using var db = await _h.OpenAsync();
        await db.ExecuteAsync(@"
            INSERT INTO landing_signups (signup_token, biz_no_hash, company_name, email, phone)
            VALUES ('gate-signup', 'gate-biz', @companyName, 'install@gate.test', '010')",
            new { companyName });
        await db.ExecuteAsync(@"
            INSERT INTO tenant_payments (payment_id, order_id, signup_token, payment_key, amount, status)
            VALUES (@paymentId, @orderId, 'gate-signup', 'gate-key', 1000, 'approved')",
            new { paymentId = Guid.NewGuid().ToString(), orderId = PaymentOrderId });
    }

    private async Task<List<GateRow>> ReadListAsync(string token)
    {
        var res = await _h.SendAsync(token, ListPath);
        Assert.True(res.Status == 200, $"목록이 200 이 아니다(실측 {res.Status}). 본문: {Trim(res.Body)}");
        return JsonSerializer.Deserialize<ListBody>(res.Body, Web)!.Items;
    }

    private async Task<GateRow> ReadDetailAsync(string token, string id)
    {
        var res = await _h.SendAsync(token, $"/api/backoffice/tenants/{id}");
        Assert.True(res.Status == 200, $"상세가 200 이 아니다(실측 {res.Status}). 본문: {Trim(res.Body)}");
        return JsonSerializer.Deserialize<DetailBody>(res.Body, Web)!.Item
            ?? throw new Xunit.Sdk.XunitException("상세 응답에 item 이 없다.");
    }

    /// <summary>목록을 읽고 대조군 전건의 상세도 읽어 <b>두 화면의 판정이 같은지</b> 먼저 본다. 돌려주는 행은 상세다 — 시각은 상세에만 있다.</summary>
    private async Task<Dictionary<string, GateRow>> ReadControlsAsync(Dictionary<string, string> seeded)
    {
        var token = await _h.LoginHqAsync();
        var list = await ReadListAsync(token);
        var byKey = new Dictionary<string, GateRow>();

        foreach (var (key, id) in seeded)
        {
            var row = list.Single(r => r.TenantId == id);
            var detail = await ReadDetailAsync(token, id);
            Assert.True(row.InstallState == detail.InstallState,
                $"{key}: 목록({row.InstallState}) ≠ 상세({detail.InstallState})");
            byKey[key] = detail;
        }
        return byKey;
    }

    private static string Trim(string body) => body.Length <= 300 ? body : body[..300];

    // ══════════════════════════════════════════════════════════════
    // 대조군 ①~⑥ — 목록 + 상세
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "설치① 🔴 거짓 미설치 — 옛 칸이 비어 있어도 ERP 기동 기록이 있으면 완료 (판정을 IsLocked 로 되돌리면 빨강)")]
    public async Task Control1_Installed_Without_Old_Columns()
    {
        if (!await SetUpAsync("설치①")) return;
        var rows = await ReadControlsAsync(await SeedControlsAsync());

        Assert.Equal(0, rows["A"].IsLocked);
        Assert.Null(rows["A"].BootstrapAt);
        Assert.True(rows["A"].InstallState == TenantInstallStateReader.Installed,
            $"A: ERP 기동 기록이 있는데 {rows["A"].InstallState} — 거짓 미설치다.");
        Assert.Equal("완료", TenantInstallText.Label(rows["A"].InstallState));
    }

    [Fact(DisplayName = "설치② 🔴 거짓 완료 — 시리얼만 통과한 고객은 설치 시작이다")]
    public async Task Control2_Serial_Pass_Is_Not_Installed()
    {
        if (!await SetUpAsync("설치②")) return;
        var rows = await ReadControlsAsync(await SeedControlsAsync());

        Assert.True(rows["C"].InstallState == TenantInstallStateReader.Started,
            $"C: 시리얼만 통과했는데 {rows["C"].InstallState} — 거짓 완료다.");
        Assert.Null(rows["C"].LastErpStartAt);
        Assert.Equal("설치 시작", TenantInstallText.Label(rows["C"].InstallState));
    }

    [Fact(DisplayName = "설치③ 양성 — 완료는 ERP 기동 시각까지 함께 나온다")]
    public async Task Control3_Installed_Carries_Time()
    {
        if (!await SetUpAsync("설치③")) return;
        var rows = await ReadControlsAsync(await SeedControlsAsync());

        Assert.Equal(TenantInstallStateReader.Installed, rows["A"].InstallState);
        Assert.Equal(OwnerAt, rows["A"].LastErpStartAt);
        Assert.Contains("최근 ERP 기동: 2026-11-02 09:30",
            TenantInstallText.Help(rows["A"].InstallState, rows["A"].LastErpStartAt));
    }

    [Fact(DisplayName = "설치④ N행 — 1고객 N행 재료가 행을 불리지 않고 전 행이 판정된다")]
    public async Task Control4_Many_Rows_Do_Not_Multiply()
    {
        if (!await SetUpAsync("설치④")) return;
        var seeded = await SeedControlsAsync();
        var list = await ReadListAsync(await _h.LoginHqAsync());

        Assert.Equal(HarnessTenants + seeded.Count, list.Count);
        Assert.Equal(list.Count, list.Select(r => r.TenantId).Distinct().Count());
        Assert.True(list.All(r => r.InstallState is not null), "판정 없는 행이 있다 — 둘째 문장이 실패했거나 행을 놓쳤다.");

        // 🔴 화면은 키로 합치므로 문장이 불어나도 응답 행 수는 그대로다 ⇒ 둘째 문장의 행 수를 직접 잰다.
        var ids = list.Select(r => r.TenantId).ToList();
        await using var db = await _h.OpenAsync();
        var facts = (await db.QueryAsync<IdRow>(TenantInstallStateReader.ReadSql, new { Ids = ids })).ToList();
        Assert.Equal(ids.Count, facts.Count);
        Assert.Equal(ids.Count, facts.Select(f => f.TenantId).Distinct().Count());
    }

    [Fact(DisplayName = "설치⑤ 🔴 거짓 완료 — 옛 칸 값 · 다른 결과 글자로는 미설치다")]
    public async Task Control5_Old_Columns_And_Other_Results_Are_None()
    {
        if (!await SetUpAsync("설치⑤")) return;
        var rows = await ReadControlsAsync(await SeedControlsAsync());

        Assert.Equal(1, rows["D"].IsLocked);
        Assert.NotNull(rows["D"].BootstrapAt);
        foreach (var key in new[] { "D", "E" })
        {
            Assert.True(rows[key].InstallState == TenantInstallStateReader.None,
                $"{key}: 재료가 없는데 {rows[key].InstallState}.");
            Assert.Equal("미설치", TenantInstallText.Label(rows[key].InstallState));
        }
    }

    [Fact(DisplayName = "설치⑥ 🔴 업데이트 보고는 완료가 아니고 미설치도 아니다 (성공 · 실패 · 시리얼 통과 + 성공 전부 설치 시작)")]
    public async Task Control6_Update_Report_Is_Started()
    {
        if (!await SetUpAsync("설치⑥")) return;
        var rows = await ReadControlsAsync(await SeedControlsAsync());

        // G = 로그인 불가 설치 — 깔다 멈춘 고객이 업데이트 보고 1건으로 찾는 목록에서 빠지면 안 된다.
        foreach (var key in new[] { "B", "F", "G" })
        {
            Assert.True(rows[key].InstallState == TenantInstallStateReader.Started,
                $"{key}: 업데이트 보고가 있는데 {rows[key].InstallState} — 설치 시작이어야 한다.");
            Assert.Null(rows[key].LastErpStartAt);
            Assert.Equal("설치 시작", TenantInstallText.Label(rows[key].InstallState));
            Assert.Contains("설치 기록은 있지만", TenantInstallText.Help(rows[key].InstallState, rows[key].LastErpStartAt));
        }
    }

    [Fact(DisplayName = "설치⑦ 🔴 시각은 상세에서만 — 목록 응답 원문에 시각 값이 없다 (시각을 되살리면 빨강)")]
    public async Task Control7_List_Has_No_Time()
    {
        if (!await SetUpAsync("설치⑦")) return;
        var seeded = await SeedControlsAsync();
        var token = await _h.LoginHqAsync();
        var stamp = OwnerAt.ToString("yyyy-MM-ddTHH:mm");

        // 화면 모델이 아니라 원문을 본다 — 화면에서 숨겨도 응답에 실려 있으면 빨강이어야 한다.
        var list = await _h.SendAsync(token, ListPath);
        Assert.True(list.Status == 200, $"목록이 200 이 아니다(실측 {list.Status}). 본문: {Trim(list.Body)}");
        Assert.DoesNotContain(stamp, list.Body);

        // 대조 — 같은 고객의 상세 원문에는 있다. 이게 없으면 「목록에 없다」 가 시드 탓일 수 있다.
        var detail = await _h.SendAsync(token, $"{ListPath}/{seeded["A"]}");
        Assert.True(detail.Status == 200, $"상세가 200 이 아니다(실측 {detail.Status}). 본문: {Trim(detail.Body)}");
        Assert.Contains(stamp, detail.Body);
    }

    // ══════════════════════════════════════════════════════════════
    // G-6 첫 읽기 보호 — 오류 세 갈래 · 시간 초과 · 연결 끊김
    // ══════════════════════════════════════════════════════════════

    /// <summary>재료를 망가뜨리고 <b>그 오류 번호가 실제로 나는지</b> 먼저 본 뒤, 목록 · 상세가 살아 있고 전부 판정 없음인지 잰다.</summary>
    private async Task AssertSurvivesAsync(string gate, string breakSql, int expectedError)
    {
        if (!await SetUpAsync(gate)) return;
        var seeded = await SeedControlsAsync();
        var token = await _h.LoginHqAsync();

        // 대조 — 망가뜨리기 전에는 판정이 나온다. 이게 없으면 「전부 null」이 보호의 증거가 아니다.
        var before = await ReadListAsync(token);
        Assert.True(before.All(r => r.InstallState is not null), $"[{gate}] 망가뜨리기 전부터 판정이 없다.");

        await using (var db = await _h.OpenAsync())
        {
            await db.ExecuteAsync(breakSql);
            var ex = await Assert.ThrowsAsync<MySqlException>(() =>
                db.QueryAsync<IdRow>(TenantInstallStateReader.ReadSql, new { Ids = seeded.Values.ToList() }));
            Assert.Equal(expectedError, ex.Number);
        }

        var after = await ReadListAsync(token);
        Assert.Equal(before.Count, after.Count);
        foreach (var row in after)
        {
            Assert.True(row.InstallState is null, $"[{gate}] 읽기가 실패했는데 판정이 있다: {row.InstallState}");
            Assert.Equal("확인 불가", TenantInstallText.Label(row.InstallState));
            Assert.NotEqual("미설치", TenantInstallText.Label(row.InstallState));
        }

        var detail = await ReadDetailAsync(token, seeded["A"]);
        Assert.Null(detail.InstallState);
        Assert.Equal("확인 불가", TenantInstallText.Label(detail.InstallState));
    }

    // 🔴 재료 표마다 잰다 — 한 표만 없을 때 남은 표로 반쪽 판정을 내면 빨강이다.
    [Fact(DisplayName = "G-6ⓐ1 🔴 시리얼 표 없음(1146) — 목록 · 상세 200 · 행 수 불변 · 확인 불가")]
    public Task G6_Serial_Table_Missing() =>
        AssertSurvivesAsync("G-6ⓐ1", "DROP TABLE serial_verify_attempts", 1146);

    [Fact(DisplayName = "G-6ⓐ2 🔴 업데이트 표 없음(1146) — 목록 · 상세 200 · 행 수 불변 · 확인 불가")]
    public Task G6_Update_Table_Missing() =>
        AssertSurvivesAsync("G-6ⓐ2", "DROP TABLE tenant_update_state", 1146);

    [Fact(DisplayName = "G-6ⓑ 🔴 칸 없음(1054) — 목록 · 상세 200 · 행 수 불변 · 확인 불가")]
    public Task G6_Column_Missing() =>
        AssertSurvivesAsync("G-6ⓑ", "ALTER TABLE tenants DROP COLUMN owner_collected_at", 1054);

    [Fact(DisplayName = "G-6ⓒ 🔴 문자 규칙 어긋남(1267) — 목록 · 상세 200 · 행 수 불변 · 확인 불가")]
    public Task G6_Collation_Mismatch() =>
        AssertSurvivesAsync("G-6ⓒ",
            "ALTER TABLE serial_verify_attempts MODIFY tenant_id varchar(36) "
          + "CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL",
            1267);

    /// <summary>요청을 끝까지 기다리되 상한을 넘기면 빨강이다. 상한이 빠진 코드는 연결 기본값까지 늘어지므로 여기서 잡힌다.</summary>
    private static async Task<BackofficeRoleGateHarness.Outcome> WithinLimitAsync(
        string gate, string path, Task<BackofficeRoleGateHarness.Outcome> request)
    {
        try
        {
            return await request.WaitAsync(LockedCallLimit);
        }
        catch (TimeoutException)
        {
            throw new Xunit.Sdk.XunitException(
                $"[{gate}] {path} 가 {LockedCallLimit.TotalSeconds}초 안에 안 끝났다 — 둘째 문장의 시간 상한이 안 먹는다.");
        }
    }

    /// <summary>재료 표가 잠긴 채 돌아온 응답 판독 — 목록 · 상세 공용 앞부분.</summary>
    private static T ReadLockedBody<T>(string gate, string what, BackofficeRoleGateHarness.Outcome res)
    {
        Assert.True(res.Status == 200, $"[{gate}] {what}이 200 이 아니다(실측 {res.Status}). 본문: {Trim(res.Body)}");
        return JsonSerializer.Deserialize<T>(res.Body, Web)!;
    }

    /// <summary>시간 초과 — 잠긴 표를 기다리다 상한에 걸려 끝난다. 잠금을 안 기다렸으면 이 시험의 증거가 아니다.</summary>
    private async Task<BackofficeRoleGateHarness.Outcome> SendUntilTimeoutAsync(string gate, string token, string path)
    {
        var watch = Stopwatch.StartNew();
        var res = await WithinLimitAsync(gate, path, Task.Run(() => _h.SendAsync(token, path)));
        watch.Stop();
        _out.WriteLine($"[{gate}] {path} {res.Status} {watch.ElapsedMilliseconds}ms");
        Assert.True(watch.Elapsed >= LockedCallFloor,
            $"[{gate}] {watch.ElapsedMilliseconds}ms 만에 끝났다 — 잠금을 기다리지 않았다.");
        return res;
    }

    /// <summary>연결 끊김 — 잠긴 표를 기다리는 둘째 문장의 연결을 서버에서 끊는다. 드라이버 연결이 망가진 채 판정이 끝나는 갈래다.</summary>
    private async Task<BackofficeRoleGateHarness.Outcome> SendAndKillAsync(string gate, string token, string path)
    {
        await using var monitor = await _h.OpenAsync();
        var request = Task.Run(() => _h.SendAsync(token, path));
        var watch = Stopwatch.StartNew();

        while (!request.IsCompleted && watch.Elapsed < LockedCallLimit)
        {
            var thread = await monitor.ExecuteScalarAsync<long?>(@"
                SELECT ID FROM information_schema.PROCESSLIST
                WHERE DB = DATABASE() AND ID <> CONNECTION_ID()
                  AND COMMAND <> 'Killed'
                  AND INFO LIKE '%serial_verify_attempts%'
                LIMIT 1");
            if (thread is not null)
            {
                await monitor.ExecuteAsync($"KILL {thread}");
                var res = await WithinLimitAsync(gate, path, request);
                _out.WriteLine($"[{gate}] {path} {res.Status} {watch.ElapsedMilliseconds}ms");
                // 시간 초과로 끝났으면 세션이 살아 있어 끊김 갈래를 잰 것이 아니다.
                Assert.True(watch.Elapsed < LockedCallFloor,
                    $"[{gate}] {watch.ElapsedMilliseconds}ms 만에 끝났다 — 끊기지 않고 시간 초과로 끝났다.");
                return res;
            }
            await Task.Delay(50);
        }

        throw new Xunit.Sdk.XunitException($"[{gate}] 잠금에 걸린 둘째 문장을 못 찾았다 — 끊김 갈래를 재지 못했다.");
    }

    /// <summary>
    /// 재료 표를 잠가 둘째 문장만 세운 뒤 목록 · 상세가 살아 있고 전부 판정 없음 · 결제 정상인지 잰다.
    /// <para>🔴 <c>LOCK TABLES</c> 여야 한다 — InnoDB 일반 읽기는 행 잠금을 안 기다린다. 첫 문장 · 결제 · 인증은 이 표를 안 읽는다.</para>
    /// <para>🔴 <c>UNLOCK</c> 을 직접 부른다 — 안 풀리면 뒷정리 <c>DROP DATABASE</c> 가 멈춘다.</para>
    /// </summary>
    private async Task AssertSurvivesLockedAsync(
        string gate, Func<string, string, Task<BackofficeRoleGateHarness.Outcome>> send)
    {
        if (!await SetUpAsync(gate)) return;
        var seeded = await SeedControlsAsync();
        await SeedPaymentAsync("설치시험A");
        var token = await _h.LoginHqAsync();
        var detailPath = $"{ListPath}/{seeded["A"]}";

        // 대조 — 잠그기 전에는 판정 · 결제 표식이 다 나온다.
        var before = await ReadListAsync(token);
        Assert.True(before.All(r => r.InstallState is not null), $"[{gate}] 잠그기 전부터 판정이 없다.");
        var beforeDetail = ReadLockedBody<DetailBody>(gate, "상세", await _h.SendAsync(token, detailPath));
        Assert.Contains(beforeDetail.Payments, p => p.OrderId == PaymentOrderId);

        await using var holder = await _h.OpenAsync();
        await holder.ExecuteAsync("LOCK TABLES serial_verify_attempts WRITE");
        try
        {
            var after = ReadLockedBody<ListBody>(gate, "목록", await send(token, ListPath)).Items;
            Assert.Equal(before.Count, after.Count);
            foreach (var row in after)
            {
                Assert.True(row.InstallState is null, $"[{gate}] 읽기가 실패했는데 판정이 있다: {row.InstallState}");
                Assert.Equal("확인 불가", TenantInstallText.Label(row.InstallState));
            }

            var detail = ReadLockedBody<DetailBody>(gate, "상세", await send(token, detailPath));
            Assert.Null(detail.Item!.InstallState);
            Assert.Equal("확인 불가", TenantInstallText.Label(detail.Item.InstallState));
            Assert.Contains(detail.Payments, p => p.OrderId == PaymentOrderId);
        }
        finally
        {
            await holder.ExecuteAsync("UNLOCK TABLES");
        }
    }

    [Fact(DisplayName = "G-6ⓓ 🔴 시간 초과 — 재료 표가 잠겨도 목록 · 상세 200 · 확인 불가 · 결제 정상 · 8초 안 (commandTimeout 을 지우면 빨강)")]
    public Task G6_Statement_Timeout() =>
        AssertSurvivesLockedAsync("G-6ⓓ", (token, path) => SendUntilTimeoutAsync("G-6ⓓ", token, path));

    [Fact(DisplayName = "G-6ⓔ 🔴 연결 끊김 — 판정 중 연결이 끊겨도 목록 · 상세 200 · 확인 불가 · 결제 정상 (호출을 결제 조회 앞으로 되돌리면 빨강)")]
    public Task G6_Connection_Killed() =>
        AssertSurvivesLockedAsync("G-6ⓔ", (token, path) => SendAndKillAsync("G-6ⓔ", token, path));

    // ══════════════════════════════════════════════════════════════
    // G-7 순수 함수 — DB 안 탄다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-7 판정식 — 재료 조합별 결과")]
    public void G7_Decide()
    {
        Assert.Equal("installed", TenantInstallStateReader.Decide(OwnerAt, 0, 0));
        Assert.Equal("installed", TenantInstallStateReader.Decide(OwnerAt, 1, 1));
        Assert.Equal("started", TenantInstallStateReader.Decide(null, 1, 0));
        Assert.Equal("started", TenantInstallStateReader.Decide(null, 0, 1));
        Assert.Equal("started", TenantInstallStateReader.Decide(null, 1, 1));
        Assert.Equal("none", TenantInstallStateReader.Decide(null, 0, 0));
    }

    [Fact(DisplayName = "G-7 🔴 낱말 — 값 없음 · 모르는 값은 확인 불가 (미설치로 떨어뜨리면 빨강)")]
    public void G7_Label()
    {
        Assert.Equal("완료", TenantInstallText.Label(TenantInstallStateReader.Installed));
        Assert.Equal("설치 시작", TenantInstallText.Label(TenantInstallStateReader.Started));
        Assert.Equal("미설치", TenantInstallText.Label(TenantInstallStateReader.None));
        Assert.Equal("확인 불가", TenantInstallText.Label(null));
        Assert.Equal("확인 불가", TenantInstallText.Label("모르는값"));
    }

    [Fact(DisplayName = "G-7 시각 — UTC 00:30 → 09:30 · 설치 시작은 시각을 안 쓴다 · 이전 판 안내가 있다")]
    public void G7_Time_And_Help()
    {
        Assert.Equal("2026-11-02 09:30", TenantInstallText.FormatKst(OwnerAt));
        // 날짜가 넘어가는 자리 — 서버 시간대를 타면 여기서 어긋난다.
        Assert.Equal("2026-11-03 08:59", TenantInstallText.FormatKst(new DateTime(2026, 11, 2, 23, 59, 0)));

        var started = TenantInstallText.Help(TenantInstallText.Started, OwnerAt);
        Assert.DoesNotContain("2026", started);
        Assert.Contains("이전 판 프로그램", started);
        // 재료가 업데이트 보고뿐인 고객도 이 문구를 받는다 — 시리얼 번호를 전제하면 틀린 말이 된다.
        Assert.DoesNotContain("시리얼", started);
        var none = TenantInstallText.Help(TenantInstallText.None, null);
        Assert.Contains("본사에 설치 진행 기록이 없습니다", none);
        Assert.Contains("설치했지만 기록이 오지 않은 고객일 수 있습니다", none);
        var installed = TenantInstallText.Help(TenantInstallText.Installed, OwnerAt);
        Assert.Contains("지워도 이 표시는 바뀌지 않습니다", installed);
        // 시각 뒤에 구분이 없으면 한 문장으로 붙는다. 「최근」 을 지금 쓰는지로 읽지 않게 바로 뒤에서 막는다.
        Assert.Contains("2026-11-02 09:30. 이 시각으로 지금 쓰는지는 알 수 없습니다.", installed);
        // 목록은 시각 없이 부른다 — 시각 없는 말이 한계 문장을 잃으면 안 된다.
        Assert.Contains("지워도 이 표시는 바뀌지 않습니다", TenantInstallText.Help(TenantInstallText.Installed, null));
        foreach (var word in new[] { "고객 지원 확인에만", "과금", "정산", "해지", "영업 연락" })
            Assert.Contains(word, TenantInstallText.ListNotice);
    }

    [Fact(DisplayName = "G-7 🔴 금지 낱말 0 — 받은 글자 전부 (설계 §5 쓰지 않는 말)")]
    public void G7_No_Banned_Words()
    {
        var states = new string?[] { "installed", "started", "none", null, "모르는값" };
        var texts = states
            .SelectMany(s => new[]
            {
                TenantInstallText.Label(s),
                TenantInstallText.Help(s, OwnerAt),
                TenantInstallText.Help(s, null),
            })
            .Append(TenantInstallText.ListNotice)
            .ToList();

        var banned = new[]
        {
            "installer-bootstrap", "부트스트랩", "텔레메트리", "워치독", "해시",
            "설치 완료", "설치일", "설치된 날짜",
            "접속 중", "온라인", "켜져 있음", "사용 중",
            "설치되지 않았습니다",
            "AI", "Claude", "Cursor",
        };
        foreach (var text in texts)
        foreach (var word in banned)
            Assert.DoesNotContain(word, text);

        // 설치 시작은 진행 중일 수 있다 — 실패로 단정하지 않는다.
        var started = TenantInstallText.Help(TenantInstallText.Started, null);
        foreach (var word in new[] { "실패", "오류", "중단됨" })
            Assert.DoesNotContain(word, started);
    }

    [Fact(DisplayName = "G-7 구판 조합 — 구판 응답 + 신판 화면 = 확인 불가 · 신판 응답 + 구판 화면 = 깨지지 않는다")]
    public void G7_Old_Shapes()
    {
        var fromOldApi = JsonSerializer.Deserialize<GateRow>("{\"tenantId\":\"x\",\"isLocked\":0}", Web)!;
        Assert.Null(fromOldApi.InstallState);
        Assert.Equal("확인 불가", TenantInstallText.Label(fromOldApi.InstallState));

        var fromNewApi = JsonSerializer.Deserialize<OldScreenRow>(
            "{\"tenantId\":\"x\",\"isLocked\":0,\"installState\":\"installed\",\"lastErpStartAt\":\"2026-11-02T00:30:00\"}", Web)!;
        Assert.Equal("x", fromNewApi.TenantId);
    }

    // ══════════════════════════════════════════════════════════════
    // G-8 규모 — 고객 500 · 시도 5,000행
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-8 🔴 둘째 문장 — 고객 키 인덱스 사용 · 전체 훑기 0 · 5초 안 (ix_result 면 빨강)")]
    public async Task G8_Second_Statement_Plan_And_Time()
    {
        if (!await SetUpAsync("G-8")) return;

        var ids = Enumerable.Range(0, 500).Select(_ => Guid.NewGuid().ToString()).ToList();
        await using var db = await _h.OpenAsync();

        await using (var tx = await db.BeginTransactionAsync())
        {
            await db.ExecuteAsync(@"
                INSERT INTO tenants (tenant_id, tenant_code, company_name, reseller_id, status, subscription_tier)
                VALUES (@id, @code, @nm, @rid, 'active', 'basic')",
                ids.Select((id, i) => new { id, code = $"GL{i:D4}", nm = $"규모시험{i}", rid = _h.ResellerA }), tx);

            await db.ExecuteAsync(@"
                INSERT INTO tenant_update_state (tenant_id_hash, tenant_id, last_result, last_reported_at)
                VALUES (@hash, @id, 'success', UTC_TIMESTAMP(6))",
                ids.Where((_, i) => i % 2 == 0).Select((id, i) => new { hash = $"gate-{i}", id }), tx);

            // 고객당 10행 · 판정 글자는 절반 고객에 1행씩 — 드문 글자일수록 ix_result 가 끌린다.
            await db.ExecuteAsync(@"
                INSERT INTO serial_verify_attempts (tenant_id, submitted_hash, result)
                VALUES (@id, 'gate-load', @result)",
                ids.SelectMany((id, i) => Enumerable.Range(0, 10).Select(j => new
                {
                    id,
                    result = j == 0 && i % 2 == 1 ? "installer-bootstrap" : "ok",
                })), tx);

            await tx.CommitAsync();
        }

        await db.ExecuteAsync("ANALYZE TABLE tenants, tenant_update_state, serial_verify_attempts");

        var plan = (await db.QueryAsync<ExplainRow>("EXPLAIN " + TenantInstallStateReader.ReadSql, new { Ids = ids })).ToList();
        foreach (var p in plan) _out.WriteLine($"[G-8] table={p.Table} type={p.Type} key={p.Key}");

        // 재료 표만 묻는다 — tenants 는 키 목록이 표 거의 전부라 전체 훑기가 정답일 수 있다.
        foreach (var (table, key) in new[] { ("u", "idx_tus_tenant"), ("a", "ix_tenant_attempted") })
        {
            var row = plan.Single(p => p.Table == table);
            Assert.True(row.Type == "ref", $"[G-8] {table} 의 접근 방식이 {row.Type} — ref 여야 한다.");
            Assert.True(row.Key == key, $"[G-8] {table} 의 인덱스가 {row.Key} — {key} 여야 한다. 힌트 즉석 투입 금지 · [2] 로 돌아간다.");
        }

        var watch = Stopwatch.StartNew();
        var facts = (await db.QueryAsync<IdRow>(TenantInstallStateReader.ReadSql, new { Ids = ids })).ToList();
        watch.Stop();
        _out.WriteLine($"[G-8] 둘째 문장 {facts.Count}행 {watch.ElapsedMilliseconds}ms");
        Assert.Equal(ids.Count, facts.Count);
        Assert.True(watch.ElapsedMilliseconds < 5000, $"[G-8] 둘째 문장이 {watch.ElapsedMilliseconds}ms — 5초 상한을 넘었다.");

        // 실물 목록도 그 상한 안에서 판정까지 마친다(넘으면 이 행들이 null 이다).
        var list = await ReadListAsync(await _h.LoginHqAsync());
        Assert.Equal(500, list.Count);   // 목록 문장의 LIMIT 500
        Assert.True(list.All(r => r.InstallState is not null), "[G-8] 판정 없는 행이 있다 — 5초 상한에 걸렸을 수 있다.");
    }
}
