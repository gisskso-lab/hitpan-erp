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
/// <para>재는 것은 둘이다: <b>거짓 완료</b>(시리얼만 통과 · 옛 칸 값)와 <b>거짓 미설치</b>(재료 있음 · 읽기 실패).
/// 실물 적용기 · 실물 JWT · 실물 컨트롤러로 HTTP 요청을 흘리고, 응답을 <b>화면이 읽는 그 형</b>
/// (<see cref="TenantInstallFields"/> 상속)으로 받아 <see cref="TenantInstallText"/> 를 불러 판독한다.
/// 사전으로 읽지 않는다(10/13 <c>attempt_count</c> 사고).</para>
///
/// <para>🔴 <b>CI 자리는 <c>bo-mysql8-gate</c> 잡이다.</b> <c>build</c> 잡은 <c>[SKIP]</c> 으로 조용히 초록이 된다
/// (G-7 은 DB 를 안 타므로 어디서나 돈다). 운영 백오피스는 MySQL 8 이라 CI 에서 MariaDB 에 붙으면 FAIL 한다.</para>
///
/// <para>음성 대조 ⓐ~ⓔ(설계 §7-3)는 CI 계측 가지에서 잰다 — 개발명세서 §4.</para>
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

    // 하니스가 심는 고객사 4행 + 대조군 5행 — 행이 불어나면 이 수가 깨진다.
    private const int HarnessTenants = 4;

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

    private sealed class DetailBody
    {
        public GateRow? Item { get; set; }
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
    /// 대조군 5건 (설계 §7-1). A = 대표 아이디 수집만 · B = 업데이트 보고만(같은 고객 2행) + tenant_id NULL 행 ·
    /// C = 시리얼 통과 3행 · D = 재료 없음 + 옛 칸 값 · E = 재료 없음 + 자기 행에 다른 결과 글자.
    /// </summary>
    private async Task<Dictionary<string, string>> SeedFiveAsync()
    {
        var five = new[] { "A", "B", "C", "D", "E" }.ToDictionary(k => k, _ => Guid.NewGuid().ToString());
        await using var db = await _h.OpenAsync();

        foreach (var (key, id) in five)
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
                    boot = key == "D" ? OwnerAt : (DateTime?)null,
                });
        }

        await db.ExecuteAsync(@"
            INSERT INTO tenant_update_state (tenant_id_hash, tenant_id, last_result, last_reported_at)
            VALUES ('gate-b-1', @b, 'success', UTC_TIMESTAMP(6)),
                   ('gate-b-2', @b, 'success', UTC_TIMESTAMP(6)),
                   ('gate-null', NULL, 'success', UTC_TIMESTAMP(6))",
            new { b = five["B"] });

        await db.ExecuteAsync(@"
            INSERT INTO serial_verify_attempts (tenant_id, submitted_hash, result)
            VALUES (@c, 'gate-c', 'installer-bootstrap'),
                   (@c, 'gate-c', 'installer-bootstrap'),
                   (@c, 'gate-c', 'installer-bootstrap'),
                   (@e, 'gate-e', 'installer-mismatch')",
            new { c = five["C"], e = five["E"] });

        return five;
    }

    private async Task<List<GateRow>> ReadListAsync(string token)
    {
        var res = await _h.SendAsync(token, "/api/backoffice/tenants");
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

    /// <summary>목록을 읽고 대조군 5건의 상세도 읽어 <b>두 화면이 같은 값</b>인지 먼저 본다.</summary>
    private async Task<Dictionary<string, GateRow>> ReadFiveAsync(Dictionary<string, string> five)
    {
        var token = await _h.LoginHqAsync();
        var list = await ReadListAsync(token);
        var byKey = new Dictionary<string, GateRow>();

        foreach (var (key, id) in five)
        {
            var row = list.Single(r => r.TenantId == id);
            var detail = await ReadDetailAsync(token, id);
            Assert.True(row.InstallState == detail.InstallState,
                $"{key}: 목록({row.InstallState}) ≠ 상세({detail.InstallState})");
            Assert.True(row.LastErpStartAt == detail.LastErpStartAt,
                $"{key}: 시각이 목록({row.LastErpStartAt}) ≠ 상세({detail.LastErpStartAt})");
            byKey[key] = row;
        }
        return byKey;
    }

    private static string Trim(string body) => body.Length <= 300 ? body : body[..300];

    // ══════════════════════════════════════════════════════════════
    // 대조군 ①~⑤ — 목록 + 상세
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "설치① 🔴 거짓 미설치 — 옛 칸이 비어 있어도 재료가 있으면 완료 (판정을 IsLocked 로 되돌리면 빨강)")]
    public async Task Control1_Installed_Without_Old_Columns()
    {
        if (!await SetUpAsync("설치①")) return;
        var rows = await ReadFiveAsync(await SeedFiveAsync());

        foreach (var key in new[] { "A", "B" })
        {
            Assert.Equal(0, rows[key].IsLocked);
            Assert.Null(rows[key].BootstrapAt);
            Assert.True(rows[key].InstallState == TenantInstallStateReader.Installed,
                $"{key}: 재료가 있는데 {rows[key].InstallState} — 거짓 미설치다.");
            Assert.Equal("완료", TenantInstallText.Label(rows[key].InstallState));
        }
    }

    [Fact(DisplayName = "설치② 🔴 거짓 완료 — 시리얼만 통과한 고객은 설치 시작이다")]
    public async Task Control2_Serial_Pass_Is_Not_Installed()
    {
        if (!await SetUpAsync("설치②")) return;
        var rows = await ReadFiveAsync(await SeedFiveAsync());

        Assert.True(rows["C"].InstallState == TenantInstallStateReader.Started,
            $"C: 시리얼만 통과했는데 {rows["C"].InstallState} — 거짓 완료다.");
        Assert.Null(rows["C"].LastErpStartAt);
        Assert.Equal("설치 시작", TenantInstallText.Label(rows["C"].InstallState));
    }

    [Fact(DisplayName = "설치③ 양성 — 재료 하나씩: A 는 시각까지 · B 는 시각 없이 완료")]
    public async Task Control3_Each_Material_Alone()
    {
        if (!await SetUpAsync("설치③")) return;
        var rows = await ReadFiveAsync(await SeedFiveAsync());

        Assert.Equal(TenantInstallStateReader.Installed, rows["A"].InstallState);
        Assert.Equal(OwnerAt, rows["A"].LastErpStartAt);
        Assert.Contains("최근 ERP 기동: 2026-11-02 09:30",
            TenantInstallText.Help(rows["A"].InstallState, rows["A"].LastErpStartAt));

        Assert.Equal(TenantInstallStateReader.Installed, rows["B"].InstallState);
        Assert.Null(rows["B"].LastErpStartAt);
        Assert.Contains("최근 ERP 기동 시각은 아직 없습니다",
            TenantInstallText.Help(rows["B"].InstallState, rows["B"].LastErpStartAt));
    }

    [Fact(DisplayName = "설치④ N행 — 1고객 N행 재료가 행을 불리지 않고 전 행이 판정된다")]
    public async Task Control4_Many_Rows_Do_Not_Multiply()
    {
        if (!await SetUpAsync("설치④")) return;
        var five = await SeedFiveAsync();
        var list = await ReadListAsync(await _h.LoginHqAsync());

        Assert.Equal(HarnessTenants + five.Count, list.Count);
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
        var rows = await ReadFiveAsync(await SeedFiveAsync());

        Assert.Equal(1, rows["D"].IsLocked);
        Assert.NotNull(rows["D"].BootstrapAt);
        foreach (var key in new[] { "D", "E" })
        {
            Assert.True(rows[key].InstallState == TenantInstallStateReader.None,
                $"{key}: 재료가 없는데 {rows[key].InstallState}.");
            Assert.Equal("미설치", TenantInstallText.Label(rows[key].InstallState));
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-6 첫 읽기 보호 — 세 갈래
    // ══════════════════════════════════════════════════════════════

    /// <summary>재료를 망가뜨리고 <b>그 오류 번호가 실제로 나는지</b> 먼저 본 뒤, 목록 · 상세가 살아 있고 전부 판정 없음인지 잰다.</summary>
    private async Task AssertSurvivesAsync(string gate, string breakSql, int expectedError)
    {
        if (!await SetUpAsync(gate)) return;
        var five = await SeedFiveAsync();
        var token = await _h.LoginHqAsync();

        // 대조 — 망가뜨리기 전에는 판정이 나온다. 이게 없으면 「전부 null」이 보호의 증거가 아니다.
        var before = await ReadListAsync(token);
        Assert.True(before.All(r => r.InstallState is not null), $"[{gate}] 망가뜨리기 전부터 판정이 없다.");

        await using (var db = await _h.OpenAsync())
        {
            await db.ExecuteAsync(breakSql);
            var ex = await Assert.ThrowsAsync<MySqlException>(() =>
                db.QueryAsync<IdRow>(TenantInstallStateReader.ReadSql, new { Ids = five.Values.ToList() }));
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

        var detail = await ReadDetailAsync(token, five["A"]);
        Assert.Null(detail.InstallState);
        Assert.Equal("확인 불가", TenantInstallText.Label(detail.InstallState));
    }

    [Fact(DisplayName = "G-6ⓐ 🔴 표 없음(1146) — 목록 · 상세 200 · 행 수 불변 · 확인 불가")]
    public Task G6_Table_Missing() =>
        AssertSurvivesAsync("G-6ⓐ", "DROP TABLE tenant_update_state", 1146);

    [Fact(DisplayName = "G-6ⓑ 🔴 칸 없음(1054) — 목록 · 상세 200 · 행 수 불변 · 확인 불가")]
    public Task G6_Column_Missing() =>
        AssertSurvivesAsync("G-6ⓑ", "ALTER TABLE tenants DROP COLUMN owner_collected_at", 1054);

    [Fact(DisplayName = "G-6ⓒ 🔴 문자 규칙 어긋남(1267) — 목록 · 상세 200 · 행 수 불변 · 확인 불가")]
    public Task G6_Collation_Mismatch() =>
        AssertSurvivesAsync("G-6ⓒ",
            "ALTER TABLE serial_verify_attempts MODIFY tenant_id varchar(36) "
          + "CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL",
            1267);

    // ══════════════════════════════════════════════════════════════
    // G-7 순수 함수 — DB 안 탄다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-7 판정식 — 재료 조합별 결과")]
    public void G7_Decide()
    {
        Assert.Equal("installed", TenantInstallStateReader.Decide(OwnerAt, 0, 0));
        Assert.Equal("installed", TenantInstallStateReader.Decide(null, 1, 0));
        Assert.Equal("installed", TenantInstallStateReader.Decide(null, 1, 1));
        Assert.Equal("started", TenantInstallStateReader.Decide(null, 0, 1));
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
        Assert.Contains("기록이 없습니다", TenantInstallText.Help(TenantInstallText.None, null));
        Assert.Contains("지워도 이 표시는 바뀌지 않습니다", TenantInstallText.Help(TenantInstallText.Installed, OwnerAt));
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
            Assert.True(row.Type != "ALL", $"[G-8] {table} 를 전체 훑기 한다.");
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
