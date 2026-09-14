using System.Data;
using Dapper;

namespace HitPan.Application.Services;

/// <summary>
/// 🔴 20260915작1 갈래 G (R-B2 · R-B) — <b>이전 프로그램에서 가져온 자료는 고치지 않는다.</b>
///
/// <para>
/// 이관 명세서·계산서(<c>source_type = 'migration'</c>)는 레거시 원장·대사표와 숫자가 맞아야 한다
/// (공영정보 합격선 = 레거시와 숫자가 맞는다). 여기서 수정·확정취소·삭제·계산서 발행·계산서 취소를 허용하면
/// 대사표와 어긋나는 편집이 생긴다. 정정은 <b>새 전표</b>로 한다(사장님 결재 2026-09-15 R-B2 「잠금」).
/// </para>
/// <para>
/// ⚠️ 화면에서 숨기는 것은 차단이 아니다 — 서버 경로마다 이 검사를 부른다.
///   · 판매: <c>SalesService.UpdateDeliveryAsync</c> · <c>DeleteDeliveryAsync</c> · <c>CancelConfirmedDeliveryAsync</c>
///   · 매입: <c>PurchaseService.DeletePurchaseReceiptAsync</c> (매입명세서는 수정·확정취소 서버 경로가 없다 — 개발명세서 §2)
///   · 계산서: <c>TaxInvoiceService.IssueAsync</c> (이관 명세서 발행) · <c>CancelAsync</c> (이관 계산서 취소)
/// </para>
/// <para>사람이 만든 전표(<c>source_type</c> 이 migration 이 아닌 값)는 이 검사를 그대로 통과한다.</para>
/// </summary>
public static class MigratedDocumentLock
{
    /// <summary>이관 행 식별 값 — <c>MdbMigrationService</c> 가 명세서·계산서에 넣는 값과 같다.</summary>
    public const string MigrationSourceType = "migration";

    /// <summary>명세서 수정·확정취소·삭제 거부 문구 (현장 용어).</summary>
    public const string EditBlockedMessage =
        "이전 프로그램에서 가져온 자료는 고칠 수 없습니다. 새 전표로 정정해 주세요.";

    /// <summary>이관 거래명세서 계산서 발행 거부 문구 (설계 §17 R-B).</summary>
    public const string IssueBlockedMessage =
        "이전 프로그램에서 가져온 거래명세서는 계산서를 새로 발행할 수 없습니다.";

    /// <summary>이관 계산서 취소 거부 문구.</summary>
    public const string CancelInvoiceBlockedMessage =
        "이전 프로그램에서 가져온 계산서는 취소할 수 없습니다. 새 전표로 정정해 주세요.";

    /// <summary>계산서 서비스 오류 코드 — 컨트롤러는 기본값(400)으로 내려보낸다.</summary>
    public const string LockedErrorCode = "migrated_locked";

    public static bool IsMigrated(string? sourceType) =>
        string.Equals(sourceType?.Trim(), MigrationSourceType, StringComparison.OrdinalIgnoreCase);

    /// <summary>레거시 계산서 번호 칸 — <c>sales_deliveries.legacy_tax_no</c> (이관이 DOCFB <c>IJ_TAXNO</c> 를 넣는다).</summary>
    public const string LegacyTaxNoColumn = "legacy_tax_no";

    /// <summary>
    /// 계산서 <b>발행</b> 잠금 판정 — 사장님 결재 R-B3(작업지시서 §14-1): <b>레거시에서 발행한 이관 명세서만</b> 잠근다.
    /// 판정 = 이관분 AND 레거시 계산서 번호가 0('00000000')·빈값이 아님. 안 끊은 이관 명세서는 발행 허용.
    /// 수정·확정취소·삭제 잠금(<see cref="IsMigrated"/>)과 분리해 둔다 — 발행 범위가 바뀌면 이 한 줄만 바꾼다.
    /// ⚠️ 한계(개발명세서 §5): 이관은 묶음 <b>첫 줄</b>의 IJ_TAXNO 만 저장하고 99999999 는 NULL 로 넣는다
    ///   → 「묶음 안 한 줄이라도 번호 있으면」 과 99999999 판정은 갈래 B 저장 보강 뒤에 정확해진다.
    /// </summary>
    public static bool IsIssueLocked(string? deliverySourceType, int? legacyTaxNo) =>
        IsMigrated(deliverySourceType) && legacyTaxNo is int no && no != 0;

    /// <summary>판매 거래명세서가 이관분이면 <see cref="InvalidOperationException"/> 을 던진다.</summary>
    public static async Task EnsureDeliveryEditableAsync(
        IDbConnection db, string deliveryId, string tenantId, CancellationToken ct = default)
    {
        var sourceType = await db.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT source_type FROM sales_deliveries WHERE delivery_id = @Id AND tenant_id = @Tid",
            new { Id = deliveryId, Tid = tenantId }, cancellationToken: ct));

        if (IsMigrated(sourceType))
        {
            throw new InvalidOperationException(EditBlockedMessage);
        }
    }

    /// <summary>매입명세서가 이관분이면 <see cref="InvalidOperationException"/> 을 던진다.</summary>
    public static async Task EnsureReceiptEditableAsync(
        IDbConnection db, string receiptId, string tenantId, CancellationToken ct = default)
    {
        var sourceType = await db.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT source_type FROM purchase_receipts WHERE receipt_id = @Id AND tenant_id = @Tid",
            new { Id = receiptId, Tid = tenantId }, cancellationToken: ct));

        if (IsMigrated(sourceType))
        {
            throw new InvalidOperationException(EditBlockedMessage);
        }
    }
}
