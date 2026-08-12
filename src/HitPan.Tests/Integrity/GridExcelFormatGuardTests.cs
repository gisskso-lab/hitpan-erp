using ClosedXML.Excel;
using HitPan.Application.Services;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 자료 내보내기 엑셀 서식을 지키는 게이트 (사장님 지시 2026-08-12 · 경쟁사 조사 반영).
/// </summary>
/// <remarks>
/// <para>
/// 원장·현황·통계·미수 자료는 <b>받아서 바로 가공하거나 거래처에 넘기는</b> 파일이다.
/// 서식이 틀리면 오류가 안 나고 <b>숫자만 틀린 채로 넘어간다</b> — 그래서 시험으로 못박는다.
/// </para>
/// <para>
/// 🔴 특히 다음 셋은 <b>틀린 숫자가 조용히 자리잡는</b> 자리라 반드시 지켜야 한다:
/// ① 합계는 값이 아니라 <c>SUM()</c> 수식 — 고객이 행을 지우면 값 합계는 틀린 채로 남는다
/// ② 잔액 같은 <b>누적</b> 열은 합계를 넣지 않는다 — 다 더한 값은 뜻이 없는데 총 잔액처럼 보인다
/// ③ 자동필터 범위에 합계행이 들면 거래처 하나를 걸러 볼 때 합계가 딸려 사라진다
/// </para>
/// </remarks>
public class GridExcelFormatGuardTests
{
    private static readonly string[] Headers = { "일자", "거래처", "구분", "매출액", "매입액", "잔액" };

    private static List<IReadOnlyList<object?>> SampleRows() => new()
    {
        new object?[] { new DateTime(2026, 8, 1), "가나다, 주식회사", "매출", 1_100_000m, null, 1_100_000m },
        new object?[] { new DateTime(2026, 8, 5), "라마바상사",        "반품",  -300_000m, null,   800_000m },
        new object?[] { new DateTime(2026, 8, 9), "사아자유통",        "매입", null,      500_000m, 300_000m },
    };

    private static XLWorkbook Build(
        string title = "매입매출장",
        IReadOnlyList<string>? headers = null,
        IReadOnlyList<IReadOnlyList<object?>>? rows = null,
        string? subtitle = "2026-08-01 ~ 2026-08-12 · 총 3건")
    {
        var bytes = new ExcelExportService()
            .GenerateGridExcel(title, headers ?? Headers, rows ?? SampleRows(), subtitle);
        return new XLWorkbook(new MemoryStream(bytes));
    }

    /// <summary>머리행(열 제목이 있는 행) 번호를 찾는다.</summary>
    private static int HeaderRow(IXLWorksheet ws, string firstHeader = "일자")
    {
        for (var r = 1; r <= 20; r++)
            if (ws.Cell(r, 1).GetString() == firstHeader) return r;
        throw new Xunit.Sdk.XunitException("머리행을 찾지 못했다 — 서식이 통째로 바뀌었는지 확인해야 한다.");
    }

    [Fact(DisplayName = "파일만 봐도 무슨 자료인지 안다")]
    public void 파일만_봐도_무슨_자료인지_안다()
    {
        // 세무사·거래처에 넘기는 파일이다 — 받는 사람은 우리 화면을 모른다.
        using var wb = Build();
        var ws = wb.Worksheet(1);

        Assert.Equal("매입매출장", ws.Cell(1, 1).GetString());
        Assert.Contains("2026-08-01", ws.Cell(2, 1).GetString());
        Assert.StartsWith("출력:", ws.Cell(3, 1).GetString());
        Assert.Equal("매입매출장", ws.Name);          // 시트 이름도 Sheet1 이 아니어야 한다
    }

