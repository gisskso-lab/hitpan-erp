using System.Text.Json;

namespace HitPan.Backoffice.Services;

/// <summary>
/// 🔴 <b>20261015작11 — 「백오피스 전체가 죽었다」 사고 봉합 (사장님 지시 「즉시 봉합해」)</b>
///
/// <para><b>[사고]</b> 2026-10-15 아침, 백오피스 모든 화면의 조회가 한꺼번에
/// <c>401 (Unauthorized)</c> 로 떨어졌다. 고객지원·A/S·백과사전·공지 — <b>전부</b>.
/// 그런데 화면은 로그인 상태로 멀쩡히 떠 있었다.</para>
///
/// <para><b>[실측]</b>(2026-10-15 · 운영 읽기만 · #39 준수)
/// <list type="bullet">
///   <item>백오피스 <b>API 는 살아 있다</b> — <c>api/admin/cs/outbound</c> 가 <c>401</c> +
///     <c>WWW-Authenticate: Bearer</c> 로 답한다. 죽은 서비스는 502·504 를 준다.</item>
///   <item>백오피스 <b>화면도 살아 있다</b> — <c>backoffice/login</c> 200,
///     <c>admin/tenants</c> 는 <b>302</b>(로그인으로 보냄). 즉 <b>쿠키가 없으면 제대로 로그인으로 보낸다.</b>
///     🔴 사장님은 로그인으로 안 보내졌다 ⇒ <b>쿠키(<c>hitpan_bo</c>)는 살아 있었다.</b></item>
///   <item>갱신(refresh) 엔드포인트가 <b>0건</b>이다 — 백오피스 API 의 인증 라우트는
///     <c>admin/login</c>·<c>reseller/login</c> <b>둘뿐</b>이고, <c>refresh_token</c> 은
///     「다음 단계」라고 적힌 <b>GUID</b>로 아무도 쓰지 않는다.</item>
/// </list></para>
///
/// <para><b>[진범]</b> 🔴 <b>쿠키는 계속 사는데, 그 안에 넣어 둔 JWT 는 8시간에 죽는다.</b>
/// 로그인할 때 받은 토큰을 <c>access_token</c> <b>클레임으로 쿠키에 굳혀</b> 넣는다
/// (<c>BackofficeAuthController.SignInWithClaimsAsync</c>). 그 쿠키는
/// <c>SlidingExpiration = true</c> 로 <b>계속 갱신되지만</b>, 안에 든 JWT 의 <c>exp</c> 는
/// <b>로그인 시각 + <c>Jwt:ExpiresHours</c>(8)</b> 에 그대로 멈춰 있다. API 는
/// <c>ValidateLifetime = true</c> 로 수명을 본다 ⇒ 8시간이 지나면
/// <b>화면은 로그인 상태 · 모든 조회는 401</b> 이 되고, <b>갱신 경로가 없어 영영 안 풀린다.</b>
/// 사장님 눈에는 이것이 「전체가 죽었다」로 보인다 — 맞는 표현이다. 실제로 아무것도 못 한다.</para>
///
/// <para><b>[봉합]</b> <b>쿠키가 그 안의 JWT 보다 오래 살지 못하게 한다.</b>
/// 토큰이 죽었으면 쿠키를 버리고 <b>로그인 화면으로 보낸다</b> — 「401 이 20곳에 뜨는 화면」 대신
/// <b>「다시 로그인하세요」</b>가 되게. <c>Program.cs</c> 의
/// <c>CookieAuthenticationEvents.OnValidatePrincipal</c> 이 이 판정을 부른다.</para>
///
/// <para>🔴 <b>판정을 여기 둔 이유</b> — <c>Program.cs</c> 의 람다 안에 두면 게이트가 <b>부를 수 없다</b>.
/// 그러면 「글자가 있나」를 세는 가짜 게이트밖에 못 만든다(누적 30회+ 사고). 이 파일은
/// <b>Blazor·ASP.NET 의존 0</b> 인 순수 C# 이라 <c>HitPan.Tests</c> 가 소스만 링크해
/// <b>값을 넣고 판정을 받아</b> 본다(<c>CsVocab.cs</c> 선례).</para>
///
/// <para>🔴 <b>아직 안 고친 것 = 결재 거리</b> — 이 봉합은 <b>8시간마다 다시 로그인</b>하게 만든다.
/// 그걸 없애려면 <b>토큰 갱신 경로</b>(refresh)를 만들어야 하는데, 그건 인증 설계 변경이라
/// 사장님 결재 거리다(결-M). 지금은 <b>조용히 죽는 것을 소리내어 죽게</b> 바꾼 것뿐이다.</para>
///
/// <para>근거: docs/검증/선행/20261015_1V_작11_백오피스전체401_세션사망_선행검증서.md</para>
/// </summary>
public static class BackofficeSessionGuard
{
    /// <summary>쿠키에 굳어 있는 JWT 를 보고 내린 판정.</summary>
    public enum Verdict
    {
        /// <summary>쿠키에 <c>access_token</c> 클레임이 없다 — <b>판정하지 않는다</b>(그 클레임 없이 만든 쿠키일 수 있다).</summary>
        NoTokenClaim,

