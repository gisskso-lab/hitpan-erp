using HitPan.Backoffice.API.Security;
using Microsoft.AspNetCore.Http;

namespace HitPan.Backoffice.API.Services;

/// <summary>
/// 🔴 ③ <b>행 범위의 유일한 출처</b> (20261007작10 ①사이클 갈래 ㄱ · 설계 §1-2).
///
/// <para>SQL 의 <c>@Rid</c> 는 <b>이 서비스에서만</b> 온다. 쿼리·바디·헤더 경로는 0 이다 — 헌법 #2
/// (<c>tenant_id</c>·<c>reseller_id</c> 는 토큰에서만). 대리점 토큰인데 값이 없으면 <b>던진다</b>(fail-closed).</para>
/// </summary>
public interface IResellerScope
{
    /// <summary>지금 요청의 대리점 범위값. 대리점 토큰이 아니면 null(본사는 전건이 정당).</summary>
    string? CurrentOrNull();

    /// <summary>대리점 범위값을 반드시 받아야 하는 자리. 없으면 <see cref="InvalidOperationException"/>.</summary>
    string Current();

    /// <summary>지금 요청이 대리점 토큰인가.</summary>
    bool IsReseller { get; }
}

/// <inheritdoc cref="IResellerScope"/>
public sealed class ResellerScope : IResellerScope
{
    private readonly IHttpContextAccessor _accessor;

    public ResellerScope(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public bool IsReseller => CurrentOrNull() is not null;

    public string? CurrentOrNull()
    {
        var user = _accessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return null;

        // 🔴 읽는 곳은 User(토큰) 하나다. 쿼리·바디·헤더를 보지 않는다(#2).
        //    BoAccessGuard 가 이미 이 클레임을 DB 의 reseller_id 와 대조해 다르면 403 으로 끊었으므로
        //    (설계 §3-3 「소속 변경 즉시 차단」), 요청이 여기까지 왔다면 토큰값 == DB값 이다.
        //    ⇒ 토큰에서 읽는 것이 「DB reseller_id 로 판정한다」와 같은 결과가 된다.
        var rid = user.FindFirst("reseller_id")?.Value;
        return string.IsNullOrWhiteSpace(rid) ? null : rid;
    }

    public string Current() =>
        CurrentOrNull()
        ?? throw new InvalidOperationException(
            "대리점 범위값이 없다 — 이 자리는 대리점 토큰만 와야 한다(BoAccessGuard 가 보장). "
          + "본사 전용 조회에 IResellerScope.Current() 를 쓰지 마라(설계 §1-2 ③).");

    /// <summary>클레임에서 4값 역할을 읽는다(진단·판정 공용). 모르는 값은 null.</summary>
    public static string? RoleOf(System.Security.Claims.ClaimsPrincipal? user) =>
        BoRoles.Normalize(user?.FindFirst("role")?.Value);
}
