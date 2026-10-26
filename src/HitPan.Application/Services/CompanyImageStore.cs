using HitPan.Application.Interfaces;
using HitPan.Contracts.Forms;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// 🔴 회사 그림 보관소 — 고객 PC 안에만 둔다(#22·#30 · 본사로 아무것도 안 보낸다).
/// </summary>
/// <remarks>
/// 🔴 <b>울타리 7개를 양식지 보관소에서 그대로 옮겨 왔다</b> — 새로 설계하지 않았다.
///   그 7개는 2026-10-24~25 에 검증팀이 <b>실제로 뚫어 본</b> 자리들이다
///   (형제 폴더 통과 · junction 으로 울타리 밖 파일 지우기 · 뿌리 자신이 링크 · 이름 거짓말).
///   같은 구멍을 새 보관소에서 다시 열지 않으려면 <b>모양을 그대로</b> 가져와야 한다.
///
/// 🔴 <b>고객이 준 파일 이름을 쓰지 않는다</b> — 이름 하나로 생기는 사고가 넷이다:
///   거슬러 오르기(<c>..\</c>) · 장치 예약어(<c>CON</c>·<c>PRN</c>) · 한글 깨짐 ·
///   같은 이름 <b>덮어쓰기</b>. 그래서 <b>서버가 새 이름을 만든다.</b>
///
/// 🔴 <b>옛 그림을 자동으로 지우지 않는다</b> — 새로 올리면 새 파일이 생기고 DB 가 그쪽을
///   가리킨다. 옛 파일은 남는다. 사람이 「지우기」를 누를 때만 지운다(#1 — 덮어쓰기 금지).
/// </remarks>
public sealed class CompanyImageStore : ICompanyImageStore
{
    private readonly ILogger<CompanyImageStore> _logger;

    public CompanyImageStore(ILogger<CompanyImageStore> logger) => _logger = logger;

    /// <inheritdoc />
    public async Task<(string? StoredPath, string? Rejection)> SaveAsync(
        string tenantId, string kind, string originalFileName, Stream content, CancellationToken ct = default)
    {
        // ① 테넌트 — 🔴 빈 값이면 아무것도 하지 않는다(#2 · 호출부가 JWT 에서만 채운다).
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            _logger.LogWarning("[회사그림] 테넌트 없이 올리려 했다 — 받지 않는다.");
            return (null, "로그인 정보를 확인할 수 없습니다. 다시 로그인해 주십시오.");
        }

        // 🔴 테넌트 값 자체가 경로를 벗어나는 모양이면 거절한다.
        //   JWT 에서 오는 값이지만, 폴더 이름으로 쓰는 값은 **한 번 더 본다**.
        if (tenantId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || tenantId.Contains("..", StringComparison.Ordinal))
        {
            _logger.LogWarning("[회사그림] 테넌트 값이 폴더 이름으로 쓸 수 없는 모양이다 — 받지 않는다.");
            return (null, "로그인 정보를 확인할 수 없습니다. 다시 로그인해 주십시오.");
        }

        // ①-2 🔴 종류 — 바깥에서 온 글자를 **파일 이름에 그냥 쓰지 않는다.**
        //   로고·직인·머리 셋뿐이다. 그 밖이면 받지 않는다.
        if (!CompanyImagePaths.IsKnownKind(kind))
        {
            _logger.LogWarning("[회사그림] 모르는 종류다({Kind}) — 받지 않는다.", kind);
            return (null, "그림 종류를 확인할 수 없습니다.");
        }

        // ② 확장자 — 🔴 고객 이름에서 **이것만** 가져온다. 목록은 읽는 쪽과 같은 자리에 있다.
        var ext = Path.GetExtension(originalFileName ?? string.Empty);
        if (!CompanyImagePaths.AllowedExts.Contains(ext))
        {
            _logger.LogWarning("[회사그림] 그림 형식이 아니다({Ext}) — 받지 않는다.", ext);
            return (null, "그림 파일만 올릴 수 있습니다. (png · jpg · bmp · webp)");
        }

        try
        {
            // ③ 바이트를 먼저 메모리로 받는다 — 🔴 **검사가 끝나기 전에 디스크에 쓰지 않는다.**
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct).ConfigureAwait(false);
            var bytes = buffer.ToArray();

            // ④ 크기 — 0바이트와 상한 초과를 같이 막는다.
            if (bytes.Length <= 0)
                return (null, "빈 파일입니다. 다른 파일을 골라 주십시오.");

