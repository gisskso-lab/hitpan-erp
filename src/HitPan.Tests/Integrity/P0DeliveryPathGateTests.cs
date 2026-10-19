using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HitPan.Backoffice.API.Controllers;
using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Sdk;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 20261019작P0 — <b>전달 경로 게이트</b>. 작업지시서 <c>20261019작P0_전달경로봉합_작업지시서.md</c> §4.
///
/// <para>무엇을 지키나 — 「본사가 만든 자격증명이 서버에 닿는 길」 세 자리:</para>
/// <list type="bullet">
///   <item>G-P0-1-* : <c>scripts/deploy-ncp-secrets.sh</c> 가 <b>섞인 대소문자 키를 실제로 적는지</b></item>
///   <item>G-P0-3-1 : <c>ENV_SECRETS.md</c> 가 닿지 않는 이름을 <b>정정 표시 없이</b> 지시하지 않는지</item>
///   <item>G-P0-4-* : 자격증명 화면이 <b>소비자와 같은 자리</b>를 보는지 (거짓 초록 금지)</item>
/// </list>
///
/// <para>🔴 <b>글자 단언이 아니다.</b> 셸은 <c>bash</c> 로 실제로 돌리고, 화면 판정은
/// <c>Probe</c> 를 직접 불러 잰다. 음성 대조군을 건별로 둔다 — 「막는다」가 아니라
/// 「맞는 것은 통과하고 틀린 것만 막는다」를 재야 게이트다.</para>
///
/// <para>🔴 <c>HITPAN_GATE_SKIP_OK</c> 를 쓰지 않는다. 돌 수 없으면 FAIL 이다.</para>
/// </summary>
// 🔴 20261019작B2 절 A (G-P0-A-1) — HITPAN_BO_JWT_SECRET 전역 직렬화. 기존 줄 삭제 0(#1).
[Collection(BoSignupTenantKeyGateCollection.Name)]
public class P0DeliveryPathGateTests
{
    // ── 공통: 레포 뿌리 찾기 ────────────────────────────────────────────────────
    private static string RepoRoot()
    {
        // 🔴 「installer 폴더가 있나」로 찾으면 안 된다 — 시험 산출물 폴더에도 installer 가
        //   복사돼 들어와서 bin/Release/net8.0 을 뿌리로 잘못 집는다(2026-10-19 실측 FAIL 6건).
        //   선례(AbsenceGuardTests.RepoRoot)와 같이 src 를 기준으로 찾는다.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !(Directory.Exists(Path.Combine(dir.FullName, "src"))
                                 && Directory.Exists(Path.Combine(dir.FullName, "scripts"))))
            dir = dir.Parent;
        if (dir is null) throw new XunitException("레포 뿌리를 못 찾았다(src + scripts 폴더 기준).");
        return dir.FullName;
    }

    private static string ScriptPath() =>
        Path.Combine(RepoRoot(), "scripts", "deploy-ncp-secrets.sh");

    // ══════════════════════════════════════════════════════════════════════════
    //  G-P0-1 — 비밀 배포 스크립트를 **실제로 돌린다**
    //
    //  🔴 스크립트 위쪽에는 root 검사·systemctl 이 있어 시험 기계에서 못 돈다.
    //    그래서 스크립트가 스스로 표시해 둔 구간(P0GATE-BEGIN ~ P0GATE-END)만 떼어 돌린다.
    //    🔴 떼어낸 구간은 제품 파일의 그 줄 그대로다 — 시험이 따로 구현한 게 아니다.
    // ══════════════════════════════════════════════════════════════════════════
    private const string NewRegex = "^([A-Za-z_][A-Za-z0-9_]*)=(.+)$";
    private const string OldRegex = "^([A-Z_][A-Z0-9_]*)=(.+)$";

    private static string ExtractGatedBlock()
    {
        var text = File.ReadAllText(ScriptPath());
        var begin = text.IndexOf("# >>> P0GATE-BEGIN", StringComparison.Ordinal);
        var end = text.IndexOf("# >>> P0GATE-END", StringComparison.Ordinal);
        if (begin < 0 || end < 0 || end <= begin)
            throw new XunitException(
                "deploy-ncp-secrets.sh 의 P0GATE-BEGIN/END 표시가 사라졌다. " +
                "그 표시는 이 게이트가 제품 코드를 그대로 떼어 돌리는 유일한 연결점이다 — 지우면 안 된다.");
        return text.Substring(begin, end - begin).Replace("\r\n", "\n");
    }

    private static string BashExe()
    {
        foreach (var c in new[] { "/bin/bash", "/usr/bin/bash", @"C:\Program Files\Git\bin\bash.exe" })
            if (File.Exists(c)) return c;
        throw new XunitException("bash 를 못 찾았다. 이 게이트는 셸 스크립트를 실제로 돌려야 의미가 있다.");
    }

    private static string ToPosix(string path)
    {
        if (path.Length > 2 && path[1] == ':')
            return "/" + char.ToLowerInvariant(path[0]) + path.Substring(2).Replace('\\', '/');
        return path.Replace('\\', '/');
    }

    private sealed record ShellRun(int ExitCode, string StdOut, string Written);

    /// <summary>
    /// 제품 블록을 돌린다. <paramref name="revertSeal"/> 가 true 면 <b>정규식만</b> 종전 모양으로
    /// 되돌린 사본을 돌린다 — 음성 대조군용이고 <b>제품 파일은 건드리지 않는다</b>.
    /// </summary>
    private static ShellRun RunBlock(string secretBody, bool revertSeal = false)
    {
        var block = ExtractGatedBlock();
        if (revertSeal)
        {
            var reverted = block.Replace(NewRegex, OldRegex);
            if (reverted == block)
                throw new XunitException(
                    $"되돌릴 자리를 못 찾았다 — 제품 블록에 '{NewRegex}' 가 없다. 봉합이 사라졌거나 모양이 바뀌었다.");
            block = reverted;
        }

        var tmp = Directory.CreateTempSubdirectory("p0gate");
        try
        {
            var secretFile = Path.Combine(tmp.FullName, "sec.txt");
            var outFile = Path.Combine(tmp.FullName, "override.conf");
            var runner = Path.Combine(tmp.FullName, "run.sh");
            var utf8 = new UTF8Encoding(false);

            File.WriteAllText(secretFile, secretBody.Replace("\r\n", "\n"), utf8);

            var sh = new StringBuilder();
            sh.Append("OVERRIDE_FILE=").Append(ToPosix(outFile)).Append('\n');
            sh.Append("SECRET_FILE=").Append(ToPosix(secretFile)).Append('\n');
            sh.Append(block);
            File.WriteAllText(runner, sh.ToString(), utf8);

            var psi = new ProcessStartInfo(BashExe())
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = tmp.FullName,
                // 🔴 2026-10-19 실측 FAIL 2건 — 이것 없으면 한글 출력이 깨진다.
                //   윈도우 기본 코드페이지로 읽혀 「등록한 키 개수」가 「?깅줉…」 이 됐다.
                //   🔴 리눅스 CI 는 기본이 UTF-8 이라 **CI 에서는 통과하고 개발 PC 에서만 깨진다** —
                //   그 비대칭이 더 위험하다. 양쪽에서 같은 결과를 내도록 못 박는다.
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false)
            };
            psi.ArgumentList.Add(ToPosix(runner));

            using var p = Process.Start(psi) ?? throw new XunitException("bash 를 못 띄웠다.");
            var so = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(60_000);

            var written = File.Exists(outFile) ? File.ReadAllText(outFile) : "";
            return new ShellRun(p.ExitCode, so, written);
        }
        finally
        {
            try { tmp.Delete(true); } catch { /* 시험 임시방이다. 못 지워도 판정에 영향 0 */ }
        }
    }

    [Fact(DisplayName = "G-P0-1-1 섞인 대소문자 정본 키가 실제로 override 파일에 적힌다")]
    public void G_P0_1_1_섞인_대소문자_키가_적힌다()
    {
        // 코드가 실제로 읽는 정본 이름들이다(실측 2026-10-18).
        var run = RunBlock("License__Pepper=aaa\nBackoffice__BizNoPepper=bbb\nSmtp__Host=ccc\n");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Environment=\"License__Pepper=", run.Written);
        Assert.Contains("Environment=\"Backoffice__BizNoPepper=", run.Written);
        Assert.Contains("Environment=\"Smtp__Host=", run.Written);
    }

    [Fact(DisplayName = "G-P0-1-2 종전 정규식으로 되돌리면 정본 키가 조용히 떨어진다 (음성 대조군)")]
    public void G_P0_1_2_봉합을_빼면_떨어진다()
    {
        // 🔴 봉합이 실제로 일하고 있다는 증거. 봉합을 빼면 FAIL 한다는 것을 게이트가 스스로 보인다.
        var run = RunBlock("License__Pepper=aaa\nHITPAN_OLD_STYLE=bbb\n", revertSeal: true);

        Assert.DoesNotContain("License__Pepper", run.Written);   // 🔴 종전엔 조용히 떨어졌다
        Assert.Contains("HITPAN_OLD_STYLE", run.Written);        // 대문자 키는 그때도 들어갔다
    }

    [Fact(DisplayName = "G-P0-1-3 모양이 틀린 줄이 있으면 종료코드가 0이 아니고 그 줄을 말한다")]
    public void G_P0_1_3_안맞는_줄이면_실패한다()
    {
        var run = RunBlock("License__Pepper=aaa\n망가진줄\n");

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("건너뛴 줄", run.StdOut);
        Assert.Contains("망가진줄", run.StdOut);
    }

    [Fact(DisplayName = "G-P0-1-4 빈 줄·주석만 섞이면 정상 종료한다 (음성 대조군)")]
    public void G_P0_1_4_주석과_빈줄은_통과한다()
    {
        var run = RunBlock("# 주석\n\nLicense__Pepper=aaa\n\n#끝\n");

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("건너뛴 줄", run.StdOut);
        Assert.Contains("등록한 키 개수: 1", run.StdOut);
    }

    [Fact(DisplayName = "G-P0-1-5 출력에 값이 한 글자도 안 찍힌다")]
    public void G_P0_1_5_값은_안_찍힌다()
    {
        const string secret = "ZZtopSecretValue9";
        var run = RunBlock("License__Pepper=" + secret + "\n");

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain(secret, run.StdOut);      // 🔴 값 금지
        Assert.Contains("License__Pepper", run.StdOut); // 이름은 말한다
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  G-P0-4 — 자격증명 화면이 **소비자와 같은 자리**를 보는가
    // ══════════════════════════════════════════════════════════════════════════
    private static CredentialsStatusController MakeController(params (string Key, string Value)[] cfg)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(cfg.Select(c => new KeyValuePair<string, string?>(c.Key, c.Value)))
            .Build();

        var owner = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("role", "owner") }, "test"));
        return new CredentialsStatusController(config, new SilentSender(),
            NullLogger<CredentialsStatusController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = owner }
            }
        };
    }

    [Fact(DisplayName = "G-P0-4-1 화면이 지시하던 옛 환경변수 이름만 있으면 칸은 빨강이다")]
    public void G_P0_4_1_옛_이름만으로는_초록이_안_된다()
    {
        // 🔴 봉합 전에는 이 상황에서 칸이 초록이었다 — 그래서 「지시대로 넣었는데 설치 500」이 났다.
        //   이제는 소비자가 읽는 자리(License:Pepper)가 비어 있으므로 빨강이어야 한다.
        var before = Environment.GetEnvironmentVariable("HITPAN_LICENSE_PEPPER");
        Environment.SetEnvironmentVariable("HITPAN_LICENSE_PEPPER", "looks-like-a-real-value");
        try
        {
            var c = MakeController();   // 설정에는 License:Pepper 가 없다
            var check = c.Probe("License:Pepper", "라이선스 Pepper");

            Assert.False(check.Present);
        }
        finally { Environment.SetEnvironmentVariable("HITPAN_LICENSE_PEPPER", before); }
    }

    [Fact(DisplayName = "G-P0-4-2 소비자가 읽는 설정키가 있으면 칸은 초록이다 (음성 대조군)")]
    public void G_P0_4_2_정본_설정키면_초록이다()
    {
        var c = MakeController(("License:Pepper", "real-pepper-value"));
        var check = c.Probe("License:Pepper", "라이선스 Pepper");

        Assert.True(check.Present);
        Assert.False(check.IsDev);
    }

    [Fact(DisplayName = "G-P0-4-3 화면에 보여주는 이름은 설정키에서 기계적으로 만든다")]
    public void G_P0_4_3_보여주는_이름이_설정키에서_나온다()
    {
        var c = MakeController(("License:Pepper", "v"), ("Backoffice:BizNoPepper", "v"));

        Assert.Equal("License__Pepper", c.Probe("License:Pepper", "x").EnvName);
        Assert.Equal("Backoffice__BizNoPepper", c.Probe("Backoffice:BizNoPepper", "x").EnvName);
    }

    // 🔴 20261019작P0-2 — G-P0-4-4 를 바꿨다. 종전 단언은 지우지 않고 아래에 남긴다(헌법 1번):
    //
    //   [Fact] G_P0_4_4_두_판정기가_같은_모양이다()
    //       Assert.Equal(smtp.GetParameters().Length, probe.GetParameters().Length);
    //
    // 🔴 왜 바꾸나 — 그 단언 자체가 내 틀린 규칙을 굳히고 있었다.
    //   「두 판정기가 같은 모양이어야 한다」가 아니다. 소비자가 두 모양이기 때문이다
    //   (설정키만 보는 소비자 / 환경변수를 먼저 보는 소비자). 같은 모양을 강요하면
    //   env 우선 소비자 쪽이 거짓 빨강이 된다 — 사장님 실측 6칸이 그것이었다.
    //
    // 🟢 지켜야 할 진짜 불변식은 이것이다:
    //      화면이 말하는 환경변수 이름은, 어느 소비자가 실제로 읽는 이름이어야 한다.
    //   이 단언이면 두 사고를 한꺼번에 막는다 —
    //     ① 20261015작B1 의 「화면 HITPAN_JWT_SECRET ↔ 소비자 HITPAN_BO_JWT_SECRET」
    //     ② 20261019작P0 의 내 과잉교정(env 를 통째로 없앤 것)
    [Fact(DisplayName = "G-P0-4-4 화면이 말하는 환경변수 이름은 실제 소비자가 읽는 이름이다")]
    public void G_P0_4_4_화면이_말하는_이름이_실재한다()
    {
        var apiDir = Path.Combine(RepoRoot(), "src", "HitPan.Backoffice.API");
        var screen = Path.Combine(apiDir, "Controllers", "CredentialsStatusController.cs");
        Assert.True(File.Exists(screen), "자격증명 화면 소스가 없다: " + screen);

        // 화면이 Probe 에 넘기는 세 번째 인자(환경변수 이름)를 모은다.
        //
        // 🔴 20261019작B2 절 C — 종전 두 줄은 지우지 않고 아래에 남긴다(헌법 1번):
        //
        //   .Matches(File.ReadAllText(screen),
        //       @"Probe\(\s*""[^""]+""\s*,\s*""[^""]+""\s*,\s*""([A-Za-z_][A-Za-z0-9_]*)""\s*\)")
        //
        // 🔴 왜 바꾸나 — 두 군데가 틀려 있었다.
        //   ① (V-7 · G-P0-C-3) **원문**을 읽어서 주석까지 셌다. 형제 게이트 4-7 은 주석을 벗긴다.
        //      ⇒ 헌법 1번대로 종전 선언을 주석으로 남기면 4-4 만 **거짓 빨강**이 됐다.
        //        지금 이 주석 블록 자체가 바로 그 함정이다 — `LiveSource` 로 같은 규칙을 쓴다.
        //   ② (V-9 확장) `\(\s*""` 라서 **명명된 인자**를 못 읽었다. 형제 게이트와 같은 맹점이고,
        //      못 읽은 칸은 이 게이트도 통째로 빠져나간다 ⇒ 같은 봉합을 여기에도 넣는다.
        var names = System.Text.RegularExpressions.Regex
            .Matches(LiveSource(File.ReadAllText(screen)),
                @"Probe\(\s*" + OptionalArgName + @"""[^""]+""\s*,\s*"
                + OptionalArgName + @"""[^""]+""\s*,\s*"
                + OptionalArgName + @"""([A-Za-z_][A-Za-z0-9_]*)""\s*\)")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(names);   // 하나도 못 찾으면 정규식이 썩은 것이다 — 조용한 통과 금지

        // 소비자 쪽 소스 전부(화면 자신은 뺀다). 화면이 자기를 근거로 삼으면 순환이다.
        var consumerText = new StringBuilder();
        foreach (var f in Directory.EnumerateFiles(apiDir, "*.cs", SearchOption.AllDirectories))
        {
            if (string.Equals(f, screen, StringComparison.OrdinalIgnoreCase)) continue;
            consumerText.Append(File.ReadAllText(f));
        }
        var consumers = consumerText.ToString();

        var phantom = names.Where(n => !consumers.Contains(n, StringComparison.Ordinal)).ToList();
        Assert.True(phantom.Count == 0,
            "🔴 화면이 아무도 읽지 않는 환경변수 이름을 지시한다 — 그 이름을 넣으면 칸만 초록이 되고 기능은 안 된다:\n  "
            + string.Join("\n  ", phantom));
    }

    [Fact(DisplayName = "G-P0-4-5 소비자가 읽는 환경변수 이름이면 칸이 초록이다 (env 우선 소비자)")]
    public void G_P0_4_5_소비자_env_이름이면_초록이다()
    {
        // 🔴 작P0-2 정정의 핵심: env 우선 소비자(InstallerBootstrapController:275 등)는
        //   환경변수만 있어도 실제로 동작한다. 그러면 칸도 초록이어야 한다.
        const string envName = "HITPAN_BOOTSTRAP_TOKEN_KEY";
        var before = Environment.GetEnvironmentVariable(envName);
        Environment.SetEnvironmentVariable(envName, "real-bootstrap-key");
        try
        {
            var c = MakeController();   // 설정에는 Bootstrap:TokenKey 가 없다
            var check = c.Probe("Bootstrap:TokenKey", "부트스트랩", envName);

            Assert.True(check.Present);
            Assert.Equal(envName, check.EnvName);   // 사람이 넣어야 할 이름을 그대로 말한다
        }
        finally { Environment.SetEnvironmentVariable(envName, before); }
    }

    [Fact(DisplayName = "G-P0-4-6 소비자가 안 읽는 이름을 넣어도 칸은 빨강이다 (음성 대조군)")]
    public void G_P0_4_6_엉뚱한_이름으로는_초록이_안_된다()
    {
        // 🔴 20261015작B1 이 적발한 그 사고 모양 그대로다 — 그럴듯하지만 아무도 안 읽는 이름.
        // 🔴 2026-10-19 실측: 이 시험이 처음엔 개발 PC 환경에 끌려 실패했다.
        //   이 PC 에는 진짜 HITPAN_BO_JWT_SECRET 이 깔려 있어서 「빨강이어야 한다」가 초록이 됐다.
        //   ⇒ 재는 동안 그 이름을 **비워** 결정적으로 만든다. 끝나면 되돌린다.
        //   환경에 끌리는 게이트는 기계에 따라 결과가 갈려 게이트가 아니다.
        var wrong = Environment.GetEnvironmentVariable("HITPAN_JWT_SECRET");
        var real = Environment.GetEnvironmentVariable("HITPAN_BO_JWT_SECRET");
        Environment.SetEnvironmentVariable("HITPAN_JWT_SECRET", "looks-real-but-nobody-reads-it");
        Environment.SetEnvironmentVariable("HITPAN_BO_JWT_SECRET", null);
        try
        {
            var c = MakeController();
            var check = c.Probe("Jwt:Secret", "JWT (백오피스)", "HITPAN_BO_JWT_SECRET");

            Assert.False(check.Present);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HITPAN_JWT_SECRET", wrong);
            Environment.SetEnvironmentVariable("HITPAN_BO_JWT_SECRET", real);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  G-P0-3 — 문서가 닿지 않는 이름을 정정 표시 없이 지시하지 않는다
    // ══════════════════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-P0-3-1 ENV_SECRETS.md 에 BACKOFFICE_ 가 정정 표시 없이 나오지 않는다")]
    public void G_P0_3_1_문서가_틀린_이름을_지시하지_않는다()
    {
        var path = Path.Combine(RepoRoot(), "docs", "개발", "설치배포", "ENV_SECRETS.md");
        Assert.True(File.Exists(path), "문서가 없다: " + path);

        var bad = new List<string>();
        foreach (var raw in File.ReadAllLines(path))
        {
            if (!raw.Contains("BACKOFFICE_", StringComparison.Ordinal)) continue;

            // 정정 표시로 인정하는 것: 취소선(~~) · 주석(#) · 설명 문맥(「닿」 · 작P0)
            var corrected = raw.Contains("~~", StringComparison.Ordinal)
                         || raw.TrimStart().StartsWith("#", StringComparison.Ordinal)
                         || raw.Contains("닿", StringComparison.Ordinal)
                         || raw.Contains("작P0", StringComparison.Ordinal);
            if (!corrected) bad.Add(raw.Trim());
        }

        Assert.True(bad.Count == 0,
            "🔴 닿지 않는 이름을 정정 표시 없이 지시하는 줄이 있다 — 표부터 읽는 사람이 재발시킨다:\n  "
            + string.Join("\n  ", bad));
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  🔴 20261019작P0-3 — 유령 칸을 영구히 막는다
    //
    //  사장님 결재로 `Jwt:Key` 칸을 없앴다. 그런데 **왜 그 칸이 거기 있었는지**를
    //  아무도 막지 않으면 다음 사람이 또 만든다. 실제로 둘이나 있었다:
    //    · Jwt:Key           — 읽는 소비자 0건
    //    · Backoffice:JwtKey — 읽는 소비자 0건 (실제는 Jwt:Secret)
    //  그 둘은 「빨강」도 「초록」도 뜻이 없는 칸이었고, 사장님이 그걸 보고 판단하셨다.
    //
    //  🔴 한계를 적어 둔다: `Jwt:Secret` 은 소비자가 분할 조회한다
    //    (`GetSection("Jwt")` 다음 `jwt["Secret"]`) ⇒ 설정키 리터럴이 소비자 소스에 없다.
    //    그래서 「설정키 **또는** env 이름」 중 하나만 있으면 통과로 본다.
    //    더 조이면 정상 코드를 막는다. 덜 조이면 유령이 다시 산다.
    // ══════════════════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-P0-4-7 모든 칸은 어느 소비자가 실제로 읽는 자리를 가리킨다 (유령 칸 금지)")]
    public void G_P0_4_7_유령_칸이_없다()
    {
        var apiDir = Path.Combine(RepoRoot(), "src", "HitPan.Backoffice.API");
        var screen = Path.Combine(apiDir, "Controllers", "CredentialsStatusController.cs");
        var screenText = File.ReadAllText(screen);

        // 주석 줄은 뺀다 — 종전 선언을 주석으로 남기는 것이 이 레포 규칙(헌법 1번)이라
        // 주석까지 세면 지운 유령이 영원히 살아 있는 것으로 읽힌다.
        var live = string.Join("\n",
            screenText.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        // 🔴 20261019작B2 절 C — G-P0-C-3. 위 두 줄은 **한 글자도 손대지 않았다**(헌법 1번 · 삭제 0).
        //   대신 형제 게이트 4-4 가 쓰는 공용 규칙 `LiveSource` 가 **이것과 똑같은지**를 여기서 문다.
        //   이 단언이 깨지면 두 게이트의 주석 제거 규칙이 또 갈라진 것이고,
        //   그때가 바로 4-4 가 거짓 빨강이 되는 순간이다(V-7 사고의 재발).
        Assert.Equal(live, LiveSource(screenText));

        // 🔴 20261019작B2 절 C(V-9) — 종전 정규식은 지우지 않고 아래에 남긴다(헌법 1번):
        //
        //   @"Probe(?:Smtp)?\(\s*""([^""]+)""\s*,\s*""[^""]+""(?:\s*,\s*""([A-Za-z_][A-Za-z0-9_]*)"")?\s*\)"
        //
        // 🔴 왜 바꾸나 — `\(\s*""` 로 시작해서 **명명된 인자**(`Probe(configKey: "...")`)를 못 읽었다.
        //   못 읽은 칸은 `panels` 에 안 담기고 ⇒ 아래 유령 검사를 **통째로 빠져나간다**.
        //   그게 거짓 초록의 뿌리였다. 바닥값(`>= 10`)은 원인이 아니라 **안 선 안전망**이었다.
        var panels = System.Text.RegularExpressions.Regex
            .Matches(live,
                @"Probe(?:Smtp)?\(\s*" + OptionalArgName + @"""([^""]+)""\s*,\s*"
                + OptionalArgName + @"""[^""]+"""
                + @"(?:\s*,\s*" + OptionalArgName + @"""([A-Za-z_][A-Za-z0-9_]*)"")?\s*\)")
            .Select(m => (Key: m.Groups[1].Value, Env: m.Groups[2].Success ? m.Groups[2].Value : null))
            .ToList();

        // 🔴 조용한 통과 금지 — 칸이 10개 미만이면 정규식이 썩은 것이다.
        Assert.True(panels.Count >= 10,
            $"칸을 {panels.Count} 개만 찾았다. 정규식이 화면 모양을 못 따라간다 — 게이트가 거짓 초록이 된다.");

        // 🔴 20261019작B2 절 C — G-P0-C-1. 위 `>= 10` 바닥값은 지우지 않고 그대로 둔다(헌법 1번).
        //   왜 더 조이나 — 바닥값은 **안 선 안전망**이었다. 칸이 11개인데 단언이 `>=10` 이라,
        //   정규식이 못 읽는 모양(명명된 인자 등)으로 칸을 하나 심어도 11 로 통과했다.
        //   ⇒ 「정규식이 읽은 칸 수」와 「화면이 실제로 부른 판정기 호출 수」를 **동치**로 묶는다.
        var probeCount = ProbeCallCount(live);
        Assert.True(panels.Count == probeCount,
            $"판정기 호출 {probeCount} 건인데 정규식은 칸 {panels.Count} 건만 읽었다 — "
            + "못 읽은 호출이 있으면 그 칸은 이 게이트를 통째로 빠져나간다(유령 칸이 산다).");

        var consumerText = new StringBuilder();
        foreach (var f in Directory.EnumerateFiles(apiDir, "*.cs", SearchOption.AllDirectories))
        {
            if (string.Equals(f, screen, StringComparison.OrdinalIgnoreCase)) continue;
            consumerText.Append(File.ReadAllText(f));
        }
        var consumers = consumerText.ToString();

        var phantom = panels
            .Where(p => !consumers.Contains(p.Key, StringComparison.Ordinal)
                     && (p.Env is null || !consumers.Contains(p.Env, StringComparison.Ordinal)))
            .Select(p => p.Env is null ? p.Key : $"{p.Key} (env {p.Env})")
            .ToList();

        Assert.True(phantom.Count == 0,
            "🔴 아무 소비자도 읽지 않는 자리를 가리키는 칸이 있다 — 그 칸의 빨강·초록은 뜻이 없다:\n  "
            + string.Join("\n  ", phantom));
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  🔴 20261019작B2 절 C — G-P0-C-3 보조. **4-4 와 4-7 이 같은 주석 제거 규칙을 쓴다.**
    //
    //  왜 공용인가 — 종전에는 4-7 만 주석을 벗기고 4-4 는 **원문**을 썼다.
    //  그래서 「종전 선언을 주석으로 남긴다」(헌법 1번)를 지키면 4-4 가 지운 유령을
    //  살아 있는 것으로 읽어 **거짓 빨강**이 됐다. 규칙이 한 곳에 있어야 다시 안 갈린다.
    //  🔴 음성 대조군: 주석 처리된 3-인자 `Probe(` 1건을 심으면 봉합 전에는 4-4 만 FAIL 했다.
    // ══════════════════════════════════════════════════════════════════════════
    private static string LiveSource(string text)
        => string.Join("\n",
            text.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    //  🔴 20261019작B2 절 C(V-9) — 명명된 인자 접두사(`configKey:` `display:` `envName:`).
    //    C# 은 `Probe(configKey: "x", display: "y")` 를 똑같이 허용한다. 정규식이 이걸 못 읽으면
    //    그 칸은 유령 검사를 빠져나간다 ⇒ **인자 이름을 고정하지 않고** 식별자 일반형으로 받는다.
    private const string OptionalArgName = @"(?:[A-Za-z_][A-Za-z0-9_]*\s*:\s*)?";

    // ══════════════════════════════════════════════════════════════════════════
    //  🔴 20261019작B2 절 C — G-P0-C-1 보조. 화면이 부른 판정기 호출 수를 센다.
    //
    //  🔴 제외 규칙 (이 한 줄이 없으면 CI 즉시 빨강 — 실측 `13 != 11`):
    //    주석 벗긴 본문의 `Probe(`·`ProbeSmtp(` 등장 13건 중 2건은 **메서드 선언**이다
    //    (`internal CredentialCheck Probe(...)` · `private CredentialCheck ProbeSmtp(...)`).
    //    선언은 칸이 아니므로 뺀다 ⇒ 호출 11 == 칸 11.
    //  🔴 선언을 「줄 번호」나 「2」 같은 상수로 빼지 않는다 — 반환형 토큰으로 가려낸다.
    //    선언이 하나 늘거나 줄면 이 식이 따라가고, 숫자로 박아 두면 그때 거짓 초록이 된다.
    // ══════════════════════════════════════════════════════════════════════════
    private static int ProbeCallCount(string live)
    {
        var all = System.Text.RegularExpressions.Regex
            .Matches(live, @"\bProbe(?:Smtp)?\s*\(").Count;
        var declarations = System.Text.RegularExpressions.Regex
            .Matches(live, @"\bCredentialCheck\s+Probe(?:Smtp)?\s*\(").Count;
        return all - declarations;
    }

    private sealed class SilentSender : IEmailSender
    {
        public Task<bool> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> SendWithAttachmentAsync(string toEmail, string subject, string htmlBody,
            byte[] attachment, string attachmentFileName, string attachmentMimeType, CancellationToken ct = default)
            => Task.FromResult(false);
    }
}
