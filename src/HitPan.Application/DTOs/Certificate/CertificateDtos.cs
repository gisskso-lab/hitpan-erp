namespace HitPan.Application.DTOs.Certificate;

/// <summary>인증서 목록 표시용 — 민감 정보(PFX, 비밀번호) 제외</summary>
public class CertificateListDto
{
    public string CertId { get; set; } = "";
    public string CertType { get; set; } = "general";      // general/financial/tax
    public string SubjectName { get; set; } = "";
    public string? IssuerName { get; set; }
    public string? SerialNo { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime ValidUntil { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public string Status { get; set; } = "active";          // active/expired/revoked
    public int DaysUntilExpiry { get; set; }
}

/// <summary>인증서 업로드 요청 (파일은 IFormFile로 따로 받음)</summary>
public class UploadCertificateRequest
{
    public string CertType { get; set; } = "general";
    public string SubjectName { get; set; } = "";
    public string? IssuerName { get; set; }
    public string? SerialNo { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime ValidUntil { get; set; }
    public string Password { get; set; } = "";              // PFX 비밀번호 (DPAPI 보호)
    public bool IsPrimary { get; set; } = false;
}

/// <summary>인증서 상태 변경 요청</summary>
public class UpdateCertificateStatusRequest
{
    public string Status { get; set; } = "active";          // active/revoked
    public bool? IsPrimary { get; set; }
    public string? Reason { get; set; }
}
