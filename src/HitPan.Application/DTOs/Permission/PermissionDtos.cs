namespace HitPan.Application.DTOs.Permission;

public class UserPermissionDto
{
    public string UserId { get; set; } = "";
    public string UserName { get; set; } = "";
    public string? EmpName { get; set; }
    public string Role { get; set; } = "";
    public List<MenuPermissionDto> Permissions { get; set; } = new();
}

public class MenuPermissionDto
{
    public string MenuCode { get; set; } = "";
    public string MenuName { get; set; } = "";
    public bool CanView { get; set; }
    public bool CanCreate { get; set; }
    public bool CanUpdate { get; set; }
    public bool CanDelete { get; set; }
    public bool CanExport { get; set; }
}

public class SavePermissionsDto
{
    public string UserId { get; set; } = "";
    public List<MenuPermissionDto> Permissions { get; set; } = new();
}
