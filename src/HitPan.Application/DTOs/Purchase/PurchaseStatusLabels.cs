namespace HitPan.Application.DTOs.Purchase;

/// <summary>
/// 매입라인 전표 상태 → 화면 한글 표시.
/// </summary>
/// <remarks>
/// 🔴 20260827작7 W3-3 (사장님 지시) — <i>"그리고 <b>화면상에는 한글로!!</b>"</i>
///
/// <para>
/// 사장님이 정하신 상태 정의(2026-08-27 그리드 오더):
/// <list type="bullet">
///   <item><b>임시저장</b> = 매입만 잡힌 상태</item>
///   <item><b>입고완료</b> = 매입확정 상태</item>
///   <item><b>반품중</b> = 반품서 작성 상태</item>
///   <item><b>반품확정</b> = 반품확정 상태</item>
/// </list>
/// </para>
///
/// <para>
/// ⚠️ <b>철자가 갈려 있다</b> — 원장문서(발주·매입)는 <c>cancelled</c>(l 2개),
/// 반품문서는 <c>canceled</c>(l 1개)로 저장된다. <c>varchar</c> 에 enum 제약이 없어
/// DB 가 둘 다 받는다. <b>여기서는 둘 다 「취소」로 받는다</b> — 화면에서만이라도
/// 같게 보여야 담당자가 헷갈리지 않는다.
/// (저장값 통일은 기존 데이터 마이그 동반이라 별건 — 사장님 결재 *"최대한 틀어지지
/// 않는 쪽으로 일괄 맞추기"*.)
/// </para>
/// </remarks>
public static class PurchaseStatusLabels
{
    // ── 매입명세서 · 매입반품 ──────────────────────────────
    public const string Draft = "draft";
    public const string Confirmed = "confirmed";

    // ── 발주서 (DB enum) ──────────────────────────────────
    public const string Ordered = "ordered";
    public const string Partial = "partial";
    public const string Received = "received";

    private static readonly Dictionary<string, string> ReceiptMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [Draft] = "임시저장",
        [Confirmed] = "입고완료",
        ["cancelled"] = "취소",
        ["canceled"] = "취소",
    };

    private static readonly Dictionary<string, string> ReturnMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [Draft] = "반품중",
        [Confirmed] = "반품확정",
        ["cancelled"] = "취소",
        ["canceled"] = "취소",
    };

    private static readonly Dictionary<string, string> OrderMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [Draft] = "임시저장",
        [Ordered] = "발주완료",
        [Partial] = "부분입고",
        [Received] = "입고완료",
        ["cancelled"] = "취소",
        ["canceled"] = "취소",
    };

    /// <summary>매입명세서 상태 — 임시저장 / 입고완료.</summary>
    public static string Receipt(string? code) => Lookup(ReceiptMap, code);

    /// <summary>매입반품 상태 — 반품중 / 반품확정.</summary>
    public static string Return(string? code) => Lookup(ReturnMap, code);

    /// <summary>발주서 상태 — 임시저장 / 발주완료 / 부분입고 / 입고완료.</summary>
    public static string Order(string? code) => Lookup(OrderMap, code);

    /// <summary>
    /// 🔴 모르는 값이 오면 <b>그 값을 그대로 돌려준다.</b>
    /// 뭉개면(예: "알 수 없음") 잘못 들어간 값이 화면에서 정상처럼 보여
    /// 정합성 사고를 숨긴다 — 사장님이 요구한 *"틀린 데이터를 빠르게 발견"* 과 반대다.
    /// </summary>
    private static string Lookup(Dictionary<string, string> map, string? code)
        => string.IsNullOrWhiteSpace(code) ? "" : (map.TryGetValue(code, out var v) ? v : code);
}
