using HitPan.Application.Interfaces;

namespace HitPan.Tests.Workflow;

// 환불 DTO 검증 (사장님 결재 2026-06-01)
public class RefundDtoTests
{
    [Fact(DisplayName = "RF-01: AdminRefundResult 성공 저장")]
    public void AdminRefundResult_should_carry_success()
    {
        var ok = new AdminRefundResult(true, "환불 완료", 59000m);
        Assert.True(ok.Success);
        Assert.Equal(59000m, ok.RefundedAmount);
    }

    [Fact(DisplayName = "RF-02: AdminRefundResult 실패 저장")]
    public void AdminRefundResult_should_carry_failure()
    {
        var fail = new AdminRefundResult(false, "이미 환불됨", null);
        Assert.False(fail.Success);
        Assert.Null(fail.RefundedAmount);
        Assert.Equal("이미 환불됨", fail.Message);
    }

    [Theory(DisplayName = "RF-03: 환불 금액은 음수 불가")]
    [InlineData(0)]
    [InlineData(29000)]
    [InlineData(59000)]
    [InlineData(100000)]
    public void AdminRefundResult_amount_should_be_non_negative(decimal amount)
    {
        var result = new AdminRefundResult(true, "ok", amount);
        Assert.True(result.RefundedAmount >= 0);
    }
}
