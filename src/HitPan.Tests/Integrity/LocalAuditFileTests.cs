using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HitPan.Application.Interfaces;
using HitPan.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-G1-52 갈래 0 — DB 밖 로컬 감사 파일 기록기 시험. <b>DB 접속 0</b> · 파일은 임시 폴더에만 쓴다(%ProgramData% 손 0).
/// 개발명세서: docs/개발/erp/20261128_W-G1-52_갈래0_감사파일_개발명세서.md (시험 ↔ 변이 표)
/// </summary>
public sealed class LocalAuditFileTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public LocalAuditFileTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "hp-audit-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "restore-audit.log");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException ex) { Console.WriteLine("임시 폴더 정리 실패(무해): " + ex.Message); }
    }

    private LocalAuditFileWriter NewWriter() => new(_path, NullLogger<LocalAuditFileWriter>.Instance);

    private static LocalAuditRecord Rec(string op, LocalAuditAction a, LocalAuditResult r = LocalAuditResult.Started, string tenant = "T1", string? detail = null)
        => new(tenant, "U-parent-1", a, r, op, detail);

    private static string[] Lines(string path) =>
        File.ReadAllText(path, Encoding.UTF8).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    // ① 줄 더하기 → 사슬 통과 · 앞 줄 지문이 이어짐
    [Fact]
    public async Task G0_1_Append_ThenVerify_ChainOk()
    {
        var w = NewWriter();
        var got = new List<LocalAuditEntry>();
        for (var i = 0; i < 5; i++)
            got.Add(await w.AppendAsync(Rec("op" + i, LocalAuditAction.RestoreStarted)));

        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, got.Select(e => e.Seq));
        Assert.Equal(new string('0', 64), got[0].PrevHash);
        for (var i = 1; i < got.Count; i++) Assert.Equal(got[i - 1].Hash, got[i].PrevHash);

        var rep = await w.VerifyChainAsync();
        Assert.True(rep.Ok, rep.Reason);
        Assert.Equal(5, rep.ValidLines);
        Assert.False(rep.TailTorn);
        Assert.Equal(5, Lines(_path).Length);
    }

    // ② 가운데 줄 지우기 → 빨강
    [Fact]
    public async Task G0_2_MiddleLineDeleted_VerifyRed()
    {
        var w = NewWriter();
        for (var i = 0; i < 5; i++) await w.AppendAsync(Rec("op" + i, LocalAuditAction.RestoreStarted));
        var lines = Lines(_path).ToList();
        lines.RemoveAt(2);
        File.WriteAllText(_path, string.Join('\n', lines) + "\n", new UTF8Encoding(false));

        var rep = await w.VerifyChainAsync();
        Assert.False(rep.Ok);
        Assert.Equal(3, rep.FirstBadLineNumber);
        Assert.Equal("chain_break", rep.Reason);
    }

    // ③ 가운데 줄 고치기(지문 그대로) → 빨강
    [Fact]
    public async Task G0_3_MiddleLineEdited_VerifyRed()
    {
        var w = NewWriter();
        for (var i = 0; i < 5; i++) await w.AppendAsync(Rec("op" + i, LocalAuditAction.SwapCompleted, LocalAuditResult.Succeeded));
        var lines = Lines(_path);
        Assert.Contains("\tSucceeded\t", lines[2]);
        lines[2] = lines[2].Replace("\tSucceeded\t", "\tFailed\t");
        File.WriteAllText(_path, string.Join('\n', lines) + "\n", new UTF8Encoding(false));

        var rep = await w.VerifyChainAsync();
        Assert.False(rep.Ok);
        Assert.Equal(3, rep.FirstBadLineNumber);
        Assert.Equal("hash_mismatch", rep.Reason);
    }

    // ④ 깨진 마지막 줄(전원 끊김) → 다음 쓰기가 닫고 표시 줄 남기고 이어 씀 → 사슬 통과
    [Fact]
    public async Task G0_4_TornTail_ThenAppend_ContinuesAndVerifies()
    {
        var w = NewWriter();
        await w.AppendAsync(Rec("a", LocalAuditAction.RestoreStarted));
        await w.AppendAsync(Rec("a", LocalAuditAction.SwapStarted));
        File.AppendAllText(_path, "v1\t3\t2026-11-28T0", new UTF8Encoding(false)); // 줄바꿈 없이 끊긴 조각

        var before = await w.VerifyChainAsync();
        Assert.True(before.Ok, before.Reason);
        Assert.True(before.TailTorn);

        var e = await w.AppendAsync(Rec("a", LocalAuditAction.SwapCompleted, LocalAuditResult.Succeeded));
        Assert.Equal(4, e.Seq); // 3 = 깨진 줄 표시

        var lines = Lines(_path);
        Assert.Equal(5, lines.Length); // 2 + 조각 + 표시 + 새 줄
        Assert.Contains("\tTornTailNoted\t", lines[3]);

        var after = await w.VerifyChainAsync();
        Assert.True(after.Ok, after.Reason);
        Assert.False(after.TailTorn);
        Assert.Equal(4, after.ValidLines);
    }

    // ⑤ 동시 쓰기 N 줄 = N 줄(인스턴스 2개 · 같은 경로)
    [Fact]
    public async Task G0_5_ConcurrentAppends_NLinesExactly()
    {
        var w1 = NewWriter();
        var w2 = NewWriter();
        const int tasks = 8, per = 25;
        await Task.WhenAll(Enumerable.Range(0, tasks).Select(t => Task.Run(async () =>
        {
            var w = t % 2 == 0 ? w1 : w2;
            for (var i = 0; i < per; i++) await w.AppendAsync(Rec($"op{t}x{i}", LocalAuditAction.RestoreStarted));
        })));

        Assert.Equal(tasks * per, Lines(_path).Length);
        var rep = await w1.VerifyChainAsync();
        Assert.True(rep.Ok, rep.Reason + " @" + rep.FirstBadLineNumber);
        Assert.Equal(tasks * per, rep.ValidLines);
    }

    // ⑥ 미완 교체 읽기 — 「교체 시작 · 끝 없음」 만 · 회사 거르기 · 깨진 끝 줄 = 안 적힌 줄
    [Fact]
    public async Task G0_6_ReadUnfinishedSwaps()
    {
        var w = NewWriter();
        await w.AppendAsync(Rec("op1", LocalAuditAction.SwapStarted));
        await w.AppendAsync(Rec("op1", LocalAuditAction.SwapCompleted, LocalAuditResult.Succeeded));
        await w.AppendAsync(Rec("op2", LocalAuditAction.SwapStarted));
        await w.AppendAsync(Rec("op3", LocalAuditAction.SwapStarted, tenant: "T2"));
        await w.AppendAsync(Rec("op3", LocalAuditAction.RecoveryResolved, LocalAuditResult.Succeeded, tenant: "T2", detail: "committed"));
        await w.AppendAsync(Rec("op4", LocalAuditAction.SwapStarted, tenant: "T2"));
        await w.AppendAsync(Rec("op5", LocalAuditAction.SwapStarted));
        File.AppendAllText(_path, "v1\t8\t2026-11-28T01:02:03.0000000Z\tT1\tU-parent-1\tSwapCompl", new UTF8Encoding(false));

        var all = await w.ReadUnfinishedSwapsAsync();
        Assert.Equal(new[] { "op2", "op4", "op5" }, all.Select(e => e.Record.OpId));

        var t1 = await w.ReadUnfinishedSwapsAsync("T1");
        Assert.Equal(new[] { "op2", "op5" }, t1.Select(e => e.Record.OpId));

        var none = await new LocalAuditFileWriter(Path.Combine(_dir, "nofile.log"), NullLogger<LocalAuditFileWriter>.Instance).ReadUnfinishedSwapsAsync();
        Assert.Empty(none);
    }

    // ⑦ 개인정보 · 줄 깨뜨리기 막기 — 빈칸 · @ · 줄바꿈 · 탭 거절, 거절이면 파일에 0줄
    [Theory]
    [InlineData("T1", "pre_20261128.sql", "has space")]
    [InlineData("T1", "pre_20261128.sql", "a@b.com")]
    [InlineData("T1", "pre_20261128.sql", "x\ny")]
    [InlineData("T\t1", "pre_20261128.sql", null)]
    public async Task G0_7_RejectsUnsafeFields(string tenant, string ok, string? bad)
    {
        var w = NewWriter();
        await w.AppendAsync(Rec("op", LocalAuditAction.PreBackupCreated, LocalAuditResult.Succeeded, detail: ok));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            w.AppendAsync(Rec("op", LocalAuditAction.PreBackupCreated, LocalAuditResult.Succeeded, tenant: tenant, detail: bad)));
        Assert.Single(Lines(_path));
    }

    // ⑧ 기본 자리 = %ProgramData%\HitPan\audit (앱 로컬 자료 뿌리 · DB 밖) — 생성자는 폴더를 만들지 않는다
    [Fact]
    public void G0_8_DefaultPath_UnderProgramDataHitPan()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HitPan");
        var w = new LocalAuditFileWriter(NullLogger<LocalAuditFileWriter>.Instance);
        Assert.Equal(Path.Combine(root, "audit", "restore-audit.log"), w.FilePath, ignoreCase: true);
        Assert.Equal(LocalAuditFileWriter.DefaultFilePath, w.FilePath, ignoreCase: true);
    }

    // ⑨ G11 — 기록기 형식(중첩 · 비동기 상태기계 포함)이 네트워크 형식을 참조하지 않음. 서명 + IL 토큰까지 훑는다.
    [Fact]
    public void G0_9_G11_WriterReferencesNoNetworkTypes()
    {
        // 양성 대조 — 훑개가 진짜 잡는지 먼저 확인(잡지 못하는 훑개면 아래 0 은 뜻이 없다)
        var control = NetworkTypeScanner.Scan(typeof(NetworkUsingControl));
        Assert.Contains(control, v => v.Contains("System.Net.Http.HttpClient"));

        var targets = new[]
        {
            typeof(LocalAuditFileWriter), typeof(ILocalAuditFile), typeof(LocalAuditRecord),
            typeof(LocalAuditEntry), typeof(LocalAuditChainReport),
        };
        var violations = targets.SelectMany(NetworkTypeScanner.Scan).Distinct().ToList();
        Assert.True(violations.Count == 0, "네트워크 형식 참조: " + string.Join(" | ", violations));
    }

    /// <summary>양성 대조용 — 네트워크를 쓰는 형식(호출되지 않는다).</summary>
    private sealed class NetworkUsingControl
    {
        public static async Task<int> Probe()
        {
            using var c = new System.Net.Http.HttpClient();
            return (await c.GetStringAsync("http://127.0.0.1/")).Length;
        }
    }
}

