using System;
using System.Collections.Generic;

namespace HitPan.Backoffice.Services;

/// <summary>
/// 아래 CS 표시줄이 「지금 어느 고객사 화면인가」 를 주소 글자만 보고 정한다 — 20261107작 갈래 B(D6 · D7 · C3).
///
/// <para>🔴 순수 C# — Blazor 의존 0. 시험(<c>CsBarScopeTests</c>)이 이 파일을 링크로 불러서 잰다.</para>
///
/// <para>규칙(이것만):
/// <list type="bullet">
/// <item><c>/admin/tenants/{한 칸}</c> 이고 그 칸이 <see cref="LiteralTenantSegments"/>(글자 주소)에 없으면 → 그 칸(주소 풀기)</item>
/// <item>경로가 정확히 <c>/admin/cs</c> 이고 질의 <c>tenant</c> 가 비지 않으면 → 그 값(주소 풀기)</item>
/// <item>그 밖 전부(<c>/admin/tenants</c> · 칸 둘 이상 · <c>/admin/cs/notice</c> · 대리점 · 대시보드 …) → null = 전체</item>
/// </list>
/// 경로 비교는 대소문자 무시(Blazor 길 찾기와 같게) · 고객사값 글자는 그대로 돌려준다.</para>
/// </summary>
public static class CsBarScope
{
    /// <summary>
    /// <c>/admin/tenants/</c> 아래 **글자 주소**(고객사 값이 아닌 고정 화면) 목록.
    /// 🔴 새 <c>@page "/admin/tenants/글자"</c> 화면을 만들면 여기에 더해야 한다 —
    /// 안 더하면 그 글자를 고객사 값으로 읽는다. 시험이 <c>@page</c> 전수와 대조해 빨강으로 알린다.
    /// </summary>
    public static readonly IReadOnlyList<string> LiteralTenantSegments = new[] { "issue" };

    /// <summary>기준 상대 주소(앞 「/」 있어도 없어도 됨) → 고객사값 또는 null(전체).</summary>
    public static string? Resolve(string? relativeUri) => Resolve(relativeUri, LiteralTenantSegments);

    /// <summary>제외 목록을 바꿔 끼울 수 있는 꼴 — 시험의 대조군용.</summary>
    public static string? Resolve(string? relativeUri, IReadOnlyList<string> literalSegments)
    {
        if (string.IsNullOrWhiteSpace(relativeUri)) return null;

        var s = relativeUri.Trim();
        var hash = s.IndexOf('#');
        if (hash >= 0) s = s[..hash];

        string path, query;
        var qm = s.IndexOf('?');
        if (qm >= 0) { path = s[..qm]; query = s[(qm + 1)..]; }
        else { path = s; query = ""; }

        // ⬛ [낡은 줄 · U1-B] var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // 🔴 20261107작 U1-B — 빈 칸을 접지 않는다(.NET 8 길 찾기: 「admin//tenants/x」 · 「admin/tenants//x」 ·
        //   「admin/tenants/x//」 = 없는 화면). 맨 앞 「/」 1개 · 맨 끝 「/」 1개만 떼고 가운데 빈 칸이 있으면 전체.
        if (path.StartsWith('/')) path = path[1..];
        if (path.EndsWith('/')) path = path[..^1];
        var segs = path.Split('/');
        foreach (var seg in segs)
            if (seg.Length == 0) return null;

        // ① /admin/tenants/{한 칸}
        if (segs.Length == 3
            && Eq(segs[0], "admin") && Eq(segs[1], "tenants"))
        {
            // ⬛ [낡은 줄 · U1-A] var raw = segs[2];
            // ⬛ [낡은 줄 · U1-A] foreach (var lit in literalSegments)
            // ⬛ [낡은 줄 · U1-A]     if (Eq(raw, lit)) return null;
            // ⬛ [낡은 줄 · U1-A] var v = Uri.UnescapeDataString(raw);   // 경로 칸의 「+」 는 글자 그대로다(질의와 다름)
            // ⬛ [낡은 줄 · U1-A] return string.IsNullOrWhiteSpace(v) ? null : v;
            // 🔴 20261107작 U1-A — 날 「/」 로 먼저 가른 칸을 **한 번만** 풀고 그다음 글자 주소와 비교한다
            //   (.NET 8 길 찾기는 「%69ssue」 를 「issue」 로 풀어 고정 화면을 연다). 이중 풀기 금지:
            //   「%2569ssue」 → 「%69ssue」 로 남는다. 경로 칸의 「+」 는 글자 그대로다(질의와 다름).
            var v = Uri.UnescapeDataString(segs[2]);
            foreach (var lit in literalSegments)
                if (Eq(v, lit)) return null;
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }

        // ② 정확히 /admin/cs + ?tenant=
        if (segs.Length == 2 && Eq(segs[0], "admin") && Eq(segs[1], "cs"))
        {
            var v = QueryValue(query, "tenant");
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }

        return null;
    }

    /// <summary>
    /// 🔴 20261107작 I-1a — 서버 고객사 숫자 입구(<c>CountsForTenant</c>)가 받는 고객사값 길이 상한.
    /// 서버는 <c>Trim</c> 뒤 <c>Length &gt; 36</c> 이면 400 이다(G-C-19 가 37 → 400 · 36 → 200 을 잰다) — 같은 값이다.
    /// </summary>
    public const int ServerTenantIdMaxLength = 36;

    /// <summary>
    /// 🔴 20261107작 I-1a — 이 고객사값을 서버에 물으면 400 이 될 길이인가(서버와 같게 <c>Trim</c> 뒤 길이로 잰다).
    /// true 면 <c>CsTenantCountsService</c> 가 서버를 부르지 않고 기존 「고객사를 찾지 못했습니다」 상태로 둔다.
    /// 해석기(<c>Resolve</c>)는 이 값을 자르지 않는다 — 길 찾기는 그 주소를 고객사 상세로 연다.
    /// </summary>
    public static bool ExceedsServerTenantIdLength(string? tenantId)
        => tenantId is not null && tenantId.Trim().Length > ServerTenantIdMaxLength;

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string? QueryValue(string query, string name)
    {
        if (string.IsNullOrEmpty(query)) return null;
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var key = eq >= 0 ? part[..eq] : part;
            if (!Eq(Unescape(key), name)) continue;
            return eq >= 0 ? Unescape(part[(eq + 1)..]) : "";
        }
        return null;
    }

    // 질의 글자의 「+」 는 빈칸이다(폼 인코딩) — 그다음 %xx 를 푼다.
    private static string Unescape(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));
}