    [Fact(DisplayName = "금액·날짜가 제 형태로 들어간다")]
    public void 금액과_날짜가_제형태로_들어간다()
    {
        // 문자열로 들어가면 받는 사람이 다시 숫자로 바꿔야 한다 — 그러면 쓰나 마나다.
        using var wb = Build();
        var ws = wb.Worksheet(1);
        var hr = HeaderRow(ws);

        Assert.Equal(XLDataType.Number, ws.Cell(hr + 1, 4).DataType);
        Assert.Equal(XLDataType.DateTime, ws.Cell(hr + 1, 1).DataType);
        // 쉼표가 든 상호 — CSV 였다면 여기서 열이 밀렸을 값이다.
        Assert.Equal("가나다, 주식회사", ws.Cell(hr + 1, 2).GetString());
    }

    [Fact(DisplayName = "🔴 마이너스가 눈에 들어온다")]
    public void 마이너스가_눈에_들어온다()
    {
        // 반품·손실·미수 차감이 섞인 자료에서 마이너스를 못 보면 대사를 놓친다.
        using var wb = Build();
        var ws = wb.Worksheet(1);
        var hr = HeaderRow(ws);

        Assert.Contains("[Red]", ws.Cell(hr + 1, 4).Style.NumberFormat.Format);
        // 값 자체는 음수 그대로여야 합계가 맞는다(표기만 괄호다).
        Assert.True(ws.Cell(hr + 2, 4).GetDouble() < 0);
    }

    [Fact(DisplayName = "머리행이 고정되고 필터가 걸린다")]
    public void 머리행_고정과_필터()
    {
        // 원장·현황은 행이 길다 — 스크롤하면 무슨 열인지 모르게 된다.
        using var wb = Build();
        var ws = wb.Worksheet(1);
        var hr = HeaderRow(ws);

        Assert.Equal(hr, ws.SheetView.SplitRow);
        Assert.True(ws.AutoFilter.IsEnabled);
    }

    [Fact(DisplayName = "🔴 자동필터에 합계행이 들어가지 않는다")]
    public void 자동필터에_합계행이_안들어간다()
    {
        // 합계행이 필터에 걸리면 거래처 하나를 걸러 볼 때 합계가 같이 사라지거나
        //   엉뚱하게 남는다 — 화면에 보이는 합계가 틀리게 된다.
        using var wb = Build();
        var ws = wb.Worksheet(1);
        var hr = HeaderRow(ws);

        var lastDataRow = hr + SampleRows().Count;
        Assert.Equal(lastDataRow, ws.AutoFilter.Range.LastRow().RowNumber());
    }

    [Fact(DisplayName = "합계 행이 맨 아래 붙는다")]
    public void 합계행이_맨아래_붙는다()
    {
        // 미수금·매입매출장은 총액이 없으면 받는 사람이 손으로 더해야 한다.
        using var wb = Build();
        var ws = wb.Worksheet(1);
        var hr = HeaderRow(ws);
        var tr = hr + SampleRows().Count + 1;

        Assert.Equal("합계", ws.Cell(tr, 1).GetString());
        Assert.True(ws.Cell(tr, 4).Style.Font.Bold);
        Assert.Equal(XLBorderStyleValues.Double, ws.Cell(tr, 4).Style.Border.TopBorder);
    }

    [Fact(DisplayName = "🔴 합계는 값이 아니라 SUM 수식이다")]
    public void 합계는_SUM_수식이다()
    {
        // 값으로 넣으면 고객이 몇 줄 지웠을 때 **틀린 합계가 그대로 남는다.**
        //   틀린 숫자가 남는 것이 없는 것보다 나쁘다.
        using var wb = Build();
        var ws = wb.Worksheet(1);
        var hr = HeaderRow(ws);
        var tr = hr + SampleRows().Count + 1;

        var cell = ws.Cell(tr, 4);
        Assert.True(cell.HasFormula, "합계가 수식이 아니라 값으로 들어갔다.");
        Assert.StartsWith("SUM(", cell.FormulaA1);
        // 1,100,000 + (-300,000) = 800,000 — 음수가 제대로 반영돼야 한다.
        Assert.Equal(800_000d, cell.GetDouble(), 0);
    }