/// <summary>
/// G11 훑개 — 형식과 그 중첩 형식(컴파일러가 만든 상태기계 · 람다 포함)의 바탕 · 인터페이스 · 필드 · 속성 · 메서드 서명 ·
/// 지역 변수 · <b>IL 이 부르는 멤버 · 형식 토큰</b>을 모두 풀어, 네트워크 이름공간 · 어셈블리를 가리키면 적어 낸다.
/// </summary>
internal static class NetworkTypeScanner
{
    private static readonly string[] BannedNamespacePrefixes =
    {
        "System.Net", "MySqlConnector", "System.Data", "Microsoft.AspNetCore.SignalR", "Microsoft.AspNetCore.Http",
        "Grpc", "System.ServiceModel",
    };

    private static readonly Dictionary<short, OpCode> OpCodeMap = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    public static IReadOnlyList<string> Scan(Type root)
    {
        var hits = new HashSet<string>();
        foreach (var t in WithNested(root))
        {
            Check(t.BaseType, t, hits);
            foreach (var i in t.GetInterfaces()) Check(i, t, hits);
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var f in t.GetFields(all)) Check(f.FieldType, t, hits);
            foreach (var p in t.GetProperties(all)) Check(p.PropertyType, t, hits);
            foreach (var m in t.GetMethods(all).Cast<MethodBase>().Concat(t.GetConstructors(all)))
            {
                if (m is MethodInfo mi) Check(mi.ReturnType, t, hits);
                foreach (var p in m.GetParameters()) Check(p.ParameterType, t, hits);
                var body = m.GetMethodBody();
                if (body is null) continue;
                foreach (var lv in body.LocalVariables) Check(lv.LocalType, t, hits);
                var il = body.GetILAsByteArray();
                if (il is not null) ScanIl(il, m, t, hits);
            }
        }
        return hits.ToList();
    }

    private static IEnumerable<Type> WithNested(Type t)
    {
        yield return t;
        foreach (var n in t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            foreach (var x in WithNested(n)) yield return x;
    }

    private static void ScanIl(byte[] il, MethodBase m, Type owner, HashSet<string> hits)
    {
        var typeArgs = owner.IsGenericType ? owner.GetGenericArguments() : null;
        var methodArgs = m.IsGenericMethod ? m.GetGenericArguments() : null;
        var i = 0;
        while (i < il.Length)
        {
            short code = il[i++];
            if (code == 0xFE) code = (short)(0xFE00 | il[i++]);
            if (!OpCodeMap.TryGetValue(code, out var op)) throw new InvalidOperationException($"IL 해독 실패 0x{code:X} @{m.Name}");
            switch (op.OperandType)
            {
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar: i += 1; break;
                case OperandType.InlineVar: i += 2; break;
                case OperandType.InlineI8:
                case OperandType.InlineR: i += 8; break;
                case OperandType.InlineSwitch:
                    var n = BitConverter.ToInt32(il, i);
                    i += 4 + 4 * n; break;
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                    var token = BitConverter.ToInt32(il, i);
                    i += 4;
                    var member = m.Module.ResolveMember(token, typeArgs, methodArgs);
                    switch (member)
                    {
                        case Type ty: Check(ty, owner, hits); break;
                        case FieldInfo fi: Check(fi.DeclaringType, owner, hits); Check(fi.FieldType, owner, hits); break;
                        case MethodBase mb:
                            Check(mb.DeclaringType, owner, hits);
                            if (mb is MethodInfo rmi) Check(rmi.ReturnType, owner, hits);
                            foreach (var p in mb.GetParameters()) Check(p.ParameterType, owner, hits);
                            break;
                    }
                    break;
                default: i += 4; break; // InlineI · InlineBrTarget · InlineSig · InlineString · ShortInlineR
            }
        }
    }

    private static void Check(Type? t, Type owner, HashSet<string> hits)
    {
        if (t is null) return;
        if (t.HasElementType) { Check(t.GetElementType(), owner, hits); return; }
        if (t.IsGenericType && !t.IsGenericTypeDefinition)
            foreach (var a in t.GetGenericArguments()) Check(a, owner, hits);
        if (t.IsGenericParameter) return;
        var ns = t.Namespace ?? string.Empty;
        var asm = t.Assembly.GetName().Name ?? string.Empty;
        if (BannedNamespacePrefixes.Any(p => ns.StartsWith(p, StringComparison.Ordinal) || asm.StartsWith(p, StringComparison.Ordinal)))
            hits.Add($"{owner.FullName} → {t.FullName} ({asm})");
    }
}