        /// <summary>토큰을 읽을 수 없다(모양이 JWT 가 아니다) — <b>판정하지 않는다.</b></summary>
        Unreadable,

        /// <summary>토큰에 <c>exp</c> 가 없다 — <b>판정하지 않는다</b>(수명 없는 토큰을 내가 죽었다고 말할 수 없다).</summary>
        NoExpiry,

        /// <summary>아직 살아 있다.</summary>
        Alive,

        /// <summary>🔴 죽었다 — 이 쿠키를 들고 가면 모든 조회가 401 이다.</summary>
        Expired,
    }

    /// <summary>
    /// 🔴 <b>쿠키를 버려야 하나</b> — 이것 하나만 보고 <c>Program.cs</c> 가 움직인다.
    /// <para><b>확실할 때만 버린다.</b> 못 읽거나 수명이 없으면 <see langword="false"/> — 멀쩡한 세션을
    /// 내 판단 착오로 끊지 않는다. 버리는 경우는 <b><see cref="Verdict.Expired"/> 하나뿐</b>이다.</para>
    /// </summary>
    public static bool ShouldDropCookie(string? accessToken, DateTimeOffset utcNow, int graceSeconds = 30)
        => Judge(accessToken, utcNow, graceSeconds) == Verdict.Expired;

    /// <summary>
    /// 토큰의 <c>exp</c> 를 읽어 수명을 판정한다. <b>서명은 검증하지 않는다</b> — 서명 검증은 API 몫이고
    /// (<c>ValidateIssuerSigningKey = true</c>), 화면은 <b>비밀키를 갖지 않는다</b>(#29 · 토큰은 쿠키에서만).
    /// 여기서 하는 일은 <b>「이 토큰을 들고 가도 거절당한다」를 미리 아는 것</b>뿐이다.
    /// <para>🔴 <paramref name="graceSeconds"/> — <c>exp</c> 직전에 미리 끊는다. 안 그러면 사용자가
    /// 누르는 **그 순간** 만료돼 401 을 본다. 늦게 끊는 것보다 조금 일찍 끊는 게 정직하다.</para>
    /// </summary>
    public static Verdict Judge(string? accessToken, DateTimeOffset utcNow, int graceSeconds = 30)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) return Verdict.NoTokenClaim;

        var exp = ReadExpiryUtc(accessToken);
        if (exp is null) return Verdict.Unreadable;
        if (exp == DateTimeOffset.MinValue) return Verdict.NoExpiry;

        var grace = graceSeconds < 0 ? 0 : graceSeconds;
        return exp.Value > utcNow.AddSeconds(grace) ? Verdict.Alive : Verdict.Expired;
    }

    /// <summary>
    /// JWT 의 <c>exp</c>(초 단위 Unix 시각)를 꺼낸다.
    /// <list type="bullet">
    ///   <item><see langword="null"/> = 토큰 모양이 아니거나 못 읽었다.</item>
    ///   <item><see cref="DateTimeOffset.MinValue"/> = 읽었는데 <c>exp</c> 가 없다.</item>
    /// </list>
    /// <para>🔴 <c>System.IdentityModel.Tokens.Jwt</c> 꾸러미를 <b>안 더한다</b> — 화면 프로젝트에
    /// 인증 꾸러미를 새로 들이면 권고(NU1903) 표면이 늘고 #19(경고 0)가 흔들린다.
    /// 필요한 건 <c>exp</c> 숫자 하나라서 <b>안에 있는 것</b>(<c>System.Text.Json</c>)으로 읽는다.</para>
    /// </summary>
    public static DateTimeOffset? ReadExpiryUtc(string? accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) return null;

        var parts = accessToken.Split('.');
        if (parts.Length != 3) return null;           // JWS 는 머리.몸.서명 세 토막이다
        if (parts[1].Length == 0) return null;

        byte[] payload;
        try
        {
            payload = DecodeBase64Url(parts[1]);
        }
        catch (FormatException)
        {
            return null;                               // base64url 이 아니다 — #15: 삼키지 않고 「못 읽음」으로 돌려준다
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("exp", out var expEl)) return DateTimeOffset.MinValue;

            // exp 는 숫자가 정석이지만, 문자열로 담아 보내는 발급자도 있다 — 둘 다 받는다.
            long seconds;
            if (expEl.ValueKind == JsonValueKind.Number)
            {
                if (!expEl.TryGetInt64(out seconds)) return null;
            }
            else if (expEl.ValueKind == JsonValueKind.String)
            {
                if (!long.TryParse(expEl.GetString(), out seconds)) return null;
            }
            else
            {
                return null;
            }

            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(seconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;                           // 말이 안 되는 숫자 — 판정하지 않는다
            }
        }
        catch (JsonException)
        {
            return null;                               // 몸통이 JSON 이 아니다
        }
    }

    /// <summary>base64url(패딩 없음) → 바이트.</summary>
    private static byte[] DecodeBase64Url(string s)
    {
        var b = s.Replace('-', '+').Replace('_', '/');
        switch (b.Length % 4)
        {
            case 2: b += "=="; break;
            case 3: b += "="; break;
            case 1: throw new FormatException("base64url 길이가 1 나머지 — 그런 base64 는 없다");
        }
        return Convert.FromBase64String(b);
    }
}
