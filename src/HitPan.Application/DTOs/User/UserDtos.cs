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
}

public class BulkRowError
{
    public int Row { get; set; }         // 엑셀 행 번호 (헤더 제외, 1부터)
    public string? Email { get; set; }
    public string Reason { get; set; } = "";
}
