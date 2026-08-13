using HitPan.Application.Interfaces;

namespace HitPan.API.Services;

/// <summary>
/// 메신저 파일을 디스크에 둔다. 작(2026-08-13) 그룹웨어 단계9.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>실행파일을 막는다.</b> 사내라도 한 명이 감염되면 메신저가
/// <b>전 직원에게 퍼뜨리는 통로</b>가 된다. 업무 파일(엑셀·한글·PDF·이미지·압축·CAD)은 전부 된다.
/// </para>
/// <para>
/// 🔴 <b>확장자만 보지 않는다.</b> <c>.exe</c> 를 <c>.xlsx</c> 로 바꿔도 막아야 하므로
/// <b>파일 앞부분(시그니처)</b> 도 함께 본다.
/// </para>
/// <para>
/// 🔴 <b>저장 이름은 우리가 정한다.</b> 원래 이름을 그대로 쓰면
/// <c>..\..\</c> 같은 경로 조작이 들어온다. 파일명은 <c>file_id</c>, 원래 이름은 DB 에만 둔다.
/// </para>
/// <para>
/// 🔴 <b>테넌트별로 폴더가 갈린다</b> — 헌법 #2(테넌트 격리)를 파일에도 적용한다.
/// </para>
/// </remarks>
public sealed class ChatFileStore : IChatFileStore
{
    private readonly string _rootPath;
    private readonly ILogger<ChatFileStore> _logger;

    /// <summary>
    /// 바로 도는 파일. 확장자로 1차 차단.
    /// </summary>
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".com", ".scr", ".pif", ".ps1", ".psm1",
        ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".jar",
        ".msi", ".msp", ".dll", ".sys", ".hta", ".cpl", ".reg", ".lnk",
        ".inf", ".scf", ".application", ".gadget", ".msc"
    };

    /// <summary>
    /// 파일 앞부분 시그니처. 🔴 이름을 바꿔도 여기서 걸린다.
    /// </summary>
    private static readonly (byte[] Magic, string Kind)[] BlockedSignatures =
    {
        (new byte[] { 0x4D, 0x5A }, "실행파일(MZ)"),                     // exe · dll · scr
        (new byte[] { 0x7F, 0x45, 0x4C, 0x46 }, "실행파일(ELF)"),        // 리눅스 실행
        (new byte[] { 0xCA, 0xFE, 0xBA, 0xBE }, "실행파일(Java/Mach-O)")  // class · Mach-O
    };

    public ChatFileStore(IConfiguration configuration, ILogger<ChatFileStore> logger)
    {
        _logger = logger;

        // 데이터 폴더 아래에 둔다. ⚠️ 백업 범위에 이 폴더가 들어가야 한다
        // (DB 만 백업하면 파일은 안 따라온다).
        var configured = configuration["ChatFiles:RootPath"];
        _rootPath = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "chat-files")
            : configured!;
    }

    public async Task<StoredChatFile> SaveAsync(string tenantId, string originalName,
        Stream content, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(originalName ?? string.Empty);

        // ① 확장자 검사
        if (!string.IsNullOrEmpty(extension) && BlockedExtensions.Contains(extension))
            throw new InvalidOperationException(
                "프로그램 파일은 보낼 수 없습니다. 압축해서 보내 주세요.");

        // ② 시그니처 검사 — 🔴 이름을 바꿔도 걸린다.
        var head = new byte[4];
        var read = 0;
        if (content.CanSeek) content.Position = 0;
        while (read < head.Length)
        {
            var n = await content.ReadAsync(head.AsMemory(read, head.Length - read), ct).ConfigureAwait(false);
            if (n == 0) break;
            read += n;
        }

        foreach (var (magic, kind) in BlockedSignatures)
        {
            if (read >= magic.Length && head.Take(magic.Length).SequenceEqual(magic))
            {
                _logger.LogWarning(
                    "메신저 파일 차단 — 확장자를 바꾼 {Kind}. 테넌트 {TenantId}, 이름 {Name}",
                    kind, tenantId, originalName);
                throw new InvalidOperationException(
                    "프로그램 파일은 보낼 수 없습니다. 파일 이름을 바꿔도 보낼 수 없습니다.");
            }
        }

        if (content.CanSeek) content.Position = 0;

        var fileId = Guid.NewGuid().ToString();
        var safeExtension = SanitizeExtension(extension);

        // 🔴 테넌트별 · 월별 폴더. 한 폴더에 수만 개가 쌓이지 않게 한다.
        var relativeDir = Path.Combine("chat-files", SanitizeSegment(tenantId),
            DateTime.Now.ToString("yyyyMM"));
        var relativePath = Path.Combine(relativeDir, fileId + safeExtension);

        var absoluteDir = Path.Combine(_rootPath, SanitizeSegment(tenantId),
            DateTime.Now.ToString("yyyyMM"));
        Directory.CreateDirectory(absoluteDir);

        var absolutePath = Path.Combine(absoluteDir, fileId + safeExtension);

        await using (var target = new FileStream(absolutePath, FileMode.CreateNew, FileAccess.Write,
                         FileShare.None, 81920, useAsync: true))
        {
            await content.CopyToAsync(target, ct).ConfigureAwait(false);
        }

        return new StoredChatFile { FileId = fileId, RelativePath = relativePath.Replace('\\', '/') };
    }

    public Stream? OpenRead(string relativePath)
    {
        var absolutePath = ResolveAbsolute(relativePath);
        if (absolutePath is null || !File.Exists(absolutePath)) return null;

        return new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, useAsync: true);
    }

    public void TryDelete(string relativePath)
    {
        try
        {
            var absolutePath = ResolveAbsolute(relativePath);
            if (absolutePath is not null && File.Exists(absolutePath)) File.Delete(absolutePath);
        }
        catch (Exception ex)
        {
            // 헌법 #15 — 빈 catch 금지. 뒷정리 실패는 업무를 막지 않는다.
            _logger.LogWarning(ex, "메신저 파일 뒷정리 실패 — {Path}", relativePath);
        }
    }

    /// <summary>
    /// 🔴 저장 루트 <b>밖으로 나가는 경로를 막는다.</b>
    /// DB 값이라도 그대로 믿지 않는다.
    /// </summary>
    private string? ResolveAbsolute(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;

        // relativePath 는 "chat-files/{tenant}/{yyyyMM}/{file}" 형태다.
        // _rootPath 가 이미 chat-files 를 가리키므로 앞머리를 걷어낸다.
        var trimmed = relativePath.Replace('\\', '/');
        const string prefix = "chat-files/";
        if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[prefix.Length..];

        var candidate = Path.GetFullPath(Path.Combine(_rootPath, trimmed));
        var root = Path.GetFullPath(_rootPath);

        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("메신저 파일 경로가 저장 폴더를 벗어났다 — {Path}", relativePath);
            return null;
        }

        return candidate;
    }

    private static string SanitizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return string.Empty;

        var cleaned = new string(extension.Where(c => char.IsLetterOrDigit(c) || c == '.').ToArray());
        if (cleaned.Length > 16) cleaned = cleaned[..16];
        return cleaned.StartsWith('.') ? cleaned : "." + cleaned;
    }

    private static string SanitizeSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment)) return "_";
        return new string(segment.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
    }
}
