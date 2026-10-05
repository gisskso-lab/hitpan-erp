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
}

public class BulkRowError
{
    public int Row { get; set; }         // 엑셀 행 번호 (헤더 제외, 1부터)
    public string? Email { get; set; }
    public string Reason { get; set; } = "";
}
