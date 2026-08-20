using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>G-45 ~ G-48</b> — 우편번호 찾기 창이 <b>CSP 에 막히지 않는다</b> (20260820작4 · 설계1).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>무엇이 났나</b> — 사장님 실측 2026-08-20: <i>"업체등록 → 우편번호(우편번호API 연결 끊어짐)"</i>.
/// 코드는 4층(스크립트 로딩 · <c>openDaumPostcode</c> interop · 버튼 · 콜백) 전부 멀쩡했다.
/// 진범은 <b>CSP 에 <c>frame-src</c> 가 없던 것</b>이다 — 6/17(1.2.13) 봉합이 <c>script-src</c> 3종만 넣었다.
/// 카카오 우편번호는 <b>iframe 으로 뜨는데</b>, <c>frame-src</c> 가 없으면
/// <c>default-src 'self'</c> 가 상속되어 바깥 도메인 틀이 차단된다.
/// ⇒ 스크립트는 받아지고 버튼도 눌리는데 <b>창만 안 뜬다.</b>
/// </para>
///
/// <para>
/// 🔴 <b>왜 개발팀이 못 봤나</b> — CSP 블록은 <c>if (!isDevelopment)</c> 안이다.
/// 개발PC 에서는 CSP 자체가 안 붙어 <b>항상 열린다.</b> 터널·운영에서만 끊긴다.
/// ⇒ <i>"개발PC 에서 됩니다"</i> 가 이 건에서는 <b>구조적으로 무의미</b>하다
/// ([[feedback_dev_pc_proves_nothing]] · [[feedback_verify_real_usage_path]]).
/// </para>
///
/// <para>
/// 🟢 <b>초록불이 어디서 오나 — 글자검사가 아니다.</b>
/// 이 시험은 <c>Program.cs</c> 에서 <b>CSP 문자열을 실제로 조립해</b> 지시문 사전으로 <b>파싱</b>하고,
/// <b>브라우저가 하는 것과 같은 판정</b>(해당 지시문이 없으면 <c>default-src</c> 로 폴백)을 돌린다.
/// ⇒ <c>frame-src</c> 줄을 지우면 <b>G-45 가 즉시 FAIL</b> 한다(봉합제거 실측 완료).
/// </para>
///
/// <para>
/// ⚠️ <b>이 시험이 못 하는 것</b> — 실제 브라우저가 창을 띄우는지는 검사하지 못한다.
/// 그것은 <b>Production 실측</b>(설계1 §5 G-2)의 몫이다. 여기서 초록불이라고
/// <i>"우편번호 고쳤다"</i> 로 적지 마라 — <b>"막을 이유가 하나 사라졌다"</b> 까지가 이 시험의 범위다.
/// </para>
/// </remarks>
public sealed class CspFrameSrcGateTests
{
    /// <summary>카카오 우편번호 iframe 문서가 실제로 오는 곳.</summary>
    private const string PostcodeFrameHost = "https://postcode.map.daum.net";

