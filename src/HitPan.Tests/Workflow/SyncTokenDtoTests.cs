using HitPan.Application.DTOs.Sync;
using HitPan.Application.Interfaces;

namespace HitPan.Tests.Workflow;

// Sync 토큰·DTO 검증 (사장님 결재 2026-06-01)
// 헌법 #18·#22 정합 검증: 직원 5컬럼·기기 3컬럼만
public class SyncTokenDtoTests
{
    [Fact(DisplayName = "ST-01: SyncEmployeeDto는 5컬럼만 (헌법 #18)")]
    public void SyncEmployeeDto_should_have_exactly_5_properties()
    {
        var props = typeof(SyncEmployeeDto).GetProperties();
        Assert.Equal(5, props.Length);
        var names = props.Select(p => p.Name).OrderBy(s => s).ToArray();
        Assert.Equal(new[] { "Email", "EmployeeId", "IsActive", "Name", "Position" }, names);
    }

    [Fact(DisplayName = "ST-02: SyncDeviceDto는 3컬럼만 (헌법 #18)")]
    public void SyncDeviceDto_should_have_exactly_3_properties()
    {
        var props = typeof(SyncDeviceDto).GetProperties();
        Assert.Equal(3, props.Length);
        var names = props.Select(p => p.Name).OrderBy(s => s).ToArray();
        Assert.Equal(new[] { "DeviceId", "DeviceName", "RegisteredAt" }, names);
    }

    [Fact(DisplayName = "ST-03: SyncEmployeeDto는 업무 컬럼 절대 금지 (헌법 #18·#22)")]
    public void SyncEmployeeDto_should_not_have_business_columns()
    {
        var props = typeof(SyncEmployeeDto).GetProperties().Select(p => p.Name).ToHashSet();
        var forbidden = new[] { "Salary", "BankAccount", "IdNoHash", "BirthDate", "Phone", "BankName" };
        foreach (var f in forbidden)
        {
            Assert.False(props.Contains(f), $"헌법 #18 위반: {f} 컬럼은 본사로 전송 금지");
        }
    }

    [Fact(DisplayName = "ST-04: SyncTokenResult 토큰·만료 박제")]
    public void SyncTokenResult_should_carry_token_and_expiry()
    {
        var expires = DateTime.UtcNow.AddHours(24);
        var result = new SyncTokenResult("test-token-base64", expires);
        Assert.Equal("test-token-base64", result.Token);
        Assert.Equal(expires, result.ExpiresAt);
    }

    [Fact(DisplayName = "ST-05: SyncEmployeeDto 전체 필드 생성")]
    public void SyncEmployeeDto_should_construct_with_all_fields()
    {
        var dto = new SyncEmployeeDto(
            EmployeeId: "550e8400-e29b-41d4-a716-446655440000",
            Name: "홍길동",
            Email: "hong@hitpan.kr",
            Position: "대리",
            IsActive: true);

        Assert.Equal("550e8400-e29b-41d4-a716-446655440000", dto.EmployeeId);
        Assert.Equal("홍길동", dto.Name);
        Assert.True(dto.IsActive);
    }

    [Fact(DisplayName = "ST-06: SyncDeviceDto 전체 필드 생성")]
    public void SyncDeviceDto_should_construct()
    {
        var registered = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var dto = new SyncDeviceDto("dev-001", "사장님 노트북", registered);
        Assert.Equal("dev-001", dto.DeviceId);
        Assert.Equal("사장님 노트북", dto.DeviceName);
        Assert.Equal(registered, dto.RegisteredAt);
    }
}
