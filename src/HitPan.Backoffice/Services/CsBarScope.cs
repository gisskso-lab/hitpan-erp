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

        var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // ① /admin/tenants/{한 칸}
        if (segs.Length == 3
            && Eq(segs[0], "admin") && Eq(segs[1], "tenants"))
        {
            var raw = segs[2];
            foreach (var lit in literalSegments)
                if (Eq(raw, lit)) return null;
            var v = Uri.UnescapeDataString(raw);   // 경로 칸의 「+」 는 글자 그대로다(질의와 다름)
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
