using System.Data;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using HitPan.Application.DTOs.Backup;
using HitPan.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-G1-52 갈래 A-2③ — 걸음 ④ · ④-끝 · ⑤ <b>비DB 정적 게이트</b>(작업지시서 §4-1 시험 줄 「G9 · G23 ② · C6 · 문장 허용 검사」 · §6 P3 · §11 D1 · D2 · D5 · D6 · D7).
/// </summary>
/// <remarks>DB 0 · 이 PC 에서 돈다. DB 몫(G4 ~ G7 · G12 · G15 · G16 · G18 · G21 · G22 ①③)은 <c>SafeRestoreSwapGateDbTests</c>(CI db-gate).</remarks>
public sealed class SafeRestoreStaticGateTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Join(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다.");
    }

    private static string Ddl() => File.ReadAllText(Path.Join(RepoRoot(), "installer", "hitpan_db_clean.sql"));

    private static List<string> DdlColumns(string ddl, string table)
    {
        var m = Regex.Match(ddl, $@"CREATE TABLE `{Regex.Escape(table)}` \((?<b>.*?)\n\) ENGINE", RegexOptions.Singleline);
        Assert.True(m.Success, $"출하 DDL 에 {table} 없음");
        return Regex.Matches(m.Groups["b"].Value, @"(?m)^\s*`(?<c>[^`]+)`\s").Select(x => x.Groups["c"].Value).ToList();
    }

    private static string DdlBlock(string ddl, string table) =>
        Regex.Match(ddl, $@"CREATE TABLE `{Regex.Escape(table)}` \((?<b>.*?)\n\) ENGINE", RegexOptions.Singleline).Groups["b"].Value;

    // ══ G9 — 원장 표 교체 함수의 부르는 곳 울타리(IL · 반사) ══

    private static readonly Dictionary<short, OpCode> OpCodes1 = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);

    /// <summary>제품 어셈블리에서 <paramref name="target"/> 의 메서드(생성자 뺌)를 부르는 자리 — 「부르는 형(겉 형) → 부른 메서드」.</summary>
    internal static List<(Type Caller, string Callee)> CallSites(Type target, params Assembly[] assemblies)
    {
        var hits = new List<(Type, string)>();
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var asm in assemblies)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }
            foreach (var t in types)
            {
                foreach (var m in t.GetMethods(all).Cast<MethodBase>().Concat(t.GetConstructors(all)))
                {
                    byte[]? il;
                    try { il = m.GetMethodBody()?.GetILAsByteArray(); }
                    catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException) { continue; }
                    if (il is null) continue;
                    var i = 0;
                    while (i < il.Length)
                    {
                        short code = il[i++];
                        if (code == 0xFE) code = (short)(0xFE00 | il[i++]);
                        if (!OpCodes1.TryGetValue(code, out var op)) break;
                        switch (op.OperandType)
                        {
                            case OperandType.InlineNone: break;
                            case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: i += 1; break;
                            case OperandType.InlineVar: i += 2; break;
                            case OperandType.InlineI8: case OperandType.InlineR: i += 8; break;
                            case OperandType.InlineSwitch: i += 4 + 4 * BitConverter.ToInt32(il, i); break;
                            case OperandType.InlineMethod:
                                var token = BitConverter.ToInt32(il, i);
                                i += 4;
                                MethodBase? callee = null;
                                try { callee = m.Module.ResolveMethod(token, t.IsGenericType ? t.GetGenericArguments() : null, m.IsGenericMethod ? m.GetGenericArguments() : null); }
                                catch (Exception ex) when (ex is ArgumentException or BadImageFormatException) { callee = null; }
                                if (callee is not null && callee.DeclaringType == target && callee is MethodInfo)
                                {
                                    var outer = t;
                                    while (outer.DeclaringType is not null) outer = outer.DeclaringType;
                                    hits.Add((outer, callee.Name));
                                }
                                break;
                            default: i += 4; break;
                        }
                    }
                }
            }
        }
        return hits;
    }

    private static Assembly[] ProductAssemblies() =>
    [
        typeof(RestoreSwapStep).Assembly,                              // Application
        typeof(HitPan.API.Services.RestoreBusyProbe).Assembly,          // API
        typeof(HitPan.Infrastructure.Services.LocalAuditFileWriter).Assembly,   // Infrastructure
    ];

    [Fact(DisplayName = "G9 🔴 원장 표 교체(RestoreSwapStep ④ · ④-끝 · ⑤)를 부르는 곳 = 그 클래스 안뿐(제품 어셈블리 · 깃발 닫힌 채 제품 길 0) · 훑개 양성 대조")]
    public void G9_교체함수_부르는곳_울타리()
    {
        var hits = CallSites(typeof(RestoreSwapStep), ProductAssemblies());
        // 양성 대조 — 훑개가 RunAsync → SwapAsync 를 잡는가(0건 no-op 초록 막음)
        Assert.Contains(hits, h => h.Caller == typeof(RestoreSwapStep) && h.Callee == nameof(RestoreSwapStep.SwapAsync));
        var outside = hits.Where(h => h.Caller != typeof(RestoreSwapStep)).Select(h => $"{h.Caller.FullName} → {h.Callee}").Distinct().ToList();
        Assert.True(outside.Count == 0, "🔴 교체 함수를 복원 서비스 밖에서 부름(G9):\n" + string.Join("\n", outside));

        // 교체 문장 만들기 · 허용 검사도 같은 울타리(RestoreSwapPlan 은 RestoreSwapStep 만)
        var plan = CallSites(typeof(RestoreSwapPlan), ProductAssemblies())
            .Where(h => h.Caller != typeof(RestoreSwapStep) && h.Caller != typeof(RestoreSwapPlan)).Select(h => $"{h.Caller.FullName} → {h.Callee}").Distinct().ToList();
        Assert.True(plan.Count == 0, "🔴 교체 문장 만들기를 복원 서비스 밖에서 부름:\n" + string.Join("\n", plan));
    }

    // ══ 문장 허용 검사(⑤ 거래 안 · 본 §15-3 ⑤ R5 · G6 「거래 안 DDL 1줄 끼움」) ══

    private static readonly RestoreSwapTable Partners = new("partners", ["partner_id", "tenant_id", "partner_name"], true);
    private static readonly RestoreSwapTable Contacts = new("partner_contacts", ["contact_id", "tenant_id", "partner_id"], true);
    private static readonly RestoreSwapTable Lines = new("purchase_order_items", ["item_line_id", "po_id"], false);

    [Fact(DisplayName = "⑤ 문장 허용 — 만든 지우기 · 붓기 · 기록 1줄 · C1′ 잠그며 읽기 · 격리 읽기 = 통과")]
    public void Swap_허용문장_통과()
    {
        var order = RestoreSwapPlan.Build([Partners, Contacts, Lines], [("partner_contacts", "partners")]);
        Assert.NotNull(order);
        foreach (var s in order!.DeleteStatements.Concat(order.InsertStatements)) RestoreSwapStatementCheck.Verify(s);
        foreach (var s in RestoreSwapStatementCheck.KnownReads) RestoreSwapStatementCheck.Verify(s);
        RestoreSwapStatementCheck.Verify(RestoreSwapSql.MarkHistorySuccess);
        Assert.Contains("DELETE FROM `purchase_order_items`", order.DeleteStatements);   // 회사 표지 칸 없는 표 = 통째
        Assert.Contains("DELETE FROM `partners` WHERE (tenant_id = @t OR tenant_id IS NULL)", order.DeleteStatements);
    }

    [Theory(DisplayName = "G6 🔴 ⑤ 거래 안 허용 목록 밖 = 멈춤(DDL · TRUNCATE · 둘째 문장 · 주석 · 지금 값 표 · 임시 표 지움 · 칸 짝 다름 · 다른 원본)")]
    [InlineData("ALTER TABLE `partners` ADD COLUMN x int")]
    [InlineData("TRUNCATE TABLE `partners`")]
    [InlineData("CREATE TABLE `x` (id int)")]
    [InlineData("DROP TABLE `partners`")]
    [InlineData("RENAME TABLE `partners` TO `p2`")]
    [InlineData("SET FOREIGN_KEY_CHECKS = 0")]
    [InlineData("LOCK TABLES `partners` WRITE")]
    [InlineData("DELETE FROM `partners`; DROP TABLE `users`")]
    [InlineData("DELETE FROM `partners` -- x")]
    [InlineData("DELETE FROM `partners` /* x */")]
    [InlineData("DELETE FROM `users`")]
    [InlineData("DELETE FROM `refresh_tokens`")]
    [InlineData("DELETE FROM `rs_new__partners`")]
    [InlineData("DELETE FROM `partners` WHERE tenant_id = 'other'")]
    [InlineData("INSERT INTO `users` (`user_id`) SELECT `user_id` FROM `rs_new__users`")]
    [InlineData("INSERT INTO `partners` (`partner_id`) SELECT `partner_name` FROM `rs_new__partners`")]
    [InlineData("INSERT INTO `partners` (`partner_id`) SELECT `partner_id` FROM `rs_new__items`")]
    [InlineData("UPDATE `partners` SET partner_name = 'x'")]
    [InlineData("UPDATE restore_history SET status = 'success'")]
    public void Swap_허용밖_멈춤(string sql)
    {
        Assert.Throws<RestoreSwapRejectedException>(() => RestoreSwapStatementCheck.Verify(sql));
    }

    [Fact(DisplayName = "⑤ 교체 차례 — 붓기 부모 먼저 · 지우기 자식 먼저 · 고리 · 자기 참조 · 꼴 밖 이름 = 차례 안 섬(null)")]
    public void Swap_차례()
    {
        var order = RestoreSwapPlan.Build([Contacts, Partners], [("partner_contacts", "partners")])!;
        var ins = order.InsertStatements.ToList();
        Assert.True(ins.FindIndex(s => s.Contains("INSERT INTO `partners`", StringComparison.Ordinal))
                    < ins.FindIndex(s => s.Contains("INSERT INTO `partner_contacts`", StringComparison.Ordinal)));
        var del = order.DeleteStatements.ToList();
        Assert.True(del.FindIndex(s => s.Contains("`partner_contacts`", StringComparison.Ordinal))
                    < del.FindIndex(s => s.Contains("`partners`", StringComparison.Ordinal)));
        Assert.Null(RestoreSwapPlan.Build([Partners, Contacts], [("partners", "partner_contacts"), ("partner_contacts", "partners")]));
        Assert.Null(RestoreSwapPlan.Build([Partners], [("partners", "partners")]));
        Assert.Null(RestoreSwapPlan.Build([new RestoreSwapTable("a.b", ["x"], false)], []));
        Assert.Null(RestoreSwapPlan.Build([new RestoreSwapTable("partners", ["x`y"], false)], []));
        Assert.DoesNotContain(order.DeleteStatements.Concat(order.InsertStatements), s => s.Contains("TRUNCATE", StringComparison.OrdinalIgnoreCase));
    }

    // ══ C6 · #2 · N1-b — SQL 글 ══

    private static IEnumerable<(string Name, string Sql)> SwapSqlTexts()
    {
        foreach (var f in typeof(RestoreSwapSql).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(string)))
            yield return (f.Name, (string)f.GetValue(null)!);
        foreach (var p in typeof(RestoreSwapSql).GetProperties(BindingFlags.Public | BindingFlags.Static).Where(p => p.PropertyType == typeof(string)))
            yield return (p.Name, (string)p.GetValue(null)!);
        yield return ("ActiveAccount", RestoreOverlayRules.ActiveAccount("u"));
        yield return ("LiveOnlyActiveLinkedWhere", RestoreOverlayRules.LiveOnlyActiveLinkedWhere("l"));
        yield return ("BPrimeExemption", RestoreOverlayRules.BPrimeExemption("l"));
    }

    [Fact(DisplayName = "C6 🔴 덧씌움 · 대조 · 교체 SQL 안 `!=` · `<>` 0 · 회사 값 = @t 하나(글자 회사 값 0) · 읽은 글 수 ≥ 30(no-op 초록 막음)")]
    public void C6_비교연산자_회사값()
    {
        var texts = SwapSqlTexts().ToList();
        Assert.True(texts.Count >= 30, $"SQL 글을 {texts.Count}개만 읽었다");
        var bad = texts.Where(t => t.Sql.Contains("!=", StringComparison.Ordinal) || t.Sql.Contains("<>", StringComparison.Ordinal)).Select(t => t.Name).ToList();
        Assert.True(bad.Count == 0, "🔴 !=/<> 가 든 SQL: " + string.Join(", ", bad));
        var lit = texts.Where(t => Regex.IsMatch(t.Sql, @"tenant_id\s*=\s*'")).Select(t => t.Name).ToList();
        Assert.True(lit.Count == 0, "🔴 글자 회사 값: " + string.Join(", ", lit));
        // 쓰는 SQL 대상은 rs_new__ · restore_history 뿐(라이브 「백업 값」 표 쓰기 = ⑤ 만든 문장만)
        var writes = texts.Where(t => Regex.IsMatch(t.Sql, @"^\s*(UPDATE|INSERT|DELETE)\b", RegexOptions.IgnoreCase))
            .Where(t => !Regex.IsMatch(t.Sql, @"^\s*(UPDATE|INSERT INTO|DELETE FROM)\s+(rs_new__|restore_history\b)", RegexOptions.IgnoreCase))
            .Select(t => t.Name).ToList();
        Assert.True(writes.Count == 0, "🔴 rs_new__ · restore_history 밖에 쓰는 SQL: " + string.Join(", ", writes));
    }

    [Fact(DisplayName = "N1-b 🔴 ⓑ′ 단서 상수 한 곳 — ④-끝 ③ 확정과 ⑤ C1′ ⓓ 가 같은 조각 · 「같은 U」 · 같은 회사 · n2.is_active 없음(CTO ⚪1)")]
    public void N1b_단서상수_한곳()
    {
        var bp = RestoreOverlayRules.BPrimeExemption("l");
        Assert.Contains("n2.tenant_id = l.tenant_id", bp, StringComparison.Ordinal);
        Assert.Contains("n2.user_id = l.user_id", bp, StringComparison.Ordinal);
        Assert.DoesNotContain("is_active", bp, StringComparison.Ordinal);
        Assert.Contains(bp, RestoreSwapSql.ListNewerAccountsAfterBPrime, StringComparison.Ordinal);
        Assert.Contains(bp, RestoreSwapSql.C1EmployeesNewerLinked, StringComparison.Ordinal);
        var where = RestoreOverlayRules.LiveOnlyActiveLinkedWhere("l");
        Assert.Contains(where, RestoreSwapSql.CountBCandidates, StringComparison.Ordinal);
        Assert.DoesNotContain(bp, RestoreSwapSql.CountBCandidates, StringComparison.Ordinal);   // ④ 은 후보만(ⓑ′ 판정 전)
        Assert.Contains(RestoreOverlayRules.ActiveAccount("ux"), where, StringComparison.Ordinal);
        Assert.Contains(RestoreOverlayRules.ActiveAccount("u"), RestoreSwapSql.LinkBackupOnly, StringComparison.Ordinal);
        Assert.Contains("l.tenant_id = @t", where, StringComparison.Ordinal);
    }

    // ══ G13 덧 · C2 · Z1 — 덧씌움 상수 ↔ 출하 DDL ══

    [Fact(DisplayName = "G13 덧 🔴 덧씌움 8칸 = D1(사원 7 + 방 1) · 칸 · 열쇠가 출하 DDL 에 실재 · 두 표 = 백업 값 · delegate_id 0 · 덧씌움 SQL 이 칸마다 n.c = l.c")]
    public void G13덧_덧씌움상수_DDL()
    {
        Assert.Equal(["role", "is_active", "user_id", "login_id", "is_resigned", "resign_date", "resign_reason"], RestoreOverlayRules.EmployeeOverlayColumns);
        Assert.Equal(["left_at"], RestoreOverlayRules.ChatMemberOverlayColumns);
        Assert.Equal(8, RestoreOverlayRules.OverlayColumnCount);
        var ddl = Ddl();
        var emp = DdlColumns(ddl, "employees");
        var chat = DdlColumns(ddl, "chat_room_members");
        var users = DdlColumns(ddl, "users");
        foreach (var c in RestoreOverlayRules.EmployeeOverlayColumns.Concat(RestoreOverlayRules.EmployeeKey)) Assert.Contains(c, emp);
        foreach (var c in RestoreOverlayRules.ChatMemberOverlayColumns.Concat(RestoreOverlayRules.ChatMemberKey)) Assert.Contains(c, chat);
        foreach (var c in new[] { "user_id", "tenant_id", "email", "is_active", "is_deleted", "user_name" }) Assert.Contains(c, users);
        Assert.Equal(RestoreTableClass.FromBackup, RestoreTableClassification.Classify(RestoreOverlayRules.EmployeesTable));
        Assert.Equal(RestoreTableClass.FromBackup, RestoreTableClassification.Classify(RestoreOverlayRules.ChatMembersTable));
        Assert.DoesNotContain("delegate_id", RestoreOverlayRules.EmployeeOverlayColumns);
        foreach (var c in RestoreOverlayRules.EmployeeOverlayColumns) Assert.Contains($"n.{c} = l.{c}", RestoreSwapSql.OverlayEmployees, StringComparison.Ordinal);
        Assert.Contains("n.left_at = l.left_at", RestoreSwapSql.OverlayChatMembers, StringComparison.Ordinal);
        Assert.Contains("UNIQUE KEY `uq_employees_tenant_user` (`tenant_id`,`user_id`)", DdlBlock(ddl, "employees"), StringComparison.Ordinal);
        Assert.Contains("UNIQUE KEY `uq_chat_member` (`room_id`,`employee_id`)", DdlBlock(ddl, "chat_room_members"), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "G13 덧 C2 🔴 사원 · 방 참여 · 결재 두 표 — 출하 DDL 외래키 0(자식 · 부모 어디에도) · 트리거 0 (생기면 고아 검사를 덧씌운 뒤로 옮김)")]
    public void G13덧_C2_외래키트리거0()
    {
        var ddl = Ddl();
        string[] four = ["employees", "chat_room_members", "approval_doc_lines", "approval_documents"];
        var fks = RestoreTableClassificationGateTests.CleanDdlForeignKeys(ddl);
        Assert.True(fks.Count > 0, "외래키를 하나도 못 읽었다(읽기 깨짐)");
        var touching = fks.Where(f => four.Contains(f.Child) || four.Contains(f.Parent)).Select(f => $"{f.Child}→{f.Parent}").ToList();
        Assert.True(touching.Count == 0, "🔴 덧씌움 · (가′) 표에 외래키: " + string.Join(", ", touching));
        var trig = RestoreTableClassificationGateTests.TriggerTables(ddl);
        Assert.DoesNotContain(trig, t => four.Contains(t));
    }

    [Fact(DisplayName = "P3-1 🔴 「백업 값 자식 → 지금 값 부모」 외래키 = 아는 목록(ai_conversations · esign_records → users) · 새로 생기면 빨강")]
    public void P3_1_경계외래키_아는목록()
    {
        var edges = RestoreTableClassificationGateTests.CleanDdlForeignKeys(Ddl())
            .Where(f => RestoreTableClassification.Classify(f.Child) == RestoreTableClass.FromBackup
                        && RestoreTableClassification.Classify(f.Parent) == RestoreTableClass.KeepCurrent)
            .Select(f => $"{f.Child}->{f.Parent}").Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(["ai_conversations->users", "esign_records->users"], edges);
    }

    [Fact(DisplayName = "P3-2 🔴 옛 마이그의 REFERENCES 도 셈 — 「지금 값 자식 → 백업 값 부모」 0 · 읽은 외래키 ≥ 1(no-op 막음)")]
    public void P3_2_옛마이그_외래키()
    {
        var dir = Path.Join(RepoRoot(), "src", "HitPan.API", "Migrations", "SQL");
        var found = 0;
        var bad = new List<string>();
        foreach (var f in Directory.GetFiles(dir, "*.sql").OrderBy(x => x, StringComparer.Ordinal))
        {
            var text = RestoreTableClassificationGateTests.StripComments(File.ReadAllText(f));
            foreach (var stmt in text.Split(';'))
            {
                var owner = Regex.Match(stmt, @"\b(?:CREATE\s+TABLE(?:\s+IF\s+NOT\s+EXISTS)?|ALTER\s+TABLE)\s+`?(?<c>[A-Za-z0-9_]+)`?", RegexOptions.IgnoreCase);
                if (!owner.Success) continue;
                foreach (Match r in Regex.Matches(stmt, @"\bREFERENCES\s+`?(?<p>[A-Za-z0-9_]+)`?", RegexOptions.IgnoreCase))
                {
                    found++;
                    var child = owner.Groups["c"].Value.ToLowerInvariant();
                    var parent = r.Groups["p"].Value.ToLowerInvariant();
                    if (RestoreTableClassification.Classify(child) == RestoreTableClass.KeepCurrent
                        && RestoreTableClassification.Classify(parent) == RestoreTableClass.FromBackup)
                        bad.Add($"{Path.GetFileName(f)}: {child}→{parent}");
                }
            }
        }
        Assert.True(found >= 1, "옛 마이그에서 REFERENCES 를 하나도 못 읽었다");
        Assert.True(bad.Count == 0, "🔴 옛 마이그: 지금 값 자식 → 백업 값 부모 외래키\n" + string.Join("\n", bad));
    }

    // ══ D7 — G13 음성 대조 상시 · 지금 값 목록 고정 ══

    [Fact(DisplayName = "D7 🔴 G13 ⓒ 음성 대조 상시 — 「지금 값 자식 → 백업 값 부모」 가짜 외래키를 판정이 잡는다 · 지금 값 핵심 표가 백업 값으로 옮겨지면 빨강")]
    public void D7_음성대조_상시()
    {
        static List<string> KeepToBackup(IEnumerable<(string Child, string Parent)> e) => e
            .Where(f => RestoreTableClassification.Classify(f.Child) == RestoreTableClass.KeepCurrent
                        && RestoreTableClassification.Classify(f.Parent) == RestoreTableClass.FromBackup)
            .Select(f => $"{f.Child}->{f.Parent}").ToList();
        var real = RestoreTableClassificationGateTests.CleanDdlForeignKeys(Ddl());
        Assert.Empty(KeepToBackup(real));
        Assert.Single(KeepToBackup(real.Append(("users", "departments"))));   // 음성 대조 — 판정이 살아 있음
        string[] pinned =
        [
            "users", "user_permissions", "refresh_tokens", "user_sessions", "local_company", "backup_history", "restore_history",
            "backup_settings", "schema_migrations", "licenses", "tenant_employees_snapshot", "tenant_devices_snapshot", "audit_logs",
            "local_update_consents", "local_update_status", "cs_requests", "cs_outbox",
        ];
        var moved = pinned.Where(t => RestoreTableClassification.Classify(t) != RestoreTableClass.KeepCurrent).ToList();
        Assert.True(moved.Count == 0, "🔴 지금 값 핵심 표가 지금 값이 아님: " + string.Join(", ", moved));
    }

    // ══ D6 — G13-S11 임시 표 틀 동작 제외 ══

    [Fact(DisplayName = "D6 🔴 G13-S11 임시 표 틀 제외 = 동작으로(변환기가 실제로 rs_new__ 만 만듦) · 음성 대조(접두 아닌 이름 · 다른 자리표 · rs_old__ 는 안 뺌)")]
    public void D6_임시표틀_동작제외()
    {
        Assert.True(RestoreTableClassificationGateTests.StagingTemplateProven.Value);
        Assert.True(RestoreTableClassificationGateTests.IsStagingTemplate("`{Prefix}{name}`"));
        Assert.True(RestoreTableClassificationGateTests.IsStagingTemplate("`rs_new__x`"));
        Assert.False(RestoreTableClassificationGateTests.IsStagingTemplate("`{Other}{name}`"));
        Assert.False(RestoreTableClassificationGateTests.IsStagingTemplate("`rs_old__x`"));
        Assert.False(RestoreTableClassificationGateTests.IsStagingTemplate("`employees`"));
        var (names, unreadable, _) = RestoreTableClassificationGateTests.CsCreatedTables("var s = $\"CREATE TABLE `{Other}{n}` (\";");
        Assert.Single(unreadable);   // 접두 아닌 자리표 = 여전히 빨강 쪽
        Assert.Empty(names);
        var conv = File.ReadAllText(Path.Join(RepoRoot(), "src", "HitPan.Application", "Services", "RestoreDumpConverter.cs"));
        Assert.Contains("$\"CREATE TABLE `{Prefix}{name}` (", conv, StringComparison.Ordinal);   // 틀 = 글 그대로(낱말 나눔 0)
    }

    // ══ P3-8 — 이름 읽기 도우미 점(.) ══

    private static readonly Regex DottedCreateOrRename = new(
        @"\b(?:CREATE\s+(?:OR\s+REPLACE\s+)?(?:TEMPORARY\s+)?TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?|RENAME\s+TABLES?\s+[^;]*?\bTO\s+)`?[A-Za-z0-9_]+`?\.`?[A-Za-z0-9_]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Fact(DisplayName = "P3-8 🔴 출하 DDL · 마이그의 CREATE TABLE · RENAME … TO 이름에 점(db.표) 0 — 있으면 읽기 도우미가 앞 이름만 읽어 놓침 · 자기 시험")]
    public void P3_8_점이름_빨강()
    {
        Assert.Matches(DottedCreateOrRename, "CREATE TABLE `other`.`t` (id int)");
        Assert.Matches(DottedCreateOrRename, "RENAME TABLE a TO other.b");
        Assert.DoesNotMatch(DottedCreateOrRename, "CREATE TABLE `t` (id decimal(18,2))");
        var bad = new List<string>();
        if (DottedCreateOrRename.IsMatch(RestoreTableClassificationGateTests.StripComments(Ddl()))) bad.Add("hitpan_db_clean.sql");
        foreach (var f in Directory.GetFiles(Path.Join(RepoRoot(), "src", "HitPan.API", "Migrations", "SQL"), "*.sql"))
            if (DottedCreateOrRename.IsMatch(RestoreTableClassificationGateTests.StripComments(File.ReadAllText(f)))) bad.Add(Path.GetFileName(f));
        Assert.True(bad.Count == 0, "🔴 점 붙은 표 이름: " + string.Join(", ", bad));
    }

    // ══ D1 · P3-9 — 고정 문구 표 ══

    [Fact(DisplayName = "P3-9 🔴 모든 DenyCode 에 고정 문구 한 줄 · 모르는 글자는 되풀이 0 · 개발 용어 0 · D1 문구 = 대표 결재 글 · 이름 5명 넘으면 「외 n명」")]
    public void P3_9_고정문구표()
    {
        var codes = typeof(SafeRestoreDenyCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!).ToList();
        Assert.True(codes.Count >= 20, $"DenyCode 를 {codes.Count}개만 읽었다");
        var missing = codes.Where(c => !RestoreOverlayRules.Messages.ContainsKey(c)).ToList();
        Assert.True(missing.Count == 0, "🔴 고정 문구 없는 DenyCode: " + string.Join(", ", missing));
        string[] jargon = ["SQL", "DB", "rs_new", "exception", "Exception", "null", "tenant", "restore_history", "트랜잭션", "테이블"];
        var dev = RestoreOverlayRules.Messages.Where(kv => jargon.Any(j => kv.Value.Contains(j, StringComparison.Ordinal))).Select(kv => kv.Key).ToList();
        Assert.True(dev.Count == 0, "🔴 개발 용어 든 문구: " + string.Join(", ", dev));
        Assert.Equal(RestoreOverlayRules.UnknownMessage, RestoreOverlayRules.MessageFor("C:\\secret\\path<script>"));
        Assert.Equal("초기화 뒤에 만든 직원 계정 {0}개({1})가 그 백업에 없습니다 — 그 계정을 [멈춤] 한 뒤 다시 되살리면 됩니다",
            RestoreOverlayRules.NewerAccountsMissingTemplate);
        var six = RestoreOverlayRules.MessageFor(SafeRestoreDenyCodes.NewerAccountsMissing, ["가", "나", "다", "라", "마", "바"]);
        Assert.Equal("초기화 뒤에 만든 직원 계정 6개(가, 나, 다, 라, 마 외 1명)가 그 백업에 없습니다 — 그 계정을 [멈춤] 한 뒤 다시 되살리면 됩니다", six);
        Assert.DoesNotContain("더 최근 백업을 고르세요", six, StringComparison.Ordinal);   // D1 — 옛 문구 쓰지 않음
        Assert.Equal(RestoreOverlayRules.DataChangedMessage, RestoreOverlayRules.MessageFor(SafeRestoreDenyCodes.VerifyFailed, dataChanged: true));
    }

    [Fact(DisplayName = "P3-9 🔴 제품 코드가 restore_history.error_message · 예외 글을 안전 복원 응답 Message 로 옮기는 줄 0")]
    public void P3_9_오류글_옮기기0()
    {
        var src = Path.Join(RepoRoot(), "src");
        var hits = new List<string>();
        foreach (var f in Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, f).Replace('\\', '/');
            if (rel.Contains("Tests", StringComparison.Ordinal) || rel.Contains("/bin/", StringComparison.Ordinal) || rel.Contains("/obj/", StringComparison.Ordinal)) continue;
            var text = File.ReadAllText(f);
            if (!text.Contains("SafeRestore", StringComparison.Ordinal) && !text.Contains("RestoreSwapResult", StringComparison.Ordinal)) continue;
            foreach (Match m in Regex.Matches(text, @"\bMessage\s*=\s*[^;,\n]*(ErrorMessage|error_message|ex\.Message|LogDetail)"))
                hits.Add($"{rel}: {m.Value}");
        }
        Assert.True(hits.Count == 0, "🔴 오류 글을 응답 문구로 옮김:\n" + string.Join("\n", hits));
    }

    // ══ G22 ① ② — 무른 참조 두 목록(비DB · 출하 DDL) ══

    [Fact(DisplayName = "G22 ① 🔴 지금 값 표의 _id · _code · _no · _by · _key 칸(tenant_id 뺌) 전부가 두 목록 중 정확히 하나 · 목록 밖 0 · 겹침 0 · 없는 칸 0")]
    public void G22_1_두목록_정확히하나()
    {
        var ddl = Ddl();
        var actual = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match tm in Regex.Matches(ddl, @"CREATE TABLE `(?<t>[^`]+)` \((?<b>.*?)\n\) ENGINE", RegexOptions.Singleline))
        {
            var t = tm.Groups["t"].Value;
            if (RestoreTableClassification.Classify(t) != RestoreTableClass.KeepCurrent) continue;
            foreach (Match cm in Regex.Matches(tm.Groups["b"].Value, @"(?m)^\s*`(?<c>[^`]+)`\s"))
            {
                var c = cm.Groups["c"].Value;
                if (c == "tenant_id") continue;
                if (Regex.IsMatch(c, "(_id|_code|_no|_by|_key)$")) actual.Add($"{t}.{c}");
            }
        }
        Assert.True(actual.Count >= 100, $"칸을 {actual.Count}개만 읽었다");
        var soft = RestoreTableClassification.SoftReferences;
        var not = RestoreTableClassification.NotReferences;
        Assert.Equal(soft.Length, soft.Distinct().Count());
        Assert.Equal(not.Length, not.Distinct().Count());
        var both = soft.Intersect(not).ToList();
        Assert.True(both.Count == 0, "🔴 두 목록에 다 있음: " + string.Join(", ", both));
        var listed = soft.Concat(not).ToHashSet(StringComparer.Ordinal);
        var outside = actual.Where(x => !listed.Contains(x)).ToList();
        Assert.True(outside.Count == 0, "🔴 목록 밖 칸(백업 값 행을 가리키는지 보고 한 목록에 더함): " + string.Join(", ", outside));
        var ghost = listed.Where(x => !actual.Contains(x)).ToList();
        Assert.True(ghost.Count == 0, "🔴 출하 DDL 에 없는 칸: " + string.Join(", ", ghost));
        Assert.Contains("tenant_etax_settings.notify_manager_id", soft);
        Assert.Equal(7, soft.Length);
    }

    [Fact(DisplayName = "G22 ② 🔴 무른 참조 C2 · C3(users.dept_id · tenant_etax_settings.notify_manager_id)을 읽는 제품 코드 0 — 읽기 시작하면 끊김 처리 다시 봄")]
    public void G22_2_읽는코드0()
    {
        var src = Path.Join(RepoRoot(), "src");
        var hits = new List<string>();
        var read = 0;
        foreach (var f in Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories).Concat(Directory.GetFiles(src, "*.razor", SearchOption.AllDirectories)))
        {
            var rel = Path.GetRelativePath(src, f).Replace('\\', '/');
            if (rel.Contains("Tests", StringComparison.Ordinal) || rel.Contains("/bin/", StringComparison.Ordinal) || rel.Contains("/obj/", StringComparison.Ordinal)
                || rel.StartsWith("HitPan.Backoffice", StringComparison.Ordinal) || rel.StartsWith("HitPan.Landing", StringComparison.Ordinal)) continue;
            if (rel.EndsWith("RestoreTableClassification.cs", StringComparison.Ordinal)) continue;   // 목록 자체
            read++;
            var lines = File.ReadAllLines(f);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimStart();
                if (line.StartsWith("//", StringComparison.Ordinal)) continue;
                if (Regex.IsMatch(lines[i], @"\bu\.dept_id\b|\busers\.dept_id\b|\bnotify_manager_id\b|\bNotifyManagerId\b"))
                    hits.Add($"{rel}:{i + 1}");
            }
        }
        Assert.True(read >= 100, $"제품 파일을 {read}개만 읽었다");
        // 지금 아는 자리 = EF 속성 선언(User.DeptId · 매핑)뿐 — 읽는 SQL · 화면 0. 선언 줄은 아래 허용(늘면 빨강)
        var allowed = hits.Where(h => !h.Contains("/Entities/", StringComparison.Ordinal) && !h.Contains("Configuration", StringComparison.Ordinal)).ToList();
        Assert.True(allowed.Count == 0, "🔴 무른 참조 칸을 읽는 코드: " + string.Join(", ", allowed));
    }

    // ══ D5 · D2 · D3 ══

    [Fact(DisplayName = "D5 🔴 깃발 닫힘 응답 DenyCode = restore_closed(null 아님) · 자료 그대로 · 문구 = 닫힘 고정 글")]
    public async Task D5_닫힘글자()
    {
        await using var conn = new MySqlConnection("Server=127.0.0.1;Port=1;Database=x;User=x;Connection Timeout=1");
        var svc = new BackupService(conn, NullLogger<BackupService>.Instance);
        var r = await svc.SafeRestoreAsync("t", "u", new SafeRestoreRequest());
        Assert.False(r.Success);
        Assert.Equal(SafeRestoreDenyCodes.RestoreClosed, r.DenyCode);
        Assert.True(r.DataUnchanged);
        Assert.Equal(RestoreOverlayRules.ClosedMessage, r.Message);
        Assert.Equal(ConnectionState.Closed, conn.State);   // DB 에 닿지 않음
    }

    [Fact(DisplayName = "D2 🔴 ④-끝 · ⑤ 거래 격리 = REPEATABLE READ 명시(상수) · 기대 글자 · D3 시각 = 주입한 지역 시각(UTC 아님)")]
    public void D2_D3_상수()
    {
        Assert.Equal(IsolationLevel.RepeatableRead, RestoreSwapStep.SwapIsolation);
        Assert.Equal("REPEATABLE-READ", RestoreSwapStep.ExpectedIsolation);
        var local = new DateTime(2026, 11, 28, 15, 0, 0, DateTimeKind.Local);
        var step = new RestoreSwapStep(new MySqlConnection(), null!, null!, NullLogger<RestoreSwapStep>.Instance, localNow: () => local);
        Assert.Equal(local, step.LocalNow());
        var src = File.ReadAllText(Path.Join(RepoRoot(), "src", "HitPan.Application", "Services", "RestoreSwapStep.cs"));
        Assert.DoesNotContain("UtcNow", src, StringComparison.Ordinal);   // restore_history 에 UTC 로 쓰는 길 0
    }

    // ══ A-2③ 고침 1 — P3-5 · P3-9 동작 · P3-1 그물 · D14 (비DB) ══

    [Fact(DisplayName = "P3-5 🔴 ⓐ″ 다시 셈의 활성 계정 조각(따로 적은 글) = 상수 ActiveAccount(\"u\") 와 같은 뜻(빈칸 정규화) — 표식이 늘어 한쪽만 바뀌면 빨강")]
    public void P3_5_활성조각_같음()
    {
        static string N(string s) => Regex.Replace(s, @"\s+", " ");
        var copy = N(RestoreSwapSql.CountBackupOnlyLinkMismatch);
        var active = N(RestoreOverlayRules.ActiveAccount("u"));
        Assert.Contains("u.is_deleted = 0", active, StringComparison.Ordinal);   // 양성 대조(빈 조각 no-op 막음)
        Assert.True(copy.Contains(active, StringComparison.Ordinal),
            "🔴 ⓐ″ 다시 셈(CountBackupOnlyLinkMismatch)의 활성 계정 조각이 상수와 다름 — 상수: " + active);
        // 덧2-4 ③ 「따로 다시 셈」 의도 그대로 — 넣는 식(LinkBackupOnly)을 부르지 않음
        Assert.DoesNotContain("IF(", copy, StringComparison.Ordinal);
    }

    private sealed class MarkerBusy : HitPan.Application.Interfaces.IRestoreBusyProbe
    {
        private readonly Func<string?> _f;
        public MarkerBusy(Func<string?> f) => _f = f;
        public string? BusyCode(string tenantId) => _f();
    }

    private const string Marker = "C:\\secret\\path<script>표식";

    private static (RestoreSwapStep Step, Moq.Mock<HitPan.Application.Interfaces.IRestoreSafePath> Path) FakeStep(HitPan.Application.Interfaces.IRestoreBusyProbe busy)
    {
        // DB 0 — 닿지 않는 자리(1번 문) · 연결 실패는 제품이 기록만 하고 넘어감
        var conn = new MySqlConnection("Server=127.0.0.1;Port=1;Database=x;User=x;Connection Timeout=1");
        var path = new Moq.Mock<HitPan.Application.Interfaces.IRestoreSafePath>();
        var step = new RestoreSwapStep(conn, path.Object, Moq.Mock.Of<HitPan.Application.Interfaces.ILocalAuditFile>(),
            NullLogger<RestoreSwapStep>.Instance, busy);
        return (step, path);
    }

    [Fact(DisplayName = "P3-9 동작 🔴 표식 글(경로 · 꺾쇠)이 든 멈춤 글자 · 예외 → 응답 Message ∈ 고정 문구 표 · 표식 0 (글자 검사 아님 · 실제로 부름)")]
    public async Task P3_9_동작_표식글_응답0()
    {
        var fixedTexts = RestoreOverlayRules.Messages.Values.Append(RestoreOverlayRules.UnknownMessage).ToHashSet(StringComparer.Ordinal);

        // ① 모르는 멈춤 글자(표식)가 바쁨 문에서 옴
        var (s1, _) = FakeStep(new MarkerBusy(() => Marker));
        var r1 = await s1.RunAsync("t", "u", "r", new HitPan.Application.Interfaces.RestoreStaging());
        Assert.False(r1.Ok);
        Assert.Contains(r1.Message, fixedTexts);
        Assert.DoesNotContain("secret", r1.Message, StringComparison.Ordinal);

        // ② 표식 글을 실은 예외가 걸음 밖으로 샘(P3-1 그물이 받음)
        var (s2, _) = FakeStep(new MarkerBusy(() => throw new InvalidOperationException(Marker)));
        var r2 = await s2.RunAsync("t", "u", "r", new HitPan.Application.Interfaces.RestoreStaging());
        Assert.False(r2.Ok);
        Assert.Contains(r2.Message, fixedTexts);
        Assert.DoesNotContain("secret", r2.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", r2.LogDetail ?? "", StringComparison.Ordinal);
    }

    [Fact(DisplayName = "P3-1 🔴 걸음 밖으로 샌 예외(대조 중) = 던지지 않고 멈춤 응답 · 자료 그대로 · 임시 표 지우기 부름(그물) · 요청 취소도 같음")]
    public async Task P3_1_그물_예외_뒷정리()
    {
        var (s, path) = FakeStep(new MarkerBusy(() => throw new InvalidOperationException("시험 예외")));
        var r = await s.RunAsync("t", "u", "r", new HitPan.Application.Interfaces.RestoreStaging());
        Assert.False(r.Ok);
        Assert.Equal(SafeRestoreDenyCodes.VerifyFailed, r.DenyCode);
        Assert.True(r.DataUnchanged);
        path.Verify(p => p.DropStagingTablesAsync(Moq.It.IsAny<CancellationToken>()), Moq.Times.Once());

        var (s2, path2) = FakeStep(new MarkerBusy(() => null));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var r2 = await s2.RunAsync("t", "u", "r", new HitPan.Application.Interfaces.RestoreStaging
        {
            StagedTables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["schema_migrations"] = "rs_new__schema_migrations", ["__efmigrationshistory"] = "rs_new__efmigrationshistory" },
        }, cts.Token);
        Assert.False(r2.Ok);
        Assert.True(r2.DataUnchanged);
        path2.Verify(p => p.DropStagingTablesAsync(Moq.It.IsAny<CancellationToken>()), Moq.Times.Once());
    }

    [Fact(DisplayName = "D14 🔴 복원 전 자동 백업도 보관 정리 — 사전 n+1 개 → 가장 최근 n 남음 · 일반 백업이 많아도 사전 n 그대로(따로 셈) · 0바이트는 안 셈")]
    public void D14_사전백업_보관정리()
    {
        var folder = Path.Combine(Path.GetTempPath(), "hitpan_d14_" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(folder);
        try
        {
            var t0 = DateTime.Now.AddDays(-10);
            void Make(string name, int minutes, int bytes = 4)
            {
                var f = Path.Combine(folder, name);
                File.WriteAllBytes(f, new byte[bytes]);
                File.SetCreationTime(f, t0.AddMinutes(minutes));
            }
            for (var i = 0; i < 4; i++) Make($"hitpan_pre_restore_{i}.sql", i);            // 사전 4 = n+1
            for (var i = 0; i < 8; i++) Make($"hitpan_backup_{i}.sql", 100 + i);            // 일반 8 (사전보다 최근)
            Make("hitpan_pre_restore_empty.sql", 999, bytes: 0);                             // 0바이트 — 안 셈 · 안 지움

            var svc = new BackupService(new MySqlConnection(), NullLogger<BackupService>.Instance);
            var m = typeof(BackupService).GetMethod("ApplyRetention", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(m);
            m!.Invoke(svc, [folder, 3]);

            var pre = Directory.GetFiles(folder, "hitpan_pre_restore_*.sql").Select(x => Path.GetFileName(x)!).OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.Equal(new[] { "hitpan_pre_restore_1.sql", "hitpan_pre_restore_2.sql", "hitpan_pre_restore_3.sql", "hitpan_pre_restore_empty.sql" }, pre);
            Assert.Equal(3, Directory.GetFiles(folder, "hitpan_backup_*.sql").Length);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); }
            catch (IOException ex) { Console.Error.WriteLine($"[D14] 시험 폴더 정리 실패: {ex.Message}"); }   // #15
        }
    }
}
