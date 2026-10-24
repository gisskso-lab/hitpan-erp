using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// 🔴 양식지 그림 보관소 — 고객 PC 안에만 둔다(#22·#30 · 본사로 아무것도 안 보낸다).
/// </summary>
/// <remarks>
/// 🔴 <b>읽기 울타리가 있다고 쓰기가 안전해지는 것이 아니다.</b>
///   작성기(<c>PdfRenderService</c>)는 폴더 가둠·링크 통과 차단·확장자·크기·미리 그려 보기까지
///   다섯 겹으로 막아 둔다. 그건 <b>읽을 때</b> 얘기다. 넣을 때 막지 않으면 보관소 안에
///   못 읽을 파일·남의 테넌트 파일·덮어쓴 파일이 쌓인다. 그래서 같은 울타리를
///   <b>쓰기 방향으로 한 번 더</b> 세운다.
///
/// 🔴 <b>고객이 준 파일 이름을 쓰지 않는다</b> — 이름 하나로 생기는 사고가 넷이다:
///   거슬러 오르기(<c>..\</c>) · 장치 예약어(<c>CON</c>·<c>PRN</c>) · 한글 깨짐 ·
///   같은 이름 <b>덮어쓰기</b>(#1 — 고객이 먼저 올린 양식지가 조용히 사라진다).
///   그래서 <b>서버가 새 이름을 만든다.</b> 확장자만 고객 파일에서 가져온다.
/// </remarks>
public sealed class FormBackgroundStore : IFormBackgroundStore
{
    private readonly ILogger<FormBackgroundStore> _logger;

    public FormBackgroundStore(ILogger<FormBackgroundStore> logger) => _logger = logger;

    /// <inheritdoc />
    public async Task<(string? StoredPath, string? Rejection)> SaveAsync(
        string tenantId, string originalFileName, Stream content, CancellationToken ct = default)
    {
        // ① 테넌트 — 🔴 빈 값이면 아무것도 하지 않는다(#2 · 호출부가 JWT 에서만 채운다).
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            _logger.LogWarning("[양식지] 테넌트 없이 올리려 했다 — 받지 않는다.");
            return (null, "로그인 정보를 확인할 수 없습니다. 다시 로그인해 주십시오.");
        }

        // 🔴 테넌트 값 자체가 경로를 벗어나는 모양이면 거절한다.
        //   JWT 에서 오는 값이지만, 폴더 이름으로 쓰는 값은 **한 번 더 본다**.
        if (tenantId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || tenantId.Contains("..", StringComparison.Ordinal))
        {
            _logger.LogWarning("[양식지] 테넌트 값이 폴더 이름으로 쓸 수 없는 모양이다 — 받지 않는다.");
            return (null, "로그인 정보를 확인할 수 없습니다. 다시 로그인해 주십시오.");
        }

        // ② 확장자 — 🔴 고객 이름에서 **이것만** 가져온다. 목록은 읽는 쪽과 같은 자리에 있다.
        var ext = Path.GetExtension(originalFileName ?? string.Empty);
        if (!FormBackgroundPaths.AllowedExts.Contains(ext))
        {
            _logger.LogWarning("[양식지] 그림 형식이 아니다({Ext}) — 받지 않는다.", ext);
            return (null, "그림 파일만 올릴 수 있습니다. (png · jpg · bmp · webp)");
        }

        try
        {
            // ③ 바이트를 먼저 메모리로 받는다 — 🔴 **검사가 끝나기 전에 디스크에 쓰지 않는다.**
            //    쓰고 나서 지우는 방식은 중간에 꺼지면 쓰레기가 남는다.
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct).ConfigureAwait(false);
            var bytes = buffer.ToArray();

            // ④ 크기 — 0바이트와 상한 초과를 같이 막는다.
            if (bytes.Length <= 0)
                return (null, "빈 파일입니다. 다른 파일을 골라 주십시오.");

            if (bytes.Length > FormBackgroundPaths.MaxBytes)
            {
                _logger.LogWarning("[양식지] 그림이 너무 크다({Len} bytes) — 받지 않는다.", bytes.Length);
                return (null, "그림이 너무 큽니다. 20MB 이하로 올려 주십시오.");
            }

            // ⑤ 🔴 **여기서 미리 그려 본다.** 작성기가 못 읽는 바이트를 보관소에 넣지 않는다.
            //    확장자만 그림이고 속은 다른 파일일 수 있다(이름은 거짓말을 한다).
            try
            {
                _ = QuestPDF.Infrastructure.Image.FromBinaryData(bytes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[양식지] 그림을 열 수 없다 — 받지 않는다.");
                return (null, "그림 파일을 열 수 없습니다. 파일이 손상되지 않았는지 확인해 주십시오.");
            }

            // ⑥ 자리를 만든다 — 테넌트마다 갈라 둔다(한 PC 에 사업장이 둘일 수 있다).
            var dir = FormBackgroundPaths.TenantRoot(tenantId);
            Directory.CreateDirectory(dir);

            // 🔴 서버가 이름을 만든다 — 고객 이름은 쓰지 않는다(덮어쓰기·예약어·거슬러 오르기 차단).
            var fileName = $"{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
            var full = Path.Combine(dir, fileName);

            // ⑦ 🔴 마지막으로 **쓰려는 자리가 그 테넌트 폴더 안인지** 다시 본다.
            //    위에서 이름을 만들었으니 벗어날 수 없지만, 벗어나는 날이 오면
            //    여기서 멈춰야 한다 — 울타리는 「그럴 리 없다」로 세우지 않는다.
            var root = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
            var resolved = Path.GetFullPath(full);
            if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("[양식지] 보관 자리가 허용 폴더 밖이다 — 받지 않는다.");
                return (null, "그림을 보관할 수 없습니다. 관리자에게 알려 주십시오.");
            }

            await File.WriteAllBytesAsync(resolved, bytes, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "[양식지] 그림을 보관했다 — 테넌트 {Tenant} · {Len} bytes", tenantId, bytes.Length);

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
            _logger.LogWarning(ex, "[양식지] 그림 보관 실패 — 테넌트 {Tenant}", tenantId);
            return (null, "그림을 보관하지 못했습니다. 잠시 뒤 다시 시도해 주십시오.");
        }
    }

    /// <inheritdoc />
    public string? Delete(string tenantId, string storedPath)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(storedPath))
            return "지울 그림을 찾을 수 없습니다.";

        try
        {
            // 🔴 **이 테넌트 폴더 안**이 아니면 지우지 않는다 — 남의 양식지를 지울 길을 안 만든다.
            var root = Path.GetFullPath(FormBackgroundPaths.TenantRoot(tenantId))
                           .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(storedPath);

            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("[양식지] 허용 폴더 밖을 지우려 했다 — 지우지 않는다.");
                return "이 그림은 지울 수 없습니다.";
            }

            if (File.Exists(full)) File.Delete(full);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[양식지] 그림 지우기 실패 — 테넌트 {Tenant}", tenantId);
            return "그림을 지우지 못했습니다. 잠시 뒤 다시 시도해 주십시오.";
        }
    }
}
