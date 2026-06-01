using HitPan.Application.DTOs.Landing;

namespace HitPan.Tests.Workflow;

// 대리점 신청 DTO 검증 (사장님 결재 2026-06-01)
public class ResellerApplicationDtoTests
{
    [Fact(DisplayName = "RA-01: ResellerApplicationRequest 필수 필드 5개")]
    public void Request_should_carry_required_fields()
    {
        var req = new ResellerApplicationRequest(
            CompanyName: "(주)히트판영업",
            RepresentativeName: "김대표",
            ContactName: "이담당",
            ContactEmail: "sales@partner.kr",
            ContactPhone: "010-1234-5678",
            BusinessNo: "123-45-67890",
            Region: "서울",
            SalesChannel: "회계세무사",
            ExpectedCustomers: 20,
            Motivation: "히트판이 좋아서");

        Assert.Equal("(주)히트판영업", req.CompanyName);
        Assert.Equal("sales@partner.kr", req.ContactEmail);
        Assert.Equal(20, req.ExpectedCustomers);
    }

    [Fact(DisplayName = "RA-02: 선택 필드는 null 허용")]
    public void Optional_fields_should_allow_null()
    {
        var req = new ResellerApplicationRequest(
            "회사", "대표", "담당", "test@x.com", "010-0000-0000",
            null, null, null, null, null);
        Assert.Null(req.BusinessNo);
        Assert.Null(req.Region);
        Assert.Null(req.ExpectedCustomers);
    }

    [Fact(DisplayName = "RA-03: ResellerApplicationResult 상태 박제")]
    public void Result_should_carry_status()
    {
        var pending = new ResellerApplicationResult("app-001", "pending", "접수 완료");
        Assert.Equal("pending", pending.Status);
        Assert.Equal("app-001", pending.ApplicationId);

        var rejected = new ResellerApplicationResult("", "rejected", "이메일 형식 오류");
        Assert.Equal("rejected", rejected.Status);
    }

    [Theory(DisplayName = "RA-04: 4가지 상태 enum 지원")]
    [InlineData("pending")]
    [InlineData("reviewing")]
    [InlineData("approved")]
    [InlineData("rejected")]
    public void Result_should_support_4_statuses(string status)
    {
        var result = new ResellerApplicationResult("id", status, "");
        Assert.Equal(status, result.Status);
    }
}