            // 🔴 상한이 양식지(20MB)보다 작다 — 로고는 워터마크 재료라 그릴 때마다
            //   base64 로 1.33배 늘고, 그 일이 모든 양식·모든 장에서 일어난다.
            if (bytes.Length > CompanyImagePaths.MaxBytes)
            {
                _logger.LogWarning("[회사그림] 그림이 너무 크다({Len} bytes) — 받지 않는다.", bytes.Length);
                return (null, $"그림이 너무 큽니다. {CompanyImageLimits.MaxBytesLabel} 이하로 올려 주십시오.");
            }

            // ⑤ 🔴 **여기서 미리 그려 본다.** 작성기가 못 읽는 바이트를 보관소에 넣지 않는다.
            //    확장자만 그림이고 속은 다른 파일일 수 있다(이름은 거짓말을 한다).
            //    🔴 이 검사가 없으면 보관소가 생기는 날 전표 인쇄가 전부 500 이 된다
            //      — 10/24 P0 가 바로 그 사고였다(#20 워크플로우 흐름은 절대 안 끊긴다).
            try
            {
                _ = QuestPDF.Infrastructure.Image.FromBinaryData(bytes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[회사그림] 그림을 열 수 없다 — 받지 않는다.");
                return (null, "그림 파일을 열 수 없습니다. 파일이 손상되지 않았는지 확인해 주십시오.");
            }

            // ⑥ 자리를 만든다 — 테넌트마다 갈라 둔다(한 PC 에 사업장이 둘일 수 있다).
            var dir = CompanyImagePaths.TenantRoot(tenantId);
            Directory.CreateDirectory(dir);

            // 🔴 서버가 이름을 만든다 — 고객 이름은 쓰지 않는다(덮어쓰기·예약어·거슬러 오르기 차단).
            //   앞머리에 종류를 적어 둔다 — 사람이 폴더를 열어 봤을 때 무엇인지 알 수 있게.
            var fileName = $"{kind}_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
            var full = Path.Combine(dir, fileName);

            // ⑦ 🔴 마지막으로 **쓰려는 자리가 그 테넌트 폴더 안인지** 다시 본다.
            //    위에서 이름을 만들었으니 벗어날 수 없지만, 벗어나는 날이 오면
            //    여기서 멈춰야 한다 — 울타리는 「그럴 리 없다」로 세우지 않는다.
            var root = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
            var resolved = Path.GetFullPath(full);
            if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("[회사그림] 보관 자리가 허용 폴더 밖이다 — 받지 않는다.");
                return (null, "그림을 보관할 수 없습니다. 관리자에게 알려 주십시오.");
            }

            // 🔴 쓰려는 자리가 **링크를 지나면 쓰지 않는다.**
            //   테넌트 폴더가 누군가 만든 junction 이면 울타리는 통과하고 파일은 바깥에 떨어진다.
            //   🔴 뿌리를 **읽는 쪽과 같은 값**으로 넘긴다 — 테넌트 폴더 자신도 검사 대상에 든다.
            if (PassesThroughLink(resolved, FenceRoot))
            {
                _logger.LogWarning("[회사그림] 보관 자리가 링크를 지난다 — 받지 않는다.");
                return (null, "그림을 보관할 수 없습니다. 관리자에게 알려 주십시오.");
            }

            await File.WriteAllBytesAsync(resolved, bytes, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "[회사그림] 그림을 보관했다 — 테넌트 {Tenant} · 종류 {Kind} · {Len} bytes",
                tenantId, kind, bytes.Length);

