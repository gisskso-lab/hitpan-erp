namespace HitPan.Application.Services;

/// <summary>파일 업로드 보안 헬퍼 — 확장자/크기/MIME 검증</summary>
public static class FileSecurityHelper
{
    // 허용 확장자 (세무사 자료, 엑셀, PDF, 이미지)
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".xlsx", ".xls", ".csv",
        ".png", ".jpg", ".jpeg", ".gif",
        ".doc", ".docx", ".hwp",
        ".zip"
    };

    // 차단 확장자 (실행 파일)
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".js",
        ".msi", ".dll", ".com", ".scr", ".pif",
        ".sh", ".bash", ".php", ".asp", ".aspx", ".jsp"
    };

    // 최대 파일 크기 (10MB)
    public const long MaxFileSize = 10 * 1024 * 1024;

    /// <summary>파일 업로드 검증 — 통과 시 null, 실패 시 에러 메시지</summary>
    public static string? Validate(string fileName, long fileSize)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "파일명이 없습니다.";

        var ext = Path.GetExtension(fileName);

        if (BlockedExtensions.Contains(ext))
            return $"실행 파일({ext})은 업로드할 수 없습니다.";

        if (!AllowedExtensions.Contains(ext))
            return $"허용되지 않는 파일 형식({ext})입니다. PDF, Excel, 이미지 파일만 가능합니다.";

        if (fileSize > MaxFileSize)
            return $"파일 크기 초과 (최대 {MaxFileSize / 1024 / 1024}MB)";

        if (fileSize == 0)
            return "빈 파일은 업로드할 수 없습니다.";

        // 다단 확장자 체크 — 모든 조각을 검사 (payload.exe.txt.pdf 같은 우회 차단)
        var segments = fileName.Split('.');
        foreach (var seg in segments.Skip(1))
        {
            if (BlockedExtensions.Contains("." + seg))
                return $"실행 파일 확장자(.{seg})가 포함되어 있어 업로드할 수 없습니다.";
        }

        return null; // 통과
    }
}
