namespace HitPan.Application.DTOs.User;

public class UserListDto
{
    public string UserId { get; set; } = "";
    public string Email { get; set; } = "";
    public string UserName { get; set; } = "";
    public string? EmpName { get; set; }
    public string? Department { get; set; }
    public string? Position { get; set; }
    public string? Phone { get; set; }
    public string Role { get; set; } = "";
    public string AccountType { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime? HireDate { get; set; }
    public DateTime CreatedAt { get; set; }
    // 헌법 #35 (사장님 결재 2026-06-04) — 부모/자식 2계층
    public bool IsParent { get; set; }
}

public class CreateUserDto
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string UserName { get; set; } = "";
    public string? EmpName { get; set; }
    public string? Department { get; set; }
    public string? Position { get; set; }
    public string? Phone { get; set; }
    public string Role { get; set; } = "User";
    public DateTime? HireDate { get; set; }
    public string? Memo { get; set; }

    // 20261005작5 V5-06 ② — 같은 이름의 재직·미등록 사원이 있어도 「다른 사람이다」라고 확인했을 때만 새 사원과 함께 만든다.
    public bool ConfirmDifferentPerson { get; set; }
}

/// <summary>
/// 20261005작5 §3 — 기존 사원에게 계정 만들기(<c>POST /api/users/for-employee</c>). 사원 행은 새로 만들지 않는다.
/// </summary>
public class CreateForEmployeeDto
{
    public string EmployeeId { get; set; } = "";
    /// <summary>로그인 아이디(<c>users.email</c> 에 저장 · 칸 이름만 email). 공백 없이 4자 이상.</summary>
    public string LoginId { get; set; } = "";
    public string Password { get; set; } = "";
    /// <summary>비우면 사원 이름.</summary>
    public string? UserName { get; set; }
    /// <summary>대표·관리자만 뜻이 있다. 2단계 직원이 보내면 무시하고 일반(User)으로 만든다(§5-3).</summary>
    public string? Role { get; set; }
}

public class UpdateUserDto
{
    public string UserName { get; set; } = "";
    public string? EmpName { get; set; }
    public string? Department { get; set; }
    public string? Position { get; set; }
    public string? Phone { get; set; }
    public string Role { get; set; } = "User";
    public bool IsActive { get; set; } = true;
    public DateTime? HireDate { get; set; }
    public string? Memo { get; set; }
}

public class BulkCreateResultDto
{
    public int TotalRows { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public List<BulkRowError> Errors { get; set; } = new();

    // 20261005작3 E-2 — 한도 사전 판정으로 한 줄도 안 넣었을 때 true (F-5)
    public bool SeatFull { get; set; }
    public int Active { get; set; }
    public int Limit { get; set; }

    // 20261005작5 V5-06 ③ — 같은 이름의 재직·미등록 사원이 이미 있던 행(막지 않고 알린다 · 쌍둥이 사원 의심)
    public List<HitPan.Application.Services.BulkSameNameWarning> SameNameWarnings { get; set; } = new();
}

public class BulkRowError
{
    public int Row { get; set; }         // 엑셀 행 번호 (헤더 제외, 1부터)
    public string? Email { get; set; }
    public string Reason { get; set; } = "";
}