            return (resolved, null);
        }
        catch (OperationCanceledException)
        {
            // 🔴 고객이 창을 닫았거나 취소했다 — 사고가 아니다. 조용히 끝낸다.
            throw;
        }
        catch (Exception ex)
        {
            // 🔴 빈 catch 금지(#15). 까닭을 남기고, 고객에게는 사람 말로 돌려준다.
            _logger.LogWarning(ex, "[회사그림] 그림 보관 실패 — 테넌트 {Tenant}", tenantId);
            return (null, "그림을 보관하지 못했습니다. 잠시 뒤 다시 시도해 주십시오.");
        }
    }

    /// <summary>
    /// 🔴🔴 울타리 검사에 넘길 <b>뿌리</b> — 테넌트 폴더가 아니라 <b>보관소 뿌리</b>다.
    /// </summary>
    /// <remarks>
    /// 🔴 양식지 보관소가 2026-10-25 재검증 S-1 에서 이 자리로 뚫렸다 —
    ///   테넌트 폴더를 넘기면 <b>그 폴더 자신이 junction 일 때 아무도 안 본다</b>.
    ///   검증팀이 테넌트 폴더를 junction 으로 바꾸고 「지우기」를 불러 <b>울타리 밖 파일을 지웠다.</b>
    ///   <c>%ProgramData%</c> 는 일반 사용자도 하위 폴더를 만들 수 있는 자리다.
    ///   ⇒ 같은 구멍을 새 보관소에서 다시 열지 않는다.
    /// </remarks>
    private static string FenceRoot =>
        Path.GetFullPath(CompanyImagePaths.Root).TrimEnd(Path.DirectorySeparatorChar)
        + Path.DirectorySeparatorChar;

    /// <inheritdoc />
    public string? Delete(string tenantId, string storedPath)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(storedPath))
            return "지울 그림을 찾을 수 없습니다.";

        // 🔴 지우기에도 테넌트 모양 검사를 둔다 — 올리기에만 두면 비대칭이 된다.
        if (tenantId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || tenantId.Contains("..", StringComparison.Ordinal))
        {
            _logger.LogWarning("[회사그림] 테넌트 값이 폴더 이름으로 쓸 수 없는 모양이다 — 지우지 않는다.");
            return "이 그림은 지울 수 없습니다.";
        }

        try
        {
            // 🔴 **이 테넌트 폴더 안**이 아니면 지우지 않는다 — 남의 로고를 지울 길을 안 만든다.
            var root = Path.GetFullPath(CompanyImagePaths.TenantRoot(tenantId))
                           .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(storedPath);

            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("[회사그림] 허용 폴더 밖을 지우려 했다 — 지우지 않는다.");
                return "이 그림은 지울 수 없습니다.";
            }

            // 🔴 링크를 지나면 지우지 않는다 — 지우기는 **고객이 준 경로**를 받으므로
            //   올리기보다 위험하다. 검증팀이 양식지 쪽 같은 자리로 밖의 파일을 지웠다.
            if (PassesThroughLink(full, FenceRoot))
            {
                _logger.LogWarning("[회사그림] 지울 경로가 링크를 지난다 — 지우지 않는다.");
                return "이 그림은 지울 수 없습니다.";
            }

            if (File.Exists(full)) File.Delete(full);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[회사그림] 그림 지우기 실패 — 테넌트 {Tenant}", tenantId);
            return "그림을 지우지 못했습니다. 잠시 뒤 다시 시도해 주십시오.";
        }
    }

    /// <summary>
    /// 🔴 경로가 <b>링크(symlink·junction)</b> 를 지나는가 — 지나면 손대지 않는다.
    /// </summary>
    /// <remarks>
    /// 🔴 <c>Path.GetFullPath</c> 는 <b>링크를 풀지 않는다.</b> 그래서 허용 폴더 <b>안에</b>
    ///   junction 을 하나 만들어 두면 앞글자 비교는 통과하고 실제 파일은 디스크 어디든 될 수 있다.
    /// 🔴 <b>뿌리 자신도 본다</b> — 이 뿌리는 코드 고정값이고 앱이 스스로 만든다.
    ///   관리자가 고른 자리가 아니다. <c>%ProgramData%</c> 는 일반 사용자도 하위 폴더를
    ///   만들 수 있어, 갓 설치본에서 직원이 <c>company-images</c> 를 먼저 junction 으로
    ///   만들어 두면 울타리가 통째로 비켜진다(2026-10-25 T-1 과 같은 구멍).
    /// 🔴 <b>못 재면 막는 쪽</b>으로 떨어진다 — 그림은 안 올라가도 전표는 산다(#20).
    /// </remarks>
    private static bool PassesThroughLink(string fullPath, string root)
    {
        var rootNoSep = root.TrimEnd(Path.DirectorySeparatorChar);

        var file = new FileInfo(fullPath);
        if (file.LinkTarget is not null) return true;

        for (var dir = file.Directory;
             dir is not null && dir.FullName.Length > rootNoSep.Length;
             dir = dir.Parent)
        {
            if (!dir.FullName.StartsWith(rootNoSep, StringComparison.OrdinalIgnoreCase)) break;
            if (dir.LinkTarget is not null) return true;
        }

        var rootDir = new DirectoryInfo(rootNoSep);
        if (rootDir.LinkTarget is not null) return true;

        return false;
    }
}
