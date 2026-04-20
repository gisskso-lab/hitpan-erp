namespace HitPan.Application.Services;

/// <summary>파일 업로드 보안 헬퍼 — 확장자/크기/매직바이트 검증</summary>
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

    // 확장자별 매직바이트 시그니처 — 파일명 위변조 방어
    // .csv 등 텍스트 형식은 시그니처 없음 → 검증 스킵
    private static readonly Dictionary<string, byte[][]> MagicSignatures = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"]  = [[0x25, 0x50, 0x44, 0x46, 0x2D]],                               // %PDF-
        [".png"]  = [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]],
        [".jpg"]  = [[0xFF, 0xD8, 0xFF]],
        [".jpeg"] = [[0xFF, 0xD8, 0xFF]],
        [".gif"]  = [
            [0x47, 0x49, 0x46, 0x38, 0x37, 0x61], // GIF87a
            [0x47, 0x49, 0x46, 0x38, 0x39, 0x61]  // GIF89a
        ],
        [".xlsx"] = [[0x50, 0x4B, 0x03, 0x04]],                                     // PK.. (ZIP 컨테이너)
        [".docx"] = [[0x50, 0x4B, 0x03, 0x04]],
        [".zip"]  = [[0x50, 0x4B, 0x03, 0x04]],
        [".xls"]  = [[0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]],             // OLE2
        [".doc"]  = [[0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]],
        [".hwp"]  = [[0xD0, 0xCF, 0x11, 0xE0]]                                      // 구형 OLE2 (신형 HWPX는 .zip로 업로드 권장)
    };

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

    /// <summary>
    /// 파일 업로드 검증 — 파일명·크기·매직바이트(파일 내용) 모두 검사.
    /// 확장자만 위조된 경우(예: malware.exe를 innocent.pdf로 rename) 차단.
    /// content Stream은 검증 후 Position이 원복되어 호출자가 그대로 저장 가능.
    /// </summary>
    public static string? Validate(string fileName, long fileSize, Stream? content)
    {
        // 파일명·크기 선행 검증
        var nameError = Validate(fileName, fileSize);
        if (nameError != null) return nameError;

        if (content == null) return null;
        if (!content.CanSeek) return null; // Seekable 아니면 내용 검증 생략 (가용성 우선)

        var ext = Path.GetExtension(fileName);
        if (!MagicSignatures.TryGetValue(ext, out var signatures)) return null; // .csv 등

        var originalPos = content.Position;
        try
        {
            var maxSigLen = signatures.Max(s => s.Length);
            var header = new byte[maxSigLen];
            var read = content.Read(header, 0, maxSigLen);

            foreach (var sig in signatures)
            {
                if (read < sig.Length) continue;
                var match = true;
                for (var i = 0; i < sig.Length; i++)
                {
                    if (header[i] != sig[i]) { match = false; break; }
                }
                if (match) return null; // 시그니처 일치 통과
            }

            return $"파일 내용이 확장자({ext})와 일치하지 않습니다. 위변조 의심 파일입니다.";
        }
        finally
        {
            content.Position = originalPos; // 저장에 재사용할 수 있게 원복
        }
    }
}
