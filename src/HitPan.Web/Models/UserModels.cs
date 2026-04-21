namespace HitPan.Web.Models;

public class UserListModel
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

public class CreateUserModel
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

public class UpdateUserModel
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

public class ResetPasswordResponse
{
    public string TempPassword { get; set; } = "";
    public string Message { get; set; } = "";
}

public class BulkCreateResult
{
    public int TotalRows { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public List<BulkRowErrorModel> Errors { get; set; } = new();
}

public class BulkRowErrorModel
{
    public int Row { get; set; }
    public string? Email { get; set; }
    public string Reason { get; set; } = "";
}
