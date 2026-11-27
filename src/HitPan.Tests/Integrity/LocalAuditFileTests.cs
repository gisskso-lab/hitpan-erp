using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using HitPan.Application.Interfaces;
using HitPan.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-G1-52 갈래 0 — DB 밖 로컬 감사 파일 기록기 시험. <b>DB 접속 0</b> · 파일은 임시 폴더에만 쓴다(%ProgramData% 손 0).
/// 개발명세서: docs/개발/erp/20261128_W-G1-52_갈래0_감사파일_개발명세서.md (시험 ↔ 변이 표 · 「고침 1」 절)
/// </summary>
/// <remarks>
/// 🆕 고침 1(4 발견 8) — 시험 이름 <c>G0_x</c> → <c>AF_x</c>(설계 §15-9 의 G0 = 제한 계정 자기 증명과 겹쳐 바꿈). 옛 이름은 각 시험 위 주석에 남김.
/// <c>[WindowsOnlyFact]</c> = Windows 권한 모델에서만 재는 시험 — 리눅스에서는 「건너뜀」 으로 드러난다(조용한 초록 아님).
/// 「읽기만 · 대리 측정」 시험(AF_12)은 시스템 폴더 · 파일을 <b>읽기만</b> 한다(있는 폴더라 만들기 걸음이 돌지 않음).
/// </remarks>
public sealed class LocalAuditFileTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public LocalAuditFileTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "hp-audit-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        // ⬛ 고침 1 전: _path = Path.Combine(_dir, "restore-audit.log");
        // 🆕 고침 1 — 기록기가 audit 폴더를 직접 만들게 한 칸 아래(X1 보호 권한 걸음이 늘 돈다)
        _path = Path.Combine(_dir, "audit", "restore-audit.log");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException ex) { Console.WriteLine("임시 폴더 정리 실패(무해): " + ex.Message); }
        catch (UnauthorizedAccessException ex) { Console.WriteLine("임시 폴더 정리 실패(무해): " + ex.Message); }
    }

    private LocalAuditFileWriter NewWriter() => new(_path, NullLogger<LocalAuditFileWriter>.Instance);

    private static LocalAuditRecord Rec(string op, LocalAuditAction a, LocalAuditResult r = LocalAuditResult.Started, string tenant = "T1", string? detail = null)
        => new(tenant, "U-parent-1", a, r, op, detail);

    private static string[] Lines(string path) =>
        File.ReadAllText(path, Encoding.UTF8).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static readonly UTF8Encoding NoBom = new(false);

    private static string[] OpIds(LocalAuditOpenReport r) => r.Entries.Select(e => e.Record.OpId).ToArray();

    /// <summary>줄 꼴(명세서 §3)대로 지문까지 맞춘 줄을 손으로 만든다 — 「지문을 다시 계산하는 사람」 흉내(N8 · X3 대조용).</summary>
    private static string Forge(long seq, string tenant, LocalAuditAction a, LocalAuditResult r, string op, string? detail, string prev, out string hash)
    {
        var body = string.Join('\t', "v1", seq.ToString(CultureInfo.InvariantCulture), "2026-11-28T01:02:03.0000000Z",
            tenant, "U-parent-1", a.ToString(), r.ToString(), op, detail ?? string.Empty, prev);
        hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        return body + "\t" + hash;
    }

    // ① ⬛ 옛 이름 G0_1_Append_ThenVerify_ChainOk — 줄 더하기 → 사슬 통과 · 앞 줄 지문이 이어짐
    [Fact]
    public async Task AF_1_Append_ThenVerify_ChainOk()
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
        Assert.Equal(LocalAuditFileState.Present, rep.State);
        Assert.Equal(5, rep.ValidLines);
        Assert.False(rep.TailTorn);
        Assert.Equal(5, Lines(_path).Length);
    }

    // ② ⬛ 옛 이름 G0_2_MiddleLineDeleted_VerifyRed — 가운데 줄 지우기 → 빨강
    [Fact]
    public async Task AF_2_MiddleLineDeleted_VerifyRed()
    {
        var w = NewWriter();
        for (var i = 0; i < 5; i++) await w.AppendAsync(Rec("op" + i, LocalAuditAction.RestoreStarted));
        var lines = Lines(_path).ToList();
        lines.RemoveAt(2);
        File.WriteAllText(_path, string.Join('\n', lines) + "\n", NoBom);

        var rep = await w.VerifyChainAsync();
        Assert.False(rep.Ok);
        Assert.Equal(3, rep.FirstBadLineNumber);
        Assert.Equal("chain_break", rep.Reason);
    }

    // ③ ⬛ 옛 이름 G0_3_MiddleLineEdited_VerifyRed — 가운데 줄 고치기(지문 그대로) → 빨강
    [Fact]
    public async Task AF_3_MiddleLineEdited_VerifyRed()
    {
        var w = NewWriter();
        for (var i = 0; i < 5; i++) await w.AppendAsync(Rec("op" + i, LocalAuditAction.SwapCompleted, LocalAuditResult.Succeeded));
        var lines = Lines(_path);
        Assert.Contains("\tSucceeded\t", lines[2]);
        lines[2] = lines[2].Replace("\tSucceeded\t", "\tFailed\t");
        File.WriteAllText(_path, string.Join('\n', lines) + "\n", NoBom);

        var rep = await w.VerifyChainAsync();
        Assert.False(rep.Ok);
        Assert.Equal(3, rep.FirstBadLineNumber);
        Assert.Equal("hash_mismatch", rep.Reason);
    }

    // ④ ⬛ 옛 이름 G0_4_TornTail_ThenAppend_ContinuesAndVerifies — 깨진 마지막 줄(전원 끊김) → 다음 쓰기가 닫고 표시 줄 남기고 이어 씀 → 사슬 통과
    [Fact]
    public async Task AF_4_TornTail_ThenAppend_ContinuesAndVerifies()
    {
        var w = NewWriter();
        await w.AppendAsync(Rec("a", LocalAuditAction.RestoreStarted));
        await w.AppendAsync(Rec("a", LocalAuditAction.SwapStarted));
        File.AppendAllText(_path, "v1\t3\t2026-11-28T0", NoBom); // 줄바꿈 없이 끊긴 조각

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
        Assert.Equal(1, after.ExplainedTornLines);
    }

    // ⑤ ⬛ 옛 이름 G0_5_ConcurrentAppends_NLinesExactly — 동시 쓰기 N 줄 = N 줄(인스턴스 2개 · 같은 경로)
    [Fact]
    public async Task AF_5_ConcurrentAppends_NLinesExactly()
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

    // ⑥ ⬛ 옛 이름 G0_6_ReadUnfinishedSwaps — 미완 교체 읽기 「교체 시작 · 끝 없음」 만 · 회사 거르기 · 깨진 끝 줄 = 안 적힌 줄
    //   🆕 고침 1(X2) — 회사 열쇠 필수(옛 「인자 없음 = 모든 회사」 단언은 지움 대신 아래 주석으로 남김)
    //   ⬛ var all = await w.ReadUnfinishedSwapsAsync(); Assert.Equal(new[] { "op2", "op4", "op5" }, ...);
    [Fact]
    public async Task AF_6_ReadUnfinishedSwaps()
    {
        var w = NewWriter();
        await w.AppendAsync(Rec("op1", LocalAuditAction.SwapStarted));
        await w.AppendAsync(Rec("op1", LocalAuditAction.SwapCompleted, LocalAuditResult.Succeeded));
        await w.AppendAsync(Rec("op2", LocalAuditAction.SwapStarted));
        await w.AppendAsync(Rec("op3", LocalAuditAction.SwapStarted, tenant: "T2"));
        await w.AppendAsync(Rec("op3", LocalAuditAction.RecoveryResolved, LocalAuditResult.Succeeded, tenant: "T2", detail: "committed"));
        await w.AppendAsync(Rec("op4", LocalAuditAction.SwapStarted, tenant: "T2"));
        await w.AppendAsync(Rec("op5", LocalAuditAction.SwapStarted));
        File.AppendAllText(_path, "v1\t8\t2026-11-28T01:02:03.0000000Z\tT1\tU-parent-1\tSwapCompl", NoBom);

        var t1 = await w.ReadUnfinishedSwapsAsync("T1");
        Assert.Equal(new[] { "op2", "op5" }, OpIds(t1));
        Assert.Equal(0, t1.UntrustedLines);
        Assert.Equal(LocalAuditFileState.Present, t1.FileState);

        var t2 = await w.ReadUnfinishedSwapsAsync("T2");
        Assert.Equal(new[] { "op4" }, OpIds(t2));

        var none = await new LocalAuditFileWriter(Path.Combine(_dir, "nofile.log"), NullLogger<LocalAuditFileWriter>.Instance).ReadUnfinishedSwapsAsync("T1");
        Assert.Empty(none.Entries);
        Assert.Equal(LocalAuditFileState.Missing, none.FileState);

        await Assert.ThrowsAsync<ArgumentException>(() => w.ReadUnfinishedSwapsAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => w.ReadUnfinishedSwapsAsync("-"));
    }

    // ⑦ ⬛ 옛 이름 G0_7_RejectsUnsafeFields — 개인정보 · 줄 깨뜨리기 막기 — 빈칸 · @ · 줄바꿈 · 탭 거절, 거절이면 파일에 0줄
    [Theory]
    [InlineData("T1", "pre_20261128.sql", "has space")]
    [InlineData("T1", "pre_20261128.sql", "a@b.com")]
    [InlineData("T1", "pre_20261128.sql", "x\ny")]
    [InlineData("T\t1", "pre_20261128.sql", null)]
    public async Task AF_7_RejectsUnsafeFields(string tenant, string ok, string? bad)
    {
        var w = NewWriter();
        await w.AppendAsync(Rec("op", LocalAuditAction.PreBackupCreated, LocalAuditResult.Succeeded, detail: ok));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            w.AppendAsync(Rec("op", LocalAuditAction.PreBackupCreated, LocalAuditResult.Succeeded, tenant: tenant, detail: bad)));
        Assert.Single(Lines(_path));
    }

    // ⑧ ⬛ 옛 이름 G0_8_DefaultPath_UnderProgramDataHitPan — 기본 자리 = %ProgramData%\HitPan\audit (앱 로컬 자료 뿌리 · DB 밖) — 생성자는 폴더를 만들지 않는다
    [Fact]
    public void AF_8_DefaultPath_UnderProgramDataHitPan()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HitPan");
        var w = new LocalAuditFileWriter(NullLogger<LocalAuditFileWriter>.Instance);
        Assert.Equal(Path.Combine(root, "audit", "restore-audit.log"), w.FilePath, ignoreCase: true);
        Assert.Equal(LocalAuditFileWriter.DefaultFilePath, w.FilePath, ignoreCase: true);
    }

    // ⑨ ⬛ 옛 이름 G0_9_G11_WriterReferencesNoNetworkTypes — G11 기록기 형식(중첩 · 비동기 상태기계 포함)이 네트워크 형식을 참조하지 않음. 서명 + IL 토큰까지 훑는다.
    //   🆕 고침 1(3-V F10) — 훑개가 부르는 HitPan 형식까지 전이로 훑는다(문지기 등) · 양성 대조 2개(직접 · 한 칸 건너).
    [Fact]
    public void AF_9_G11_WriterReferencesNoNetworkTypes()
    {
        // 양성 대조 — 훑개가 진짜 잡는지 먼저 확인(잡지 못하는 훑개면 아래 0 은 뜻이 없다)
        var control = NetworkTypeScanner.Scan(typeof(NetworkUsingControl));
        Assert.Contains(control, v => v.Contains("System.Net.Http.HttpClient"));
        var indirect = NetworkTypeScanner.Scan(typeof(IndirectNetworkControl));
        Assert.Contains(indirect, v => v.Contains("System.Net.Http.HttpClient"));

        var targets = new[]
        {
            typeof(LocalAuditFileWriter), typeof(ILocalAuditFile), typeof(LocalAuditRecord),
            typeof(LocalAuditEntry), typeof(LocalAuditChainReport),
            typeof(LocalAuditFolderGuard), typeof(LocalAuditOpenReport), typeof(LocalAuditWriteException), typeof(LocalAuditWords),
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

    /// <summary>양성 대조용 — 네트워크 형식을 직접 안 쓰고 한 칸 건너 부른다(전이 훑기 확인 · 호출되지 않는다).</summary>
    private sealed class IndirectNetworkControl
    {
        public static Task<int> Probe() => NetworkUsingControl.Probe();
    }

    // ───────────────────────── 🆕 고침 1 ─────────────────────────

    /// <summary>폴더 연결을 만든다 — Windows = 교차점(mklink /J · 관리자 권한 불필요) · 그 밖 = 심볼릭 링크.</summary>
    private static void MakeDirLink(string link, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            var psi = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in new[] { "/c", "mklink", "/J", link, target }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            p.WaitForExit(15000);
        }
        else
        {
            Directory.CreateSymbolicLink(link, target);
        }
        Assert.True(new DirectoryInfo(link).LinkTarget is not null, "폴더 연결을 만들지 못했다 — 재지 못함: " + link);
    }

    // X1 — 재분석 지점(교차점 · 심볼릭 링크) 폴더 → 거절 · 거절 때 파일 0 생성(audit 폴더 자신 · 그 위 폴더 둘 다) · 변이 「재분석 검사 뺌」
    [Fact]
    public async Task AF_10_X1_ReparsePointFolder_Refused_NoFileCreated()
    {
        var real = Path.Combine(_dir, "real");
        Directory.CreateDirectory(real);

        var linkAsAudit = Path.Combine(_dir, "link-audit");
        MakeDirLink(linkAsAudit, real);
        var w1 = new LocalAuditFileWriter(Path.Combine(linkAsAudit, "restore-audit.log"), NullLogger<LocalAuditFileWriter>.Instance);
        var ex1 = await Assert.ThrowsAsync<LocalAuditWriteException>(() => w1.AppendAsync(Rec("op", LocalAuditAction.RestoreStarted)));
        Assert.Equal("folder_reparse_point", ex1.Reason);

        var linkAsParent = Path.Combine(_dir, "link-parent");
        MakeDirLink(linkAsParent, real);
        var w2 = new LocalAuditFileWriter(Path.Combine(linkAsParent, "audit", "restore-audit.log"), NullLogger<LocalAuditFileWriter>.Instance);
        var ex2 = await Assert.ThrowsAsync<LocalAuditWriteException>(() => w2.AppendAsync(Rec("op", LocalAuditAction.RestoreStarted)));
        Assert.Equal("folder_reparse_point", ex2.Reason);

        Assert.Empty(Directory.EnumerateFileSystemEntries(real, "*", SearchOption.AllDirectories));
    }

    // X1 · X7 — 기록기가 만든 audit 폴더 = 상속 끊김 · 허용 주체(SYSTEM · Administrators · 실행 계정)만 · Users · Everyone · 인증된 사용자 줄 0
    //   (일반 사용자는 손잡이조차 못 연다 → 읽기 손잡이로 쓰기 막기(3-V F5) 닫힘) · 변이 「ACL 걸음 뺌」
    [WindowsOnlyFact]
    [SupportedOSPlatform("windows")]
    public async Task AF_11_X1_CreatedFolder_IsProtected_NoBroadGroupRules()
    {
        var w = NewWriter();
        await w.AppendAsync(Rec("op", LocalAuditAction.RestoreStarted));

        var folder = new DirectoryInfo(Path.GetDirectoryName(_path)!);
        var acl = folder.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        Assert.True(acl.AreAccessRulesProtected, "audit 폴더가 위 폴더 권한을 상속한다(보호 권한 아님)");

        using var me = WindowsIdentity.GetCurrent();
        var allowed = new HashSet<SecurityIdentifier>
        {
            new(WellKnownSidType.LocalSystemSid, null),
            new(WellKnownSidType.BuiltinAdministratorsSid, null),
            me.User!,
        };
        var broad = new[]
        {
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            new SecurityIdentifier(WellKnownSidType.CreatorOwnerSid, null),
        };
        var rules = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
        Assert.NotEmpty(rules);
        foreach (var r in rules.Where(r => r.AccessControlType == AccessControlType.Allow))
        {
            var sid = (SecurityIdentifier)r.IdentityReference;
            Assert.DoesNotContain(sid, broad);
            Assert.Contains(sid, allowed);
        }

        // 파일도 같은 보호 권한을 물려받았다(넓은 그룹 줄 0)
        var fileRules = new FileInfo(_path).GetAccessControl(AccessControlSections.Access)
            .GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>();
        Assert.DoesNotContain(fileRules, r => broad.Contains((SecurityIdentifier)r.IdentityReference));
    }

    // X1 — 소유자가 허용 셋 밖인 기존 폴더 · 파일 → 소유자 까닭으로 거절. 🟡 읽기만 · 대리 측정(시스템 폴더 · 파일 · 있는 것이라 만들기 걸음 안 돎)
    //   실제 공격 꼴(일반 사용자가 먼저 만든 폴더)은 관리자 권한 없이 남의 소유 폴더를 만들 수 없어 대리로 잰다(LocalSwapSealGateTests G-OWN1 과 같은 꼴).
    [WindowsOnlyFact]
    [SupportedOSPlatform("windows")]
    public void AF_12_X1_FolderOrFileOwnedOutsideAllowed_RefusedByOwner()
    {
        var folder = Path.Combine(Environment.SystemDirectory, "drivers", "etc");
        var why = WhyOwnerUnmeasurable(new DirectoryInfo(folder));
        Assert.True(why is null, "AF_12 폴더: 재지 못했다 — " + why);
        var ex = Assert.Throws<LocalAuditWriteException>(() => LocalAuditFolderGuard.EnsureFolder(folder));
        Assert.Equal("folder_owner", ex.Reason);

        var file = Path.Combine(Environment.SystemDirectory, "kernel32.dll");
        var whyFile = WhyOwnerUnmeasurable(new FileInfo(file));
        Assert.True(whyFile is null, "AF_12 파일: 재지 못했다 — " + whyFile);
        var exFile = Assert.Throws<LocalAuditWriteException>(() => LocalAuditFolderGuard.EnsureExistingFileSafe(file));
        Assert.Equal("file_owner", exFile.Reason);
    }

    [SupportedOSPlatform("windows")]
    private static string? WhyOwnerUnmeasurable(FileSystemInfo info)
    {
        if (!info.Exists) return "없다(부르면 만들기를 시도할 수 있다): " + info.FullName;
        for (var d = info is FileInfo f ? f.Directory : (DirectoryInfo)info; d is not null; d = d.Parent)
            if (d.Attributes.HasFlag(FileAttributes.ReparsePoint)) return "재분석 지점이 있다: " + d.FullName;
        FileSystemSecurity acl = info is DirectoryInfo di ? di.GetAccessControl(AccessControlSections.Owner) : ((FileInfo)info).GetAccessControl(AccessControlSections.Owner);
        if (acl.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner) return "소유자를 읽지 못했다";
        return LocalAuditFolderGuard.AllowedPrincipals().Contains(owner) ? "소유자가 허용 셋 안이다: " + owner.Value : null;
    }

    // X2 · N6 — 회사 A 의 미완 교체가 회사 B 읽기에 안 나옴(같은 OpId 라도) · 회사 열쇠 필수
    [Fact]
    public async Task AF_13_X2_TenantKeyRequired_OtherTenantNotSeen()
    {
        var w = NewWriter();
        await w.AppendAsync(Rec("same", LocalAuditAction.SwapStarted, tenant: "TA"));
        await w.AppendAsync(Rec("onlyA", LocalAuditAction.SwapStarted, tenant: "TA"));
        await w.AppendAsync(Rec("same", LocalAuditAction.SwapStarted, tenant: "TB"));
        await w.AppendAsync(Rec("same", LocalAuditAction.SwapCompleted, LocalAuditResult.Succeeded, tenant: "TB"));

        var b = await w.ReadUnfinishedSwapsAsync("TB");
        Assert.Empty(b.Entries);
        var bOpen = await w.ReadOpenOperationsAsync("TB");
        Assert.Empty(bOpen.Entries);

        var a = await w.ReadUnfinishedSwapsAsync("TA");
        Assert.Equal(new[] { "same", "onlyA" }, OpIds(a));
        Assert.All(a.Entries, e => Assert.Equal("TA", e.Record.TenantId));
    }

    // X3 — 아무 16진 64자로 꾸민 줄 1 → 미완 목록 0 · 못 믿는 줄 1 / 꾸민 「끝」 줄로 진짜 미완을 숨기지 못함
    [Fact]
    public async Task AF_14_X3_ForgedLine_NotTrusted_Counted()
    {
        var fake1 = new string('a', 64);
        var fake2 = new string('b', 64);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, $"v1\t1\t2026-11-28T01:02:03.0000000Z\tT1\tU-parent-1\tSwapStarted\tStarted\topX\t\t{fake1}\t{fake2}\n", NoBom);
        var w = NewWriter();
        var r1 = await w.ReadUnfinishedSwapsAsync("T1");
        Assert.Empty(r1.Entries);
        Assert.Equal(1, r1.UntrustedLines);
        Assert.Equal("hash_mismatch", r1.UntrustedReason);

        File.Delete(_path);
        await w.AppendAsync(Rec("op1", LocalAuditAction.SwapStarted));
        File.AppendAllText(_path, $"v1\t2\t2026-11-28T01:02:03.0000000Z\tT1\tU-parent-1\tSwapCompleted\tSucceeded\top1\t\t{fake1}\t{fake2}\n", NoBom);
        var r2 = await w.ReadUnfinishedSwapsAsync("T1");
        Assert.Equal(new[] { "op1" }, OpIds(r2));
        Assert.Equal(1, r2.UntrustedLines);
    }

    // X4 — 걸음 ② 시작만 있는 파일 → 미완 1 · 「중단」 줄 더한 뒤 → 0 / 교체 묶음 · 대조 실패 묶음도
    [Fact]
    public async Task AF_15_X4_OpenOperations_ClosedByInterruptedLine()
    {
        var w = NewWriter();
        await w.AppendAsync(Rec("op1", LocalAuditAction.RestoreStarted));
        await w.AppendAsync(Rec("op1", LocalAuditAction.PreBackupCreated, LocalAuditResult.Succeeded, detail: "pre_20261128.sql"));

        var open = await w.ReadOpenOperationsAsync("T1");
        Assert.Equal(new[] { "op1" }, OpIds(open));
        Assert.Equal(LocalAuditAction.RestoreStarted, open.Entries[0].Record.Action);
        Assert.Empty((await w.ReadUnfinishedSwapsAsync("T1")).Entries); // 교체 전 끊김 → 교체 미완 아님

        await w.AppendAsync(Rec("op1", LocalAuditAction.RestoreInterrupted, LocalAuditResult.Noted, detail: "startup_cleanup"));
        Assert.Empty((await w.ReadOpenOperationsAsync("T1")).Entries);

        await w.AppendAsync(Rec("op2", LocalAuditAction.RestoreStarted));
        await w.AppendAsync(Rec("op2", LocalAuditAction.SwapStarted));
        Assert.Equal(new[] { "op2" }, OpIds(await w.ReadOpenOperationsAsync("T1")));
        Assert.Equal(new[] { "op2" }, OpIds(await w.ReadUnfinishedSwapsAsync("T1")));
        await w.AppendAsync(Rec("op2", LocalAuditAction.RecoveryResolved, LocalAuditResult.Succeeded, detail: "committed"));

        await w.AppendAsync(Rec("op3", LocalAuditAction.RestoreStarted));
        await w.AppendAsync(Rec("op3", LocalAuditAction.VerifyFailed, LocalAuditResult.Failed, detail: "employees"));
        Assert.Equal(new[] { "op3" }, OpIds(await w.ReadOpenOperationsAsync("T1"))); // 대조 실패는 끝이 아니다
        await w.AppendAsync(Rec("op3", LocalAuditAction.RestoreAborted, LocalAuditResult.Failed));

        await w.AppendAsync(Rec("pw", LocalAuditAction.ParentPasswordResetFailed, LocalAuditResult.Failed, detail: "otp_mismatch"));
        Assert.Empty((await w.ReadOpenOperationsAsync("T1")).Entries);
        Assert.Empty((await w.ReadUnfinishedSwapsAsync("T1")).Entries);
    }

    // X5 — 전원이 두 번 이상 끊겨도(쓰는 중 또 끊김 · 4 반증 P1) 다음 쓰기가 끊긴 끝을 모두 설명해 고객 손 없이 초록 · 끊김 수 2 → 3
    [Fact]
    public async Task AF_16_X5_DoubleTorn_SelfHealsGreen()
    {
        var w = NewWriter();
        await w.AppendAsync(Rec("a", LocalAuditAction.RestoreStarted));
        await w.AppendAsync(Rec("a", LocalAuditAction.SwapStarted));
        // 끊김 1: 3번 줄 앞부분만
        File.AppendAllText(_path, "v1\t3\t2026-11-28T0", NoBom);
        // 쓰기 중 끊김 2: 다음 쓰기의 「닫는 \n + 표시 줄」 이 반만 적힘
        File.AppendAllText(_path, "\nv1\t3\t2026-11-28T01:02:03.0000000Z\t-\t-\tTornTai", NoBom);

        var before = await w.VerifyChainAsync();
        Assert.True(before.Ok, before.Reason);
        Assert.True(before.TailTorn);

        await w.AppendAsync(Rec("a", LocalAuditAction.SwapCompleted, LocalAuditResult.Succeeded));
        var after = await w.VerifyChainAsync();
        Assert.True(after.Ok, after.Reason + " @" + after.FirstBadLineNumber);
        Assert.Equal(2, after.ExplainedTornLines);
        Assert.False(after.TailTorn);

        // 끊김 → 쓰기 한 번 더(차례로)
        File.AppendAllText(_path, "v1\t6\t2026", NoBom);
        await w.AppendAsync(Rec("b", LocalAuditAction.RestoreStarted));
        var again = await w.VerifyChainAsync();
        Assert.True(again.Ok, again.Reason);
        Assert.Equal(3, again.ExplainedTornLines);
        Assert.Equal(0, (await w.ReadOpenOperationsAsync("T1")).UntrustedLines);
    }

    // X6 — 파일 없음 · 빈 파일 = 초록과 다른 상태(Ok=true · ValidLines=0 금지)
    [Fact]
    public async Task AF_17_X6_MissingAndEmpty_AreNotGreen()
    {
        var w = NewWriter();
        var none = await w.VerifyChainAsync();
        Assert.False(none.Ok);
        Assert.Equal(LocalAuditFileState.Missing, none.State);
        Assert.Equal("file_missing", none.Reason);

        await w.AppendAsync(Rec("op", LocalAuditAction.RestoreStarted));
        File.Delete(_path);
        var deleted = await w.VerifyChainAsync();
        Assert.False(deleted.Ok);
        Assert.Equal(LocalAuditFileState.Missing, deleted.State);

        File.WriteAllBytes(_path, Array.Empty<byte>());
        var empty = await w.VerifyChainAsync();
        Assert.False(empty.Ok);
        Assert.Equal(LocalAuditFileState.Empty, empty.State);
        Assert.Equal("file_empty", empty.Reason);
        Assert.Equal(LocalAuditFileState.Empty, (await w.ReadOpenOperationsAsync("T1")).FileState);
    }

    // X7 · F8 — 쓰기 실패 = LocalAuditWriteException 하나뿐 · 쓰기 뒤 던짐이면 줄이 남았는지 사실대로
    [Fact]
    public async Task AF_18_X7_WriteFailure_OnlyNamedException()
    {
        // (가) audit 자리를 파일이 차지 → 폴더를 못 만듦
        File.WriteAllText(Path.Combine(_dir, "audit"), "x");
        var blocked = NewWriter();
        var exIo = await Assert.ThrowsAsync<LocalAuditWriteException>(() => blocked.AppendAsync(Rec("op", LocalAuditAction.RestoreStarted)));
        Assert.Equal("io", exIo.Reason);
        Assert.False(exIo.LineWritten);
        File.Delete(Path.Combine(_dir, "audit"));

        // (나) 줄을 쓴 뒤 디스크 내려쓰기에서 던짐 → 줄 남음 = true
        var w = NewWriter();
        await w.AppendAsync(Rec("op", LocalAuditAction.RestoreStarted));
        w.WriteStep = (fs, b) => { LocalAuditFileWriter.WriteAndFlush(fs, b); throw new IOException("가짜 디스크 오류(시험)"); };
        var exLanded = await Assert.ThrowsAsync<LocalAuditWriteException>(() => w.AppendAsync(Rec("op", LocalAuditAction.SwapStarted)));
        Assert.Equal("flush_failed_line_present", exLanded.Reason);
        Assert.True(exLanded.LineWritten);
        Assert.Equal(2, Lines(_path).Length);

        // (다) 한 바이트도 못 쓰고 던짐 → 줄 남음 = false
        w.WriteStep = (_, _) => throw new IOException("가짜 디스크 오류(시험)");
        var exLost = await Assert.ThrowsAsync<LocalAuditWriteException>(() => w.AppendAsync(Rec("op", LocalAuditAction.SwapCompleted, LocalAuditResult.Succeeded)));
        Assert.Equal("write_failed", exLost.Reason);
        Assert.False(exLost.LineWritten);
        Assert.Equal(2, Lines(_path).Length);
    }

    // N1 — 끝 동작 SwapFailed 가 교체를 닫는다(되돌려진 교체가 다음 기동에 「미완」 으로 다시 회복되지 않음)
    [Fact]
    public async Task AF_19_N1_SwapFailed_ClosesSwap()
    {
        var w = NewWriter();
        await w.AppendAsync(Rec("op1", LocalAuditAction.SwapStarted));
        await w.AppendAsync(Rec("op1", LocalAuditAction.SwapFailed, LocalAuditResult.Failed));
        Assert.Empty((await w.ReadUnfinishedSwapsAsync("T1")).Entries);
        Assert.Empty((await w.ReadOpenOperationsAsync("T1")).Entries);
    }

    // N2 — 표시 줄이 남은 뒤 조각 바이트를 고치면 빨강(표시 줄의 조각 지문 대조 · 4 반증 P7)
    [Fact]
    public async Task AF_20_N2_MarkerMustMatchFragment()
    {
        var w = NewWriter();
        await w.AppendAsync(Rec("a", LocalAuditAction.RestoreStarted));
        await w.AppendAsync(Rec("a", LocalAuditAction.SwapStarted));
        File.AppendAllText(_path, "v1\t3\t2026-11-28T0", NoBom);
        await w.AppendAsync(Rec("a", LocalAuditAction.SwapCompleted, LocalAuditResult.Succeeded));
        Assert.True((await w.VerifyChainAsync()).Ok);

        var text = File.ReadAllText(_path, Encoding.UTF8);
        File.WriteAllText(_path, text.Replace("v1\t3\t2026-11-28T0\n", "v1\t3\t2026-11-28T1\n"), NoBom); // 꼴은 그대로 · 바이트만 바꿈
        var rep = await w.VerifyChainAsync();
        Assert.False(rep.Ok);
        Assert.Equal("torn_unexplained", rep.Reason);
        Assert.Equal(3, rep.FirstBadLineNumber);
    }

    // N3 — 조각을 지우고 표시 줄만 남기면 빨강(torn_marker_without_torn · 4 반증 P8)
    [Fact]
    public async Task AF_21_N3_MarkerWithoutFragment_Red()
    {
        var w = NewWriter();
        await w.AppendAsync(Rec("a", LocalAuditAction.RestoreStarted));
        File.AppendAllText(_path, "v1\t2\t2026-11-28T0", NoBom);
        await w.AppendAsync(Rec("a", LocalAuditAction.SwapStarted));
        var lines = Lines(_path).ToList();
        Assert.Contains("\tTornTailNoted\t", lines[2]);
        lines.RemoveAt(1); // 조각 지움
        File.WriteAllText(_path, string.Join('\n', lines) + "\n", NoBom);

        var rep = await w.VerifyChainAsync();
        Assert.False(rep.Ok);
        Assert.Equal("torn_marker_without_torn", rep.Reason);
        Assert.Equal(2, rep.FirstBadLineNumber);
    }

    /// <summary>로그를 모은다(N4 — 공유 위반 기다림 경고가 났는지).</summary>
    private sealed class CapturingLogger : ILogger<LocalAuditFileWriter>
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Enqueue(formatter(state, exception));
    }

    // N4 — 같은 프로세스 두 기록기: 경로별 자물쇠 한 겹만으로 OS 손잡이 다툼 0(자물쇠를 인스턴스별로 바꾸면 둘째가 공유 위반으로 기다림 → 빨강)
    [Fact]
    public async Task AF_22_N4_SameProcessTwoWriters_SerializedByPathLock()
    {
        var w1 = NewWriter();
        await w1.AppendAsync(Rec("first", LocalAuditAction.RestoreStarted));
        using var held = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        w1.WriteStep = (fs, b) => { held.Set(); release.Wait(TimeSpan.FromSeconds(5)); LocalAuditFileWriter.WriteAndFlush(fs, b); };

        var log = new CapturingLogger();
        var w2 = new LocalAuditFileWriter(_path, log) { ShareRetryCount = 200 };

        var t1 = Task.Run(() => w1.AppendAsync(Rec("w1", LocalAuditAction.RestoreStarted)));
        Assert.True(held.Wait(TimeSpan.FromSeconds(10)), "첫 기록기가 손잡이를 쥐지 못했다");
        var t2 = Task.Run(() => w2.AppendAsync(Rec("w2", LocalAuditAction.RestoreStarted)));
        await Task.Delay(400);
        release.Set();
        await Task.WhenAll(t1, t2);

        Assert.DoesNotContain(log.Messages, m => m.Contains("다른 쪽이 쓰는 중", StringComparison.Ordinal));
        Assert.Equal(3, Lines(_path).Length);
        Assert.True((await w2.VerifyChainAsync()).Ok);
    }

    // N5 — 남(다른 프로세스 흉내)의 쓰기 손잡이가 열려 있으면 기록기는 열지 못한다(FileShare.Read · ReadWrite 로 바꾸면 겹쳐 씀 → 빨강)
    [Fact]
    public async Task AF_23_N5_ForeignWriteHandle_BlocksWriter()
    {
        var w = NewWriter();
        await w.AppendAsync(Rec("first", LocalAuditAction.RestoreStarted));
        w.ShareRetryCount = 3;
        using (new FileStream(_path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
        {
            var ex = await Assert.ThrowsAsync<LocalAuditWriteException>(() => w.AppendAsync(Rec("second", LocalAuditAction.RestoreStarted)));
            Assert.Equal("busy", ex.Reason);
        }
        Assert.Single(Lines(_path));
    }

    // N7 — 가운데 끼워 넣은 쓰레기 줄 → 빨강(사슬은 그대로라 torn_unexplained 만 잡음 · 4 반증 P2) · 못 믿는 줄로 셈
    [Fact]
    public async Task AF_24_N7_MiddleGarbageLine_Red()
    {
        var w = NewWriter();
        for (var i = 0; i < 4; i++) await w.AppendAsync(Rec("op" + i, LocalAuditAction.RestoreStarted));
        var lines = Lines(_path).ToList();
        lines.Insert(2, "inserted garbage line");
        File.WriteAllText(_path, string.Join('\n', lines) + "\n", NoBom);

        var rep = await w.VerifyChainAsync();
        Assert.False(rep.Ok);
        Assert.Equal("torn_unexplained", rep.Reason);
        Assert.Equal(3, rep.FirstBadLineNumber);
        Assert.Equal(1, (await w.ReadOpenOperationsAsync("T1")).UntrustedLines);
    }

    // N8 — 지문 · 사슬은 맞추고 순번만 건너뛴 줄 → seq_gap 빨강 · 못 믿는 줄
    [Fact]
    public async Task AF_25_N8_SeqGap_Red()
    {
        var w = NewWriter();
        var first = await w.AppendAsync(Rec("op1", LocalAuditAction.RestoreStarted));
        File.AppendAllText(_path, Forge(3, "T1", LocalAuditAction.SwapStarted, LocalAuditResult.Started, "op1", null, first.Hash, out _) + "\n", NoBom);

        var rep = await w.VerifyChainAsync();
        Assert.False(rep.Ok);
        Assert.Equal("seq_gap", rep.Reason);
        Assert.Equal(2, rep.FirstBadLineNumber);
        var open = await w.ReadUnfinishedSwapsAsync("T1");
        Assert.Empty(open.Entries);
        Assert.Equal("seq_gap", open.UntrustedReason);
    }

    /// <summary>Flush 인자를 적는 파일 흐름(N9 — 디스크까지 내려 쓰는지).</summary>
    private sealed class RecordingFileStream : FileStream
    {
        public RecordingFileStream(string path) : base(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None) { }
        public List<bool> Flushes { get; } = new();
        public override void Flush(bool flushToDisk)
        {
            Flushes.Add(flushToDisk);
            base.Flush(flushToDisk);
        }
    }

    // N9 — 쓰기 걸음이 디스크까지 내려 쓴다(Flush(true)) — Flush() 로 바꾸면 빨강
    [Fact]
    public void AF_26_N9_WriteStep_FlushesToDisk()
    {
        using var fs = new RecordingFileStream(Path.Combine(_dir, "flush-probe.bin"));
        LocalAuditFileWriter.WriteAndFlush(fs, Encoding.UTF8.GetBytes("line\n"));
        Assert.Contains(true, fs.Flushes);
        Assert.Equal(5, fs.Length);
    }

    // F7 · F10 — 끝 줄을 쓰레기로 덮음(줄바꿈 있음 · 없음) → 빨강 · 다음 쓰기도 표시 줄로 덮어 주지 않음(빨강 그대로)
    [Theory]
    [InlineData("garbage tail line\n")]
    [InlineData("garbage tail line")]
    public async Task AF_27_F7_TailGarbage_RedAndNotLaundered(string garbage)
    {
        var w = NewWriter();
        for (var i = 0; i < 3; i++) await w.AppendAsync(Rec("op" + i, LocalAuditAction.RestoreStarted));
        var lines = Lines(_path).ToList();
        lines.RemoveAt(2);
        File.WriteAllText(_path, string.Join('\n', lines) + "\n" + garbage, NoBom);

        var before = await w.VerifyChainAsync();
        Assert.False(before.Ok);
        Assert.Equal("tail_garbage", before.Reason);
        Assert.False(before.TailTorn);

        await w.AppendAsync(Rec("next", LocalAuditAction.RestoreStarted));
        var after = await w.VerifyChainAsync();
        Assert.False(after.Ok);
        Assert.Equal("torn_unexplained", after.Reason);
        Assert.Equal(3, after.FirstBadLineNumber);
        Assert.DoesNotContain(Lines(_path), l => l.Contains("\tTornTailNoted\t", StringComparison.Ordinal));
    }

    // F6 — 덧말 비밀 모양 거절(JWT · 대소문자 섞인 긴 덩어리 · 주민번호 · 휴대전화) / 정상 덧말 통과
    //   🔴 시험 소스에 자격증명 · 개인정보 「모양」 글자를 그대로 두지 않는다 — 실행 때 조각을 이어 만든다(값은 가짜).
    [Theory]
    [InlineData("jwt")]
    [InlineData("b64")]
    [InlineData("rrn")]
    [InlineData("rrn_nohyphen")]
    [InlineData("phone")]
    public async Task AF_28_F6_DetailSensitiveShapes_Rejected(string shape)
    {
        var six = "90" + "01" + "01";
        var seven = "1" + new string('0', 5) + "7";
        var detail = shape switch
        {
            "jwt" => "e" + "yJ" + "hdr.e" + "yJ" + "body.sig",
            "b64" => string.Concat(Enumerable.Repeat("Ab9", 12)),
            "rrn" => "rrn:" + six + "-" + seven,
            "rrn_nohyphen" => six + seven,
            _ => "tel:" + "01" + "0-" + "0000" + "-" + "0000",
        };
        var w = NewWriter();
        await Assert.ThrowsAsync<ArgumentException>(() => w.AppendAsync(Rec("op", LocalAuditAction.VerifyFailed, LocalAuditResult.Failed, detail: detail)));
        Assert.False(File.Exists(_path));
    }

    [Theory]
    [InlineData("hitpan_backup_20261128_120000.sql")]
    [InlineData("사전백업_20261128.sql")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("committed")]
    [InlineData("3f2504e04f8911d39a0c0305e82c3301")]
    public async Task AF_28b_F6_NormalDetails_Accepted(string detail)
    {
        var w = NewWriter();
        var e = await w.AppendAsync(Rec("op", LocalAuditAction.PreBackupCreated, LocalAuditResult.Succeeded, detail: detail));
        Assert.Equal(detail, e.Record.Detail);
        Assert.True((await w.VerifyChainAsync()).Ok);
    }

    // 4 P9 — 「사용자 못 찾음」 = 계약 낱말 unknown 하나 · 「-」 (표시 줄 자리 값) 거절
    [Fact]
    public async Task AF_29_UnknownUserWord_Accepted_DashRejected()
    {
        var w = NewWriter();
        var ok = await w.AppendAsync(new LocalAuditRecord("T1", LocalAuditWords.UnknownUser, LocalAuditAction.ParentPasswordResetFailed, LocalAuditResult.Failed, "op", "user_not_found"));
        Assert.Equal("unknown", ok.Record.ParentUserId);
        foreach (var bad in new[] { "-", "--", "_" })
            await Assert.ThrowsAsync<ArgumentException>(() =>
                w.AppendAsync(new LocalAuditRecord("T1", bad, LocalAuditAction.ParentPasswordResetFailed, LocalAuditResult.Failed, "op")));
        Assert.Single(Lines(_path));
    }

    // ───────────────────────── 🆕 고침 1 덧 (짧은 재확인 발견 1 · 2 · 3 — 살아남은 변이 M1 · M2 · M3 · M5 를 잡는 시험) ─────────────────────────

    /// <summary>
    /// 파일 자리에 연결을 세운다 — 먼저 파일 심볼릭 링크를 시도하고, Windows 에서 권한이 없으면(개발자 모드 · 관리자 아님)
    /// 같은 자리에 교차점(폴더 연결 · 권한 불필요)을 세운다. 어느 쪽이든 그 자리는 재분석 지점이다. 돌려주는 값 = 어느 쪽으로 쟀는지(실패 문구에 씀).
    /// </summary>
    private static string MakeFileLinkOrJunction(string link, string fileTarget, string fallbackDirTarget)
    {
        string why;
        try
        {
            File.CreateSymbolicLink(link, fileTarget);
            return "파일 심볼릭 링크";
        }
        catch (Exception ex) when (OperatingSystem.IsWindows() && ex is IOException or UnauthorizedAccessException)
        {
            // 판정 값 — 이 PC 권한으로 파일 링크를 못 만듦 → 교차점으로 대신 잰다(빈 catch 아님)
            why = ex.GetType().Name;
        }
        Directory.CreateDirectory(fallbackDirTarget);
        MakeDirLink(link, fallbackDirTarget);
        return "교차점(파일 링크 못 만듦: " + why + ")";
    }

    // 발견 1 · 변이 M1(Guard `EnsureExistingFileSafe` 「파일 자신이 재분석 지점이면 거절」) — 감사 파일 자리 자신이 연결이면
    //   열기 전에 file_reparse_point 로 거절 · 연결 너머 파일에 줄 0. 판정은 internal(InternalsVisibleTo HitPan.Tests)이라 직접 부르고 기록기로도 한 번 더.
    //   폴더 검사가 먼저 통과하도록 기록기가 만든 보호 audit 폴더 안에 연결을 세운다(폴더 까닭으로 초록 · 빨강이 섞이지 않게).
    [Fact]
    public async Task AF_30_M1_FileItselfReparsePoint_Refused()
    {
        var w = NewWriter();
        await w.AppendAsync(Rec("op", LocalAuditAction.RestoreStarted));
        var link = Path.Combine(Path.GetDirectoryName(_path)!, "linked-audit.log");
        var how = MakeFileLinkOrJunction(link, _path, Path.Combine(_dir, "junction-target"));

        var ex = Assert.Throws<LocalAuditWriteException>(() => LocalAuditFolderGuard.EnsureExistingFileSafe(link));
        Assert.True(ex.Reason == "file_reparse_point", how + " → 까닭 " + ex.Reason);

        var linked = new LocalAuditFileWriter(link, NullLogger<LocalAuditFileWriter>.Instance);
        var ex2 = await Assert.ThrowsAsync<LocalAuditWriteException>(() => linked.AppendAsync(Rec("op2", LocalAuditAction.RestoreStarted)));
        Assert.True(ex2.Reason == "file_reparse_point", how + " → 기록기 까닭 " + ex2.Reason);
        Assert.Single(Lines(_path));
    }

    // 발견 2 · 변이 M2(Guard `CheckOwnerAndRulesWindows` 「개별 사용자 아닌 그룹 권한 줄 거절」) — 기록기가 만든 보호 폴더 · 파일에
    //   넓은 그룹 쓰기 줄을 붙이면 거절(파일 = Users → file_acl · 폴더 = Everyone → folder_acl). 자기 소유 임시 폴더라 관리자 권한 불필요.
    //   AF_11 은 「만든 폴더에 넓은 줄이 없다」 만 보고, 이 시험은 「넓은 줄이 있으면 문지기가 거절한다」 를 잰다. 붙이기 전 바닥 = 통과(대조).
    [WindowsOnlyFact]
    [SupportedOSPlatform("windows")]
    public async Task AF_31_M2_BroadGroupWriteRule_FileAndFolder_Refused()
    {
        var w = NewWriter();
        await w.AppendAsync(Rec("op", LocalAuditAction.RestoreStarted));
        var folder = new DirectoryInfo(Path.GetDirectoryName(_path)!);
        var file = new FileInfo(_path);

        // 바닥(대조) — 기록기가 만든 그대로는 통과
        LocalAuditFolderGuard.EnsureFolder(folder.FullName);
        LocalAuditFolderGuard.EnsureExistingFileSafe(file.FullName);

        // ① 파일에 Users 쓰기 줄
        var fileAcl = file.GetAccessControl(AccessControlSections.Access);
        fileAcl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.Write, AccessControlType.Allow));
        file.SetAccessControl(fileAcl);

        var exFile = Assert.Throws<LocalAuditWriteException>(() => LocalAuditFolderGuard.EnsureExistingFileSafe(file.FullName));
        Assert.Equal("file_acl", exFile.Reason);
        var exWrite = await Assert.ThrowsAsync<LocalAuditWriteException>(() => w.AppendAsync(Rec("op2", LocalAuditAction.RestoreStarted)));
        Assert.Equal("file_acl", exWrite.Reason);
        Assert.Single(Lines(_path));

        // ② 폴더에 Everyone 쓰기 줄(상속 없음 — 폴더 판정만)
        var dirAcl = folder.GetAccessControl(AccessControlSections.Access);
        dirAcl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Write, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
        folder.SetAccessControl(dirAcl);

        var exDir = Assert.Throws<LocalAuditWriteException>(() => LocalAuditFolderGuard.EnsureFolder(folder.FullName));
        Assert.Equal("folder_acl", exDir.Reason);
    }

    // 발견 3 · 변이 M5(Writer `IsTornShaped` 순번 칸) · M3(같은 함수 앞 줄 지문 칸) — 「기록기 꼴을 흉내 낸」 끝 조각:
    //   v1 · 칸 꼴은 맞고 <b>순번만</b> 틀림 / <b>앞 줄 지문만</b> 틀림(다 적힌 칸 · 앞부분 칸 둘 다) → tail_garbage 빨강 · 다음 쓰기도 덮어 주지 않음.
    //   대조: 같은 자리에 순번 · 지문이 맞는 앞부분을 두면 전원 끊김 흔적(초록 · TailTorn) — 빨강이 다른 칸 때문이 아님을 먼저 보인다.
    [Theory]
    [InlineData("seq_wrong")]
    [InlineData("prev_wrong_full")]
    [InlineData("prev_wrong_partial")]
    public async Task AF_32_M3M5_TornShapedButWrongSeqOrPrev_TailGarbage(string kind)
    {
        var w = NewWriter();
        await w.AppendAsync(Rec("a", LocalAuditAction.RestoreStarted));
        var last = await w.AppendAsync(Rec("a", LocalAuditAction.SwapStarted));
        var baseText = File.ReadAllText(_path, Encoding.UTF8);

        var wrongPrev = (last.Hash[0] == '0' ? "1" : "0") + last.Hash[1..];
        Assert.NotEqual(last.Hash, wrongPrev);
        Assert.False(last.Hash.StartsWith(wrongPrev[..10], StringComparison.Ordinal));

        // 앞 줄 지문 칸(10번째 칸)까지 다 적은 줄 앞부분 + 꼬리 — Forge 와 같은 칸 순서
        string UpToPrev(long seq, string prev) =>
            string.Join('\t', "v1", seq.ToString(CultureInfo.InvariantCulture), "2026-11-28T01:02:03.0000000Z",
                "T1", "U-parent-1", nameof(LocalAuditAction.SwapCompleted), nameof(LocalAuditResult.Succeeded), "a", string.Empty, prev);

        var (control, bad) = kind switch
        {
            "seq_wrong" => ("v1\t3\t2026-11-28T0", "v1\t9\t2026-11-28T0"),
            "prev_wrong_full" => (UpToPrev(3, last.Hash) + "\tab", UpToPrev(3, wrongPrev) + "\tab"),
            _ => (UpToPrev(3, last.Hash[..10]), UpToPrev(3, wrongPrev[..10])),
        };

        File.WriteAllText(_path, baseText + control, NoBom);
        var ok = await w.VerifyChainAsync();
        Assert.True(ok.Ok, kind + " 대조가 초록이 아님: " + ok.Reason);
        Assert.True(ok.TailTorn, kind + " 대조가 끊김 흔적으로 안 읽힘");

        File.WriteAllText(_path, baseText + bad, NoBom);
        var rep = await w.VerifyChainAsync();
        Assert.False(rep.Ok, kind + " 흉내 조각이 초록");
        Assert.Equal("tail_garbage", rep.Reason);
        Assert.False(rep.TailTorn);

        await w.AppendAsync(Rec("next", LocalAuditAction.RestoreStarted));
        var after = await w.VerifyChainAsync();
        Assert.False(after.Ok, kind + " 다음 쓰기가 흉내 조각을 덮어 초록이 됨");
        Assert.DoesNotContain(Lines(_path), l => l.Contains("\tTornTailNoted\t", StringComparison.Ordinal));
    }
}

