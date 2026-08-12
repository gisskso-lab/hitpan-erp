using System.Reflection;
using HitPan.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Data;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 양식용지(preprint) 좌표를 지키는 게이트 (사장님 지시 2026-08-12 "양식정보 설정의 양식용지만 해").
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>왜 필요한가</b> — 종전에 이 기능은 <b>좌표를 읽지도 않았다.</b>
/// 고객이 시판 양식지에 자를 대고 위치를 아무리 맞춰 넣어도, 인쇄물은 코드에 고정된
/// 자리로만 나갔다. 오류는 안 났다 — 그냥 칸이 안 맞았다. 그래서 아무도 못 잡았다.
/// </para>
/// <para>
/// ⚠️ 이 자리에서 <b>예외를 던지면 인쇄 자체가 막힌다.</b> 좌표는 거들 뿐이고 문서는 나가야 한다.
/// 고객이 JSON 을 손으로 적기 때문에 오타가 반드시 들어온다 ⇒ 깨진 항목은 건너뛰고
/// 전부 깨졌으면 종전 기본 배치로 되돌아가는 것이 옳은 동작이다.
/// 아래 시험은 그 "되돌아감" 을 못박는다.
/// </para>
/// </remarks>
public class PreprintCoordGuardTests
{
    // ParseFieldCoords 는 내부 구현이라 리플렉션으로 부른다.
    //   ⇒ 공개 API 를 늘리려고 설계를 비틀지 않으면서도 규칙은 지킨다.
    private static readonly MethodInfo Parse =
        typeof(PdfRenderService).GetMethod("ParseFieldCoords",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "ParseFieldCoords 가 없다 — 양식용지 좌표 경로가 사라졌는지 확인해야 한다.");

    private static PdfRenderService NewService() =>
        new(Mock.Of<IDbConnection>(), NullLogger<PdfRenderService>.Instance);

    private static int CountOf(string? json)
    {
        var result = Parse.Invoke(NewService(), new object?[] { json });
        return ((System.Collections.ICollection)result!).Count;
    }

    private static object FirstOf(string json)
    {
        var list = (System.Collections.IEnumerable)Parse.Invoke(NewService(), new object?[] { json })!;
        return list.Cast<object>().First();
    }

    private static T Prop<T>(object o, string name) =>
        (T)o.GetType().GetProperty(name)!.GetValue(o)!;

    [Fact(DisplayName = "좌표를 적으면 그대로 읽힌다")]
    public void 좌표를_적으면_그대로_읽힌다()
    {
        var one = FirstOf("""[{"key":"거래처","x_mm":30,"y_mm":40,"font_pt":11}]""");

        Assert.Equal("거래처", Prop<string>(one, "Key"));
        Assert.Equal(30f, Prop<float>(one, "XMm"));
        Assert.Equal(40f, Prop<float>(one, "YMm"));
        Assert.Equal(11f, Prop<float>(one, "FontPt"));
    }

    [Theory(DisplayName = "고객이 어떻게 적어도 받는다")]
    // 고객은 손으로 적는다 — 표기가 한 가지로 오지 않는다.
    [InlineData("""[{"key":"거래처","x_mm":30,"y_mm":40}]""")]   // 밑줄 표기
    [InlineData("""[{"key":"거래처","xMm":30,"yMm":40}]""")]     // 낙타 표기
    [InlineData("""[{"key":"거래처","x":30,"y":40}]""")]         // 짧은 표기
    [InlineData("""[{"key":"거래처","x":"30","y":"40"}]""")]     // 따옴표 씌운 숫자
    [InlineData("""[{"name":"거래처","x":30,"y":40}]""")]        // key 대신 name
    public void 고객이_어떻게_적어도_받는다(string json)
        => Assert.Equal(1, CountOf(json));

    [Theory(DisplayName = "🔴 잘못 적혀도 인쇄를 막지 않는다")]
    // 🔴 여기서 예외가 나면 고객은 인쇄 자체를 못 한다. 반드시 빈 목록으로 되돌아가야 한다.
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ 이건 JSON 이 아니다")]
    [InlineData("""{"key":"거래처"}""")]        // 목록이 아니라 덩어리 하나
    [InlineData("""["문자열만 들었다"]""")]
    [InlineData("[]")]
    public void 잘못_적혀도_인쇄를_막지_않는다(string? json)
        => Assert.Equal(0, CountOf(json));

    [Theory(DisplayName = "못 쓰는 칸은 건너뛴다")]
    [InlineData("""[{"x_mm":30,"y_mm":40}]""")]                  // 어느 칸인지 없음
    [InlineData("""[{"key":"거래처","y_mm":40}]""")]              // 가로 위치 없음
    [InlineData("""[{"key":"거래처","x_mm":30}]""")]              // 세로 위치 없음
    [InlineData("""[{"key":"거래처","x_mm":-5,"y_mm":40}]""")]    // 종이 왼쪽 밖
    [InlineData("""[{"key":"거래처","x_mm":30,"y_mm":900}]""")]   // 종이 아래 밖
    public void 못_쓰는_칸은_건너뛴다(string json)
        => Assert.Equal(0, CountOf(json));

    [Fact(DisplayName = "성한 칸은 살리고 깨진 칸만 버린다")]
    public void 성한_칸은_살리고_깨진_칸만_버린다()
    {
        // 고객이 세 칸 중 한 칸을 잘못 적었다고 나머지 두 칸까지 버리면 안 된다.
        var json = """
        [
          {"key":"거래처","x_mm":30,"y_mm":40},
          {"key":"일자"},
          {"key":"합계","x_mm":150,"y_mm":250,"align":"right","width_mm":40}
        ]
        """;
        Assert.Equal(2, CountOf(json));
    }

    [Fact(DisplayName = "금액 칸 오른쪽 정렬이 읽힌다")]
    public void 금액칸_오른쪽정렬이_읽힌다()
    {
        // 금액은 오른쪽으로 붙어야 자릿수가 맞는다 — 이 값이 안 읽히면 숫자가 칸을 벗어난다.
        var one = FirstOf("""[{"key":"합계","x_mm":150,"y_mm":250,"align":"RIGHT","width_mm":40}]""");

        Assert.Equal("right", Prop<string>(one, "Align"));   // 대문자로 적어도 통해야 한다
        Assert.Equal(40f, Prop<float>(one, "WidthMm"));
    }

    [Fact(DisplayName = "글꼴 크기를 안 적으면 10pt 로 본다")]
    public void 글꼴크기를_안적으면_기본값()
    {
        var one = FirstOf("""[{"key":"거래처","x_mm":30,"y_mm":40}]""");
        Assert.Equal(10f, Prop<float>(one, "FontPt"));
    }
}
