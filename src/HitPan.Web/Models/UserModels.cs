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
    // 헌법 #35 (사장님 결재 2026-06-04) — 부모/자식 2계층 구분
    public bool IsParent { get; set; }
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

    /// <summary>20261005작5 V5-06 ② — 같은 이름 사원이 있어도 「다른 사람입니다」로 확인했을 때만 true. 서버 CreateUserDto 와 짝(#12).</summary>
    public bool ConfirmDifferentPerson { get; set; }
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

    /// <summary>20261005작5 V5-06 ③ — 같은 이름의 재직·미등록 사원이 이미 있던 행(막지 않고 알린다). 서버 BulkCreateResultDto 와 짝(#12).</summary>
    public List<BulkSameNameWarningModel> SameNameWarnings { get; set; } = new();
}

/// <summary>엑셀 일괄 결과의 같은 이름 경고 한 줄.</summary>
public class BulkSameNameWarningModel
{
    public int Row { get; set; }
    public string Name { get; set; } = "";
    public List<HitPan.Web.Services.LinkableEmployeeModel> Candidates { get; set; } = new();
}

public class BulkRowErrorModel
{
    public int Row { get; set; }
    public string? Email { get; set; }
    public string Reason { get; set; } = "";
}
