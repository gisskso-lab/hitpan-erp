namespace HitPan.Application.Services;

// 20261005작5 — 사원 ↔ 계정 연결의 거절 사유. 컨트롤러가 code 로 응답 모양을 정한다(화면은 code 로 가른다).
//   InvalidOperationException 을 상속하지 않는다 — 기존 catch(InvalidOperationException) 가 400/409 로 뭉개지 않게.

/// <summary>409 로 응답할 연결 거절(퇴사 · 이미 계정 있음 · 동시 연결 · 일반 직무 아님 · 같은 이름 사원 있음).</summary>
public sealed class AccountLinkConflictException : Exception
{
    public string Code { get; }

    public AccountLinkConflictException(string code, string message) : base(message)
    {
        Code = code;
    }
}

/// <summary>403 으로 응답할 거절(2단계 직원이 대표·관리자·본인·권한 가진 직원을 건드림 · §5-3 울타리).</summary>
public sealed class AccountActionForbiddenException : Exception
{
    public string Code { get; }

    public AccountActionForbiddenException(string code, string message) : base(message)
    {
        Code = code;
    }
}

/// <summary>사원을 못 찾음(404).</summary>
public sealed class EmployeeNotFoundForAccountException : Exception
{
    public EmployeeNotFoundForAccountException() : base("사원을 찾을 수 없습니다.") { }
}

/// <summary>[계정 만들기]·「기존 사원 고르기」 대상 사원 한 줄.</summary>
public sealed class LinkableEmployeeDto
{
    public string EmployeeId { get; set; } = string.Empty;
    public string EmpNo { get; set; } = string.Empty;
    public string EmpName { get; set; } = string.Empty;
    public string? DeptName { get; set; }
    public string? Position { get; set; }
    public string? Role { get; set; }
}

/// <summary>엑셀 일괄 — 같은 이름 미등록 사원 경고(막지 않는다 · V5-06 ③).</summary>
public sealed class BulkSameNameWarning
{
    public int Row { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<LinkableEmployeeDto> Candidates { get; set; } = new();
}