    [Fact(DisplayName = "🔴 잔액처럼 누적인 열은 합계를 내지 않는다")]
    public void 누적열은_합계를_내지_않는다()
    {
        // 잔액을 전부 더한 값은 아무 뜻이 없다. 그런데 합계 자리에 그럴듯하게 찍히면
        //   보는 사람은 그게 총 잔액인 줄 안다 — 조용히 틀리는 자리라 막는다.
        using var wb = Build();
        var ws = wb.Worksheet(1);
        var hr = HeaderRow(ws);
        var tr = hr + SampleRows().Count + 1;

        Assert.False(ws.Cell(tr, 6).HasFormula, "잔액 열에 합계가 붙었다 — 뜻이 없는 숫자다.");
    }

    [Fact(DisplayName = "문자만 든 열에는 합계가 붙지 않는다")]
    public void 문자열에는_합계가_안붙는다()
    {
        using var wb = Build();
        var ws = wb.Worksheet(1);
        var hr = HeaderRow(ws);
        var tr = hr + SampleRows().Count + 1;

        Assert.False(ws.Cell(tr, 2).HasFormula);   // 거래처
        Assert.False(ws.Cell(tr, 3).HasFormula);   // 구분
    }

    [Fact(DisplayName = "숫자 열이 없으면 합계 행 자체가 없다")]
    public void 숫자열이_없으면_합계행도_없다()
    {
        // 거래처 명단 같은 자료에 "합계" 만 덩그러니 붙으면 오히려 이상하다.
        var headers = new[] { "코드", "거래처", "담당자" };
        var rows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "A001", "가나다", "홍길동" },
            new object?[] { "A002", "라마바", "김철수" },
        };

        using var wb = Build("거래처 목록", headers, rows, null);
        var ws = wb.Worksheet(1);
        var hr = HeaderRow(ws, "코드");

        Assert.NotEqual("합계", ws.Cell(hr + rows.Count + 1, 1).GetString());
    }

    [Fact(DisplayName = "자료가 없어도 파일은 나온다")]
    public void 자료가_없어도_파일은_나온다()
    {
        // 조회 결과가 0건일 때 예외가 나면 고객은 무엇이 잘못됐는지 알 수 없다.
        var bytes = new ExcelExportService()
            .GenerateGridExcel("재고현황", Headers, new List<IReadOnlyList<object?>>(), null);

        Assert.True(bytes.Length > 1000);
        Assert.Equal(0x50, bytes[0]);   // PK — 진짜 xlsx
        Assert.Equal(0x4B, bytes[1]);
    }

    [Fact(DisplayName = "행 길이가 짧아도 내보내기가 막히지 않는다")]
    public void 행이_짧아도_막히지_않는다()
    {
        // 한 줄 때문에 내보내기 전체가 실패하면 흐름이 끊긴다(헌법 #20).
        var rows = new List<IReadOnlyList<object?>>
        {
            new object?[] { new DateTime(2026, 8, 1), "가나다" },   // 열이 모자란 행
        };

        var bytes = new ExcelExportService().GenerateGridExcel("매입매출장", Headers, rows, null);
        Assert.True(bytes.Length > 1000);
    }

    [Theory(DisplayName = "엑셀이 못 여는 시트 이름을 만들지 않는다")]
    // ⚠️ 이걸 안 지키면 파일은 만들어지는데 **엑셀이 열지 못한다.**
    [InlineData("업체별 원장 (2026/01~08)")]
    [InlineData("재고현황: 전체")]
    [InlineData("손익[2026]")]
    [InlineData("아주아주아주아주아주아주아주아주아주아주긴제목입니다정말로깁니다")]
    public void 엑셀이_못여는_시트이름을_만들지_않는다(string title)
    {
        using var wb = Build(title);
        var name = wb.Worksheet(1).Name;

        Assert.True(name.Length <= 31, $"시트 이름이 31자를 넘었다: {name.Length}자");
        Assert.DoesNotContain(name, c => ":\\/?*[]".Contains(c));
    }
}