    /// <summary>우편번호 스크립트가 오는 곳.</summary>
    private const string PostcodeScriptHost = "https://t1.daumcdn.net";

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        Assert.True(dir is not null && Directory.Exists(Path.Combine(dir, "src")),
            "레포 루트를 찾아야 한다");
        return dir!;
    }

    /// <summary>
    /// <c>Program.cs</c> 의 CSP 대입문에서 <b>연결된 문자열 리터럴만</b> 뽑아 실제 헤더값을 조립한다.
    /// </summary>
    /// <remarks>
    /// 🔴 주석 줄을 <b>먼저 걷어낸다.</b> 안 걷어내면 설명문에 적힌 <c>frame-src</c> 글자가
    /// 헤더에 있는 것처럼 잡혀 <b>주석만 고쳐도 통과하는 가짜 게이트</b>가 된다.
    /// </remarks>
    private static string BuildCspHeader()
    {
        var path = Path.Combine(RepoRoot(), "src", "HitPan.API", "Program.cs");
        Assert.True(File.Exists(path), $"파일이 있어야 한다: {path}");
        var src = File.ReadAllText(path);

        var start = src.IndexOf("h[\"Content-Security-Policy\"]", StringComparison.Ordinal);
        Assert.True(start >= 0, "Program.cs 에 CSP 대입문이 있어야 한다");

        // ⚠️ 세미콜론으로 끊으면 안 된다 — CSP **문자열 안에** 세미콜론이 잔뜩 들어 있어
        //   (`default-src 'self'; script-src …`) 첫 지시문에서 잘려 나간다.
        //   대입문의 끝은 **다음 대입문**(`h[` 로 시작하는 줄)이다.
        var nextAssign = src.IndexOf("h[\"", start + 1, StringComparison.Ordinal);
        Assert.True(nextAssign > start, "CSP 대입문 다음에 또 다른 헤더 대입이 있어야 한다");

        var assignment = src[start..nextAssign];

        // 주석 제거 — 설명문이 판정에 끼지 않게 한다.
        var codeOnly = string.Join('\n', assignment
            .Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        // 남은 문자열 리터럴을 이어 붙이면 런타임 헤더값과 같아진다.
        var literals = Regex.Matches(codeOnly, "\"([^\"]*)\"")
            .Select(m => m.Groups[1].Value)
            .ToList();
        Assert.True(literals.Count > 1, "CSP 는 여러 리터럴이 이어진 형태여야 한다");

        // 첫 리터럴은 헤더 이름(Content-Security-Policy) 이므로 뺀다.
        return string.Concat(literals.Skip(1));
    }

    /// <summary>CSP 문자열을 브라우저처럼 지시문 사전으로 파싱한다.</summary>
    private static Dictionary<string, string[]> ParseCsp(string header)
    {
        var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in header.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var tokens = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;
            map[tokens[0]] = tokens.Skip(1).ToArray();
        }

        return map;
    }

    /// <summary>
    /// 브라우저 판정을 그대로 따라한다 — 지시문이 없으면 <c>default-src</c> 로 <b>폴백</b>한다.
    /// 이 폴백이 바로 이번 사고의 원인이다(<c>frame-src</c> 없음 ⇒ <c>default-src 'self'</c> ⇒ 차단).
    /// </summary>
    private static bool IsAllowed(Dictionary<string, string[]> csp, string directive, string origin)
    {
        var sources = csp.TryGetValue(directive, out var s)
            ? s
            : csp.TryGetValue("default-src", out var d) ? d : Array.Empty<string>();

        return sources.Any(src =>
            string.Equals(src, origin, StringComparison.OrdinalIgnoreCase)
            || src == "*"
            || string.Equals(src, "https:", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 🔴 <b>G-45 — 이 게이트의 수문장.</b> 우편번호 iframe 도메인이 <b>실제로 허용</b>되는가.
    /// <c>frame-src</c> 줄을 지우면 <c>default-src 'self'</c> 폴백으로 <b>즉시 FAIL</b> 한다.
    /// </summary>
    [Fact]
    public void G45_우편번호_iframe_도메인이_CSP_에_허용된다()
    {
        var csp = ParseCsp(BuildCspHeader());

        Assert.True(IsAllowed(csp, "frame-src", PostcodeFrameHost),
            $"우편번호 창은 iframe 으로 뜬다. {PostcodeFrameHost} 가 frame-src 에 없으면 "
            + "default-src 'self' 로 폴백되어 창이 안 뜬다 (사장님 실측 2026-08-20).");
    }

    /// <summary>
    /// G-46 — 스크립트 도메인도 함께 허용돼야 한다. 둘 중 하나만 있으면 여전히 안 뜬다.
    /// </summary>
    [Fact]
    public void G46_우편번호_스크립트_도메인도_frame_src_에_있다()
    {
        var csp = ParseCsp(BuildCspHeader());

        Assert.True(IsAllowed(csp, "frame-src", PostcodeScriptHost),
            $"{PostcodeScriptHost} 도 frame-src 에 있어야 한다 — 위젯이 두 도메인을 함께 쓴다.");
    }

    /// <summary>
    /// 🔴 G-47 — <b>보안 후퇴 방지.</b> 우편번호를 열겠다고 <c>frame-src</c> 를
    /// <c>https:</c>·<c>*</c> 로 통째 열면 안 된다. 열되 <b>좁게</b> 연다.
    /// </summary>
    [Fact]
    public void G47_frame_src_를_통째로_열지_않았다()
    {
        var csp = ParseCsp(BuildCspHeader());
        Assert.True(csp.TryGetValue("frame-src", out var sources), "frame-src 가 있어야 한다");

        Assert.DoesNotContain("*", sources);
        Assert.DoesNotContain("https:", sources, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 🔴 G-48 — <b>클릭재킹 방어는 그대로다.</b> <c>frame-src</c>(우리가 남을 넣는 것)와
    /// <c>frame-ancestors</c>(남이 우리를 넣는 것)는 <b>반대 방향</b>이다.
    /// 이번 봉합이 <c>frame-ancestors 'none'</c> 을 건드리지 않았음을 지킨다.
    /// </summary>
    [Fact]
    public void G48_frame_ancestors_none_은_그대로다()
    {
        var csp = ParseCsp(BuildCspHeader());

        Assert.True(csp.TryGetValue("frame-ancestors", out var ancestors),
            "frame-ancestors 가 사라지면 클릭재킹에 열린다");
        Assert.Contains("'none'", ancestors);
    }
}