/// <summary>🆕 고침 1 — Windows 권한 모델에서만 재는 시험. 다른 OS 에서는 「건너뜀」 으로 드러난다(조용한 초록 아님).</summary>
internal sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows 에서만 잰다(폴더 권한 · 소유자) — 이 OS 는 건너뜀";
    }
}

/// <summary>
/// G11 훑개 — 형식과 그 중첩 형식(컴파일러가 만든 상태기계 · 람다 포함)의 바탕 · 인터페이스 · 필드 · 속성 · 메서드 서명 ·
/// 지역 변수 · <b>IL 이 부르는 멤버 · 형식 토큰</b>을 모두 풀어, 네트워크 이름공간 · 어셈블리를 가리키면 적어 낸다.
/// 🆕 고침 1(3-V F10) — 가리킨 형식이 <c>HitPan*</c> 어셈블리 것이면 그 형식도 같은 방식으로 <b>전이로</b> 훑는다(한 칸만 보던 빈틈 닫음).
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
        var visited = new HashSet<Type>();
        var queue = new Queue<Type>();
        void Follow(Type t)
        {
            var def = t.IsGenericType && !t.IsGenericTypeDefinition ? t.GetGenericTypeDefinition() : t;
            if (visited.Add(def)) queue.Enqueue(def);
        }

        Follow(root);
        while (queue.Count > 0)
        {
            var next = queue.Dequeue();
            foreach (var t in WithNested(next))
            {
                visited.Add(t);
                ScanOne(t, hits, Follow);
            }
        }
        return hits.ToList();
    }

    private static void ScanOne(Type t, HashSet<string> hits, Action<Type> follow)
    {
        Check(t.BaseType, t, hits, follow);
        foreach (var i in t.GetInterfaces()) Check(i, t, hits, follow);
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var f in t.GetFields(all)) Check(f.FieldType, t, hits, follow);
        foreach (var p in t.GetProperties(all)) Check(p.PropertyType, t, hits, follow);
        foreach (var m in t.GetMethods(all).Cast<MethodBase>().Concat(t.GetConstructors(all)))
        {
            if (m is MethodInfo mi) Check(mi.ReturnType, t, hits, follow);
            foreach (var p in m.GetParameters()) Check(p.ParameterType, t, hits, follow);
            var body = m.GetMethodBody();
            if (body is null) continue;
            foreach (var lv in body.LocalVariables) Check(lv.LocalType, t, hits, follow);
            var il = body.GetILAsByteArray();
            if (il is not null) ScanIl(il, m, t, hits, follow);
        }
    }

    private static IEnumerable<Type> WithNested(Type t)
    {
        yield return t;
        foreach (var n in t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            foreach (var x in WithNested(n)) yield return x;
    }

    private static void ScanIl(byte[] il, MethodBase m, Type owner, HashSet<string> hits, Action<Type> follow)
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
                        case Type ty: Check(ty, owner, hits, follow); break;
                        case FieldInfo fi: Check(fi.DeclaringType, owner, hits, follow); Check(fi.FieldType, owner, hits, follow); break;
                        case MethodBase mb:
                            Check(mb.DeclaringType, owner, hits, follow);
                            if (mb is MethodInfo rmi) Check(rmi.ReturnType, owner, hits, follow);
                            foreach (var p in mb.GetParameters()) Check(p.ParameterType, owner, hits, follow);
                            break;
                    }
                    break;
                default: i += 4; break; // InlineI · InlineBrTarget · InlineSig · InlineString · ShortInlineR
            }
        }
    }

    private static void Check(Type? t, Type owner, HashSet<string> hits, Action<Type> follow)
    {
        if (t is null) return;
        if (t.HasElementType) { Check(t.GetElementType(), owner, hits, follow); return; }
        if (t.IsGenericType && !t.IsGenericTypeDefinition)
            foreach (var a in t.GetGenericArguments()) Check(a, owner, hits, follow);
        if (t.IsGenericParameter) return;
        var ns = t.Namespace ?? string.Empty;
        var asm = t.Assembly.GetName().Name ?? string.Empty;
        if (BannedNamespacePrefixes.Any(p => ns.StartsWith(p, StringComparison.Ordinal) || asm.StartsWith(p, StringComparison.Ordinal)))
            hits.Add($"{owner.FullName} → {t.FullName} ({asm})");
        // 🆕 고침 1(F10) — HitPan 형식이면 그 너머까지 훑는다.
        if (asm.StartsWith("HitPan", StringComparison.Ordinal)) follow(t);
    }
}
