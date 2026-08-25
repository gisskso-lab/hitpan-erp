using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 작(20260825작15) — <b>dynamic 에 타입을 못박으면 DB 가 다른 타입을 줄 때 500 이 난다.</b>
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>반품확정 500 의 진짜 원인이었다.</b> 사장님이 1.3.13~1.3.16 내내 겪으신 그것이다.
/// </para>
/// <para>
/// <c>(int)(it.is_loss ?? 0)</c> 이 <c>RuntimeBinderException: Cannot convert type 'bool' to 'int'</c> 로 터졌다.
/// MySqlConnector 는 <c>TINYINT(1)</c> 을 <b><c>Boolean</c></b> 으로 돌려준다
/// (연결문자열에 <c>TreatTinyAsBoolean=false</c> 가 없어 기본값 <c>true</c>).
/// <c>dynamic</c> 캐스팅은 런타임 실제 타입이 <b>정확히</b> 맞아야 해서 <c>bool → int</c> 는 예외다.
/// </para>
/// <para>
/// 🔴 <b>세 번의 봉합이 전부 거꾸로였다.</b>
/// 작10·작12 는 <i>"마이그(DB-108)가 안 들어간 DB"</i> 를 고쳤는데,
/// 폴백(<c>0 AS is_loss</c>)은 <c>Int32</c> 라 <b>정상 동작</b>했고
/// 정작 죽는 것은 <b>마이그가 들어간 DB</b>(실컬럼 → <c>Boolean</c>)였다.
/// <b>고친 쪽은 멀쩡했고, 안 고친 쪽이 터지고 있었다.</b>
/// </para>
/// <para>
/// <b>[증상 모양 대조]</b> <c>RuntimeBinderException</c> 은 <c>InvalidOperationException</c>(→400)도
/// <c>MySqlException</c>(1054/1146/1062→400, 1451/1452→409)도 아니라
/// 미들웨어 마지막 <c>catch(Exception)</c> 으로 떨어져 <b>정확히 500</b>.
/// </para>
/// <para>
/// ⚠️ 이 예외는 트랜잭션 <b>안</b>에서 터져 롤백된다 ⇒ <b>원장이 안 남는다</b> ⇒
/// 작13 의 "이미 재고에 반영됨" 가드에도 안 걸린다. 그래서 <b>몇 번을 눌러도 똑같이 500</b> 이었다.
/// </para>
/// </remarks>
public class DynamicCastSafetyGateTests
{
    private static string FindRepoRoot()
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

    private static string Read(params string[] parts)
    {
        var path = Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"{path} 가 있어야 한다");
        return File.ReadAllText(path);
    }

    /// <summary>주석을 걷어낸 실제 코드만 남긴다 — 봉합 설명에 든 낱말이 거짓 초록불을 만들지 않게.</summary>
    private static string CodeLines(string source) =>
        string.Join('\n', source.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l =>
            {
                var t = l.TrimStart();
                return t.Length > 0
                       && !t.StartsWith("//", StringComparison.Ordinal)
                       && !t.StartsWith("*", StringComparison.Ordinal)
                       && !t.StartsWith("/*", StringComparison.Ordinal)
                       && !t.StartsWith("///", StringComparison.Ordinal);
            }));

    private static string SalesService() =>
        CodeLines(Read("src", "HitPan.Application", "Services", "SalesService.cs"));

    /// <summary>
    /// 🔴 <b><c>is_loss</c> 를 <c>(int)</c> 로 직접 캐스팅하면 안 된다.</b>
    /// DB 가 <c>bool</c> 을 주는 순간 <c>RuntimeBinderException</c> → 500.
    /// </summary>
    [Fact]
    public void is_loss_를_int_로_직접_캐스팅하면_안_된다()
    {
        var svc = SalesService();

        Assert.DoesNotContain("(int)(it.is_loss", svc, StringComparison.Ordinal);
        Assert.DoesNotContain("(int)it.is_loss", svc, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>안전 판정 헬퍼를 쓰고 있어야 한다.</b>
    /// 확정·취소 <b>양쪽</b>이다 — 한쪽만 고치면 되돌릴 때 그대로 재발한다(작7 의 비대칭).
    /// </summary>
    [Fact]
    public void 확정과_취소_양쪽이_안전판정을_써야_한다()
    {
        var svc = SalesService();

        Assert.Contains("IsLossValue", svc, StringComparison.Ordinal);

        // 정의 1 + 확정 1 + 취소 1 = 3회 이상.
        var uses = svc.Split("IsLossValue").Length - 1;
        Assert.True(uses >= 3,
            $"IsLossValue 가 확정·취소 양쪽에 쓰여야 한다 — 현재 {uses}회(정의 포함). "
            + "한쪽만 고치면 확정은 되는데 취소에서 같은 500 이 난다.");
    }

    /// <summary>
    /// 🔴 <b>헬퍼가 타입을 하나로 못박지 않아야 한다.</b>
    /// 연결문자열 설정·DB 버전·컬럼 정의에 따라 <c>bool</c>·<c>sbyte</c>·<c>int</c>·<c>long</c> 중
    /// 무엇이든 올 수 있다. <c>Convert.ToInt32</c> 는 이 전부를 받는다.
    /// </summary>
    [Fact]
    public void 안전판정은_모든_수치타입을_받아야_한다()
    {
        var svc = SalesService();

        var at = svc.IndexOf("private static bool IsLossValue", StringComparison.Ordinal);
        Assert.True(at > 0, "IsLossValue 헬퍼가 있어야 한다");

        var body = svc.Substring(at, Math.Min(700, svc.Length - at));

        // 특정 타입으로 못박는 캐스팅이 아니라 Convert 를 써야 한다.
        Assert.Contains("Convert.ToInt32", body, StringComparison.Ordinal);

        // NULL 도 안전해야 한다(컬럼이 없거나 값이 비었을 때).
        Assert.Contains("DBNull", body, StringComparison.Ordinal);
    }
}
