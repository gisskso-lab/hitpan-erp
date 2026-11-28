using System;
using System.IO;
using System.Text.RegularExpressions;
using HitPan.Application.Services;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>W-G1-51 후1 §15-9 (K13 · K14 가)</b> — 거래명세서 수정 저장 때 머리 UPDATE 가 1행이 아니면 <b>저장하지 않는다</b>.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>무엇이 났나</b> — <c>UpdateDeliveryAsync</c> 의 상태 검사는 트랜잭션 밖이고,
/// 머리 UPDATE(<c>AND status='draft'</c>)의 영향 행 수를 버렸다. 검사 뒤 · UPDATE 전에 다른 곳에서 확정되면
/// UPDATE 는 0행인데 줄 DELETE(상태 조건 없음) · INSERT 는 그대로 돌아 <b>확정 문서 줄 ≠ 원장</b>.
/// </para>
/// <para>
/// ⚠️ <b>이 시험이 못 하는 것</b> — 경합을 한 줄기 시험으로 재현할 갈고리가 제품에 없다(서비스 비가상 · 임시 표 트리거 불가).
/// 그래서 소스 본문의 꼴만 본다(선례 <c>SalesChainLinkGateTests</c> G3 의 Slice 꼴). 실제 겹침 측정은 EVF 혼돈 영역 별 측정의 몫이다.
/// DB 를 쓰지 않는 시험이라 build 잡에서 돈다.
/// </para>
/// </remarks>
public sealed class DeliveryUpdateRowCountGateTests
{
    private const string ExpectedMessage =
        "이미 확정되었거나 다른 곳에서 바뀐 거래명세서라 저장하지 않았습니다. 화면을 새로 열어 확인해 주세요.";

    [Fact]
    public void R1_머리UPDATE_결과가_1이_아니면_줄을_지우기_전에_던진다()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(),
            "src", "HitPan.Application", "Services", "SalesService.cs"));

        var body = Slice(src, "public async Task UpdateDeliveryAsync", "public async Task DeleteDeliveryAsync");

        // ① 머리 UPDATE(updateSql) 실행 결과를 변수로 받는다 — 버리지 않는다
        var exec = Regex.Match(body,
            @"var\s+(?<v>\w+)\s*=\s*await\s+_db\.ExecuteAsync\(\s*new\s+CommandDefinition\(\s*updateSql\b");
        Assert.True(exec.Success, "머리 UPDATE(updateSql) 실행 결과를 변수로 받아야 한다");
        var v = exec.Groups["v"].Value;

        // ② 그 변수가 1 이 아니면 상수로 던진다
        var guard = Regex.Match(body,
            @"if\s*\(\s*" + Regex.Escape(v) + @"\s*!=\s*1\s*\)\s*\{?\s*throw\s+new\s+InvalidOperationException\(\s*DeliveryChangedElsewhereMessage\s*\)");
        Assert.True(guard.Success, $"'{v}' 가 1 이 아니면 DeliveryChangedElsewhereMessage 로 던져야 한다");
        Assert.True(guard.Index > exec.Index, "던짐은 머리 UPDATE 실행 뒤에 있어야 한다");

        // ③ 그 던짐이 줄 DELETE 보다 앞 — 0행이면 줄을 건드리지 않는다
        var del = body.IndexOf("DELETE FROM sales_delivery_items", StringComparison.Ordinal);
        Assert.True(del >= 0, "줄 DELETE 앵커를 찾지 못했다");
        Assert.True(guard.Index < del, "던짐이 줄 DELETE(DELETE FROM sales_delivery_items) 보다 앞이어야 한다");
    }

    [Fact]
    public void R2_상수_글자가_지시대로다()
    {
        Assert.Equal(ExpectedMessage, SalesService.DeliveryChangedElsewhereMessage);
    }

    private static string Slice(string src, string from, string to)
    {
        var i = src.IndexOf(from, StringComparison.Ordinal);
        Assert.True(i >= 0, $"앵커를 찾지 못했다: {from}");
        var j = src.IndexOf(to, i + from.Length, StringComparison.Ordinal);
        return j < 0 ? src[i..] : src[i..j];
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "HitPan.Application")))
            dir = dir.Parent;
        Assert.True(dir is not null, "레포 루트를 찾아야 한다");
        return dir!.FullName;
    }
}
